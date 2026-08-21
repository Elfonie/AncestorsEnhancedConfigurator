using System.Text;
using System.Text.Json.Nodes;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.SystemSave;
using AncestorsEnhanced.Infrastructure.Tests.SystemSave;

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

        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
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
        Assert.False(editor.CanRemoveToolChanges(snapshot));

        SettingsOperationResult appliedAgain = editor.Apply(editor.CreatePlan(
            snapshot,
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.3")]));
        Assert.True(appliedAgain.Succeeded, appliedAgain.Message);
    }

    [Fact]
    public void StartupRecoveryRestoresAnInterruptedConfigurationWrite()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        string captured = Path.Combine(
            Path.GetDirectoryName(engineIni)!,
            $".Engine.ini.{Guid.NewGuid():N}.cas");
        File.WriteAllBytes(captured, [1, 2, 3]);

        bool recovered = CreateEditor(gameRunning: false)
            .RecoverInterruptedChanges(CreateSnapshot(userData));

        Assert.True(recovered);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(engineIni));
        Assert.False(File.Exists(captured));
    }

    [Fact]
    public void RemoveToolChangesRestoresCapturedBaselineAndDeletesItsMarker()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        const string original = "[SystemSettings]\nr.ViewDistanceScale=1.0\n";
        File.WriteAllText(engineIni, original);
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);

        SettingsOperationResult applied = editor.Apply(editor.CreatePlan(
            snapshot,
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]));

        Assert.True(applied.Succeeded, applied.Message);
        Assert.True(editor.CanRemoveToolChanges(snapshot));
        SettingsChangePlan removal = editor.CreateRemoveToolChangesPlan(snapshot);
        Assert.True(removal.IsToolChangeRemoval);

        SettingsOperationResult removed = editor.Apply(removal);

        Assert.True(removed.Succeeded, removed.Message);
        Assert.Equal(original, File.ReadAllText(engineIni));
        Assert.False(editor.CanRemoveToolChanges(snapshot));
    }

    [Fact]
    public void UndoingRemoveToolChangesRestoresTheManagedStateAndKeepsRemovalAvailable()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        const string original = "[SystemSettings]\nr.ViewDistanceScale=1.0\n";
        File.WriteAllText(engineIni, original);
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);

        Assert.True(editor.Apply(editor.CreatePlan(
            snapshot,
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")])).Succeeded);
        Assert.True(editor.Apply(editor.CreateRemoveToolChangesPlan(snapshot)).Succeeded);
        Assert.Equal(original, File.ReadAllText(engineIni));

        SettingsOperationResult undoRemoval = editor.RevertLast(snapshot);

        Assert.True(undoRemoval.Succeeded, undoRemoval.Message);
        Assert.Contains("r.ViewDistanceScale=1.2", File.ReadAllText(engineIni), StringComparison.Ordinal);
        Assert.True(editor.CanRemoveToolChanges(snapshot));
        Assert.True(editor.Apply(editor.CreateRemoveToolChangesPlan(snapshot)).Succeeded);
        Assert.Equal(original, File.ReadAllText(engineIni));
    }

    [Fact]
    public void LegacyBaselineSurvivesRemoveUndoAndSecondRemove()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        const string original = "[SystemSettings]\nr.ViewDistanceScale=1.0\n";
        File.WriteAllText(engineIni, original);
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);

        Assert.True(editor.Apply(editor.CreatePlan(
            snapshot,
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")])).Succeeded);

        string baselineRoot = ConfigurationFileOperations.GetToolChangesRoot(userData);
        string filesRoot = Path.Combine(baselineRoot, "files");
        File.Move(
            Path.Combine(filesRoot, "0-Engine.ini.before"),
            Path.Combine(filesRoot, "000.before"));
        string manifestPath = Path.Combine(baselineRoot, "baseline.json");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["Version"] = 1;
        manifest["Files"]![0]!["BackupName"] = "000.before";
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        Assert.True(editor.Apply(editor.CreateRemoveToolChangesPlan(snapshot)).Succeeded);
        Assert.Equal(original, File.ReadAllText(engineIni));
        Assert.True(File.Exists(Path.Combine(filesRoot, "0-Engine.ini.before")));

        SettingsOperationResult undoRemoval = editor.RevertLast(snapshot);

        Assert.True(undoRemoval.Succeeded, undoRemoval.Message);
        Assert.Contains("r.ViewDistanceScale=1.2", File.ReadAllText(engineIni), StringComparison.Ordinal);
        Assert.True(editor.CanRemoveToolChanges(snapshot));
        Assert.True(editor.Apply(editor.CreateRemoveToolChangesPlan(snapshot)).Succeeded);
        Assert.Equal(original, File.ReadAllText(engineIni));
    }

    [Fact]
    public void FurtherApplyRefusesACorruptedOriginalBaselineBackup()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);
        Assert.True(editor.Apply(editor.CreatePlan(
            snapshot,
            [Change("first", "View distance", "r.ViewDistanceScale", "1.2")])).Succeeded);
        string baselineBackup = Path.Combine(
            ConfigurationFileOperations.GetToolChangesRoot(userData),
            "files",
            "0-Engine.ini.before");
        File.WriteAllText(baselineBackup, "tampered");

        SettingsOperationResult result = editor.Apply(editor.CreatePlan(
            snapshot,
            [Change("second", "View distance", "r.ViewDistanceScale", "1.3")]));

        Assert.False(result.Succeeded);
        Assert.Contains("failed validation", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("r.ViewDistanceScale=1.2", File.ReadAllText(engineIni), StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveToolChangesIsUnavailableWithoutCapturedBaseline()
    {
        string userData = CreateUserData();
        File.WriteAllText(EngineIniPath(userData), "[SystemSettings]\nr.ViewDistanceScale=1.0\n");

        Assert.False(CreateEditor(gameRunning: false).CanRemoveToolChanges(CreateSnapshot(userData)));
    }

    [Fact]
    public void RemoveToolChangesRefusesExternalEdits()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);

        SettingsOperationResult applied = editor.Apply(editor.CreatePlan(
            snapshot,
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]));
        Assert.True(applied.Succeeded, applied.Message);
        File.AppendAllText(engineIni, "; external edit\n");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => editor.CreateRemoveToolChangesPlan(snapshot));

        Assert.Contains("changed outside", error.Message, StringComparison.Ordinal);
        Assert.Contains("external edit", File.ReadAllText(engineIni), StringComparison.Ordinal);
    }

    [Fact]
    public void UndoLatestOperationKeepsBaselineForEarlierOperation()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);

        Assert.True(editor.Apply(editor.CreatePlan(snapshot,
            [Change("first", "View distance", "r.ViewDistanceScale", "1.2")])).Succeeded);
        Assert.True(editor.Apply(editor.CreatePlan(snapshot,
            [Change("second", "View distance", "r.ViewDistanceScale", "1.5")])).Succeeded);

        Assert.True(editor.RevertLast(snapshot).Succeeded);
        Assert.Contains("r.ViewDistanceScale=1.2", File.ReadAllText(engineIni), StringComparison.Ordinal);
        Assert.True(editor.CanRemoveToolChanges(snapshot));

        SettingsOperationResult appliedThird = editor.Apply(editor.CreatePlan(snapshot,
            [Change("third", "View distance", "r.ViewDistanceScale", "1.3")]));
        Assert.True(appliedThird.Succeeded, appliedThird.Message);

        Assert.True(editor.Apply(editor.CreateRemoveToolChangesPlan(snapshot)).Succeeded);
        Assert.Contains("r.ViewDistanceScale=1.0", File.ReadAllText(engineIni), StringComparison.Ordinal);
    }

    [Fact]
    public void UndoMultiFileOperationRestoresBaselineForFurtherApply()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        string gameIni = GameIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        File.WriteAllText(gameIni, "[/Script/MoviePlayer.MoviePlayerSettings]\n+StartupMovies=Intro\n");
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);

        SettingsOperationResult applied = editor.Apply(editor.CreatePlan(snapshot,
        [
            Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2"),
            new SettingChangeRequest("Skip intro", "Game.ini", "/Script/MoviePlayer.MoviePlayerSettings", "!StartupMovies", "ClearArray"),
        ]));
        Assert.True(applied.Succeeded, applied.Message);
        Assert.True(editor.RevertLast(snapshot).Succeeded);

        SettingsOperationResult reapplied = editor.Apply(editor.CreatePlan(snapshot,
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.3")]));
        Assert.True(reapplied.Succeeded, reapplied.Message);
        Assert.Contains("+StartupMovies=Intro", File.ReadAllText(gameIni), StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyEditsSystemSaveAndRevertRestoresExactOriginal()
    {
        string userData = CreateUserData();
        string saveDirectory = Directory.CreateDirectory(Path.Combine(userData, "SaveGames")).FullName;
        string systemSave = Path.Combine(saveDirectory, "System.sav");
        byte[] original = VerifiedSystemSaveFixture.Read();
        File.WriteAllBytes(systemSave, original);

        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData) with
        {
            BinarySettingsFile = new BinarySettingsFileSnapshot(
                "System.sav",
                systemSave,
                true,
                original.Length,
                DateTimeOffset.UnixEpoch,
                "Decoded and verified",
                AncestorsSystemSaveCodec.Read(original)),
        };
        SettingsChangePlan plan = editor.CreatePlan(
            snapshot,
            [
                SystemChange(
                    "game-fullscreen-resolution",
                    "Fullscreen resolution",
                    SystemSaveSettingKeys.FullscreenResolution,
                    "2560x1440"),
                SystemChange(
                    "game-shadow-preset",
                    "Shadow preset",
                    SystemSaveSettingKeys.ShadowQuality,
                    "High"),
            ]);

        Assert.Single(plan.Files);
        Assert.Equal(SettingFileTarget.SystemSave, plan.Files[0].Target);
        Assert.Equal(original, File.ReadAllBytes(systemSave));

        SettingsOperationResult applied = editor.Apply(plan);

        Assert.True(applied.Succeeded, applied.Message);
        SystemGraphicsSettingsSnapshot updated = AncestorsSystemSaveCodec.Read(File.ReadAllBytes(systemSave));
        Assert.Equal((2560, 1440), (updated.FullscreenWidth, updated.FullscreenHeight));
        Assert.Equal(GameGraphicsQuality.High, updated.ShadowQuality);
        Assert.True(editor.CanRevertLast(snapshot));

        SettingsOperationResult reverted = editor.RevertLast(snapshot);

        Assert.True(reverted.Succeeded, reverted.Message);
        Assert.Equal(original, File.ReadAllBytes(systemSave));
    }

    [Fact]
    public void ApplyRefusesWhenFileChangedAfterPlanning()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");

        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
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
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: true);
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
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);

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
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
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
    public void RevertRefusesACorruptedBackupAndKeepsTheAppliedFile()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        File.WriteAllText(engineIni, "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);
        SettingsChangePlan plan = editor.CreatePlan(
            snapshot,
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]);
        SettingsOperationResult applied = editor.Apply(plan);
        string appliedContent = File.ReadAllText(engineIni);
        string operationDirectory = Path.GetDirectoryName(applied.ManifestPath!)!;
        File.WriteAllText(Path.Combine(operationDirectory, "Engine.ini.before"), "corrupted");

        SettingsOperationResult result = editor.RevertLast(snapshot);

        Assert.False(result.Succeeded);
        // F127: a tampered backup is detected up-front by FindLast, so the only (and
        // now ineligible) operation cannot be restored and nothing is changed.
        Assert.Contains("unchanged configurator operation", result.Message, StringComparison.Ordinal);
        Assert.Equal(appliedContent, File.ReadAllText(engineIni));
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
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
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
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
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

    [Fact]
    public void ApplyAcceptsAnIssuedPlanOnlyOnce()
    {
        string userData = CreateUserData();
        File.WriteAllText(EngineIniPath(userData), "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        SettingsChangePlan plan = editor.CreatePlan(
            CreateSnapshot(userData),
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]);

        Assert.True(editor.Apply(plan).Succeeded);
        SettingsOperationResult replay = editor.Apply(plan);

        Assert.False(replay.Succeeded);
        Assert.Contains("already been used", replay.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatingANewPlanInvalidatesThePreviousPlan()
    {
        string userData = CreateUserData();
        File.WriteAllText(EngineIniPath(userData), "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);
        SettingsChangePlan oldPlan = editor.CreatePlan(
            snapshot,
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]);
        SettingsChangePlan currentPlan = editor.CreatePlan(
            snapshot,
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.5")]);

        Assert.False(editor.Apply(oldPlan).Succeeded);
        Assert.True(editor.Apply(currentPlan).Succeeded);
        Assert.Contains("r.ViewDistanceScale=1.5", File.ReadAllText(EngineIniPath(userData)));
    }

    [Fact]
    public void ApplyRejectsAModifiedIssuedPlan()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        const string Original = "[SystemSettings]\nr.ViewDistanceScale=1.0\n";
        File.WriteAllText(engineIni, Original);
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        SettingsChangePlan plan = editor.CreatePlan(
            CreateSnapshot(userData),
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]);
        plan.Files[0].UpdatedContent[0] ^= 1;

        SettingsOperationResult result = editor.Apply(plan);

        Assert.False(result.Succeeded);
        Assert.Contains("modified", result.Message, StringComparison.Ordinal);
        Assert.Equal(Original, File.ReadAllText(engineIni));
    }

    [Fact]
    public void DiscardedPlanCannotBeApplied()
    {
        string userData = CreateUserData();
        File.WriteAllText(EngineIniPath(userData), "[SystemSettings]\nr.ViewDistanceScale=1.0\n");
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        SettingsChangePlan plan = editor.CreatePlan(
            CreateSnapshot(userData),
            [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]);

        editor.DiscardPlan(plan);

        Assert.False(editor.Apply(plan).Succeeded);
    }

    [Fact]
    public void ApplyAndRevertTreatEngineAndGameIniAsOneOperation()
    {
        string userData = CreateUserData();
        string engineIni = EngineIniPath(userData);
        string gameIni = GameIniPath(userData);
        const string OriginalEngine = "; engine\r\n[SystemSettings]\r\nr.ViewDistanceScale=1.0\r\n";
        const string OriginalGame = "; game\r\n[/Script/MoviePlayer.MoviePlayerSettings]\r\nKeepThis=True\r\n";
        File.WriteAllText(engineIni, OriginalEngine, new UTF8Encoding(false));
        File.WriteAllText(gameIni, OriginalGame, new UTF8Encoding(false));
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);
        SettingsChangePlan plan = editor.CreatePlan(
            snapshot,
            [
                Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2"),
                new SettingChangeRequest(
                    "Startup splash videos",
                    "Game.ini",
                    "/Script/MoviePlayer.MoviePlayerSettings",
                    "!StartupMovies",
                    "ClearArray"),
            ]);

        SettingsOperationResult applied = editor.Apply(plan);

        Assert.True(applied.Succeeded, applied.Message);
        Assert.Contains("r.ViewDistanceScale=1.2", File.ReadAllText(engineIni));
        Assert.Contains("!StartupMovies=ClearArray", File.ReadAllText(gameIni));
        Assert.True(editor.RevertLast(snapshot).Succeeded);
        Assert.Equal(OriginalEngine, File.ReadAllText(engineIni));
        Assert.Equal(OriginalGame, File.ReadAllText(gameIni));
    }

    [Fact]
    public void ApplyCanRemoveTheNoIntroOverrideWithoutChangingOtherGameIniValues()
    {
        string userData = CreateUserData();
        string gameIni = GameIniPath(userData);
        File.WriteAllText(
            gameIni,
            "; keep\n[/Script/MoviePlayer.MoviePlayerSettings]\n!StartupMovies=ClearArray\nKeepThis=True\n");
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        SettingsChangePlan plan = editor.CreatePlan(
            CreateSnapshot(userData),
            [
                new SettingChangeRequest(
                    "Startup splash videos",
                    "Game.ini",
                    "/Script/MoviePlayer.MoviePlayerSettings",
                    "!StartupMovies",
                    null),
            ]);

        Assert.True(editor.Apply(plan).Succeeded);
        string result = File.ReadAllText(gameIni);
        Assert.DoesNotContain("!StartupMovies", result, StringComparison.Ordinal);
        Assert.DoesNotContain("bWaitForMoviesToComplete", result, StringComparison.Ordinal);
        Assert.Contains("; keep", result, StringComparison.Ordinal);
        Assert.Contains("KeepThis=True", result, StringComparison.Ordinal);
    }

    [Fact]
    public void NoIntroIsWrittenAndRemovedAsOneSetting()
    {
        string userData = CreateUserData();
        string gameIni = GameIniPath(userData);
        File.WriteAllText(gameIni, "; keep\n");
        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);

        SettingsChangePlan enable = editor.CreatePlan(
            snapshot,
            [
                new SettingChangeRequest(
                    "Startup splash videos",
                    "Game.ini",
                    "/Script/MoviePlayer.MoviePlayerSettings",
                    "!StartupMovies",
                    "ClearArray"),
            ]);
        Assert.True(editor.Apply(enable).Succeeded);
        string enabled = File.ReadAllText(gameIni);
        Assert.Contains("!StartupMovies=ClearArray", enabled, StringComparison.Ordinal);
        Assert.Contains("bWaitForMoviesToComplete=False", enabled, StringComparison.Ordinal);

        snapshot = snapshot with
        {
            ConfigurationFiles =
            [
                new ConfigurationFileSnapshot(
                    "Game.ini",
                    gameIni,
                    true,
                    new FileInfo(gameIni).Length,
                    DateTimeOffset.UnixEpoch,
                    [],
                    null),
            ],
        };
        SettingsChangePlan disable = editor.CreatePlan(
            snapshot,
            [
                new SettingChangeRequest(
                    "Startup splash videos",
                    "Game.ini",
                    "/Script/MoviePlayer.MoviePlayerSettings",
                    "!StartupMovies",
                    null),
            ]);
        Assert.True(editor.Apply(disable).Succeeded);
        string disabled = File.ReadAllText(gameIni);
        Assert.DoesNotContain("!StartupMovies", disabled, StringComparison.Ordinal);
        Assert.DoesNotContain("bWaitForMoviesToComplete", disabled, StringComparison.Ordinal);
        Assert.Contains("; keep", disabled, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StoreKind.Steam, HostKind.Windows, CompatibilityLayerKind.None, false)]
    public void CreatePlanRejectsUnverifiedTargets(
        StoreKind store,
        HostKind host,
        CompatibilityLayerKind compatibilityLayer,
        bool executableExists)
    {
        string userData = CreateUserData();
        GameInspectionSnapshot valid = CreateSnapshot(userData);
        GameInspectionSnapshot unsupported = valid with
        {
            Installation = valid.Installation! with
            {
                Store = store,
                Host = host,
                CompatibilityLayer = compatibilityLayer,
                ExecutableExists = executableExists,
            },
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEditor(gameRunning: false).CreatePlan(
                unsupported,
                [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]));

        Assert.Contains("supported Ancestors installation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionPathGateRejectsAnUnexpectedUserDataDirectory()
    {
        string userData = CreateUserData();
        var editor = new SafeGameSettingsEditor(
            () => DateTimeOffset.UnixEpoch,
            () => false,
            path => path == "expected-native-path");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            editor.CreatePlan(
                CreateSnapshot(userData),
                [Change("view-distance", "View distance", "r.ViewDistanceScale", "1.2")]));

        Assert.Contains("not a supported Ancestors location", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void RevertRestoresPakCreationAndRemoval(bool existed, bool resultExists)
    {
        string userData = CreateUserData();
        string install = Path.Combine(_temporaryDirectory, "install");
        string pakDirectory = Directory.CreateDirectory(Path.Combine(
            install, "Ancestors", "Content", "Paks")).FullName;
        string pakPath = Path.Combine(pakDirectory, "AncestorsEnhanced-Vignette_P.pak");
        byte[] original = existed ? [1, 2, 3] : [];
        byte[] updated = resultExists ? [4, 5, 6] : [];
        if (existed)
        {
            File.WriteAllBytes(pakPath, original);
        }

        DateTimeOffset created = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var plan = new SettingsChangePlan(
            "20260801-120000-000-test",
            created,
            "5495393",
            userData,
            [new SettingChangePreview("Vignette", Path.GetFileName(pakPath), "mod.VignettePercent", "50", "35")],
            [new ConfigurationFileChangePlan(
                Path.GetFileName(pakPath),
                pakPath,
                existed,
                ConfigurationFileOperations.Sha256(original),
                original,
                updated,
                SettingFileTarget.Pak,
                resultExists)],
            install);
        string operation = SettingsBackupStore.Prepare(plan);
        if (resultExists)
        {
            File.WriteAllBytes(pakPath, updated);
        }
        else
        {
            File.Delete(pakPath);
        }

        SettingsBackupStore.MarkApplied(operation, created);
        GameInspectionSnapshot snapshot = CreateSnapshot(userData);
        snapshot = snapshot with
        {
            Installation = snapshot.Installation! with { InstallDirectory = install },
        };

        SettingsOperationResult result = CreateEditor(gameRunning: false).RevertLast(snapshot);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(existed, File.Exists(pakPath));
        if (existed)
        {
            Assert.Equal(original, File.ReadAllBytes(pakPath));
        }
    }

    [Fact]
    public void ApplyFailureRollsBackADeletedFile()
    {
        string userData = CreateUserData();
        string install = Path.Combine(_temporaryDirectory, "install-apply");
        string pakDirectory = Directory.CreateDirectory(Path.Combine(
            install, "Ancestors", "Content", "Paks")).FullName;
        string vignettePath = Path.Combine(pakDirectory, "AncestorsEnhanced-Vignette_P.pak");
        byte[] original = [1, 2, 3];
        File.WriteAllBytes(vignettePath, original);

        // Occupying the second target with a directory makes its write fail, forcing the
        // apply to roll back after the vignette file was already deleted (F066).
        string blockerPath = Path.Combine(pakDirectory, "pakchunk99-WindowsNoEditor_P.pak");
        Directory.CreateDirectory(blockerPath);

        DateTimeOffset created = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var plan = new SettingsChangePlan(
            "apply-del",
            created,
            "5495393",
            userData,
            [
                new SettingChangePreview("Vignette", "AncestorsEnhanced-Vignette_P.pak", "mod.VignettePercent", "50", "35"),
                new SettingChangePreview("Vignette", "pakchunk99-WindowsNoEditor_P.pak", "mod.VignettePercent", "50", "35"),
            ],
            [
                new ConfigurationFileChangePlan(
                    "AncestorsEnhanced-Vignette_P.pak",
                    vignettePath,
                    true,
                    ConfigurationFileOperations.Sha256(original),
                    original,
                    [],
                    SettingFileTarget.Pak,
                    false),
                new ConfigurationFileChangePlan(
                    "pakchunk99-WindowsNoEditor_P.pak",
                    blockerPath,
                    false,
                    ConfigurationFileOperations.Sha256([]),
                    [4],
                    [5, 6],
                    SettingFileTarget.Pak,
                    true),
            ],
            install);

        SafeGameSettingsEditor editor = CreateEditor(gameRunning: false);
        SettingsOperationResult result = editor.Apply(plan);

        Assert.False(result.Succeeded, result.Message);
        // The apply must fail in the write/rollback phase, not in the pre-check;
        // otherwise the deleted vignette file was never touched and the rollback
        // path is not exercised (NEW-IMP-TEST-02).
        Assert.DoesNotContain("changed after the preview", result.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(vignettePath));
        Assert.Equal(original, File.ReadAllBytes(vignettePath));
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

    private static string GameIniPath(string userData) =>
        Path.Combine(userData, "Config", "WindowsNoEditor", "Game.ini");

    private static SafeGameSettingsEditor CreateEditor(bool gameRunning) =>
        new(
            () => new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            () => gameRunning);

    private static SettingChangeRequest Change(
        string _,
        string name,
        string key,
        string? value) =>
        new(name, "Engine.ini", "SystemSettings", key, value);

    private static SettingChangeRequest SystemChange(
        string _,
        string name,
        string key,
        string value) =>
        new(name, "System.sav", "GraphicsOptions", key, value);

    private static GameInspectionSnapshot CreateSnapshot(string userData) =>
        new(
            DateTimeOffset.UnixEpoch,
            new GameInstallationSnapshot(
                StoreKind.Steam,
                HostKind.Windows,
                CompatibilityLayerKind.None,
                "library",
                "install",
                "5495393",
                ExecutableExists: true),
            userData,
            [],
            null,
            [],
            []);
}
