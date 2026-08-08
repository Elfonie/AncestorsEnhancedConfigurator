using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Environment;
using AncestorsEnhanced.Infrastructure.FileSystem;
using AncestorsEnhanced.Infrastructure.Inspection;

namespace AncestorsEnhanced.Infrastructure.Tests.Inspection;

public sealed class ReadOnlyAncestorsInspectorTests
{
    [Fact]
    public void InspectFindsSteamLibrarySettingsAndPakMetadata()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string steamRoot = temporaryDirectory.CreateDirectory("Steam");
        string libraryRoot = temporaryDirectory.CreateDirectory("Library");
        string localAppData = temporaryDirectory.CreateDirectory("LocalAppData");
        CreateSteamLibraryList(steamRoot, libraryRoot);
        CreateValidInstallation(libraryRoot);
        CreateUserConfiguration(localAppData);

        ReadOnlyAncestorsInspector inspector = new(
            new PhysicalReadOnlyFileSystem(),
            new TestHostEnvironment(steamRoot, localAppData));

        GameInspectionSnapshot snapshot = inspector.Inspect();

        Assert.True(snapshot.IsGameDetected);
        Assert.False(snapshot.HasErrors);
        Assert.Equal("5495393", snapshot.Installation?.BuildId);
        Assert.True(snapshot.Installation?.ExecutableExists);
        ConfigurationFileSnapshot engine = Assert.Single(snapshot.ConfigurationFiles);
        Assert.Equal("Engine.ini", engine.Name);
        Assert.Contains(engine.Settings, setting =>
            setting.Section == "SystemSettings" &&
            setting.Key == "r.MaxAnisotropy" &&
            setting.Value == "16");
        Assert.True(snapshot.BinarySettingsFile?.Exists);
        Assert.Contains("Could not decode", snapshot.BinarySettingsFile?.FormatStatus, StringComparison.Ordinal);
        Assert.Null(snapshot.BinarySettingsFile?.GraphicsSettings);
        Assert.Equal(2, snapshot.PakFiles.Count);
        Assert.Contains(snapshot.PakFiles, pak => pak.Classification == PakClassification.BaseGame);
        Assert.Single(
            snapshot.PakFiles,
            pak => pak.Classification == PakClassification.PatchStyle);
    }

    [Fact]
    public void InspectRejectsManifestDirectoryTraversal()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string steamRoot = temporaryDirectory.CreateDirectory("Steam");
        string localAppData = temporaryDirectory.CreateDirectory("LocalAppData");
        string steamApps = Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps")).FullName;
        File.WriteAllText(
            Path.Combine(steamApps, "appmanifest_536270.acf"),
            """
            "AppState"
            {
                "appid" "536270"
                "installdir" "..\\Outside"
                "buildid" "5495393"
            }
            """);

        ReadOnlyAncestorsInspector inspector = new(
            new PhysicalReadOnlyFileSystem(),
            new TestHostEnvironment(steamRoot, localAppData));

        GameInspectionSnapshot snapshot = inspector.Inspect();

        Assert.False(snapshot.IsGameDetected);
        Assert.Contains(snapshot.Notices, notice => notice.Code == "steam.manifest-invalid");
    }
    [Theory]
    [InlineData("../outside/file")]
    [InlineData("..\\outside\\file")]
    [InlineData("folder/../../outside/file")]
    [InlineData("folder\\..\\..\\outside\\file")]
    [InlineData("/absolute/unix/path")]
    [InlineData("C:\\absolute\\windows\\path")]
    [InlineData("C:/absolute/windows/path")]
    [InlineData("\\\\server\\share\\file")]
    [InlineData("//server/share/file")]
    [InlineData("Ancestors/outside")]
    [InlineData("Ancestors\\outside")]
    public void InspectRejectsManifestDirectoryTraversalHostIndependent(string installName)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string steamRoot = temporaryDirectory.CreateDirectory("Steam");
        string localAppData = temporaryDirectory.CreateDirectory("LocalAppData");
        string steamApps = Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps")).FullName;
        string escaped = installName.Replace("\\", "\\\\", StringComparison.Ordinal);
        string manifest = "\"AppState\"\n"
            + "{\n"
            + "  \"appid\" \"536270\"\n"
            + "  \"installdir\" \"" + escaped + "\"\n"
            + "  \"buildid\" \"5495393\"\n"
            + "}\n";
        File.WriteAllText(
            Path.Combine(steamApps, "appmanifest_536270.acf"),
            manifest);

        ReadOnlyAncestorsInspector inspector = new(
            new PhysicalReadOnlyFileSystem(),
            new TestHostEnvironment(steamRoot, localAppData));

        GameInspectionSnapshot snapshot = inspector.Inspect();

        Assert.False(snapshot.IsGameDetected);
        Assert.Contains(snapshot.Notices, notice => notice.Code == "steam.manifest-invalid");
    }

    [Fact]
    public void InspectFindsEpicManifest()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string install = CreateStoreInstallation(temporaryDirectory.CreateDirectory("EpicGame"));
        string manifests = temporaryDirectory.CreateDirectory("EpicManifests");
        string jsonPath = install.Replace("\\", "\\\\", StringComparison.Ordinal);
        File.WriteAllText(
            Path.Combine(manifests, "ancestor.item"),
            $$"""
            {
              "DisplayName": "Ancestors The Humankind Odyssey",
              "InstallLocation": "{{jsonPath}}",
              "BuildVersion": "epic build"
            }
            """);

        ReadOnlyAncestorsInspector inspector = new(
            new PhysicalReadOnlyFileSystem(),
            new TestHostEnvironment(
                [],
                temporaryDirectory.CreateDirectory("Local"),
                EpicManifests: [manifests]));

        GameInstallationSnapshot installation = Assert.IsType<GameInstallationSnapshot>(
            inspector.Inspect().Installation);
        Assert.Equal(StoreKind.EpicGames, installation.Store);
        Assert.Equal(install, installation.InstallDirectory);
    }

    [Fact]
    public void InspectFindsGogCandidate()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string install = CreateStoreInstallation(temporaryDirectory.CreateDirectory("GogGame"));
        ReadOnlyAncestorsInspector inspector = new(
            new PhysicalReadOnlyFileSystem(),
            new TestHostEnvironment(
                [],
                temporaryDirectory.CreateDirectory("Local"),
                GogCandidates: [install]));

        Assert.Equal(StoreKind.Gog, inspector.Inspect().Installation?.Store);
    }

    [Fact]
    public void InspectFindsProtonUserData()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string steamRoot = temporaryDirectory.CreateDirectory("Steam");
        CreateValidInstallation(steamRoot);
        string saved = Directory.CreateDirectory(Path.Combine(
            steamRoot,
            "steamapps", "compatdata", "536270", "pfx", "drive_c", "users", "steamuser",
            "AppData", "Local", "Ancestors", "Saved")).FullName;
        ReadOnlyAncestorsInspector inspector = new(
            new PhysicalReadOnlyFileSystem(),
            new TestHostEnvironment([steamRoot], null, HostKind.Linux));

        GameInspectionSnapshot snapshot = inspector.Inspect();

        Assert.Equal(CompatibilityLayerKind.Proton, snapshot.Installation?.CompatibilityLayer);
        Assert.Equal(saved, snapshot.UserDataDirectory);
    }

    [Fact]
    public void InspectRejectsAmbiguousProtonUserData()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string steamRoot = temporaryDirectory.CreateDirectory("Steam");
        CreateValidInstallation(steamRoot);
        CreateProtonSaved(steamRoot, "userA");
        CreateProtonSaved(steamRoot, "userB");

        GameInspectionSnapshot snapshot = new ReadOnlyAncestorsInspector(
            new PhysicalReadOnlyFileSystem(), new TestHostEnvironment([steamRoot], null, HostKind.Linux)).Inspect();

        Assert.Null(snapshot.UserDataDirectory);
        Assert.Contains(snapshot.Notices, notice => notice.Code == "userdata.ambiguous-proton-user");
    }

    [Fact]
    public void InspectReturnsNoProtonUserDataWhenNoWineUserHasSaved()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string steamRoot = temporaryDirectory.CreateDirectory("Steam");
        CreateValidInstallation(steamRoot);
        Directory.CreateDirectory(Path.Combine(
            steamRoot, "steamapps", "compatdata", "536270", "pfx", "drive_c", "users", "userA"));

        GameInspectionSnapshot snapshot = new ReadOnlyAncestorsInspector(
            new PhysicalReadOnlyFileSystem(), new TestHostEnvironment([steamRoot], null, HostKind.Linux)).Inspect();

        Assert.Null(snapshot.UserDataDirectory);
        Assert.Contains(snapshot.Notices, notice => notice.Code == "userdata.not-found");
    }

    [Fact]
    public void InspectIgnoresWineUsersWithoutAncestorsSaved()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string steamRoot = temporaryDirectory.CreateDirectory("Steam");
        CreateValidInstallation(steamRoot);
        string saved = CreateProtonSaved(steamRoot, "userA");
        Directory.CreateDirectory(Path.Combine(
            steamRoot, "steamapps", "compatdata", "536270", "pfx", "drive_c", "users", "userB"));

        GameInspectionSnapshot snapshot = new ReadOnlyAncestorsInspector(
            new PhysicalReadOnlyFileSystem(), new TestHostEnvironment([steamRoot], null, HostKind.Linux)).Inspect();

        Assert.Equal(saved, snapshot.UserDataDirectory);
    }

    [Fact]
    public void InspectReturnsNoWindowsUserDataBeforeTheGameCreatesSaved()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string steamRoot = temporaryDirectory.CreateDirectory("Steam");
        string localAppData = temporaryDirectory.CreateDirectory("Local");
        CreateValidInstallation(steamRoot);

        GameInspectionSnapshot snapshot = new ReadOnlyAncestorsInspector(
            new PhysicalReadOnlyFileSystem(), new TestHostEnvironment(steamRoot, localAppData)).Inspect();

        Assert.Null(snapshot.UserDataDirectory);
        Assert.Contains(snapshot.Notices, notice => notice.Code == "userdata.not-found");
    }

    private static string CreateProtonSaved(string steamRoot, string user) => Directory.CreateDirectory(Path.Combine(
        steamRoot, "steamapps", "compatdata", "536270", "pfx", "drive_c", "users", user,
        "AppData", "Local", "Ancestors", "Saved")).FullName;

    private static void CreateSteamLibraryList(string steamRoot, string libraryRoot)
    {
        string steamApps = Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps")).FullName;
        string escapedLibraryPath = libraryRoot.Replace("\\", "\\\\", StringComparison.Ordinal);
        File.WriteAllText(
            Path.Combine(steamApps, "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path" "{{escapedLibraryPath}}"
                    "apps"
                    {
                        "536270" "9373929711"
                    }
                }
            }
            """);
    }

    private static void CreateValidInstallation(string libraryRoot)
    {
        string steamApps = Directory.CreateDirectory(Path.Combine(libraryRoot, "steamapps")).FullName;
        File.WriteAllText(
            Path.Combine(steamApps, "appmanifest_536270.acf"),
            """
            "AppState"
            {
                "appid" "536270"
                "installdir" "Ancestors The Humankind Odyssey"
                "buildid" "5495393"
            }
            """);

        string gameRoot = Path.Combine(
            steamApps,
            "common",
            "Ancestors The Humankind Odyssey");
        string binaryDirectory = Directory.CreateDirectory(Path.Combine(
            gameRoot,
            "Ancestors",
            "Binaries",
            "Win64")).FullName;
        File.WriteAllBytes(Path.Combine(binaryDirectory, "Ancestors-Win64-Shipping.exe"), []);

        string pakDirectory = Directory.CreateDirectory(Path.Combine(
            gameRoot,
            "Ancestors",
            "Content",
            "Paks")).FullName;
        File.WriteAllBytes(Path.Combine(pakDirectory, "Ancestors-WindowsNoEditor.pak"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(pakDirectory, "pakchunk99-WindowsNoEditor_P.pak"), [4, 5]);
    }

    private static void CreateUserConfiguration(string localAppData)
    {
        string configurationDirectory = Directory.CreateDirectory(Path.Combine(
            localAppData,
            "Ancestors",
            "Saved",
            "Config",
            "WindowsNoEditor")).FullName;
        File.WriteAllText(
            Path.Combine(configurationDirectory, "Engine.ini"),
            """
            [SystemSettings]
            r.MaxAnisotropy=16
            """);

        string saveDirectory = Directory.CreateDirectory(Path.Combine(
            localAppData,
            "Ancestors",
            "Saved",
            "SaveGames")).FullName;
        File.WriteAllBytes(Path.Combine(saveDirectory, "System.sav"), [1, 2, 3, 4]);
    }

    private static string CreateStoreInstallation(string gameRoot)
    {
        string binaryDirectory = Directory.CreateDirectory(Path.Combine(
            gameRoot,
            "Ancestors",
            "Binaries",
            "Win64")).FullName;
        File.WriteAllBytes(Path.Combine(binaryDirectory, "Ancestors-Win64-Shipping.exe"), []);
        Directory.CreateDirectory(Path.Combine(gameRoot, "Ancestors", "Content", "Paks"));
        return gameRoot;
    }

    private sealed class TestHostEnvironment(
        IReadOnlyList<string> SteamRoots,
        string? LocalData,
        HostKind CurrentHost = HostKind.Windows,
        IReadOnlyList<string>? EpicManifests = null,
        IReadOnlyList<string>? GogCandidates = null,
        IReadOnlyList<string>? HeroicConfigs = null) : IHostEnvironment
    {
        public TestHostEnvironment(string steamRoot, string localData)
            : this([steamRoot], localData)
        {
        }

        public HostKind Host => CurrentHost;

        public string? LocalApplicationDataPath => LocalData;

        public DateTimeOffset UtcNow { get; } = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        public IReadOnlyList<string> GetSteamRootCandidates() => SteamRoots;

        public IReadOnlyList<string> GetEpicManifestDirectories() => EpicManifests ?? [];

        public IReadOnlyList<string> GetGogInstallCandidates() => GogCandidates ?? [];

        public IReadOnlyList<string> GetHeroicConfigDirectories() => HeroicConfigs ?? [];
    }




    [Fact]
    public void InspectFindsHeroicGameConfigOnLinux()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string install = CreateStoreInstallation(temporaryDirectory.CreateDirectory("HeroicGog"));
        string heroic = temporaryDirectory.CreateDirectory("Heroic");
        string configs = Directory.CreateDirectory(Path.Combine(heroic, "games_config")).FullName;
        string escaped = install.Replace("\\", "\\\\", StringComparison.Ordinal);
        File.WriteAllText(
            Path.Combine(configs, "ancestors.json"),
            $$"""
            {
              "title": "Ancestors The Humankind Odyssey",
              "install_path": "{{escaped}}"
            }
            """);

        ReadOnlyAncestorsInspector inspector = new(
            new PhysicalReadOnlyFileSystem(),
            new TestHostEnvironment([], null, HostKind.Linux, HeroicConfigs: [heroic]));

        GameInstallationSnapshot installation = Assert.IsType<GameInstallationSnapshot>(
            inspector.Inspect().Installation);
        Assert.Equal(HostKind.Linux, installation.Host);
        Assert.Equal(CompatibilityLayerKind.Proton, installation.CompatibilityLayer);
        Assert.Equal(install, installation.InstallDirectory);
    }

    [Fact]
    public void InspectFindsHeroicLegendaryEpicInstallOnLinux()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string install = CreateStoreInstallation(temporaryDirectory.CreateDirectory("HeroicEpic"));
        string heroic = temporaryDirectory.CreateDirectory("Heroic");
        string legendary = Directory.CreateDirectory(Path.Combine(heroic, "legendary")).FullName;
        string escaped = install.Replace("\\", "\\\\", StringComparison.Ordinal);
        File.WriteAllText(
            Path.Combine(legendary, "installed.json"),
            $$"""
            [
              {
                "title": "Ancestors The Humankind Odyssey",
                "install_path": "{{escaped}}",
                "version": "heroic epic build"
              }
            ]
            """);

        ReadOnlyAncestorsInspector inspector = new(
            new PhysicalReadOnlyFileSystem(),
            new TestHostEnvironment([], null, HostKind.Linux, HeroicConfigs: [heroic]));

        GameInstallationSnapshot installation = Assert.IsType<GameInstallationSnapshot>(
            inspector.Inspect().Installation);
        Assert.Equal(install, installation.InstallDirectory);
        Assert.Equal(CompatibilityLayerKind.Proton, installation.CompatibilityLayer);
    }    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            FullPath = Path.Combine(Path.GetTempPath(), $"AncestorsEnhanced-{Guid.NewGuid():N}");
            Directory.CreateDirectory(FullPath);
        }

        public string FullPath { get; }

        public string CreateDirectory(string name) =>
            Directory.CreateDirectory(Path.Combine(FullPath, name)).FullName;

        public void Dispose()
        {
            Directory.Delete(FullPath, recursive: true);
            GC.SuppressFinalize(this);
        }
    }
}
