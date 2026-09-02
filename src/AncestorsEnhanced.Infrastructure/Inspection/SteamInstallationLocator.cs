using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.Environment;
using AncestorsEnhanced.Infrastructure.FileSystem;
using AncestorsEnhanced.Infrastructure.Parsing;

namespace AncestorsEnhanced.Infrastructure.Inspection;

internal sealed class SteamInstallationLocator(
    IReadOnlyFileSystem fileSystem,
    IHostEnvironment environment)
{
    public IReadOnlyList<GameInstallationSnapshot> Find(List<InspectionNotice> notices)
    {
        List<GameInstallationSnapshot> installations = [];
        HashSet<string> visitedLibraries = new(PathComparer);
        foreach (string rawSteamRoot in environment.GetSteamRootCandidates())
        {
            string steamRoot = ConfigurationFileOperations.ResolvePhysicalPath(rawSteamRoot);
            if (!fileSystem.DirectoryExists(steamRoot))
            {
                continue;
            }

            foreach (string rawLibraryRoot in GetLibraries(steamRoot, notices))
            {
                string libraryRoot = ConfigurationFileOperations.ResolvePhysicalPath(rawLibraryRoot);
                if (!visitedLibraries.Add(libraryRoot))
                {
                    continue;
                }

                GameInstallationSnapshot? installation = ReadManifest(
                    libraryRoot,
                    notices);
                if (installation is not null)
                {
                    installations.Add(installation);
                }
            }
        }

        return installations;
    }

    private IReadOnlyList<string> GetLibraries(string steamRoot, List<InspectionNotice> notices)
    {
        string canonicalSteamRoot = ConfigurationFileOperations.ResolvePhysicalPath(steamRoot);
        List<string> libraries = [canonicalSteamRoot];
        string libraryFile = Path.Combine(canonicalSteamRoot, "steamapps", "libraryfolders.vdf");
        if (!fileSystem.FileExists(libraryFile))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Information,
                "steam.library-file-missing",
                $"Steam library list was not found at {libraryFile}."));
            return libraries;
        }

        try
        {
            ReadOnlyFileMetadata metadata = fileSystem.GetFileMetadata(libraryFile);
            if (metadata.SizeBytes > InspectionLimits.TextFile)
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Warning,
                    "steam.library-file-too-large",
                    "Steam library list is unexpectedly large and was not read."));
                return libraries;
            }

            ValveKeyValueObject root = ValveKeyValueParser.Parse(fileSystem.ReadAllText(libraryFile));
            ValveKeyValueObject? folders = root.GetObject("libraryfolders");
            if (folders is null)
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Warning,
                    "steam.library-file-invalid",
                    "Steam library list does not contain the expected libraryfolders object."));
                return libraries;
            }

            foreach (ValveKeyValueEntry entry in folders.Entries)
            {
                string? path = entry.Child?.GetString("path");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    // Never resolve a relative library path against the process working
                    // directory; resolve it against the Steam root instead.
                    string candidatePath = Path.IsPathRooted(path) ? path : Path.Combine(canonicalSteamRoot, path);
                    libraries.Add(ConfigurationFileOperations.ResolvePhysicalPath(candidatePath));
                }
            }
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "steam.library-file-unreadable",
                $"Steam library list could not be read: {exception.Message}"));
        }

        return libraries.Distinct(PathComparer).ToArray();
    }

    private GameInstallationSnapshot? ReadManifest(
        string libraryRoot,
        List<InspectionNotice> notices)
    {
        string canonicalLibraryRoot = ConfigurationFileOperations.ResolvePhysicalPath(libraryRoot);
        string manifestPath = Path.Combine(
            canonicalLibraryRoot,
            "steamapps",
            $"appmanifest_{AncestorsGameProfile.SteamAppId}.acf");
        if (!fileSystem.FileExists(manifestPath))
        {
            return null;
        }

        try
        {
            if (fileSystem.GetFileMetadata(manifestPath).SizeBytes > InspectionLimits.TextFile)
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Error,
                    "steam.manifest-too-large",
                    "Ancestors Steam manifest is unexpectedly large and was not read."));
                return null;
            }

            ValveKeyValueObject root = ValveKeyValueParser.Parse(fileSystem.ReadAllText(manifestPath));
            ValveKeyValueObject? appState = root.GetObject("AppState");
            string? installName = appState?.GetString("installdir");
            if (!string.Equals(
                    appState?.GetString("appid"),
                    AncestorsGameProfile.SteamAppId,
                    StringComparison.Ordinal) ||
                !IsSafeSingleDirectoryName(installName))
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Error,
                    "steam.manifest-invalid",
                    $"The Ancestors Steam manifest at {manifestPath} is invalid."));
                return null;
            }

            string installDirectory = ConfigurationFileOperations.ResolvePhysicalPath(Path.Combine(
                canonicalLibraryRoot,
                "steamapps",
                "common",
                installName!));
            string executable = GameInstallationFactory.GetExecutablePath(installDirectory);
            bool executableExists = fileSystem.FileExists(executable);
            if (!executableExists)
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Error,
                    "game.executable-missing",
                    "The Steam manifest exists, but the expected Ancestors executable is missing."));
            }
            (string? Signature, bool Failed) signature = GameInstallationFactory.ReadContentSignature(installDirectory);
            return new GameInstallationSnapshot(
                StoreKind.Steam,
                environment.Host,
                environment.Host == HostKind.Linux
                    ? CompatibilityLayerKind.Proton
                    : CompatibilityLayerKind.None,
                canonicalLibraryRoot,
                installDirectory,
                appState?.GetString("buildid"),
                executableExists,
                signature.Signature,
                signature.Failed);
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Error,
                "steam.manifest-unreadable",
                $"Ancestors Steam manifest could not be read: {exception.Message}"));
            return null;
        }
    }

    // Both separators are manifest syntax regardless of the host separator, so
    // Windows-style paths are rejected on Linux as well.
    private static bool IsSafeSingleDirectoryName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value is not "." and not ".." &&
        value.IndexOfAny(['/', '\\']) < 0 &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
