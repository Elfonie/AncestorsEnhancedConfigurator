using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.SystemSave;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

public sealed class SafeSaveGameManager : ISaveGameManager
{
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<bool> _isGameRunning;
    private readonly SaveGameCheckpointStore _store;
    private readonly string _userDataDirectory;
    private readonly Func<bool>? _revalidate;
    private readonly Action? _beforeRestoreCommit;

    public SafeSaveGameManager(
        VerifiedGameContext context,
        GameContextVerifier verifier,
        SaveGameManagerOptions? options = null)
        : this(
            context.UserDataDirectory,
            () => DateTimeOffset.UtcNow,
            IsAncestorsRunning,
            options ?? new SaveGameManagerOptions(),
            () => verifier.Verify(context))
    {
    }

    internal SafeSaveGameManager(
        string userDataDirectory,
        SaveGameManagerOptions? options = null,
        Func<bool>? revalidate = null)
        : this(
            userDataDirectory,
            () => DateTimeOffset.UtcNow,
            IsAncestorsRunning,
            options ?? new SaveGameManagerOptions(),
            revalidate)
    {
    }

    internal SafeSaveGameManager(
        string userDataDirectory,
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning,
        SaveGameManagerOptions options,
        Func<bool>? revalidate = null,
        Action? beforeRestoreCommit = null)
    {
        _userDataDirectory = userDataDirectory;
        _utcNow = utcNow;
        _isGameRunning = isGameRunning;
        _store = new SaveGameCheckpointStore(utcNow, options.MaxCheckpointsPerSlot);
        _revalidate = revalidate;
        _beforeRestoreCommit = beforeRestoreCommit;
    }

    private SaveGameOperationResult? RevalidateFailure(string action)
    {
        if (_revalidate is not null && !_revalidate())
        {
            return Failure($"The game context changed; {action}. Refresh and try again.");
        }

        return null;
    }

    public SaveGamesSnapshot Inspect()
    {
        SaveGameGuard.ValidateUserData(_userDataDirectory);
        try
        {
            var slots = Enumerable.Range(0, SaveGamePaths.SlotCount)
                .Select(ReadSlot)
                .ToArray();
            return new SaveGamesSnapshot(_utcNow(), _userDataDirectory, slots);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            throw new InvalidOperationException($"Could not inspect save games: {exception.Message}", exception);
        }
    }

    public SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual")
    {
        int slot = ParseSlot(slotNumber);
        SaveGameGuard.ValidateSlot(_userDataDirectory, slot);
        return MutationCoordinator.Run(() => ExecuteCreateCheckpoint(slot, origin));
    }

    private SaveGameOperationResult ExecuteCreateCheckpoint(int slot, string origin)
    {
        try
        {
            SaveGameOperationResult? revalidateFailure = RevalidateFailure("the checkpoint cannot be created safely");
            if (revalidateFailure is not null)
            {
                return revalidateFailure;
            }
            string slotPath = SaveGamePaths.GetSlotPath(_userDataDirectory, slot);
            if (!File.Exists(slotPath))
            {
                return Failure($"There is no save in slot {slot + 1} to back up.");
            }

            byte[] content = ReadSaveWithRetries(slotPath);
            try
            {
                _ = SnappyBlockCodec.Decode(content);
            }
            catch (InvalidDataException)
            {
                return Failure($"Slot {slot + 1} is currently being written or is corrupt; skipped backup.");
            }
            IReadOnlyList<SaveGameCheckpoint> latest = SaveGameCheckpointStore.ListCheckpoints(_userDataDirectory, slot);
            // A restore can legitimately make the live slot equal to an older manual
            // checkpoint while the newest checkpoint is the automatic PreRestore copy.
            // Dedupe against every valid checkpoint so reconciliation cannot publish a
            // redundant AutoBackup of the restored content (F162).
            string liveHash = Sha256(content);
            foreach (SaveGameCheckpoint checkpoint in latest)
            {
                if (!string.Equals(checkpoint.Sha256, liveHash, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    if (IsIdentical(content, SaveGameCheckpointStore.Read(_userDataDirectory, slot, checkpoint.Id)))
                    {
                        return new SaveGameOperationResult(true, $"Slot {slot + 1} is unchanged; no checkpoint was created.", null);
                    }
                }
                catch (Exception exception) when (IsExpectedException(exception))
                {
                    // A damaged historical checkpoint must not prevent a current save
                    // from being protected (F008). Continue with other candidates.
                }
            }

            string checkpointId = _store.Create(_userDataDirectory, slot, content, origin);
            return new SaveGameOperationResult(
                true,
                $"Checkpoint saved for slot {slot + 1}.",
                checkpointId);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            return Failure($"No checkpoint was created: {exception.Message}");
        }
    }
    public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId)
    {
        int slot = ParseSlot(slotNumber);
        ArgumentNullException.ThrowIfNull(checkpointId);
        SaveGameGuard.ValidateSlot(_userDataDirectory, slot);
        if (_isGameRunning())
        {
            return Failure("Close Ancestors before loading a save checkpoint.");
        }

        return MutationCoordinator.Run(() => ExecuteLoadCheckpoint(slot, checkpointId));
    }

