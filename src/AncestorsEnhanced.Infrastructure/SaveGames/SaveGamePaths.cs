using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

internal static class SaveGamePaths
{
    public const int SlotCount = 5;

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
        string path = Path.GetFullPath(Path.Combine(slotRoot, checkpointId, "save.sav"));
        if (!string.Equals(Path.GetDirectoryName(path), Path.Combine(slotRoot, checkpointId), PathComparison))
        {
            throw new InvalidOperationException("The checkpoint path leaves the checkpoint directory.");
        }

        return path;
    }

    public static void ValidateCheckpointId(string checkpointId)
    {
        if (checkpointId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new InvalidOperationException("The checkpoint identifier is invalid.");
        }
    }

    public static void ValidateUserDataDirectory(string userDataDirectory) =>
        ValidateConfigurationPath(
            userDataDirectory,
            GetSaveGamesDirectory(userDataDirectory));
}
