using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal static class GameEditingGuard
{
    public static void ValidateSnapshot(
        GameInspectionSnapshot snapshot,
        Func<string, bool> isExpectedUserDataDirectory)
    {
        if (!EditableSettingsCatalog.IsVerifiedEditingTarget(snapshot))
        {
            throw new InvalidOperationException("Editing requires a supported Ancestors installation.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.UserDataDirectory))
        {
            throw new InvalidOperationException("The Ancestors user-data directory was not detected.");
        }

        if (!isExpectedUserDataDirectory(snapshot.UserDataDirectory))
        {
            throw new InvalidOperationException("The detected user-data directory is not a supported Ancestors location.");
        }
    }

    public static void ValidatePlan(SettingsChangePlan plan)
    {
        // The plan may be identified either by its recognised build ID or by its
        // recognised content signature, but never by a stale/wrong claim (F061, F064).
        // If both are present they must not contradict each other.
        bool buildOk = !string.IsNullOrWhiteSpace(plan.BuildId) &&
            string.Equals(plan.BuildId, AncestorsGameProfile.SupportedBuildId, StringComparison.Ordinal);
        bool contentOk = string.Equals(
            plan.ContentSignature,
            AncestorsGameProfile.SupportedContentSignature,
            StringComparison.Ordinal);
        if (((buildOk || contentOk) == false) ||
            plan.Files.Count == 0 ||
            string.IsNullOrWhiteSpace(plan.UserDataDirectory))
        {
            throw new InvalidOperationException("The change plan is not valid for this release.");
        }

        ValidateConfigurationPath(
            plan.UserDataDirectory,
            GetConfigurationDirectory(plan.UserDataDirectory));
        if (plan.Files.Any(file => file.Target == SettingFileTarget.SystemSave))
        {
            ValidateConfigurationPath(
                plan.UserDataDirectory,
                GetSystemSaveDirectory(plan.UserDataDirectory));
        }

        if (plan.Files.Any(file => file.Target == SettingFileTarget.Pak))
        {
            if (string.IsNullOrWhiteSpace(plan.InstallDirectory))
            {
                throw new InvalidOperationException("The game installation directory is missing.");
            }

            ValidateConfigurationPath(
                plan.InstallDirectory,
                GetPakDirectory(plan.InstallDirectory));
        }

        foreach (ConfigurationFileChangePlan file in plan.Files)
        {
            string expectedPath = GetTargetPath(
                plan.UserDataDirectory,
                plan.InstallDirectory,
                file.FileName,
                file.Target);
            if (!string.Equals(expectedPath, Path.GetFullPath(file.FullPath), PathComparison))
            {
                throw new InvalidOperationException("The change plan contains an unexpected target path.");
            }

            ValidateWritableTarget(expectedPath);
        }
    }

    public static bool IsExpectedNativeUserDataDirectory(string path)
    {
        string localApplicationData = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.LocalApplicationData);
        string fullPath = Path.GetFullPath(path);
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            string expected = Path.GetFullPath(Path.Combine(localApplicationData, "Ancestors", "Saved"));
            if (string.Equals(expected, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string normalized = fullPath.Replace('\\', '/');
        return normalized.Contains(
                   $"/steamapps/compatdata/{AncestorsGameProfile.SteamAppId}/pfx/drive_c/users/",
                   StringComparison.Ordinal) &&
               normalized.EndsWith(
                   "/AppData/Local/Ancestors/Saved",
                   StringComparison.Ordinal);
    }
}
