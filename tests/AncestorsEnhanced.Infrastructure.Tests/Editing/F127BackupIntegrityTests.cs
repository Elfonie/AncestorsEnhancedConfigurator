using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

/// <summary>F127 - FindLast only accepts intact backups; a tampered newest one falls back to an older valid candidate.</summary>
public sealed class F127BackupIntegrityTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ae-f127-{Guid.NewGuid():N}");

    [Fact]
    public void TamperedNewestBackupFallsBackToOlderValidOperation()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        SafeGameSettingsEditor editor = CreateEditor();
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);

        SettingsOperationResult applied1 = editor.Apply(
            editor.CreatePlan(snapshot, [Change("a", "r.ViewDistanceScale", "1.2")]));
        Assert.True(applied1.Succeeded, applied1.Message);
        SettingsOperationResult applied2 = editor.Apply(
            editor.CreatePlan(snapshot, [Change("b", "r.ViewDistanceScale", "1.5")]));
        Assert.True(applied2.Succeeded, applied2.Message);

        // Make the current file reflect the OLDER operation's result so that an older
        // operation is still "unchanged" and therefore the fallback target.
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.2\n");

        // Tamper with the newest operation's backup so it is no longer a valid candidate.
        string newestDirectory = Path.GetDirectoryName(applied2.ManifestPath!)!;
        File.WriteAllText(Path.Combine(newestDirectory, "Engine.ini.before"), "corrupted");

        SettingsOperationResult reverted = editor.RevertLast(snapshot);

        Assert.True(reverted.Succeeded, reverted.Message);
        // The older, intact operation was used: back to its original value.
        Assert.Contains("r.ViewDistanceScale=1.0", File.ReadAllText(engineIni), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBackupFileMakesCandidateIneligible()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        SafeGameSettingsEditor editor = CreateEditor();
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);

        SettingsOperationResult applied = editor.Apply(
            editor.CreatePlan(snapshot, [Change("a", "r.ViewDistanceScale", "1.2")]));
        Assert.True(applied.Succeeded, applied.Message);
        File.Delete(Path.Combine(Path.GetDirectoryName(applied.ManifestPath!)!, "Engine.ini.before"));

        SettingsOperationResult reverted = editor.RevertLast(snapshot);

        Assert.False(reverted.Succeeded);
        Assert.Contains("unchanged configurator operation", reverted.Message, StringComparison.Ordinal);
        Assert.Equal("[SystemSettings]\nr.ViewDistanceScale=1.2\n", File.ReadAllText(engineIni));
    }

    [Fact]
    public void ContextFingerprintFromAnotherInstallationMakesOperationIneligible()
    {
        string userData = CreateUserData();
        File.WriteAllText(EngineIniPath(userData), "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        SafeGameSettingsEditor editor = CreateEditor();
        GameInspectionSnapshot source = CreateSnapshot(userData);
        SettingsOperationResult applied = editor.Apply(
            editor.CreatePlan(source, [Change("a", "r.ViewDistanceScale", "1.2")]));
        Assert.True(applied.Succeeded, applied.Message);

        VerifiedGameContext otherInstallation = Assert.IsType<VerifiedGameContext>(
            VerifiedGameContext.TryCreateFromSnapshot(source with
            {
                Installation = source.Installation! with { InstallDirectory = "other-install" },
            }));

        Assert.Null(SettingsBackupStore.FindLast(otherInstallation));
    }

    [Fact]
    public void LinuxCaseDifferentPathsAreNotEqual()
    {
        Assert.False(SettingsBackupStore.PathEqualsForPlatform("Saved", "saved", isWindows: false));
        Assert.True(SettingsBackupStore.PathEqualsForPlatform("Saved", "saved", isWindows: true));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private string CreateUserData()
    {
        string userData = Path.Combine(_temporaryDirectory, "Saved");
        Directory.CreateDirectory(Path.Combine(userData, "Config", "WindowsNoEditor"));
        return userData;
    }

    private static string EngineIniPath(string userData) =>
        Path.Combine(userData, "Config", "WindowsNoEditor", "Engine.ini");

    private static SafeGameSettingsEditor CreateEditor() =>
        new(() => DateTimeOffset.UnixEpoch, () => false);

    private static SettingChangeRequest Change(string _, string key, string value) =>
        new("Distance", "Engine.ini", "SystemSettings", key, value);

    private static GameInspectionSnapshot CreateSnapshot(string userData) =>
        new(
            DateTimeOffset.UnixEpoch,
            new GameInstallationSnapshot(
                StoreKind.Steam,
                HostKind.Windows,
                CompatibilityLayerKind.None,
                "library",
                "install",
                AncestorsEnhanced.Core.AncestorsGameProfile.SupportedSteamBuildId,
                ExecutableExists: true,
                AncestorsEnhanced.Core.AncestorsGameProfile.SupportedContentSignature),
            userData,
            [],
            null,
            [],
            []);
}
