using System.Globalization;
using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.Profiles;
using AncestorsEnhanced.Core.SaveGames;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void HighContrastChangeUsesTheInjectedApplicationThemeHandler()
    {
        bool? observed = null;
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            highContrastEnabled: false,
            highContrastChanged: enabled => observed = enabled);

        viewModel.IsHighContrastEnabled = true;

        Assert.True(observed);
    }

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
    public async Task StartupReinspectsAfterRecoveringAnInterruptedWrite()
    {
        var inspector = new CountingInspector(CreateSnapshot());
        var editor = new RecordingEditor { RecoverInterrupted = true };
        var viewModel = new MainViewModel(inspector, editor);

        await viewModel.InitializeAsync();

        Assert.Equal(1, editor.RecoveryCount);
        Assert.Equal(2, inspector.Count);
        Assert.Equal("Ancestors is ready", viewModel.DetectionStatus);
        Assert.Equal("An interrupted tool operation was recovered safely.", viewModel.OperationMessage);
    }

    [Fact]
    public async Task StartupReportsSaveRecoveryInTheGlobalMessage()
    {
        const string RecoveryMessage = "Recovered an interrupted save restore safely.";
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            _ => new RecoverySaveGameManager(RecoveryMessage));

        await viewModel.InitializeAsync();

        Assert.Equal(RecoveryMessage, viewModel.OperationMessage);
        Assert.Equal("#B4D941", viewModel.OperationAccent);
        Assert.DoesNotContain("No files were changed", viewModel.OperationMessage, StringComparison.Ordinal);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(RecoveryMessage, viewModel.OperationMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("No files were changed", viewModel.OperationMessage, StringComparison.Ordinal);
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

        Assert.Equal("#FF5A00", viewModel.OperationAccent);

        viewModel.OpenReviewCommand.Execute(null);

        Assert.True(viewModel.IsReviewingChanges);
        Assert.Single(viewModel.ReviewChanges);
        Assert.Equal(0, editor.ApplyCount);
        Assert.Equal("#FF5A00", viewModel.OperationAccent);

        await viewModel.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.Equal(1, editor.ApplyCount);
        Assert.False(viewModel.IsReviewingChanges);
    }

    [Fact]
    public async Task PartialRollbackRefreshesDiskStateAndRequiresManualRecovery()
    {
        var inspector = new CountingInspector(CreateSnapshot());
        var editor = new RecordingEditor
        {
            ApplyResult = SettingsOperationResult.PartialRollbackRequired(
                "Some files remain changed.",
                "operation.json"),
        };
        var viewModel = new MainViewModel(inspector, editor);
        await viewModel.InitializeAsync();
        FindViewDistanceEditor(viewModel).NumberValue = 1.5m;
        viewModel.OpenReviewCommand.Execute(null);

        await viewModel.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.Equal(2, inspector.Count);
        Assert.Equal("#D6BC84", viewModel.OperationAccent);
        Assert.Contains("Manual recovery required", viewModel.OperationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PartialUndoRefreshesDiskStateAndRequiresManualRecovery()
    {
        var inspector = new CountingInspector(CreateSnapshot());
        var editor = new RecordingEditor
        {
            CanRevert = true,
            RevertResult = SettingsOperationResult.PartialRollbackRequired(
                "A file could not be restored.",
                "operation.json"),
        };
        var viewModel = new MainViewModel(inspector, editor);
        await viewModel.InitializeAsync();

        await viewModel.RevertLastCommand.ExecuteAsync(null);

        Assert.Equal(2, inspector.Count);
        Assert.Equal("#D6BC84", viewModel.OperationAccent);
        Assert.Contains("Manual recovery required", viewModel.OperationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentOverrideTakesPrecedenceOverMatchingPresetLabel()
    {
        var editor = new SettingEditorViewModel(new SettingEditSnapshot(
            "Engine.ini",
            "SystemSettings",
            "r.ViewDistanceScale",
            SettingEditorKind.Number,
            "1.2",
            "1.2"));
        var row = new FeatureSettingRowViewModel(
            "View distance",
            "120%",
            "Description",
            "Engine.ini",
            "r.ViewDistanceScale",
            "#B4D941",
            true,
            true,
            [new SettingPresetValueRowViewModel("High", "120%")],
            "High",
            editor);

        Assert.Equal("Custom override", row.ValueLabel);
    }

    [Fact]
    public void FailedInspectionDoesNotClaimTheGameDefault()
    {
        var row = new FeatureSettingRowViewModel(
            "Environmental vignette",
            "Not verified",
            "Description",
            "Unsupported game asset",
            "Vignette asset",
            "#D6BC84",
            true,
            true,
            [],
            null,
            null);

        Assert.Equal("Inspection status", row.ValueLabel);
        Assert.Equal("Unsupported game asset", row.ReadOnlyLabel);
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
    public async Task RemoveToolChangesReviewUsesANonErrorAccent()
    {
        var editor = new RecordingEditor { CanRemove = true };
        var viewModel = new MainViewModel(new FixedInspector(CreateSnapshot()), editor);
        await viewModel.InitializeAsync();

        viewModel.RemoveToolChangesCommand.Execute(null);

        Assert.True(viewModel.IsReviewingChanges);
        Assert.Equal("#FF5A00", viewModel.OperationAccent);
    }

    [Fact]
    public async Task RefreshCommandDoesNotRunDuringASaveMutation()
    {
        var inspector = new CountingInspector(CreateSnapshot());
        var viewModel = new MainViewModel(
            inspector,
            new RecordingEditor(),
            _ => new RecoverySaveGameManager(null));
        await viewModel.InitializeAsync();
        int inspectionsBeforeRefresh = inspector.Count;
        viewModel.SaveManager!.IsBusy = true;

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(inspectionsBeforeRefresh, inspector.Count);
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
            Assert.True(setting.ShowDescription));

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

        await WaitForAsync(() => viewModel.FeatureGroups.Count == 1);

        FeatureGroupRowViewModel result = Assert.Single(viewModel.FeatureGroups);
        FeatureSettingRowViewModel setting = Assert.Single(result.Settings);
        Assert.Equal("Maximum shadow resolution", setting.Name);
        Assert.True(result.IsExpanded);

        viewModel.SearchText = "setting-that-does-not-exist";

        await WaitForAsync(() => viewModel.FeatureGroups.Count == 0);

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
        await AssertTexturesSummaryAsync("de-DE", "4× · 1,46 GB");
    }

    [Fact]
    public async Task GroupSummaryFormatsBytesInvariantCulture()
    {
        await AssertTexturesSummaryAsync("en-US", "4× · 1.46 GB");
    }

    private static async Task AssertTexturesSummaryAsync(string cultureName, string expected)
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

        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

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
            Assert.Equal(expected, advancedTextures.Summary);
            Assert.Equal(8, advancedTextures.Settings.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
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

        Assert.Equal("Game controlled", sharpening.ValueLabel);
        Assert.Equal("Game controlled", sharpening.Value);
        Assert.True(sharpening.Editor!.ShowUnknownGameValue);
    }

    [Fact]
    public async Task LoadingSavedProfileStagesValuesForNormalReview()
    {
        UserProfile profile = new(
            UserProfile.CurrentSchemaVersion,
            "Clean high",
            null,
            DateTimeOffset.UnixEpoch,
            "1.0.0",
            [new ProfileSetting("r.ViewDistanceScale", "1.5")],
            [],
            []);
        var profiles = new RecordingProfileLibrary(new StoredUserProfile("11111111111111111111111111111111", profile));
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            profileLibrary: profiles);
        await viewModel.InitializeAsync();

        viewModel.ShowProfilesCommand.Execute(null);
        viewModel.LoadProfileCommand.Execute(Assert.Single(viewModel.UserProfiles));

        Assert.True(viewModel.ShowGraphicsView, viewModel.OperationMessage);
        Assert.True(viewModel.HasPendingChanges);
        Assert.Contains("Loaded Clean high", viewModel.OperationMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsReviewingChanges);
    }

    [Fact]
    public async Task LoadingMatchingProfileDoesNotShowAReviewWarning()
    {
        UserProfile profile = new(
            UserProfile.CurrentSchemaVersion,
            "Current setup",
            null,
            DateTimeOffset.UnixEpoch,
            "1.0.0",
            [new ProfileSetting("r.ViewDistanceScale", "1.2")],
            [],
            []);
        var profiles = new RecordingProfileLibrary(new StoredUserProfile("11111111111111111111111111111111", profile));
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            profileLibrary: profiles);
        await viewModel.InitializeAsync();

        viewModel.LoadProfileCommand.Execute(Assert.Single(viewModel.UserProfiles));

        Assert.False(viewModel.HasPendingChanges);
        Assert.Equal("#B4D941", viewModel.OperationAccent);
        Assert.Contains("already matches", viewModel.OperationMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Review the pending changes", viewModel.OperationMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GameplayProfileDoesNotStageAnyValuesUntilGameplayPakSupportExists()
    {
        UserProfile profile = new(
            UserProfile.CurrentSchemaVersion,
            "Future gameplay",
            null,
            DateTimeOffset.UnixEpoch,
            "1.0.0",
            [],
            [],
            [new ProfileSetting("r.ViewDistanceScale", "1.5")]);
        var profiles = new RecordingProfileLibrary(new StoredUserProfile("11111111111111111111111111111111", profile));
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            profileLibrary: profiles);
        await viewModel.InitializeAsync();

        viewModel.LoadProfileCommand.Execute(Assert.Single(viewModel.UserProfiles));

        Assert.False(viewModel.HasPendingChanges);
        Assert.Contains("Gameplay profiles are not available", viewModel.OperationMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatingProfileStoresTechnicalKeysNotUiIds()
    {
        var profiles = new RecordingProfileLibrary();
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            profileLibrary: profiles);
        await viewModel.InitializeAsync();

        viewModel.StartCreatingProfileCommand.Execute(null);
        viewModel.NewProfileName = "My setup";
        viewModel.CreateProfileCommand.Execute(null);

        StoredUserProfile saved = Assert.Single(profiles.List());
        ProfileSetting setting = Assert.Single(saved.Profile.Graphics);
        Assert.Equal("r.ViewDistanceScale", setting.Key);
        Assert.Equal("1.2", setting.Value);
        Assert.Equal("My setup", saved.Profile.Name);
    }

    [Fact]
    public async Task CreatingProfileUsesTheVignetteEditorKeyInsteadOfItsAssetLabel()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot() with
        {
            Vignette = new VignetteModSnapshot(50, IsEditable: true, "Managed patch"),
        };
        var profiles = new RecordingProfileLibrary();
        var viewModel = new MainViewModel(
            new FixedInspector(snapshot),
            new RecordingEditor(),
            profileLibrary: profiles);
        await viewModel.InitializeAsync();
        viewModel.NewProfileName = "Reduced vignette";

        viewModel.CreateProfileCommand.Execute(null);

        StoredUserProfile saved = Assert.Single(profiles.List());
        Assert.Contains(saved.Profile.Graphics, setting =>
            setting.Key == "mod.VignettePercent" && setting.Value == "50");
        Assert.DoesNotContain(saved.Profile.Graphics, setting =>
            setting.Key == "VL01E01_Vignette_Intensity");
    }

    [Fact]
    public async Task FailedProfileSaveIsReportedWithoutThrowingFromTheCommand()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            profileLibrary: new FailingProfileLibrary());
        await viewModel.InitializeAsync();
        viewModel.NewProfileName = "My setup";

        viewModel.CreateProfileCommand.Execute(null);

        Assert.Contains("The profile was not saved", viewModel.OperationMessage, StringComparison.Ordinal);
        Assert.False(viewModel.IsCreatingProfile);
    }

    [Fact]
    public async Task ProfileSaveAvailabilityUpdatesWhenAnOverrideChanges()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot() with
        {
            ConfigurationFiles =
            [
                new ConfigurationFileSnapshot(
                    "Engine.ini",
                    "Engine.ini",
                    Exists: true,
                    0,
                    DateTimeOffset.UnixEpoch,
                    [],
                    null),
            ],
        };
        var viewModel = new MainViewModel(new FixedInspector(snapshot), new RecordingEditor());
        await viewModel.InitializeAsync();
        viewModel.NewProfileName = "My setup";

        Assert.False(viewModel.HasCustomProfileSettings);
        Assert.False(viewModel.CanSaveProfile);

        SettingEditorViewModel editor = FindViewDistanceEditor(viewModel);
        editor.UseCustomValue = true;

        Assert.True(viewModel.HasCustomProfileSettings);
        Assert.True(viewModel.CanSaveProfile);
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

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("The debounced search did not settle in time.");
            }

            await Task.Delay(25);
        }
    }

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

    private sealed class RecoverySaveGameManager(string? recoveryMessage) : ISaveGameManager
    {
        public SaveGamesSnapshot Inspect() =>
            new(DateTimeOffset.UnixEpoch, "user-data", [], recoveryMessage);

        public SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual") =>
            new(false, "not used");

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) =>
            new(false, "not used");

        public SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId) =>
            new(false, "not used");
    }

    private sealed class RecordingProfileLibrary(params StoredUserProfile[] profiles) : IUserProfileLibrary
    {
        private readonly Dictionary<string, UserProfile> _profiles = profiles.ToDictionary(profile => profile.Id, profile => profile.Profile);

        public IReadOnlyList<StoredUserProfile> List() =>
            _profiles.Select(pair => new StoredUserProfile(pair.Key, pair.Value)).ToArray();

        public UserProfile Read(string id) => _profiles[id];

        public StoredUserProfile Save(UserProfile profile)
        {
            UserProfile validated = UserProfileCodec.Deserialize(UserProfileCodec.Serialize(profile));
            string id = Guid.NewGuid().ToString("N");
            _profiles.Add(id, validated);
            return new StoredUserProfile(id, validated);
        }

        public UserProfile ReadExternal(string path) => throw new NotSupportedException();
    }

    private sealed class FailingProfileLibrary : IUserProfileLibrary
    {
        public IReadOnlyList<StoredUserProfile> List() => [];

        public UserProfile Read(string id) => throw new FileNotFoundException();

        public StoredUserProfile Save(UserProfile profile) =>
            throw new InvalidDataException("unsupported test value");

        public UserProfile ReadExternal(string path) => throw new NotSupportedException();
    }

    private sealed class RecordingEditor : IGameSettingsEditor
    {
        public int ApplyCount { get; private set; }

        public int DiscardCount { get; private set; }

        public bool RecoverInterrupted { get; init; }

        public int RecoveryCount { get; private set; }

        public SettingsOperationResult ApplyResult { get; init; } =
            SettingsOperationResult.Applied("Applied.", null);

        public SettingsOperationResult RevertResult { get; init; } =
            SettingsOperationResult.Failed("Nothing to revert.");

        public bool CanRevert { get; init; }

        public bool CanRemove { get; init; }

        public bool RecoverInterruptedChanges(GameInspectionSnapshot snapshot)
        {
            RecoveryCount++;
            return RecoverInterrupted;
        }

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
            return ApplyResult;
        }

        public void DiscardPlan(SettingsChangePlan plan) => DiscardCount++;

        public bool CanRevertLast(GameInspectionSnapshot snapshot) => CanRevert;

        public SettingsOperationResult RevertLast(GameInspectionSnapshot snapshot) =>
            RevertResult;

        public bool CanRemoveToolChanges(GameInspectionSnapshot snapshot) => CanRemove;

        public SettingsChangePlan CreateRemoveToolChangesPlan(GameInspectionSnapshot snapshot) =>
            new(
                "remove",
                DateTimeOffset.UnixEpoch,
                "5495393",
                snapshot.UserDataDirectory!,
                [new SettingChangePreview(
                    "Engine.ini",
                    "Engine.ini",
                    "r.ViewDistanceScale",
                    "1.2",
                    null)],
                []);
    }
}
