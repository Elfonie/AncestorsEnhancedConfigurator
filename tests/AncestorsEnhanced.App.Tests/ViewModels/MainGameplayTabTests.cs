using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.Platform;

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
    public void DiagnosticsNavigationIsIndependentOfOtherViews()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        viewModel.ShowDiagnosticsCommand.Execute(null);

        Assert.True(viewModel.ShowDiagnosticsView);
        Assert.False(viewModel.ShowGraphicsView);
        Assert.False(viewModel.ShowSaveGamesView);
        Assert.False(viewModel.ShowGameplayView);
        Assert.False(viewModel.ShowProfilesView);
        Assert.False(viewModel.ShowSettingsView);
    }

    [Fact]
    public async Task GameplayResearchValuesAreReadOnlyReferenceDataForTheSupportedBuild()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        await viewModel.InitializeAsync();

        Assert.Equal(3, viewModel.GameplayResearchValues.Count);
        Assert.Contains(
            viewModel.GameplayResearchValues,
            value => value.Name == "Stamina regained on consumed portion" && value.StockValue == "0.03 stamina");
        Assert.All(viewModel.GameplayResearchValues, value =>
        {
            Assert.False(string.IsNullOrWhiteSpace(value.Name));
            Assert.False(string.IsNullOrWhiteSpace(value.StockValue));
            Assert.False(string.IsNullOrWhiteSpace(value.Description));
            Assert.Contains("deterministic PAK", value.Evidence, StringComparison.Ordinal);
            Assert.Contains("Blocked", value.Editability, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GameplayDifficultyModesAreIndependentAndExposeSupportedControls()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsGameplaySimpleMode);
        Assert.False(viewModel.IsGameplayAdvancedMode);
        Assert.Equal(5, viewModel.GameplayDifficultyPresets.Count);
        Assert.Equal(6, viewModel.GameplaySimpleControls.Count);
        Assert.Contains(viewModel.GameplaySimpleControls, control =>
            control.Name == "Food need" &&
            control.StockValue == "24 portions per day · game default");

        viewModel.ShowGameplayAdvancedCommand.Execute(null);

        Assert.True(viewModel.IsGameplayAdvancedMode);
        Assert.False(viewModel.IsGameplaySimpleMode);
        Assert.Equal(9, viewModel.GameplayAdvancedControls.Count);
        Assert.Equal(3, viewModel.GameplayResearchValues.Count);

        GameplayDifficultyPresetViewModel survival = Assert.Single(viewModel.GameplayDifficultyPresets, preset => preset.Name == "Survival");
        viewModel.SelectGameplayPresetCommand.Execute(survival);

        Assert.All(viewModel.GameplaySimpleControls, control => Assert.Equal(130, control.MultiplierPercent));
        Assert.All(viewModel.GameplayAdvancedControls, control => Assert.Equal(100, control.MultiplierPercent));
        Assert.Contains("Survival", viewModel.GameplayDraftStatus, StringComparison.Ordinal);

        viewModel.ShowGameplaySimpleCommand.Execute(null);

        Assert.True(viewModel.IsGameplaySimpleMode);
        Assert.False(viewModel.IsGameplayAdvancedMode);
    }

    [Fact]
    public async Task ExperimentalGameplayRangeExtendsTheStandardControlsOnlyAfterExplicitOptIn()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        await viewModel.InitializeAsync();

        Assert.Equal(10, viewModel.GameplayMinimumPercent);
        Assert.Equal(200, viewModel.GameplayMaximumPercent);

        viewModel.IsExperimentalGameplaySettingsEnabled = true;
        Assert.Equal(10, viewModel.GameplayMinimumPercent);
        Assert.Equal(1000, viewModel.GameplayMaximumPercent);

        viewModel.ShowGameplayAdvancedCommand.Execute(null);
        Assert.Equal(10, viewModel.GameplayMinimumPercent);
        Assert.Equal(1000, viewModel.GameplayMaximumPercent);
    }

    [Fact]
    public async Task GameplayCatalogIsHiddenForAnUnverifiedBuild()
    {
        GameInspectionSnapshot supported = CreateSnapshot();
        var viewModel = new MainViewModel(
            new FixedInspector(supported with
            {
                Installation = supported.Installation! with { BuildId = "not-supported" },
            }),
            new NoopEditor(),
            _ => new NoopManager());

        await viewModel.InitializeAsync();

        Assert.Empty(viewModel.GameplayDifficultyPresets);
        Assert.Empty(viewModel.GameplaySimpleControls);
        Assert.Empty(viewModel.GameplayResearchValues);
        Assert.Contains("Exact Steam build", viewModel.GameplayDraftStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GameplayCatalogRequiresTheExactResearchedContentSignature()
    {
        GameInspectionSnapshot supported = CreateSnapshot();
        var viewModel = new MainViewModel(
            new FixedInspector(supported with
            {
                Installation = supported.Installation! with { ContentSignature = "PAK5:other" },
            }),
            new NoopEditor(),
            _ => new NoopManager());

        await viewModel.InitializeAsync();

        Assert.Empty(viewModel.GameplaySimpleControls);
        Assert.Equal("Exact game identity required", viewModel.GameplayReadiness.Title);
    }

    [Fact]
    public async Task GameplayReadinessBlocksWhenAdditionalPaksCannotBeInspectedForAssetConflicts()
    {
        GameInspectionSnapshot supported = CreateSnapshot() with
        {
            PakFiles =
            [
                new PakFileSnapshot(
                    "SomeMod_P.pak",
                    "SomeMod_P.pak",
                    123,
                    DateTimeOffset.UnixEpoch,
                    PakClassification.PatchStyle),
            ],
        };
        var viewModel = new MainViewModel(
            new FixedInspector(supported),
            new NoopEditor(),
            _ => new NoopManager());

        await viewModel.InitializeAsync();

        Assert.NotEmpty(viewModel.GameplaySimpleControls);
        Assert.Equal("External PAKs detected", viewModel.GameplayReadiness.Title);
        Assert.True(viewModel.GameplayReadiness.IsBlocked);
    }

    [Fact]
    public async Task GameplayReadinessDoesNotTreatAecOwnedPakAsAnExternalConflict()
    {
        GameInspectionSnapshot supported = CreateSnapshot() with
        {
            PakFiles =
            [
                new PakFileSnapshot(
                    "AncestorsEnhanced-Vignette_P.pak",
                    "AncestorsEnhanced-Vignette_P.pak",
                    123,
                    DateTimeOffset.UnixEpoch,
                    PakClassification.AecOwned),
            ],
        };
        var viewModel = new MainViewModel(
            new FixedInspector(supported),
            new NoopEditor(),
            _ => new NoopManager());

        await viewModel.InitializeAsync();

        Assert.Equal("Ready to build · runtime verification pending", viewModel.GameplayReadiness.Title);
    }

    [Fact]
    public async Task EditingASimpleControlMarksTheCurrentDifficultyPendingReview()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager());

        await viewModel.InitializeAsync();
        Assert.Single(viewModel.GameplaySimpleControls, control => control.Name == "Food need").MultiplierPercent = 120;

        Assert.Equal("Custom gameplay difficulty · pending review", viewModel.GameplayDraftStatus);
    }

    [Fact]
    public async Task GameplayUsesTheNormalReviewAndConfirmFlow()
    {
        var gameplayEditor = new RecordingGameplayEditor();
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager(),
            gameplayDifficultyEditor: gameplayEditor);
        await viewModel.InitializeAsync();
        Assert.Single(viewModel.GameplaySimpleControls, control => control.Id == "food").MultiplierPercent = 120;

        viewModel.OpenGameplayReviewCommand.Execute(null);

        Assert.True(viewModel.IsReviewingChanges);
        Assert.Equal("Review gameplay difficulty", viewModel.ReviewSummary);
        Assert.Equal("Food need", Assert.Single(viewModel.ReviewChanges).Name);

        await viewModel.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.Equal(1, gameplayEditor.ApplyCalls);
        Assert.False(viewModel.IsReviewingChanges);
        Assert.Equal(120, gameplayEditor.State.Settings.FoodPercent);
    }

    [Fact]
    public async Task HardwareRecommendationIsAvailableOnlyAfterALocalProbeReturnsEnoughData()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new NoopEditor(),
            _ => new NoopManager(),
            hardwareProbe: new FixedHardwareProbe());

        await viewModel.InitializeAsync();

        Assert.Equal("Balanced Tweak", viewModel.HardwareDiagnostics.Recommendation.PresetName);
        Assert.True(viewModel.CanStageHardwareRecommendation);
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
                ExecutableExists: true,
                ContentSignature: AncestorsEnhanced.Core.AncestorsGameProfile.SupportedContentSignature),
            "user-data",
            [],
            null,
            [],
            []);

    private sealed class FixedInspector(GameInspectionSnapshot snapshot) : IReadOnlyGameInspector
    {
        public GameInspectionSnapshot Inspect() => snapshot;
    }

    private sealed class FixedHardwareProbe : IHardwareProbe
    {
        public HardwareSnapshot Inspect(bool includeDetailedGraphics = false) => new(
            "Windows",
            "CPU",
            8,
            4,
            16UL * 1024 * 1024 * 1024,
            [new GraphicsAdapterSnapshot("GPU", 8UL * 1024 * 1024 * 1024, true)]);
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

    private sealed class RecordingGameplayEditor : IGameplayDifficultyEditor
    {
        private GameplayDifficultySettings? _planned;

        public GameplayDifficultyState State { get; private set; } = GameplayDifficultyState.GameDefault;

        public int ApplyCalls { get; private set; }

        public GameplayDifficultyState Inspect(GameInspectionSnapshot snapshot) => State;

        public SettingsChangePlan CreatePlan(
            GameInspectionSnapshot snapshot,
            GameplayDifficultySettings settings)
        {
            _planned = settings;
            return new SettingsChangePlan(
                "gameplay-review",
                DateTimeOffset.UnixEpoch,
                "5495393",
                snapshot.UserDataDirectory!,
                [new SettingChangePreview("Food need", "AncestorsEnhanced-Gameplay_P.pak", "gameplay.food", "100%", $"{settings.FoodPercent}%")],
                []);
        }

        public SettingsOperationResult Apply(SettingsChangePlan plan)
        {
            ApplyCalls++;
            GameplayDifficultySettings settings = _planned ?? throw new InvalidOperationException();
            State = new GameplayDifficultyState(GameplayDifficultyStateKind.Active, settings, "AEC gameplay PAK active");
            return SettingsOperationResult.Applied("Gameplay applied.", null);
        }

        public void DiscardPlan(SettingsChangePlan plan) => _planned = null;
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
