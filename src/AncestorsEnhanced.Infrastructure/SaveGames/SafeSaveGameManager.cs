using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

public sealed class SafeSaveGameManager : ISaveGameManager
{
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<bool> _isGameRunning;
    private readonly SaveGameCheckpointStore _store;
    private readonly string _userDataDirectory;

    public SafeSaveGameManager(
        string userDataDirectory,
        SaveGameManagerOptions? options = null)
        : this(
            userDataDirectory,
            () => DateTimeOffset.UtcNow,
            IsAncestorsRunning,
            options ?? new SaveGameManagerOptions())
    {
    }

    internal SafeSaveGameManager(
        string userDataDirectory,
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning,
        SaveGameManagerOptions options)
    {
        _userDataDirectory = userDataDirectory;
        _utcNow = utcNow;
        _isGameRunning = isGameRunning;
        _store = new SaveGameCheckpointStore(utcNow, options.MaxCheckpointsPerSlot);
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

    public SaveGameOperationResult CreateCheckpoint(string slotNumber)
    {
        int slot = ParseSlot(slotNumber);
        SaveGameGuard.ValidateSlot(_userDataDirectory, slot);
        if (_isGameRunning())
        {
            return Failure("Close Ancestors before creating a save checkpoint.");
        }

        try
        {
            string slotPath = SaveGamePaths.GetSlotPath(_userDataDirectory, slot);
            if (!File.Exists(slotPath))
            {
                return Failure($"There is no save in slot {slot} to back up.");
            }

            byte[] content = File.ReadAllBytes(slotPath);
            string checkpointId = _store.Create(_userDataDirectory, slot, content);
            return new SaveGameOperationResult(
                true,
                $"Checkpoint saved for slot {slot}.",
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
        SaveGamePaths.ValidateCheckpointId(checkpointId);
        SaveGameGuard.ValidateSlot(_userDataDirectory, slot);
        if (_isGameRunning())
        {
            return Failure("Close Ancestors before loading a save checkpoint.");
        }

        try
        {
            string slotPath = SaveGamePaths.GetSlotPath(_userDataDirectory, slot);
            if (File.Exists(slotPath))
            {
                byte[] current = File.ReadAllBytes(slotPath);
                _store.Create(_userDataDirectory, slot, current);
            }

            byte[] checkpoint = SaveGameCheckpointStore.Read(_userDataDirectory, slot, checkpointId);
            WriteBytesAtomically(slotPath, checkpoint);
            return new SaveGameOperationResult(
                true,
                $"Loaded checkpoint for slot {slot}. Start Ancestors to continue.");
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            return Failure($"Nothing was loaded: {exception.Message}");
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

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException;
}
