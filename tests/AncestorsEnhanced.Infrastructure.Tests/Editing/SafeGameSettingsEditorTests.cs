using System.Text;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

public sealed class SafeGameSettingsEditorTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ancestors-enhanced-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ApplyCreatesBackupAndRevertRestoresExactOriginal()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        const string Original =
            "; user comment\r\n" +
            "[SystemSettings]\r\n" +
            "r.ViewDistanceScale=1.0\r\n" +
            "KeepThis=42\r\n";
        File.WriteAllText(engineIni, Original, new UTF8Encoding(false));

        var editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);
        SettingsChangePlan plan = editor.CreatePlan(
            snapshot,
            [
                Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2"),
                Change("anisotropy", "Texture filtering", "r.MaxAnisotropy", "16"),
            ]);

        SettingsOperationResult applied = editor.Apply(plan);

        Assert.True(applied.Succeeded, applied.Message);
        Assert.Contains("; user comment", File.ReadAllText(engineIni), StringComparison.Ordinal);
        Assert.Contains("KeepThis=42", File.ReadAllText(engineIni), StringComparison.Ordinal);
        Assert.Contains("r.ViewDistanceScale=1.2", File.ReadAllText(engineIni), StringComparison.Ordinal);
        Assert.Contains("r.MaxAnisotropy=16", File.ReadAllText(engineIni), StringComparison.Ordinal);
        Assert.True(File.Exists(applied.ManifestPath));
        Assert.True(editor.CanRevertLast(snapshot));

        SettingsOperationResult reverted = editor.RevertLast(snapshot);

        Assert.True(reverted.Succeeded, reverted.Message);
        Assert.Equal(Original, File.ReadAllText(engineIni));
        Assert.False(editor.CanRevertLast(snapshot));
    }

    [Fact]
    public void ApplyRefusesWhenFileChangedAfterPlanning()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");

        var editor = CreateEditor(gameRunning: false);
        SettingsChangePlan plan = editor.CreatePlan(
            CreateSnapshot(userData),
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]);
        File.AppendAllText(engineIni, "; changed elsewhere\n");

        SettingsOperationResult result = editor.Apply(plan);

        Assert.False(result.Succeeded);
        Assert.Contains("changed after the preview", result.Message, StringComparison.Ordinal);
        Assert.Contains("changed elsewhere", File.ReadAllText(engineIni), StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyRefusesWhileGameIsRunning()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        var editor = CreateEditor(gameRunning: true);
        SettingsChangePlan plan = editor.CreatePlan(
            CreateSnapshot(userData),
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]);

        SettingsOperationResult result = editor.Apply(plan);

        Assert.False(result.Succeeded);
        Assert.Equal("[SystemSettings]\nr.ViewDistanceScale=1.0\n", File.ReadAllText(engineIni));
    }

    [Fact]
    public void CreatePlanDoesNotCreateMissingConfigurationDirectory()
    {
        string userData = Path.Combine(_temporaryDirectory, "Saved");
        Directory.CreateDirectory(userData);
        string configDirectory = Path.Combine(userData, "Config", "WindowsNoEditor");
        var editor = CreateEditor(gameRunning: false);

        SettingsChangePlan plan = editor.CreatePlan(
            CreateSnapshot(userData),
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]);

        Assert.False(Directory.Exists(configDirectory));
        Assert.True(editor.Apply(plan).Succeeded);
        Assert.True(Directory.Exists(configDirectory));
    }

    [Fact]
    public void RevertRefusesAfterAnotherProgramChangedTheResult()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        var editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);
        SettingsChangePlan plan = editor.CreatePlan(
            snapshot,
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]);
        Assert.True(editor.Apply(plan).Succeeded);
        File.AppendAllText(engineIni, "; external change\n");

        SettingsOperationResult result = editor.RevertLast(snapshot);

        Assert.False(editor.CanRevertLast(snapshot));
        Assert.False(result.Succeeded);
        Assert.Contains("external change", File.ReadAllText(engineIni), StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyPreservesUtf16Encoding()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(
            engineIni,
            "[SystemSettings]\r\nr.ViewDistanceScale=1.0\r\n",
            Encoding.Unicode);
        var editor = CreateEditor(gameRunning: false);
        SettingsChangePlan plan = editor.CreatePlan(
            CreateSnapshot(userData),
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]);

        Assert.True(editor.Apply(plan).Succeeded);

        byte[] result = File.ReadAllBytes(engineIni);
        Assert.True(result.AsSpan().StartsWith(Encoding.Unicode.Preamble));
        Assert.Contains("r.ViewDistanceScale=1.2", Encoding.Unicode.GetString(result), StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePlanRejectsOutOfRangeAndUnknownSettings()
    {
        string userData = CreateUserData();
        var editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);

        InvalidOperationException outOfRange = Assert.Throws<InvalidOperationException>(() =>
            editor.CreatePlan(
                snapshot,
                [Change("view-distance", "View distance", "r.ViewDistanceScale", "9")]));
        InvalidOperationException unknown = Assert.Throws<InvalidOperationException>(() =>
            editor.CreatePlan(
                snapshot,
                [Change("unknown", "Unknown", "r.UnsafeUnknown", "1")]));

        Assert.Contains("not a valid value", outOfRange.Message, StringComparison.Ordinal);
        Assert.Contains("not editable", unknown.Message, StringComparison.Ordinal);
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

    private static SafeGameSettingsEditor CreateEditor(bool gameRunning) =>
        new(
            () => new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            () => gameRunning);

    private static SettingChangeRequest Change(
        string id,
        string name,
        string key,
        string? value) =>
        new(id, name, "Engine.ini", "SystemSettings", key, value);

    private static GameInspectionSnapshot CreateSnapshot(string userData) =>
        new(
            DateTimeOffset.UnixEpoch,
            new GameInstallationSnapshot(
                StoreKind.Steam,
                HostKind.Windows,
                CompatibilityLayerKind.None,
                "store",
                "library",
                "install",
                "Ancestors-Win64-Shipping.exe",
                "5495393",
                ExecutableExists: true),
            userData,
            [],
            null,
            [],
            []);
}
