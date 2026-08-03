using AncestorsEnhanced.Core;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

internal static class SaveGameGuard
{
    public static void ValidateUserData(string? userDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(userDataDirectory))
        {
            throw new InvalidOperationException("The Ancestors user-data directory was not detected.");
        }

        SaveGamePaths.ValidateUserDataDirectory(userDataDirectory);
    }

    public static void ValidateSlot(string? userDataDirectory, int slotNumber)
    {
        ValidateUserData(userDataDirectory);
        _ = SaveGamePaths.GetSlotPath(userDataDirectory!, slotNumber);

        string slotPath = SaveGamePaths.GetSlotPath(userDataDirectory!, slotNumber);
        ValidateWritableTarget(slotPath);
    }
}
