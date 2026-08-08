using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Environment;
using AncestorsEnhanced.Infrastructure.FileSystem;

namespace AncestorsEnhanced.Infrastructure.Inspection;

internal sealed class UserDataLocator(
    IReadOnlyFileSystem fileSystem,
    IHostEnvironment environment)
{
    public string? Find(
        GameInstallationSnapshot? installation,
        List<InspectionNotice> notices)
    {
        if (installation is { Host: HostKind.Linux, Store: StoreKind.Steam })
        {
            string users = Path.Combine(
                installation.LibraryRoot,
                "steamapps",
                "compatdata",
                AncestorsGameProfile.SteamAppId,
                "pfx",
                "drive_c",
                "users");
            if (fileSystem.DirectoryExists(users))
            {
                // Only accept a user whose Ancestors Saved directory actually exists.
                // If several wine users own a save, the location is ambiguous and must
                // not be guessed (F113).
                string[] candidates = fileSystem.EnumerateDirectories(users)
                    .Select(path => Path.Combine(path, "AppData", "Local", "Ancestors", "Saved"))
                    .Where(fileSystem.DirectoryExists)
                    .ToArray();
                if (candidates.Length == 1)
                {
                    return candidates[0];
                }

                if (candidates.Length > 1)
                {
                    notices.Add(new InspectionNotice(
                        InspectionSeverity.Warning,
                        "userdata.ambiguous-proton-user",
                        "Multiple Wine users with Ancestors saves were detected. Resolve the user before making changes."));
                    return null;
                }
            }

            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "userdata.not-found",
                "The Ancestors Proton prefix was not found. Start the game once."));
            return null;
        }

        if (string.IsNullOrWhiteSpace(environment.LocalApplicationDataPath))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "userdata.base-path-missing",
                "The local application-data directory could not be determined."));
            return null;
        }

        string userDataDirectory = Path.Combine(
            environment.LocalApplicationDataPath,
            "Ancestors",
            "Saved");
        if (!fileSystem.DirectoryExists(userDataDirectory))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "userdata.not-found",
                "Ancestors user data was not found. It is normally created after the game starts."));
            return null;
        }

        return userDataDirectory;
    }
}
