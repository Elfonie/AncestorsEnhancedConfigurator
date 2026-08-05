using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task InspectionStartsAfterConstruction()
    {
        var inspector = new CountingInspector(CreateSnapshot());
        var viewModel = new MainViewModel(inspector, new RecordingEditor());

        Assert.Equal(0, inspector.Count);
        Assert.Equal("Not checked yet", viewModel.DetectionStatus);

        await viewModel.InitializeAsync();

        Assert.Equal(1, inspector.Count);
        Assert.Equal("Ancestors is ready", viewModel.DetectionStatus);
        Assert.Equal("Configuration loaded. No files were changed.", viewModel.OperationMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task InspectionFailureProducesAClearEmptyState()
    {
        var viewModel = new MainViewModel(new ThrowingInspector(), new RecordingEditor());

        await viewModel.InitializeAsync();

        Assert.Equal("Scan failed", viewModel.DetectionStatus);
        Assert.Empty(viewModel.FeatureGroups);
        Assert.Empty(viewModel.ConfigurationFiles);
        NoticeRowViewModel notice = Assert.Single(viewModel.Notices);
        Assert.Equal("Error", notice.Severity);
        Assert.Contains("test failure", notice.Message, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ReviewDoesNotWriteUntilTheUserConfirms()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot();
        var editor = new RecordingEditor();
        var viewModel = new MainViewModel(new FixedInspector(snapshot), editor);
        await viewModel.InitializeAsync();
        SettingEditorViewModel viewDistance = FindViewDistanceEditor(viewModel);
        viewDistance.NumberValue = 1.5m;

        viewModel.OpenReviewCommand.Execute(null);

        Assert.True(viewModel.IsReviewingChanges);
        Assert.Single(viewModel.ReviewChanges);
        Assert.Equal(0, editor.ApplyCount);

        await viewModel.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.Equal(1, editor.ApplyCount);
        Assert.False(viewModel.IsReviewingChanges);
    }

    [Fact]
    public async Task ReturningFromReviewInvalidatesThePlanButKeepsDraftValues()
    {
        var editor = new RecordingEditor();
        var viewModel = new MainViewModel(new FixedInspector(CreateSnapshot()), editor);
        await viewModel.InitializeAsync();
        SettingEditorViewModel viewDistance = FindViewDistanceEditor(viewModel);
        viewDistance.NumberValue = 1.5m;
        viewModel.OpenReviewCommand.Execute(null);

        viewModel.CancelReviewCommand.Execute(null);

        Assert.Equal(1, editor.DiscardCount);
        Assert.False(viewModel.IsReviewingChanges);
        Assert.True(viewModel.HasPendingChanges);
        Assert.Equal(0, editor.ApplyCount);
    }

    [Fact]
    public async Task RestoreGameDefaultsReviewsEveryActiveOverrideBeforeWriting()
    {
        var editor = new RecordingEditor();
        var viewModel = new MainViewModel(new FixedInspector(CreateSnapshot()), editor);
        await viewModel.InitializeAsync();

        Assert.True(viewModel.CanRestoreGameDefaults);
        viewModel.RestoreGameDefaultsCommand.Execute(null);

        Assert.True(viewModel.IsReviewingChanges);
        ChangeReviewRowViewModel change = Assert.Single(viewModel.ReviewChanges);
        Assert.Equal("View distance", change.Name);
        Assert.Equal("Game default", change.After);
        Assert.Equal(0, editor.ApplyCount);

        await viewModel.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.Equal(1, editor.ApplyCount);
    }

    [Fact]
    public async Task SimpleModeKeepsOnlyTheCuratedControls()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor());
        await viewModel.InitializeAsync();

        FeatureGroupRowViewModel shadows = Assert.Single(
            viewModel.FeatureGroups,
            group => group.Id == "shadows-lighting");

        Assert.True(viewModel.IsSimpleMode);
        Assert.Equal(14, viewModel.FeatureGroups.Sum(group => group.Settings.Count));
        Assert.Single(shadows.Settings);
        Assert.Equal("Shadow quality", shadows.Settings[0].Name);
        Assert.All(viewModel.FeatureGroups.SelectMany(group => group.Settings), setting =>
            Assert.False(setting.ShowDescription));

        FeatureSettingRowViewModel foliage = Assert.Single(
            viewModel.FeatureGroups.SelectMany(group => group.Settings),
            setting => setting.Name == "Foliage density");
        Assert.Equal("Game preset value unknown", foliage.ValueLabel);
        Assert.Equal("Game preset", foliage.Value);
        Assert.Collection(
            foliage.PresetValues,
            low => Assert.Equal(new SettingPresetValueRowViewModel("Low", "100%"), low),
            medium => Assert.Equal(new SettingPresetValueRowViewModel("Medium", "125%"), medium),
            high => Assert.Equal(new SettingPresetValueRowViewModel("High", "150%"), high));
    }

    [Fact]
    public async Task AdvancedModeShowsEverythingAndFiltersByRendererKey()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor());
        await viewModel.InitializeAsync();
        viewModel.ShowAdvancedCommand.Execute(null);

        FeatureGroupRowViewModel shadows = Assert.Single(
            viewModel.FeatureGroups,
            group => group.Id == "shadows-lighting");
        Assert.Equal(8, shadows.Settings.Count);
        Assert.All(shadows.Settings, setting => Assert.True(setting.ShowDescription));
        Assert.Contains(shadows.Settings, setting => setting.Name == "CSM maximum resolution");
        Assert.DoesNotContain(shadows.Settings, setting => setting.Name == "Fog history supersamples");

        viewModel.SearchText = "r.Shadow.MaxResolution";

        await Task.Delay(400);

        FeatureGroupRowViewModel result = Assert.Single(viewModel.FeatureGroups);
        FeatureSettingRowViewModel setting = Assert.Single(result.Settings);
        Assert.Equal("Maximum shadow resolution", setting.Name);
        Assert.True(result.IsExpanded);

        viewModel.SearchText = "setting-that-does-not-exist";

        await Task.Delay(400);

        Assert.Empty(viewModel.FeatureGroups);
        Assert.True(viewModel.HasNoSearchResults);
    }

    [Fact]
    public async Task GroupSummaryOnlyIncludesValuesVisibleInTheCurrentMode()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot() with
        {
            ConfigurationFiles =
            [
                new ConfigurationFileSnapshot(
                    "Engine.ini",
                    "Engine.ini",
                    Exists: true,
                    42,
                    DateTimeOffset.UnixEpoch,
                    [
                        new IniSettingSnapshot("SystemSettings", "r.MaxAnisotropy", "4", 1),
                        new IniSettingSnapshot("SystemSettings", "r.Streaming.PoolSize", "1500", 2),
                    ],
                    null),
            ],
        };
        var viewModel = new MainViewModel(
            new FixedInspector(snapshot),
            new RecordingEditor());
        await viewModel.InitializeAsync();

        FeatureGroupRowViewModel simpleTextures = Assert.Single(
            viewModel.FeatureGroups,
            group => group.Id == "textures");
        Assert.Equal("4×", simpleTextures.Summary);
        Assert.Single(simpleTextures.Settings);

        viewModel.ShowAdvancedCommand.Execute(null);

        FeatureGroupRowViewModel advancedTextures = Assert.Single(
            viewModel.FeatureGroups,
            group => group.Id == "textures");
        Assert.Equal("4× · 1,46 GB", advancedTextures.Summary);
        Assert.Equal(8, advancedTextures.Settings.Count);
    }

    [Fact]
    public async Task UnstoredSharpeningValueIsNotPresentedAsAGamePreset()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor());
        await viewModel.InitializeAsync();

        FeatureSettingRowViewModel sharpening = Assert.Single(
            viewModel.FeatureGroups.SelectMany(group => group.Settings),
            setting => setting.Name == "Image sharpening");

        Assert.Equal("No override", sharpening.ValueLabel);
        Assert.Equal("Game controlled", sharpening.Value);
        Assert.True(sharpening.Editor!.ShowUnknownGameValue);
    }

    private static SettingEditorViewModel FindViewDistanceEditor(MainViewModel viewModel) =>
        Assert.Single(
                viewModel.FeatureGroups.SelectMany(group => group.Settings),
                setting => setting.Name == "View distance")
            .Editor!;

    private static GameInspectionSnapshot CreateSnapshot() =>
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
            "user-data",
            [
                new ConfigurationFileSnapshot(
                    "Engine.ini",
                    "Engine.ini",
                    Exists: true,
                    42,
                    DateTimeOffset.UnixEpoch,
                    [new IniSettingSnapshot("SystemSettings", "r.ViewDistanceScale", "1.2", 1)],
                    null),
            ],
            null,
            [],
            []);

    private sealed class FixedInspector(GameInspectionSnapshot snapshot) : IReadOnlyGameInspector
    {
        public GameInspectionSnapshot Inspect() => snapshot;
    }

    private sealed class CountingInspector(GameInspectionSnapshot snapshot) : IReadOnlyGameInspector
    {
        public int Count { get; private set; }

        public GameInspectionSnapshot Inspect()
        {
            Count++;
            return snapshot;
        }
    }

    private sealed class ThrowingInspector : IReadOnlyGameInspector
    {
        public GameInspectionSnapshot Inspect() => throw new IOException("test failure");
    }

    private sealed class RecordingEditor : IGameSettingsEditor
    {
        public int ApplyCount { get; private set; }

        public int DiscardCount { get; private set; }

        public SettingsChangePlan CreatePlan(
            GameInspectionSnapshot snapshot,
            IReadOnlyList<SettingChangeRequest> requests) =>
            new(
                "review",
                DateTimeOffset.UnixEpoch,
                "5495393",
                snapshot.UserDataDirectory!,
                requests.Select(request => new SettingChangePreview(
                    request.DisplayName,
                    request.FileName,
                    request.Key,
                    "1.2",
                    request.Value)).ToArray(),
                []);

        public SettingsOperationResult Apply(SettingsChangePlan plan)
        {
            ApplyCount++;
            return new SettingsOperationResult(true, "Applied.");
        }

        public void DiscardPlan(SettingsChangePlan plan) => DiscardCount++;

        public bool CanRevertLast(GameInspectionSnapshot snapshot) => false;

        public SettingsOperationResult RevertLast(GameInspectionSnapshot snapshot) =>
            new(false, "Nothing to revert.");
    }
}