    private SaveGameOperationResult ExecuteLoadCheckpoint(int slot, string checkpointId)
    {
        try
        {
            SaveGameOperationResult? revalidateFailure = RevalidateFailure("the checkpoint cannot be loaded safely");
            if (revalidateFailure is not null)
            {
                return revalidateFailure;
            }
            SaveGamePaths.ValidateCheckpointId(checkpointId);
            string slotPath = SaveGamePaths.GetSlotPath(_userDataDirectory, slot);
            byte[] checkpoint = SaveGameCheckpointStore.Read(_userDataDirectory, slot, checkpointId);

            // Everything before WriteBytesAtomically is part of the "not committed"
            // phase: if any of it fails, the live save is still intact.
            bool expectedExists = File.Exists(slotPath);
            string? expectedSha256 = null;
            if (expectedExists)
            {
                byte[] current = ReadStableBounded(slotPath, 64L * 1024 * 1024);
                expectedSha256 = Sha256(current);
                if (!IsIdentical(current, checkpoint))
                {
                    _store.Create(_userDataDirectory, slot, current, "PreRestore");
                }
            }

            // The atomic replace is the commit point. After it, the save has already
            // been changed, so a later failure must be reported as committed-with-warning
            // and never as "Nothing was loaded".
            // Re-check game process and the exact live state after the PreRestore
            // checkpoint has been published. Neither Cloud nor another tool may win
            // this window and be silently overwritten (lost-update Restore CAS).
            _beforeRestoreCommit?.Invoke();
            if (_isGameRunning())
            {
                return Failure("Close Ancestors before loading a save checkpoint.");
            }

            CompareAndReplace(slotPath, checkpoint, expectedSha256, expectedExists);
            try
            {
                File.SetLastWriteTimeUtc(slotPath, _utcNow().UtcDateTime);
            }
            catch (Exception warning) when (IsExpectedException(warning))
            {
                return new SaveGameOperationResult(
                    true,
                    $"The save was loaded, but its timestamp could not be updated: {warning.Message}",
                    CommitState: SaveOperationCommitState.CommittedWithWarning);
            }

            return new SaveGameOperationResult(
                true,
                $"Loaded checkpoint for slot {slot + 1}. Start Ancestors to continue.",
                CommitState: SaveOperationCommitState.Committed);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            return Failure($"Nothing was loaded: {exception.Message}");
        }
    }

    public SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId)
    {
        int slot = ParseSlot(slotNumber);
        ArgumentNullException.ThrowIfNull(checkpointId);
        SaveGameGuard.ValidateSlot(_userDataDirectory, slot);
        return MutationCoordinator.Run(() => ExecuteDeleteCheckpoint(slot, checkpointId));
    }

    private SaveGameOperationResult ExecuteDeleteCheckpoint(int slot, string checkpointId)
    {
        try
        {
            SaveGameOperationResult? revalidateFailure = RevalidateFailure("the checkpoint cannot be deleted safely");
            if (revalidateFailure is not null)
            {
                return revalidateFailure;
            }
            SaveGamePaths.ValidateCheckpointId(checkpointId);
            // Validated again with containment checks immediately before the recursive delete.
            string checkpointDirectory = SaveGamePaths.GetCheckpointDirectory(
                SaveGamePaths.GetSlotRoot(_userDataDirectory, slot),
                checkpointId);
            if (!Directory.Exists(checkpointDirectory))
            {
                return Failure("The checkpoint could not be found.");
            }

            Directory.Delete(checkpointDirectory, recursive: true);
            return new SaveGameOperationResult(true, $"Checkpoint deleted from slot {slot + 1}.");
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            return Failure($"Nothing was deleted: {exception.Message}");
        }
    }

    private SaveGameSlotSnapshot ReadSlot(int slotNumber)
    {
        string slotPath = SaveGamePaths.GetSlotPath(_userDataDirectory, slotNumber);
        bool exists = File.Exists(slotPath);
        long? size = null;
        DateTimeOffset? lastWrite = null;
        if (exists)
        {
            FileInfo info = new(slotPath);
            size = info.Length;
            lastWrite = info.LastWriteTimeUtc;
        }

        IReadOnlyList<SaveGameCheckpoint> checkpoints = SaveGameCheckpointStore.ListCheckpoints(_userDataDirectory, slotNumber);
        return new SaveGameSlotSnapshot(
            slotNumber.ToString(CultureInfo.InvariantCulture),
            SaveGamePaths.GetSlotFileName(slotNumber),
            slotPath,
            exists,
            size,
            lastWrite,
            checkpoints);
    }

    private static int ParseSlot(string slotNumber)
    {
        if (int.TryParse(slotNumber, out int slot) &&
            slot >= 0 &&
            slot < SaveGamePaths.SlotCount)
        {
            return slot;
        }

        throw new InvalidOperationException("The save slot is invalid.");
    }

    private static SaveGameOperationResult Failure(string message) =>
        new(false, message);

    private static bool IsAncestorsRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("Ancestors-Win64-Shipping").Length > 0 ||
                   System.Diagnostics.Process.GetProcessesByName("Ancestors").Length > 0;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool IsIdentical(byte[] first, byte[] second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        return first.AsSpan().SequenceEqual(second);
    }

    private static byte[] ReadSaveWithRetries(string slotPath) =>
        ReadStableBounded(slotPath, 64L * 1024 * 1024);
    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException;
}
