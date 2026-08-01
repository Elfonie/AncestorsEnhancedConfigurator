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
        Assert.Contains("not decoded", snapshot.BinarySettingsFile?.FormatStatus, StringComparison.Ordinal);
        Assert.Equal(2, snapshot.PakFiles.Count);
        Assert.Contains(snapshot.PakFiles, pak => pak.Classification == PakClassification.BaseGame);
        PakFileSnapshot patch = Assert.Single(
            snapshot.PakFiles,
            pak => pak.Classification == PakClassification.PatchStyle);
        Assert.NotNull(patch.Sha256);
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

    private sealed class TestHostEnvironment(
        string steamRoot,
        string localApplicationDataPath) : IHostEnvironment
    {
        public bool IsWindows => true;

        public string? LocalApplicationDataPath => localApplicationDataPath;

        public DateTimeOffset UtcNow { get; } = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        public IReadOnlyList<string> GetSteamRootCandidates() => [steamRoot];
    }

    private sealed class TemporaryDirectory : IDisposable
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
