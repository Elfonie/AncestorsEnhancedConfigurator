using System.Text.Json;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

internal static class SaveRestoreJournalStore
{
    private const int ManifestVersion = 1;
    private const int MaximumManifestSize = 64 * 1024;
    private const string PendingFileName = "restore-pending.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static SaveRestoreOperation Prepare(
        string userDataDirectory,
        int slotNumber,
        string checkpointId,
        DateTimeOffset createdAtUtc,
        bool originalExists,
        string? originalSha256,
        string resultSha256)
    {
        SaveGamePaths.ValidateCheckpointId(checkpointId);
        ValidateHash(resultSha256, "restore result");
        if (originalExists)
        {
            ValidateHash(originalSha256, "original save");
        }
        else if (originalSha256 is not null)
        {
            throw new InvalidDataException("A missing original save cannot have a hash.");
        }

        string path = GetJournalPath(userDataDirectory, slotNumber);
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"An unfinished save restore already exists: {path}");
        }

        var operation = new SaveRestoreOperation(
            ManifestVersion,
            slotNumber,
            checkpointId,
            createdAtUtc,
            originalExists,
            originalSha256,
            resultSha256);
        WriteBytesAtomically(path, JsonSerializer.SerializeToUtf8Bytes(operation, JsonOptions));
        if (Read(path, slotNumber) != operation)
        {
            throw new IOException("The save restore journal failed validation.");
        }
        return operation;
    }

    public static void Complete(string userDataDirectory, SaveRestoreOperation operation)
    {
        string path = GetJournalPath(userDataDirectory, operation.SlotNumber);
        SaveRestoreOperation stored = Read(path, operation.SlotNumber);
        if (stored != operation)
        {
            throw new IOException("The save restore journal changed before commit.");
        }

        string slotPath = SaveGamePaths.GetSlotPath(userDataDirectory, operation.SlotNumber);
        IReadOnlyCollection<string> hashes = ExpectedHashes(operation);
        _ = RecoverInterruptedTarget(slotPath, hashes);
        if (!File.Exists(slotPath) || !string.Equals(
                Sha256(ReadStableBounded(slotPath, 64L * 1024 * 1024)),
                operation.ResultSha256,
                StringComparison.Ordinal))
        {
            throw new IOException("The restored live save does not match the journal result.");
        }
        DeleteJournal(path);
    }

    public static void CancelFailedCommit(string userDataDirectory, SaveRestoreOperation operation)
    {
        string path = GetJournalPath(userDataDirectory, operation.SlotNumber);
        if (!File.Exists(path) || Read(path, operation.SlotNumber) != operation)
        {
            return;
        }

        string slotPath = SaveGamePaths.GetSlotPath(userDataDirectory, operation.SlotNumber);
        if (!ValidateInterruptedTargetRecovery(slotPath, ExpectedHashes(operation)))
        {
            DeleteJournal(path);
        }
    }

    public static bool HasRecoveryWork(string userDataDirectory)
    {
        string saveDirectory = SaveGamePaths.GetSaveGamesDirectory(userDataDirectory);
        for (int slot = 0; slot < SaveGamePaths.SlotCount; slot++)
        {
            string journal = GetJournalPath(userDataDirectory, slot);
            if (File.Exists(journal) || Directory.Exists(journal))
            {
                return true;
            }
            if (Directory.Exists(saveDirectory) && Directory.EnumerateFiles(
                    saveDirectory,
                    $".{SaveGamePaths.GetSlotFileName(slot)}.*.cas").Any())
            {
                return true;
            }
        }
        return false;
    }

    public static bool RecoverAll(string userDataDirectory)
    {
        bool recovered = false;
        for (int slot = 0; slot < SaveGamePaths.SlotCount; slot++)
        {
            string journalPath = GetJournalPath(userDataDirectory, slot);
            if (Directory.Exists(journalPath))
            {
                throw ManualRecovery(journalPath, "the restore journal path is a directory");
            }
            if (File.Exists(journalPath))
            {
                recovered |= RecoverPending(userDataDirectory, slot, journalPath);
                continue;
            }

            string slotPath = SaveGamePaths.GetSlotPath(userDataDirectory, slot);
            try
            {
                recovered |= RecoverInterruptedTarget(slotPath);
            }
            catch (IOException exception)
            {
                throw ManualRecovery(slotPath, exception.Message);
            }
        }
        return recovered;
    }

    private static bool RecoverPending(string userDataDirectory, int slot, string journalPath)
    {
        SaveRestoreOperation operation = Read(journalPath, slot);
        string slotPath = SaveGamePaths.GetSlotPath(userDataDirectory, slot);
        IReadOnlyCollection<string> hashes = ExpectedHashes(operation);

        if (File.Exists(slotPath))
        {
            string current = Sha256(ReadStableBounded(slotPath, 64L * 1024 * 1024));
            bool isOriginal = operation.OriginalExists &&
                string.Equals(current, operation.OriginalSha256, StringComparison.Ordinal);
            bool isResult = string.Equals(current, operation.ResultSha256, StringComparison.Ordinal);
            if (!isOriginal && !isResult)
            {
                throw ManualRecovery(journalPath, "the live save matches neither the original nor the restore result");
            }

            try
            {
                _ = RecoverInterruptedTarget(slotPath, hashes);
            }
            catch (IOException exception)
            {
                throw ManualRecovery(journalPath, exception.Message);
            }
        }
        else
        {
            try
            {
                if (operation.OriginalExists)
                {
                    _ = RecoverInterruptedTarget(slotPath, [operation.OriginalSha256!]);
                }
                else
                {
                    bool hasSidecar = ValidateInterruptedTargetRecovery(slotPath, hashes);
                    if (hasSidecar)
                    {
                        throw new IOException("A restore from an empty slot cannot own a CAS sidecar.");
                    }
                }
            }
            catch (IOException exception)
            {
                throw ManualRecovery(journalPath, exception.Message);
            }
        }

        bool exists = File.Exists(slotPath);
        string? finalHash = exists
            ? Sha256(ReadStableBounded(slotPath, 64L * 1024 * 1024))
            : null;
        bool finalOriginal = exists == operation.OriginalExists &&
            (!exists || string.Equals(finalHash, operation.OriginalSha256, StringComparison.Ordinal));
        bool finalResult = exists && string.Equals(finalHash, operation.ResultSha256, StringComparison.Ordinal);
        if (!finalOriginal && !finalResult)
        {
            throw ManualRecovery(journalPath, "the final live-save state is ambiguous");
        }

        DeleteJournal(journalPath);
        return true;
    }

    private static SaveRestoreOperation Read(string path, int expectedSlot)
    {
        SaveRestoreOperation? operation;
        try
        {
            if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException("The restore journal is missing or linked.");
            }
            operation = JsonSerializer.Deserialize<SaveRestoreOperation>(
                ReadStableBounded(path, MaximumManifestSize));
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or
                NotSupportedException or InvalidDataException)
        {
            throw ManualRecovery(path, exception.Message);
        }
        if (operation is null || operation.Version != ManifestVersion ||
            operation.SlotNumber != expectedSlot)
        {
            throw ManualRecovery(path, "the restore journal identity is invalid");
        }
        try
        {
            SaveGamePaths.ValidateCheckpointId(operation.CheckpointId);
            ValidateHash(operation.ResultSha256, "restore result");
            if (operation.OriginalExists)
            {
                ValidateHash(operation.OriginalSha256, "original save");
            }
            else if (operation.OriginalSha256 is not null)
            {
                throw new InvalidDataException("A missing original save cannot have a hash.");
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            throw ManualRecovery(path, exception.Message);
        }
        return operation;
    }

    private static string GetJournalPath(string userDataDirectory, int slot)
    {
        string slotRoot = SaveGamePaths.GetSlotRoot(userDataDirectory, slot);
        ValidateConfigurationPath(userDataDirectory, slotRoot);
        return Path.Combine(slotRoot, PendingFileName);
    }

    private static List<string> ExpectedHashes(SaveRestoreOperation operation)
    {
        var hashes = new List<string>(2);
        if (operation.OriginalExists)
        {
            hashes.Add(operation.OriginalSha256!);
        }
        hashes.Add(operation.ResultSha256);
        return hashes;
    }

    private static void ValidateHash(string? hash, string name)
    {
        if (hash is null || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException($"The {name} hash is invalid.");
        }
    }

    private static void DeleteJournal(string path)
    {
        File.Delete(path);
        if (File.Exists(path))
        {
            throw new IOException("The completed save restore journal could not be removed.");
        }
    }

    private static IOException ManualRecovery(string path, string reason) =>
        new($"Save restore recovery requires manual action because {reason}. No save file was overwritten. Inspect {path}.");
}

internal sealed record SaveRestoreOperation(
    int Version,
    int SlotNumber,
    string CheckpointId,
    DateTimeOffset CreatedAtUtc,
    bool OriginalExists,
    string? OriginalSha256,
    string ResultSha256);
