using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.SaveGames;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class MainGameplayTabTests
{
    [Fact]
    public void GameplayNavigationIsIndependentOfGraphicsAndSaves()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        viewModel.ShowGameplayCommand.Execute(null);

        Assert.True(viewModel.ShowGameplayView);
        Assert.False(viewModel.ShowGraphicsView);
        Assert.False(viewModel.ShowSaveGamesView);

        viewModel.ShowSaveGamesCommand.Execute(null);

        Assert.True(viewModel.ShowSaveGamesView);
        Assert.False(viewModel.ShowGameplayView);

        viewModel.ShowGraphicsCommand.Execute(null);

        Assert.True(viewModel.ShowGraphicsView);
        Assert.False(viewModel.ShowGameplayView);
    }

    [Fact]
    public void ProfilesNavigationIsIndependentOfGraphicsSavesAndGameplay()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        viewModel.ShowProfilesCommand.Execute(null);

        Assert.True(viewModel.ShowProfilesView);
        Assert.False(viewModel.ShowGraphicsView);
        Assert.False(viewModel.ShowSaveGamesView);
        Assert.False(viewModel.ShowGameplayView);

        viewModel.ShowGameplayCommand.Execute(null);

        Assert.True(viewModel.ShowGameplayView);
        Assert.False(viewModel.ShowProfilesView);
    }

    [Fact]
    public void SettingsNavigationIsIndependentOfOtherViews()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        viewModel.ShowSettingsCommand.Execute(null);

        Assert.True(viewModel.ShowSettingsView);
        Assert.False(viewModel.ShowGraphicsView);
        Assert.False(viewModel.ShowSaveGamesView);
        Assert.False(viewModel.ShowGameplayView);
        Assert.False(viewModel.ShowProfilesView);

        viewModel.ShowGraphicsCommand.Execute(null);

        Assert.True(viewModel.ShowGraphicsView);
        Assert.False(viewModel.ShowSettingsView);
    }

    [Fact]
    public void GameplayResearchValuesAreReadOnlyReferenceData()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        Assert.Equal(7, viewModel.GameplayResearchValues.Count);
        Assert.Contains(
            viewModel.GameplayResearchValues,
            value => value.Name == "Energy recovery delay" && value.StockValue == "1.5 seconds");
        Assert.All(viewModel.GameplayResearchValues, value =>
        {
            Assert.False(string.IsNullOrWhiteSpace(value.Name));
            Assert.False(string.IsNullOrWhiteSpace(value.StockValue));
            Assert.False(string.IsNullOrWhiteSpace(value.Description));
        });
    }

    [Fact]
    public void GameplayDifficultyModesAreIndependentAndExposeOnlyPlannedControls()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        Assert.True(viewModel.IsGameplaySimpleMode);
        Assert.False(viewModel.IsGameplayAdvancedMode);
        Assert.Equal(4, viewModel.GameplayDifficultyPresets.Count);
        Assert.Equal(5, viewModel.GameplaySimpleControls.Count);
        Assert.Contains(viewModel.GameplaySimpleControls, control =>
            control.Name == "Food need" &&
            control.StockValue == "24 portions per day · game default");

        viewModel.ShowGameplayAdvancedCommand.Execute(null);

        Assert.True(viewModel.IsGameplayAdvancedMode);
        Assert.False(viewModel.IsGameplaySimpleMode);
        Assert.Equal(7, viewModel.GameplayResearchValues.Count);

        GameplayDifficultyPresetViewModel survival = Assert.Single(viewModel.GameplayDifficultyPresets, preset => preset.Name == "Survival (planned)");
        viewModel.SelectGameplayPresetCommand.Execute(survival);

        Assert.All(viewModel.GameplaySimpleControls, control => Assert.Equal(130, control.MultiplierPercent));
        Assert.Contains("Survival", viewModel.GameplayDraftStatus, StringComparison.Ordinal);

        viewModel.ShowGameplaySimpleCommand.Execute(null);

        Assert.True(viewModel.IsGameplaySimpleMode);
        Assert.False(viewModel.IsGameplayAdvancedMode);
    }

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
            [],
            null,
            [],
            []);

    private sealed class FixedInspector(GameInspectionSnapshot snapshot) : IReadOnlyGameInspector
    {
        public GameInspectionSnapshot Inspect() => snapshot;
    }

    private sealed class NoopEditor : IGameSettingsEditor
    {
        public SettingsChangePlan CreatePlan(
            GameInspectionSnapshot snapshot,
            IReadOnlyList<SettingChangeRequest> requests) =>
            new("review", DateTimeOffset.UnixEpoch, "5495393", snapshot.UserDataDirectory!, [], []);

        public SettingsOperationResult Apply(SettingsChangePlan plan) => new(true, "Applied.");

        public void DiscardPlan(SettingsChangePlan plan)
        {
        }

        public bool CanRevertLast(GameInspectionSnapshot snapshot) => false;

        public SettingsOperationResult RevertLast(GameInspectionSnapshot snapshot) =>
            new(false, "Nothing to revert.");
    }

    private sealed class NoopManager : ISaveGameManager
    {
        public SaveGamesSnapshot Inspect() => new(DateTimeOffset.UnixEpoch, "user-data", []);

        public SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual") =>
            new(true, "Checkpoint saved.");

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) =>
            new(true, "Loaded.");

        public SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId) =>
            new(true, "Deleted.");
    }
}
