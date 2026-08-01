using System.Text.Json;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Environment;
using AncestorsEnhanced.Infrastructure.FileSystem;
using AncestorsEnhanced.Infrastructure.Paks;
using AncestorsEnhanced.Infrastructure.Parsing;

namespace AncestorsEnhanced.Infrastructure.Inspection;

public sealed class ReadOnlyAncestorsInspector : IReadOnlyGameInspector
{
    private const string SteamAppId = "536270";
    private const long MaximumTextFileSizeBytes = 4 * 1024 * 1024;
    private const long MaximumPakHashSizeBytes = 1024 * 1024;

    private readonly IReadOnlyFileSystem _fileSystem;
    private readonly IHostEnvironment _environment;

    internal ReadOnlyAncestorsInspector(
        IReadOnlyFileSystem fileSystem,
        IHostEnvironment environment)
    {
        _fileSystem = fileSystem;
        _environment = environment;
    }

    public static ReadOnlyAncestorsInspector CreateDefault() =>
        new(new PhysicalReadOnlyFileSystem(), CreateHostEnvironment());

    private static IHostEnvironment CreateHostEnvironment()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsHostEnvironment();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxHostEnvironment();
        }

        throw new PlatformNotSupportedException("Windows and Linux are supported.");
    }

    public GameInspectionSnapshot Inspect()
    {
        List<InspectionNotice> notices = [];
        GameInstallationSnapshot? installation = DiscoverInstallation(notices);
        string? userDataDirectory = GetUserDataDirectory(installation, notices);
        ConfigurationFileSnapshot[] configurationFiles =
            ReadConfigurationFiles(userDataDirectory, notices);
        BinarySettingsFileSnapshot? binarySettingsFile = ReadBinarySettingsFile(userDataDirectory);
        PakFileSnapshot[] pakFiles = ReadPakFiles(installation, notices);
        VignetteModSnapshot? vignette = installation is null
            ? null
            : VignettePakEditor.Inspect(installation.InstallDirectory);

        return new GameInspectionSnapshot(
            _environment.UtcNow,
            installation,
            userDataDirectory,
            configurationFiles,
            binarySettingsFile,
            pakFiles,
            notices,
            vignette);
    }

    private GameInstallationSnapshot? DiscoverInstallation(List<InspectionNotice> notices)
    {
        List<GameInstallationSnapshot> installations = [];
        HashSet<string> visitedLibraries = new(StringComparer.OrdinalIgnoreCase);

        foreach (string steamRoot in _environment.GetSteamRootCandidates())
        {
            if (!_fileSystem.DirectoryExists(steamRoot))
            {
                continue;
            }

            foreach (string libraryRoot in GetSteamLibraries(steamRoot, notices))
            {
                if (!visitedLibraries.Add(libraryRoot))
                {
                    continue;
                }

                GameInstallationSnapshot? candidate = ReadSteamManifest(
                    steamRoot,
                    libraryRoot,
                    notices);
                if (candidate is not null)
                {
                    installations.Add(candidate);
                }
            }
        }

        installations.AddRange(DiscoverEpicInstallations(notices));
        installations.AddRange(DiscoverGogInstallations());

        if (installations.Count == 0)
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "game.not-found",
                "Ancestors was not found. Steam Epic and GOG were checked."));
            return null;
        }

        if (installations.Count > 1)
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "game.multiple-installations",
                "Multiple Ancestors installations were detected. The first valid installation is displayed."));
        }

        return installations[0];
    }

    private IReadOnlyList<string> GetSteamLibraries(
        string steamRoot,
        List<InspectionNotice> notices)
    {
        List<string> libraries = [Path.GetFullPath(steamRoot)];
        string libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!_fileSystem.FileExists(libraryFile))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Information,
                "steam.library-file-missing",
                $"Steam library list was not found at {libraryFile}."));
            return libraries;
        }

        try
        {
            ReadOnlyFileMetadata metadata = _fileSystem.GetFileMetadata(libraryFile);
            if (metadata.SizeBytes > MaximumTextFileSizeBytes)
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Warning,
                    "steam.library-file-too-large",
                    "Steam library list is unexpectedly large and was not read."));
                return libraries;
            }

            ValveKeyValueObject root = ValveKeyValueParser.Parse(_fileSystem.ReadAllText(libraryFile));
            ValveKeyValueObject? libraryFolders = root.GetObject("libraryfolders");
            if (libraryFolders is null)
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Warning,
                    "steam.library-file-invalid",
                    "Steam library list does not contain the expected libraryfolders object."));
                return libraries;
            }

            foreach (ValveKeyValueEntry entry in libraryFolders.Entries)
            {
                string? path = entry.Child?.GetString("path");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    libraries.Add(Path.GetFullPath(path));
                }
            }
        }
        catch (Exception exception) when (IsExpectedReadException(exception))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "steam.library-file-unreadable",
                $"Steam library list could not be read: {exception.Message}"));
        }

        return libraries
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private GameInstallationSnapshot? ReadSteamManifest(
        string steamRoot,
        string libraryRoot,
        List<InspectionNotice> notices)
    {
        string manifestPath = Path.Combine(
            libraryRoot,
            "steamapps",
            $"appmanifest_{SteamAppId}.acf");
        if (!_fileSystem.FileExists(manifestPath))
        {
            return null;
        }

        try
        {
            ReadOnlyFileMetadata metadata = _fileSystem.GetFileMetadata(manifestPath);
            if (metadata.SizeBytes > MaximumTextFileSizeBytes)
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Error,
                    "steam.manifest-too-large",
                    "Ancestors Steam manifest is unexpectedly large and was not read."));
                return null;
            }

            ValveKeyValueObject root = ValveKeyValueParser.Parse(_fileSystem.ReadAllText(manifestPath));
            ValveKeyValueObject? appState = root.GetObject("AppState");
            string? appId = appState?.GetString("appid");
            string? installDirectoryName = appState?.GetString("installdir");

            if (!string.Equals(appId, SteamAppId, StringComparison.Ordinal) ||
                !IsSafeSingleDirectoryName(installDirectoryName))
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Error,
                    "steam.manifest-invalid",
                    $"The Ancestors Steam manifest at {manifestPath} is invalid."));
                return null;
            }

            string installDirectory = Path.GetFullPath(Path.Combine(
                libraryRoot,
                "steamapps",
                "common",
                installDirectoryName!));
            string executablePath = Path.Combine(
                installDirectory,
                "Ancestors",
                "Binaries",
                "Win64",
                "Ancestors-Win64-Shipping.exe");
            bool executableExists = _fileSystem.FileExists(executablePath);

            if (!executableExists)
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Error,
                    "game.executable-missing",
                    "The Steam manifest exists, but the expected Ancestors executable is missing."));
            }

            return new GameInstallationSnapshot(
                StoreKind.Steam,
                _environment.Host,
                _environment.Host == HostKind.Linux
                    ? CompatibilityLayerKind.Proton
                    : CompatibilityLayerKind.None,
                steamRoot,
                libraryRoot,
                installDirectory,
                executablePath,
                appState?.GetString("buildid"),
                executableExists,
                ReadContentSignature(installDirectory));
        }
        catch (Exception exception) when (IsExpectedReadException(exception))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Error,
                "steam.manifest-unreadable",
                $"Ancestors Steam manifest could not be read: {exception.Message}"));
            return null;
        }
    }

    private string? GetUserDataDirectory(
        GameInstallationSnapshot? installation,
        List<InspectionNotice> notices)
    {
        if (installation is { Host: HostKind.Linux, Store: StoreKind.Steam })
        {
            string users = Path.Combine(
                installation.LibraryRoot,
                "steamapps", "compatdata", SteamAppId, "pfx", "drive_c", "users");
            if (_fileSystem.DirectoryExists(users))
            {
                string? protonData = Directory.EnumerateDirectories(users)
                    .Select(path => Path.Combine(path, "AppData", "Local", "Ancestors", "Saved"))
                    .FirstOrDefault(_fileSystem.DirectoryExists);
                if (protonData is not null)
                {
                    return protonData;
                }
            }

            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "userdata.not-found",
                "The Ancestors Proton prefix was not found. Start the game once."));
            return null;
        }

        if (string.IsNullOrWhiteSpace(_environment.LocalApplicationDataPath))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "userdata.base-path-missing",
                "The local application-data directory could not be determined."));
            return null;
        }

        string userDataDirectory = Path.Combine(
            _environment.LocalApplicationDataPath,
            "Ancestors",
            "Saved");
        if (!_fileSystem.DirectoryExists(userDataDirectory))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "userdata.not-found",
                "Ancestors user data was not found. It is normally created after the game starts."));
        }

        return userDataDirectory;
    }

    private List<GameInstallationSnapshot> DiscoverEpicInstallations(
        List<InspectionNotice> notices)
    {
        List<GameInstallationSnapshot> found = [];
        foreach (string directory in _environment.GetEpicManifestDirectories()
                     .Where(_fileSystem.DirectoryExists))
        {
            foreach (ReadOnlyFileMetadata file in _fileSystem.EnumerateFiles(directory, "*.item"))
            {
                try
                {
                    if (file.SizeBytes > MaximumTextFileSizeBytes)
                    {
                        continue;
                    }

                    using JsonDocument json = JsonDocument.Parse(_fileSystem.ReadAllText(file.FullPath));
                    JsonElement root = json.RootElement;
                    string? name = ReadJsonString(root, "DisplayName");
                    string? install = ReadJsonString(root, "InstallLocation");
                    if (string.IsNullOrWhiteSpace(install) ||
                        name?.Contains("Ancestors", StringComparison.OrdinalIgnoreCase) != true)
                    {
                        continue;
                    }

                    GameInstallationSnapshot? snapshot = CreateStoreInstallation(
                        StoreKind.EpicGames,
                        directory,
                        install,
                        ReadJsonString(root, "BuildVersion"));
                    if (snapshot is not null)
                    {
                        found.Add(snapshot);
                    }
                }
                catch (Exception exception) when (IsExpectedReadException(exception))
                {
                    notices.Add(new InspectionNotice(
                        InspectionSeverity.Information,
                        "epic.manifest-unreadable",
                        $"An Epic manifest could not be read: {exception.Message}"));
                }
            }
        }

        return found;
    }

    private GameInstallationSnapshot[] DiscoverGogInstallations() =>
        _environment.GetGogInstallCandidates()
            .Select(path => CreateStoreInstallation(StoreKind.Gog, path, path, null))
            .OfType<GameInstallationSnapshot>()
            .ToArray();

    private GameInstallationSnapshot? CreateStoreInstallation(
        StoreKind store,
        string storeRoot,
        string installDirectory,
        string? buildId)
    {
        string fullInstall = Path.GetFullPath(installDirectory);
        string executable = Path.Combine(
            fullInstall, "Ancestors", "Binaries", "Win64", "Ancestors-Win64-Shipping.exe");
        if (!_fileSystem.FileExists(executable))
        {
            return null;
        }

        return new GameInstallationSnapshot(
            store,
            HostKind.Windows,
            CompatibilityLayerKind.None,
            Path.GetFullPath(storeRoot),
            fullInstall,
            fullInstall,
            executable,
            buildId,
            true,
            ReadContentSignature(fullInstall));
    }

    private static string? ReadJsonString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadContentSignature(string installDirectory)
    {
        try
        {
            string paks = Path.Combine(installDirectory, "Ancestors", "Content", "Paks");
            string main = PakV5Archive.ReadIndexIdentity(Path.Combine(paks, "Ancestors-WindowsNoEditor.pak"));
            string level = PakV5Archive.ReadIndexIdentity(Path.Combine(paks, "VL01E01.pak"));
            return $"{main}:{level[(level.IndexOf(':') + 1)..]}";
        }
        catch (Exception exception) when (IsExpectedReadException(exception))
        {
            return null;
        }
    }

    private ConfigurationFileSnapshot[] ReadConfigurationFiles(
        string? userDataDirectory,
        List<InspectionNotice> notices)
    {
        if (userDataDirectory is null)
        {
            return [];
        }

        string configurationDirectory = Path.Combine(
            userDataDirectory,
            "Config",
            "WindowsNoEditor");
        if (!_fileSystem.DirectoryExists(configurationDirectory))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "config.directory-not-found",
                "The Ancestors configuration directory was not found."));
            return [];
        }

        try
        {
            return _fileSystem
                .EnumerateFiles(configurationDirectory, "*.ini")
                .Select(ReadConfigurationFile)
                .ToArray();
        }
        catch (Exception exception) when (IsExpectedReadException(exception))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Error,
                "config.directory-unreadable",
                $"The configuration directory could not be read: {exception.Message}"));
            return [];
        }
    }

    private ConfigurationFileSnapshot ReadConfigurationFile(ReadOnlyFileMetadata metadata)
    {
        if (metadata.SizeBytes > MaximumTextFileSizeBytes)
        {
            return new ConfigurationFileSnapshot(
                metadata.Name,
                metadata.FullPath,
                Exists: true,
                metadata.SizeBytes,
                metadata.LastWriteTimeUtc,
                [],
                "File is unexpectedly large and was not read.");
        }

        try
        {
            string content = _fileSystem.ReadAllText(metadata.FullPath);
            return new ConfigurationFileSnapshot(
                metadata.Name,
                metadata.FullPath,
                Exists: true,
                metadata.SizeBytes,
                metadata.LastWriteTimeUtc,
                IniSnapshotParser.Parse(content),
                null);
        }
        catch (Exception exception) when (IsExpectedReadException(exception))
        {
            return new ConfigurationFileSnapshot(
                metadata.Name,
                metadata.FullPath,
                Exists: true,
                metadata.SizeBytes,
                metadata.LastWriteTimeUtc,
                [],
                exception.Message);
        }
    }

    private BinarySettingsFileSnapshot? ReadBinarySettingsFile(string? userDataDirectory)
    {
        if (userDataDirectory is null)
        {
            return null;
        }

        string path = Path.Combine(userDataDirectory, "SaveGames", "System.sav");
        if (!_fileSystem.FileExists(path))
        {
            return new BinarySettingsFileSnapshot(
                "System.sav",
                path,
                Exists: false,
                null,
                null,
                "Not found");
        }

        try
        {
            ReadOnlyFileMetadata metadata = _fileSystem.GetFileMetadata(path);
            return new BinarySettingsFileSnapshot(
                metadata.Name,
                metadata.FullPath,
                Exists: true,
                metadata.SizeBytes,
                metadata.LastWriteTimeUtc,
                "Detected; current graphics-setting values are not readable yet");
        }
        catch (Exception exception) when (IsExpectedReadException(exception))
        {
            return new BinarySettingsFileSnapshot(
                "System.sav",
                path,
                Exists: true,
                null,
                null,
                $"Metadata could not be read: {exception.Message}");
        }
    }

    private PakFileSnapshot[] ReadPakFiles(
        GameInstallationSnapshot? installation,
        List<InspectionNotice> notices)
    {
        if (installation is null)
        {
            return [];
        }

        string pakDirectory = Path.Combine(
            installation.InstallDirectory,
            "Ancestors",
            "Content",
            "Paks");
        if (!_fileSystem.DirectoryExists(pakDirectory))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Error,
                "paks.directory-not-found",
                "The expected Ancestors Paks directory is missing."));
            return [];
        }

        try
        {
            return _fileSystem
                .EnumerateFiles(pakDirectory, "*.pak")
                .Select(CreatePakSnapshot)
                .ToArray();
        }
        catch (Exception exception) when (IsExpectedReadException(exception))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Error,
                "paks.directory-unreadable",
                $"The Paks directory could not be read: {exception.Message}"));
            return [];
        }
    }

    private PakFileSnapshot CreatePakSnapshot(ReadOnlyFileMetadata file)
    {
        PakClassification classification = ClassifyPak(file.Name);
        string? sha256 = classification == PakClassification.PatchStyle &&
            file.SizeBytes <= MaximumPakHashSizeBytes
                ? _fileSystem.ComputeSha256(file.FullPath)
                : null;

        return new PakFileSnapshot(
            file.Name,
            file.FullPath,
            file.SizeBytes,
            file.LastWriteTimeUtc,
            classification,
            sha256);
    }

    private static PakClassification ClassifyPak(string name)
    {
        if (string.Equals(name, "Ancestors-WindowsNoEditor.pak", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "VL01E01.pak", StringComparison.OrdinalIgnoreCase))
        {
            return PakClassification.BaseGame;
        }

        return name.EndsWith("_P.pak", StringComparison.OrdinalIgnoreCase)
            ? PakClassification.PatchStyle
            : PakClassification.Unclassified;
    }

    private static bool IsSafeSingleDirectoryName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value is not "." and not ".." &&
            value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0 &&
            value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static bool IsExpectedReadException(Exception exception) =>
        exception is IOException or
        InvalidDataException or
        UnauthorizedAccessException or
        System.Security.SecurityException or
        FormatException or
        JsonException or
        ArgumentException or
        NotSupportedException or
        OverflowException;
}
