using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void ReviewDoesNotWriteUntilTheUserConfirms()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot();
        var editor = new RecordingEditor();
        var viewModel = new MainViewModel(new FixedInspector(snapshot), editor);
        SettingEditorViewModel viewDistance = FindViewDistanceEditor(viewModel);
        viewDistance.NumberValue = 1.5m;

        viewModel.OpenReviewCommand.Execute(null);

        Assert.True(viewModel.IsReviewingChanges);
        Assert.Single(viewModel.ReviewChanges);
        Assert.Equal(0, editor.ApplyCount);

        viewModel.ConfirmApplyCommand.Execute(null);

        Assert.Equal(1, editor.ApplyCount);
        Assert.False(viewModel.IsReviewingChanges);
    }

    [Fact]
    public void ReturningFromReviewInvalidatesThePlanButKeepsDraftValues()
    {
        var editor = new RecordingEditor();
        var viewModel = new MainViewModel(new FixedInspector(CreateSnapshot()), editor);
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
    public void SimpleModeKeepsOnlyTheCuratedControls()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor());

        FeatureGroupRowViewModel shadows = Assert.Single(
            viewModel.FeatureGroups,
            group => group.Id == "shadows-lighting");

        Assert.True(viewModel.IsSimpleMode);
        Assert.Equal(14, viewModel.FeatureGroups.Sum(group => group.Settings.Count));
        Assert.Single(shadows.Settings);
        Assert.Equal("Shadow quality", shadows.Settings[0].Name);
        Assert.All(viewModel.FeatureGroups.SelectMany(group => group.Settings), setting =>
            Assert.False(setting.ShowDescription));
    }

    [Fact]
    public void AdvancedModeShowsEverythingAndFiltersByRendererKey()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor());
        viewModel.ShowAdvancedCommand.Execute(null);

        FeatureGroupRowViewModel shadows = Assert.Single(
            viewModel.FeatureGroups,
            group => group.Id == "shadows-lighting");
        Assert.Equal(18, shadows.Settings.Count);
        Assert.All(shadows.Settings, setting => Assert.True(setting.ShowDescription));

        viewModel.SearchText = "r.Shadow.MaxResolution";

        FeatureGroupRowViewModel result = Assert.Single(viewModel.FeatureGroups);
        FeatureSettingRowViewModel setting = Assert.Single(result.Settings);
        Assert.Equal("Maximum shadow resolution", setting.Name);
        Assert.True(result.IsExpanded);

        viewModel.SearchText = "setting-that-does-not-exist";

        Assert.Empty(viewModel.FeatureGroups);
        Assert.True(viewModel.HasNoSearchResults);
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
                "store",
                "library",
                "install",
                "Ancestors-Win64-Shipping.exe",
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
