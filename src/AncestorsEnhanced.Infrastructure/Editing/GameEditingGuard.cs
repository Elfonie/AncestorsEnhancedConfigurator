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

        // For Proton (Linux) installs the user-data path must live inside the concrete
        // Steam library that owns this installation, rather than matching any compatible
        // prefix anywhere on disk (F014).
        if (snapshot.Installation is { CompatibilityLayer: CompatibilityLayerKind.Proton } proton &&
            !string.IsNullOrWhiteSpace(proton.LibraryRoot) &&
            !string.IsNullOrWhiteSpace(snapshot.UserDataDirectory))
        {
            string libraryRoot = Path.GetFullPath(proton.LibraryRoot);
            string userData = Path.GetFullPath(snapshot.UserDataDirectory);
            string? rootOfUserData = GetLibraryRootOfUserData(userData);
            if (!string.Equals(rootOfUserData, libraryRoot, PathComparison))
            {
                throw new InvalidOperationException("The Proton user-data directory does not belong to the detected installation.");
            }
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
        // The plan may be identified by its recognised build ID or its recognised
        // content signature, but never by a stale/wrong claim. When both forms of
        // evidence are present they must both match, so contradictory evidence is
        // fail-closed (F061/F063/F064).
        bool buildPending = !string.IsNullOrWhiteSpace(plan.BuildId);
        bool contentPending = !string.IsNullOrWhiteSpace(plan.ContentSignature);
        bool buildOk = string.Equals(plan.BuildId, AncestorsGameProfile.SupportedBuildId, StringComparison.Ordinal);
        bool contentOk = string.Equals(
            plan.ContentSignature,
            AncestorsGameProfile.SupportedContentSignature,
            StringComparison.Ordinal);
        bool identityOk = buildPending && contentPending
            ? buildOk && contentOk
            : buildOk || contentOk;
        if (!identityOk ||
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
    /// <summary>
    /// Extracts the Steam library root that owns a Proton user-data path, or null when
    /// the path is not a Proton path. A Proton path always contains
    /// "steamapps/compatdata/<appid>/pfx/...".
    /// </summary>
    private static string? GetLibraryRootOfUserData(string userDataDirectory)
    {
        string normalized = userDataDirectory.Replace('\\', '/');
        string marker = $"/steamapps/compatdata/{AncestorsGameProfile.SteamAppId}/pfx/";
        int index = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        return normalized[..index];
    }
}
