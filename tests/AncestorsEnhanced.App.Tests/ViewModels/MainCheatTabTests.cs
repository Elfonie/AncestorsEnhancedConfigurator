using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.SaveGames;
using Xunit;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class MainCheatTabTests
{
    [Fact]
    public void CheatTabNavigationIsIndependentOfGraphicsAndSaves()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        Assert.False(viewModel.ShowCheatView);
        Assert.True(viewModel.ShowGraphicsView);

        viewModel.ShowCheatCommand.Execute(null);

        Assert.True(viewModel.ShowCheatView);
        Assert.False(viewModel.ShowGraphicsView);
        Assert.False(viewModel.ShowSaveGamesView);

        viewModel.ShowSaveGamesCommand.Execute(null);

        Assert.True(viewModel.ShowSaveGamesView);
        Assert.False(viewModel.ShowCheatView);

        viewModel.ShowCheatCommand.Execute(null);
        viewModel.ShowGraphicsCommand.Execute(null);

        Assert.False(viewModel.ShowCheatView);
        Assert.True(viewModel.ShowGraphicsView);
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
            new(
                "review",
                DateTimeOffset.UnixEpoch,
                "5495393",
                snapshot.UserDataDirectory!,
                [],
                []);

        public SettingsOperationResult Apply(SettingsChangePlan plan) =>
            new(true, "Applied.");

        public void DiscardPlan(SettingsChangePlan plan)
        {
        }

        public bool CanRevertLast(GameInspectionSnapshot snapshot) => false;

        public SettingsOperationResult RevertLast(GameInspectionSnapshot snapshot) =>
            new(false, "Nothing to revert.");
    }

    private sealed class NoopManager : ISaveGameManager
    {
        public SaveGamesSnapshot Inspect() =>
            new(DateTimeOffset.UnixEpoch, "user-data", []);

        public SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual") =>
            new(true, "Checkpoint saved.");

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) =>
            new(true, "Loaded.");

        public SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId) =>
            new(true, "Deleted.");
    }
}
