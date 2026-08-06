using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

internal static class SaveGamePaths
{
    public const int SlotCount = 5;

    /// <summary>Hard limit for checkpoint identifiers created by this tool.</summary>
    public const int MaxCheckpointIdLength = 32;

    public static string GetSaveGamesDirectory(string userDataDirectory) =>
        Path.GetFullPath(Path.Combine(userDataDirectory, "SaveGames"));

    public static string GetSlotFileName(int slotNumber)
    {
        if (slotNumber < 0 || slotNumber >= SlotCount)
        {
            throw new InvalidOperationException("The save slot is outside the supported range.");
        }

        return $"Savegame{slotNumber}.sav";
    }

    public static string GetSlotPath(string userDataDirectory, int slotNumber) =>
        Path.GetFullPath(Path.Combine(GetSaveGamesDirectory(userDataDirectory), GetSlotFileName(slotNumber)));

    public static string GetCheckpointRoot(string userDataDirectory) =>
        Path.GetFullPath(Path.Combine(userDataDirectory, "AncestorsEnhanced", "SaveBackups"));

    public static string GetSlotRoot(string userDataDirectory, int slotNumber) =>
        Path.GetFullPath(Path.Combine(GetCheckpointRoot(userDataDirectory), $"slot{slotNumber}"));

    public static string GetCheckpointPath(string userDataDirectory, int slotNumber, string checkpointId)
    {
        ValidateCheckpointId(checkpointId);
        string slotRoot = GetSlotRoot(userDataDirectory, slotNumber);
        string checkpointDirectory = GetCheckpointDirectory(slotRoot, checkpointId);
        string path = Path.GetFullPath(Path.Combine(checkpointDirectory, "save.sav"));
        if (!string.Equals(Path.GetDirectoryName(path), checkpointDirectory, PathComparison))
        {
            throw new InvalidOperationException("The checkpoint path leaves the checkpoint directory.");
        }

        return path;
    }

    /// <summary>
    /// Returns the fully resolved checkpoint directory as a direct child of the slot root.
    /// Verifies containment after path normalization and refuses the slot root itself.
    /// May only be called when the slot root itself has already been validated.
    /// </summary>
    public static string GetCheckpointDirectory(string slotRoot, string checkpointId)
    {
        ValidateCheckpointId(checkpointId);
        string fullSlotRoot = Path.GetFullPath(slotRoot);
        string combined = Path.GetFullPath(Path.Combine(fullSlotRoot, checkpointId));
        if (string.Equals(combined, fullSlotRoot, PathComparison))
        {
            throw new InvalidOperationException("The checkpoint path must not address the slot root itself.");
        }

        if (!string.Equals(Path.GetDirectoryName(combined), fullSlotRoot, PathComparison))
        {
            throw new InvalidOperationException("The checkpoint path leaves the checkpoint root.");
        }

        if (Directory.Exists(combined) &&
            File.GetAttributes(combined).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The checkpoint path must not be a reparse point.");
        }

        return combined;
    }

    public static void ValidateCheckpointId(string checkpointId)
    {
        if (string.IsNullOrWhiteSpace(checkpointId))
        {
            throw new InvalidOperationException("The checkpoint identifier must not be empty.");
        }

        if (checkpointId.Length > MaxCheckpointIdLength)
        {
            throw new InvalidOperationException("The checkpoint identifier is too long.");
        }

        // Only the identifier shape produced by NewCheckpointId() is accepted: no dots,
        // slashes, backslashes, colons or other path separators, no whitespace.
        bool valid = checkpointId.Length > 0 &&
            checkpointId.All(character =>
                char.IsAsciiLetterOrDigit(character) || character == '-') &&
            !checkpointId.Contains('.', StringComparison.Ordinal);
        if (!valid)
        {
            throw new InvalidOperationException("The checkpoint identifier is invalid.");
        }
    }

    /// <summary>Generates a fresh checkpoint identifier using only the accepted character set.</summary>
    public static string NewCheckpointId(DateTimeOffset createdAt) =>
        $"{createdAt:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..MaxCheckpointIdLength];

    public static void ValidateUserDataDirectory(string userDataDirectory) =>
        ValidateConfigurationPath(
            userDataDirectory,
            GetSaveGamesDirectory(userDataDirectory));
}
