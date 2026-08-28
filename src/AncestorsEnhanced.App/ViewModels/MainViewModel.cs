using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.Profiles;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Core.Settings;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.Platform;
using AncestorsEnhanced.Infrastructure.SaveGames;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyGameInspector _inspector;
    private readonly IGameSettingsEditor _settingsEditor;
    private readonly IGameplayDifficultyEditor _gameplayEditor;
    private readonly Func<VerifiedGameContext, ISaveGameManager> _saveManagerFactory;
    private readonly IUserProfileLibrary _profileLibrary;
    private readonly GameContextVerifier _gameContextVerifier;
    private readonly IHardwareProbe _hardwareProbe;
    private readonly Func<string, bool> _directoryOpener;
    private readonly UiMutationGate _mutationGate = new();
    private VerifiedGameContext? _verifiedGameContext;
    private bool _saveGamesRefreshFailed;
    private bool _lastRefreshRecoveredOperation;
    private string? _lastSaveRecoveryMessage;
    private readonly Dictionary<string, SettingEditorViewModel> _editors =
        new(StringComparer.Ordinal);
    private IReadOnlyList<FeatureGroupSnapshot> _allFeatureGroups = [];
    private readonly Dictionary<string, bool> _groupExpansionStates = new(StringComparer.Ordinal);
    private GameInspectionSnapshot? _snapshot;
    private SettingsChangePlan? _reviewPlan;
    private bool _reviewIsToolChangeRemoval;
    private bool _reviewIsGameplay;
    private bool _reviewRemovesGameplayPak;
    private readonly Action<bool>? _highContrastChanged;
    private readonly Action<bool>? _discordRichPresenceChanged;
    private readonly Action? _onboardingCompleted;
    private readonly Action<bool>? _experimentalGraphicsSettingsChanged;
    private readonly Action<bool>? _experimentalGameplaySettingsChanged;
    private readonly int _gameplayMinimumPercent = 10;
    private readonly Action<HardwareSnapshot>? _detailedHardwareScanCompleted;
    private HardwareSnapshot? _detailedHardwareSnapshot;

    [ObservableProperty]
    public partial string DetectionStatus { get; set; } = "Not checked yet";

    [ObservableProperty]
    public partial string DetectionColor { get; set; } = "#7A877A";

    [ObservableProperty]
    public partial string DetectionDotColor { get; set; } = "#7A877A";

    [ObservableProperty]
    public partial string InstallationPath { get; set; } = "Not detected";

    [ObservableProperty]
    public partial string InstallationDetails { get; set; } = "Store and build unknown";

    [ObservableProperty]
    public partial string UserDataPath { get; set; } = "Not detected";

    [ObservableProperty]
    public partial string BinarySettingsPath { get; set; } = "Not detected";

    [ObservableProperty]
    public partial string BinarySettingsStatus { get; set; } = "Not inspected";

    [ObservableProperty]
    public partial IReadOnlyList<FeatureGroupRowViewModel> FeatureGroups { get; set; } = [];

    [ObservableProperty]
    public partial bool IsAdvancedMode { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string GraphicsFilter { get; set; } = "All";

    [ObservableProperty]
    public partial IReadOnlyList<BuiltInGraphicsPresetViewModel> BuiltInGraphicsPresets { get; set; } =
    [
        new("Clear Image", "Remove blur and reduce vignette", CreateBuiltInProfile("Clear Image", [
            new ProfileSetting("r.MotionBlurQuality", "0"),
            new ProfileSetting("r.DepthOfFieldQuality", "0"),
            new ProfileSetting("r.SceneColorFringeQuality", "0"),
            new ProfileSetting("r.Tonemapper.Sharpen", "0.4"),
            new ProfileSetting("mod.VignettePercent", "50")])),
        new("Performance Setup", "Complete baseline for constrained hardware", CreateHardwareBaselineProfile("Performance Setup", GameGraphicsQuality.Medium, [
            new ProfileSetting("r.PostProcessAAQuality", "3"),
            new ProfileSetting("r.MaxAnisotropy", "16"),
            new ProfileSetting("r.Streaming.PoolSize", "2048"),
            new ProfileSetting("r.Streaming.LimitPoolSizeToVRAM", "1")])),
        new("Balanced Setup", "Complete High-quality baseline for mainstream hardware", CreateHardwareBaselineProfile("Balanced Setup", GameGraphicsQuality.High, [
            new ProfileSetting("r.PostProcessAAQuality", "4"),
            new ProfileSetting("r.MaxAnisotropy", "16"),
            new ProfileSetting("r.Streaming.PoolSize", "3072"),
            new ProfileSetting("r.Streaming.LimitPoolSizeToVRAM", "1")])),
        new("High Quality Setup", "Complete High-quality baseline with extra world and reflection detail", CreateHardwareBaselineProfile("High Quality Setup", GameGraphicsQuality.High, [
            new ProfileSetting("r.PostProcessAAQuality", "4"),
            new ProfileSetting("r.MaxAnisotropy", "16"),
            new ProfileSetting("r.Streaming.PoolSize", "4096"),
            new ProfileSetting("r.Streaming.LimitPoolSizeToVRAM", "1"),
            new ProfileSetting("r.ViewDistanceScale", "1.1"),
            new ProfileSetting("r.SSR.Quality", "3")])),
        new("Ultra Setup", "Complete High-quality baseline for exceptional hardware", CreateHardwareBaselineProfile("Ultra Setup", GameGraphicsQuality.High, [
            new ProfileSetting("r.PostProcessAAQuality", "4"),
            new ProfileSetting("r.MaxAnisotropy", "16"),
            new ProfileSetting("r.Streaming.PoolSize", "6144"),
            new ProfileSetting("r.Streaming.LimitPoolSizeToVRAM", "1"),
            new ProfileSetting("r.ViewDistanceScale", "1.2"),
            new ProfileSetting("foliage.DensityScale", "1.5"),
            new ProfileSetting("grass.DensityScale", "1.5"),
            new ProfileSetting("r.SSR.Quality", "3"),
            new ProfileSetting("r.Shadow.MaxResolution", "4096")])),
        new("Low VRAM Setup", "Complete baseline that protects limited graphics memory", CreateHardwareBaselineProfile("Low VRAM Setup", GameGraphicsQuality.Low, [
            new ProfileSetting("r.PostProcessAAQuality", "0"),
            new ProfileSetting("r.MaxAnisotropy", "16"),
            new ProfileSetting("r.Streaming.PoolSize", "1024"),
            new ProfileSetting("r.Streaming.LimitPoolSizeToVRAM", "1")])),
        new("Cinematic Tweak", "Atmosphere and post-processing adjustments without resetting other choices", CreateBuiltInProfile("Cinematic Tweak", [
            new ProfileSetting("r.DepthOfFieldQuality", "4"),
            new ProfileSetting("r.MotionBlurQuality", "4"),
            new ProfileSetting("r.SceneColorFringeQuality", "1"),
            new ProfileSetting("r.BloomQuality", "5"),
            new ProfileSetting("r.VolumetricFog", "1")])),
    ];

    [ObservableProperty]
    public partial bool IsGraphicsPresetsExpanded { get; set; }

    [ObservableProperty]
    public partial string ViewModeTitle { get; set; } = "Simple";

    [ObservableProperty]
    public partial string ViewModeDescription { get; set; } = "The settings that make the clearest visual difference";

    [ObservableProperty]
    public partial IReadOnlyList<PendingChangeRowViewModel> PendingChanges { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ChangeReviewRowViewModel> ReviewChanges { get; set; } = [];

    [ObservableProperty]
    public partial bool IsReviewingChanges { get; set; }

    [ObservableProperty]
    public partial string OperationMessage { get; set; } = "Ready. Nothing is written until you review and confirm.";

    [ObservableProperty]
    public partial string OperationAccent { get; set; } = "#7A877A";

    [ObservableProperty]
    public partial bool CanRevertLast { get; set; }

    [ObservableProperty]
    public partial bool HasRemovableToolChanges { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ConfigurationFileRowViewModel> ConfigurationFiles { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<SettingRowViewModel> Settings { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PakFileRowViewModel> PakFiles { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<NoticeRowViewModel> Notices { get; set; } = [];

    [ObservableProperty]
    public partial HardwareDiagnosticsViewModel HardwareDiagnostics { get; set; } = HardwareDiagnosticsViewModel.FromSnapshot(EmptyHardwareProbe.Instance.Inspect());

    [ObservableProperty]
    public partial bool IsDetailedHardwareDetectionRunning { get; set; }

    [ObservableProperty]
    public partial string HardwareScanMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSaveGamesView { get; set; }

    [ObservableProperty]
    public partial bool IsHomeView { get; set; } = true;

    [ObservableProperty]
    public partial SaveManagerViewModel? SaveManager { get; set; }

    [ObservableProperty]
    public partial bool IsGameplayView { get; set; }

    [ObservableProperty]
    public partial bool IsGameplayAdvancedMode { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<GameplayDifficultyPresetViewModel> GameplayDifficultyPresets { get; set; } = [];

    [ObservableProperty]
    public partial bool IsGameplayPresetsExpanded { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<GameplayDifficultyControlViewModel> GameplaySimpleControls { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GameplayDifficultyControlViewModel> GameplayAdvancedControls { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GameplayResearchValueViewModel> GameplayResearchValues { get; set; } = [];

    [ObservableProperty]
    public partial GameplayReadinessViewModel GameplayReadiness { get; set; } = new(
        "Game not checked",
        "Reload to verify the exact game identity before viewing gameplay research.",
        "#D6BC84",
        true);

    [ObservableProperty]
    public partial GameplayDifficultyState GameplayState { get; set; } = GameplayDifficultyState.GameDefault;

    [ObservableProperty]
    public partial bool IsProfilesView { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsView { get; set; }

    [ObservableProperty]
    public partial bool IsDiagnosticsView { get; set; }

    [ObservableProperty]
    public partial bool IsOnboardingVisible { get; set; }

    [ObservableProperty]
    public partial int OnboardingStep { get; set; }

    [ObservableProperty]
    public partial bool IsHighContrastEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsDiscordRichPresenceEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsExperimentalGraphicsSettingsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsExperimentalGameplaySettingsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IncludeClanInGameplayPatch { get; set; }

    [ObservableProperty]
    public partial bool HasAcknowledgedDetailedHardwareScan { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<UserProfileRowViewModel> UserProfiles { get; set; } = [];

    [ObservableProperty]
    public partial ImportedProfileViewModel? ImportedProfile { get; set; }

    [ObservableProperty]
    public partial UserProfileRowViewModel? ProfilePendingDeletion { get; set; }

    [ObservableProperty]
    public partial UserProfileRowViewModel? ProfilePendingRename { get; set; }

    [ObservableProperty]
    public partial string RenamedProfileName { get; set; } = "";

    [ObservableProperty]
    public partial string? ProfileComparisonName { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ProfileComparisonRowViewModel> ProfileComparisonRows { get; set; } = [];

    [ObservableProperty]
    public partial bool IsCreatingProfile { get; set; }

    [ObservableProperty]
    public partial string NewProfileName { get; set; } = "";

    [ObservableProperty]
    public partial string NewProfileDescription { get; set; } = "";

    public MainViewModel(
        IReadOnlyGameInspector inspector,
        IGameSettingsEditor settingsEditor,
        Func<VerifiedGameContext, ISaveGameManager>? saveManagerFactory = null,
        IUserProfileLibrary? profileLibrary = null,
        bool highContrastEnabled = false,
        Action<bool>? highContrastChanged = null,
        bool discordRichPresenceEnabled = false,
        Action<bool>? discordRichPresenceChanged = null,
        bool showOnboarding = false,
        Action? onboardingCompleted = null,
        bool experimentalGraphicsSettingsEnabled = false,
        Action<bool>? experimentalGraphicsSettingsChanged = null,
        bool experimentalGameplaySettingsEnabled = false,
        Action<bool>? experimentalGameplaySettingsChanged = null,
        bool hasAcknowledgedDetailedHardwareScan = false,
        HardwareSnapshot? detailedHardwareSnapshot = null,
        Action<HardwareSnapshot>? detailedHardwareScanCompleted = null,
        IHardwareProbe? hardwareProbe = null,
        IGameplayDifficultyEditor? gameplayDifficultyEditor = null,
        Func<string, bool>? directoryOpener = null)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(settingsEditor);
        _inspector = inspector;
        _gameContextVerifier = new GameContextVerifier(inspector);
        _gameplayEditor = gameplayDifficultyEditor ?? new SafeGameplayDifficultyEditor(_gameContextVerifier);
        _hardwareProbe = hardwareProbe ?? EmptyHardwareProbe.Instance;
        _directoryOpener = directoryOpener ?? TryOpenDirectory;
        _settingsEditor = settingsEditor;
        _saveManagerFactory = saveManagerFactory ?? (context => new SafeSaveGameManager(context, _gameContextVerifier));
        _profileLibrary = profileLibrary ?? EmptyUserProfileLibrary.Instance;
        _highContrastChanged = highContrastChanged;
        _discordRichPresenceChanged = discordRichPresenceChanged;
        _onboardingCompleted = onboardingCompleted;
        _experimentalGraphicsSettingsChanged = experimentalGraphicsSettingsChanged;
        _experimentalGameplaySettingsChanged = experimentalGameplaySettingsChanged;
        _detailedHardwareSnapshot = detailedHardwareSnapshot;
        _detailedHardwareScanCompleted = detailedHardwareScanCompleted;
        IsHighContrastEnabled = highContrastEnabled;
        IsDiscordRichPresenceEnabled = discordRichPresenceEnabled;
        IsOnboardingVisible = showOnboarding;
        IsExperimentalGraphicsSettingsEnabled = experimentalGraphicsSettingsEnabled;
        IsExperimentalGameplaySettingsEnabled = experimentalGameplaySettingsEnabled;
        HasAcknowledgedDetailedHardwareScan = hasAcknowledgedDetailedHardwareScan;
        _mutationGate.Changed += OnMutationGateChanged;

        ProductName = "Ancestors Enhanced Configurator";
        string version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        Phase = $"{version} Graphics, Saves and Gameplay";
        SystemDiagnostics = $"{RuntimeInformation.OSDescription.Trim()} · {RuntimeInformation.OSArchitecture} · {Environment.ProcessorCount} logical processors";
    }

    public string ProductName { get; }

    public string Phase { get; }

    public bool ShowHomeView => IsHomeView;

    public bool ShowGraphicsView => !IsHomeView && !IsSaveGamesView && !IsGameplayView && !IsProfilesView && !IsSettingsView && !IsDiagnosticsView;

    public bool ShowSaveGamesView => IsSaveGamesView;

    public bool ShowGameplayView => IsGameplayView;

    public bool HasGameplayResearchValues => GameplayResearchValues.Count > 0;

    public bool IsGameplaySimpleMode => !IsGameplayAdvancedMode;

    public bool HasGameplayDifficultyPresets => GameplayDifficultyPresets.Count > 0;

    public bool HasGameplaySimpleControls => GameplaySimpleControls.Count > 0;

    public bool HasGameplayAdvancedControls => GameplayAdvancedControls.Count > 0;

    public int GameplayMinimumPercent => _gameplayMinimumPercent;

    public int GameplayMaximumPercent => IsExperimentalGameplaySettingsEnabled ? 1000 : 200;

    public string GameplayControlRangeLabel => IsExperimentalGameplaySettingsEnabled
        ? "Extended range: 10% to 1000% · 10% steps"
        : "Standard range: 10% to 200% · 10% steps";

    public string GameplayDraftStatus { get; private set; } = "Game default · no AEC gameplay PAK installed";

    public bool HasGameplayPendingChanges =>
        GameplaySimpleControls.Count > 0 &&
        GameplayAdvancedControls.Count > 0 &&
        CurrentGameplaySettings != GameplayState.Settings;

    public bool CanReviewGameplay =>
        HasGameplayPendingChanges &&
        !GameplayReadiness.IsBlocked &&
        GameplayState.Kind is not GameplayDifficultyStateKind.Unverified &&
        !IsReviewingChanges &&
        !IsAnyOperationRunning;

    public bool CanResetGameplay =>
        GameplayState.Kind == GameplayDifficultyStateKind.Active &&
        !IsReviewingChanges &&
        !IsAnyOperationRunning;

    public bool CanEditGameplay =>
        !GameplayReadiness.IsBlocked &&
        GameplayState.Kind is not GameplayDifficultyStateKind.Unverified &&
        !IsReviewingChanges &&
        !IsAnyOperationRunning;

    public string GameplayReviewButtonLabel => CurrentGameplaySettings.IsGameDefault
        ? "Review removal"
        : GameplayState.Kind == GameplayDifficultyStateKind.Active
            ? "Review gameplay update"
            : "Review gameplay mod";

    public string HomeGraphicsSummary => HasGamePreset
        ? $"{GamePresetName} · {CustomOverrideCount} custom change(s)"
        : "Graphics settings will appear after game detection.";

    public string HomeGameplaySummary => HasGameplayPendingChanges
        ? GameplayDraftStatus
        : GameplayState.Description;

    public int ExternalPakCount => _snapshot?.PakFiles.Count(pak =>
        pak.Classification is not PakClassification.BaseGame and not PakClassification.AecOwned) ?? 0;

    public bool HasExternalPaks => ExternalPakCount > 0;

    public string ExternalPakHelpText => ExternalPakCount == 1
        ? "1 external PAK is preventing gameplay editing. AEC will not remove it; inspect or remove it manually from the game PAK folder."
        : $"{ExternalPakCount} external PAKs are preventing gameplay editing. AEC will not remove them; inspect or remove them manually from the game PAK folder.";

    public string? GamePakFolderPath => TryGetGamePakFolder(_snapshot?.Installation?.InstallDirectory);

    public bool CanOpenGamePakFolder => HasExternalPaks && GamePakFolderPath is not null;

    public string HomeTitle => IsBusy
        ? "Checking Ancestors…"
        : DetectionStatus switch
        {
            "Ancestors is ready" => "Ready to play",
            "Scan failed" => "Attention required",
            "Ancestors installation not detected" => "Game not found",
            "Multiple Ancestors installations detected" => "Choose an Ancestors installation",
            "Ancestors detected but not supported for editing" => "Unsupported game version",
            "Ancestors detected with problems" => "Attention required",
            _ => "Checking Ancestors…",
        };

    public string HomeSavesSummary => SaveManager is null
        ? "Save games will appear after detection."
        : SaveManager.HasSlots
            ? $"{SaveManager.Slots.Count(slot => slot.HasSave)} save slot(s) · {SaveManager.BackupHealthSummary}"
            : "No save slots found yet.";

    public bool ShowProfilesView => IsProfilesView;

    public bool ShowSettingsView => IsSettingsView;

    public bool ShowDiagnosticsView => IsDiagnosticsView;

    public bool IsGraphicsSectionActive => ShowGraphicsView || IsProfilesView;

    public bool IsSettingsSectionActive => IsSettingsView || IsDiagnosticsView;

    public string PageContextLabel => IsProfilesView
        ? "Graphics / Profiles"
        : IsDiagnosticsView
            ? "Settings / Diagnostics"
            : IsSettingsView
                ? "Settings"
            : IsHomeView
                ? "Home"
                : IsGameplayView
                    ? "Gameplay"
                    : IsSaveGamesView
                        ? "Saves"
                        : "Graphics";

    public string SystemDiagnostics { get; }

    public bool CanStageHardwareRecommendation =>
        HardwareDiagnostics.Recommendation.CanStagePreset &&
        !HasPendingChanges &&
        !IsReviewingChanges &&
        !IsAnyOperationRunning;

    public bool CanRunDetailedHardwareDetection => OperatingSystem.IsWindows() && !IsAnyOperationRunning;

    public string DetailedHardwareActionLabel => IsDetailedHardwareDetectionRunning
        ? "Checking hardware…"
        : "Refresh hardware details";

    public bool HasHardwareScanMessage => !string.IsNullOrWhiteSpace(HardwareScanMessage);

    public bool CanShowHardwareScanAction =>
        !CanStageHardwareRecommendation &&
        CanRunDetailedHardwareDetection;

    public string OnboardingTitle => OnboardingStep switch
    {
        0 => "Welcome to Ancestors Enhanced",
        1 => "Your game stays in control",
        _ => "Choose your level of detail",
    };

    public string OnboardingDescription => OnboardingStep switch
    {
        0 => "AEC detects your installation first and only enables writing after a supported game context is verified.",
        1 => "Graphics changes are staged, reviewed and backed up. Save games are not changed by graphics profiles or gameplay research.",
        _ => "Simple keeps common controls focused. Advanced exposes the verified technical controls when you want them.",
    };

    public string OnboardingActionLabel => OnboardingStep < 2 ? "Next" : "Get started";

    public bool HasUserProfiles => UserProfiles.Count > 0;

    public bool HasImportedProfile => ImportedProfile is not null;

    public bool HasProfilePendingDeletion => ProfilePendingDeletion is not null;

    public bool HasProfilePendingRename => ProfilePendingRename is not null;

    public bool CanConfirmProfileRename =>
        ProfilePendingRename is not null && !IsAnyOperationRunning && !string.IsNullOrWhiteSpace(RenamedProfileName);

    public bool HasProfileComparison => ProfileComparisonRows.Count > 0;

    public bool HasCustomProfileSettings => _editors.Values.Any(editor =>
        editor.TryGetCustomProfileValue(out _));

    public bool CanSaveProfile =>
        !IsAnyOperationRunning &&
        HasCustomProfileSettings &&
        !string.IsNullOrWhiteSpace(NewProfileName);

    public bool IsSaveManagerAvailable => SaveManager is not null;

    public bool IsSaveManagerUnavailable => SaveManager is null;

    /// <summary>Retries are useful only after game identity was verified and save-manager creation failed.</summary>
    public bool CanRetrySaveManagerInitialization => _verifiedGameContext is not null && SaveManager is null;

    public bool ShouldKeepRunningInTrayOnClose =>
        SaveManager is { IsWatchdogEnabled: true, KeepRunningInTrayWhenClosing: true };

    public bool HasPendingChanges => PendingChanges.Count > 0;

    public bool CanUndo => CanRevertLast && !HasPendingChanges && !IsReviewingChanges && !IsAnyOperationRunning;

    public bool CanRemoveToolChanges =>
        HasRemovableToolChanges && !HasPendingChanges && !IsReviewingChanges && !IsAnyOperationRunning;

    public bool ShowPendingActions => HasPendingChanges && !IsReviewingChanges;

    public bool ShowReviewActions => IsReviewingChanges;

    public bool ShowBottomBar => ShowGraphicsView || ShowGameplayView || HasPendingChanges || HasGameplayPendingChanges || IsReviewingChanges;

    public bool IsAnyOperationRunning =>
        IsBusy ||
        IsDetailedHardwareDetectionRunning ||
        _mutationGate.IsBusy ||
        (SaveManager?.IsBusy ?? false);

    partial void OnIsDetailedHardwareDetectionRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyOperationRunning));
        OnPropertyChanged(nameof(CanRunDetailedHardwareDetection));
        OnPropertyChanged(nameof(CanShowHardwareScanAction));
        OnPropertyChanged(nameof(CanReviewGameplay));
        OnPropertyChanged(nameof(CanResetGameplay));
        OnPropertyChanged(nameof(CanEditGameplay));
        OnPropertyChanged(nameof(CanStageHardwareRecommendation));
        OnPropertyChanged(nameof(DetailedHardwareActionLabel));
        OnPropertyChanged(nameof(HomeTitle));
    }
    public bool CanEditSettings => !IsReviewingChanges && !IsAnyOperationRunning;

    public bool CanRestoreGameDefaults =>
        !IsAnyOperationRunning &&
        !HasPendingChanges &&
        !IsReviewingChanges &&
        _editors.Values.Any(editor => editor.HasActiveOverride);

    public bool IsSimpleMode => !IsAdvancedMode;

    public bool IsAllGraphicsFilter => GraphicsFilter == "All";

    public bool IsModifiedGraphicsFilter => GraphicsFilter == "Modified";

    public IReadOnlyList<BuiltInGraphicsPresetViewModel> PrimaryGraphicsPresets =>
        BuiltInGraphicsPresets.Where(preset => preset.Name is not "Ultra Setup" and not "Low VRAM Setup").ToArray();

    public IReadOnlyList<BuiltInGraphicsPresetViewModel> AdditionalGraphicsTweaks =>
        BuiltInGraphicsPresets.Where(preset => preset.Name is "Ultra Setup" or "Low VRAM Setup").ToArray();

    public bool IsGameDefaultsGraphicsFilter => GraphicsFilter == "Game defaults";

    public string GraphicsPresetsToggleLabel => IsGraphicsPresetsExpanded ? "Hide presets" : "Show presets";

    public string GameplayPresetsToggleLabel => IsGameplayPresetsExpanded ? "Hide presets" : "Show presets";

    public bool HasNoSearchResults =>
        IsAdvancedMode &&
        SearchText.Trim().Length > 0 &&
        FeatureGroups.Count == 0;

    public int CustomOverrideCount =>
        _editors.Values.Count(editor => editor.HasActiveOverride);

    public bool HasGamePreset => _snapshot?.IsGameDetected == true;
    public string GamePresetName
    {
        get
        {
            var menu = _allFeatureGroups.FirstOrDefault(g => g.Id == "game-menu-settings");
            return menu?.Summary ?? "Unknown";
        }
    }

    public string ReviewSummary => _reviewIsGameplay
        ? _reviewRemovesGameplayPak
            ? "Restore game-default difficulty"
            : "Review gameplay difficulty"
        : _reviewIsToolChangeRemoval
        ? "Remove Configurator changes"
        : ReviewChanges.Count == 1
        ? "Review 1 change before writing"
        : $"Review {ReviewChanges.Count} changes before writing";

    public string ReviewDescription => _reviewIsGameplay
        ? "AEC will change only its verified gameplay PAK and ownership record. Save games are not edited. Runtime behavior is still marked as awaiting in-game verification."
        : _reviewIsToolChangeRemoval
        ? "The listed files will be restored to their state before you first used this Configurator. Save games and other mods are not changed"
        : "Check the old and new values before anything is written";

    public string ConfirmReviewLabel => _reviewIsGameplay
        ? _reviewRemovesGameplayPak ? "Remove gameplay PAK" : "Confirm & Install"
        : _reviewIsToolChangeRemoval ? "Remove Configurator changes" : "Confirm & Apply";

    public string PendingSummary => PendingChanges.Count switch
    {
        0 => "No pending changes",
        1 => "1 pending change",
        _ => $"{PendingChanges.Count} pending changes",
    };

    public string PendingDetails => string.Join(
        " · ",
        PendingChanges.Take(3).Select(change => $"{change.Name}: {change.DesiredValue}")) +
        (PendingChanges.Count > 3 ? $" · +{PendingChanges.Count - 3} more" : string.Empty);

    public int SettingCount => Settings.Count;

    public async Task InitializeAsync()
    {
        RefreshProfileLibrary();
        if (await RefreshFromDiskAsync())
        {
            ShowMessage(
                GetRefreshMessage(isReload: false),
                _saveGamesRefreshFailed
                    ? "#D6BC84"
                    : _lastRefreshRecoveredOperation || _lastSaveRecoveryMessage is not null
                        ? "#B4D941"
                        : "#7A877A");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsAnyOperationRunning)
        {
            return;
        }

        if (HasPendingChanges)
        {
            ShowMessage("Apply or discard the pending changes before refreshing.", "#E04D42");
            return;
        }

        if (await RefreshFromDiskAsync())
        {
            ShowMessage(
                GetRefreshMessage(isReload: true),
                _saveGamesRefreshFailed ? "#D6BC84" : "#B4D941");
        }
    }

    [RelayCommand]
    private void ShowSimple() => IsAdvancedMode = false;

    [RelayCommand]
    private void ShowAdvanced() => IsAdvancedMode = true;

    [RelayCommand]
    private void ToggleGraphicsPresets() => IsGraphicsPresetsExpanded = !IsGraphicsPresetsExpanded;

    [RelayCommand]
    private void ShowAllGraphics() => GraphicsFilter = "All";

    [RelayCommand]
    private void ShowModifiedGraphics() => GraphicsFilter = "Modified";

    [RelayCommand]
    private void ShowGameDefaultsGraphics() => GraphicsFilter = "Game defaults";

    [RelayCommand]
    private void LoadBuiltInGraphicsPreset(BuiltInGraphicsPresetViewModel? preset)
    {
        if (preset is not null)
        {
            LoadBuiltInTweakForReview(preset.Profile);
        }
    }

    [RelayCommand]
    private void ResetGraphicsGroup(FeatureGroupRowViewModel? row)
    {
        if (row is null || IsAnyOperationRunning)
        {
            return;
        }

        foreach (FeatureSettingSnapshot setting in _allFeatureGroups
                     .FirstOrDefault(group => group.Id == row.Id)?.Settings ?? [])
        {
            if (_editors.GetValueOrDefault(setting.Id) is { ShowOverrideToggle: true } editor)
            {
                editor.UseGameDefault();
            }
        }
    }
    [RelayCommand]
    private void ShowSaveGames()
    {
        IsHomeView = false;
        IsGameplayView = false;
        IsProfilesView = false;
        IsSettingsView = false;
        IsDiagnosticsView = false;
        IsSaveGamesView = true;
        UpdateViewVisibility();
    }

    [RelayCommand]
    private void ShowGameplay()
    {
        IsHomeView = false;
        IsSaveGamesView = false;
        IsProfilesView = false;
        IsSettingsView = false;
        IsDiagnosticsView = false;
        IsGameplayView = true;
        UpdateViewVisibility();
    }

    [RelayCommand]
    private void OpenGamePakFolder()
    {
        string? pakFolder = GamePakFolderPath;
        if (pakFolder is null || !HasExternalPaks)
        {
            return;
        }

        if (_directoryOpener(pakFolder))
        {
            ShowMessage("Opened the game PAK folder. AEC will not remove external files for you.", "#B4D941");
        }
        else
        {
            ShowMessage("The game PAK folder could not be opened. Check that the game installation still exists.", "#E04D42");
        }
    }

    [RelayCommand]
    private void ShowGameplaySimple() => IsGameplayAdvancedMode = false;

    [RelayCommand]
    private void ShowGameplayAdvanced() => IsGameplayAdvancedMode = true;

    [RelayCommand]
    private void ToggleGameplayPresets() => IsGameplayPresetsExpanded = !IsGameplayPresetsExpanded;

    [RelayCommand]
    private void SelectGameplayPreset(GameplayDifficultyPresetViewModel? preset)
    {
        if (preset is null)
        {
            return;
        }

        foreach (GameplayDifficultyControlViewModel control in GameplaySimpleControls)
        {
            control.MultiplierPercent = control.HigherIsHarder
                ? preset.MultiplierPercent
                : Math.Max(10, 200 - preset.MultiplierPercent);
        }

        foreach (GameplayDifficultyControlViewModel control in GameplayAdvancedControls)
        {
            control.MultiplierPercent = 100;
        }

        SetGameplayDraftStatus(preset.Name);
    }

    [RelayCommand]
    private void ResetGameplay()
    {
        if (!CanResetGameplay)
        {
            return;
        }

        foreach (GameplayDifficultyControlViewModel control in AllGameplayControls)
        {
            control.MultiplierPercent = 100;
        }

        SetGameplayDraftStatus("Game default");
        OpenGameplayReview();
    }

    [RelayCommand]
    private void OpenGameplayReview()
    {
        if (_snapshot is null || !CanReviewGameplay)
        {
            return;
        }

        try
        {
            _reviewPlan = _gameplayEditor.CreatePlan(_snapshot, CurrentGameplaySettings);
            _reviewIsGameplay = true;
            _reviewIsToolChangeRemoval = false;
            _reviewRemovesGameplayPak = CurrentGameplaySettings.IsGameDefault;
            ReviewChanges = _reviewPlan.Changes
                .Select(change => new ChangeReviewRowViewModel(
                    change.DisplayName,
                    $"{change.FileName} · {change.Key}",
                    change.Before ?? "Game default",
                    change.After ?? "Game default"))
                .ToArray();
            OnPropertyChanged(nameof(ReviewSummary));
            OnPropertyChanged(nameof(ReviewDescription));
            OnPropertyChanged(nameof(ConfirmReviewLabel));
            IsReviewingChanges = true;
            ShowMessage("Check every gameplay value, then confirm the PAK operation.", "#FF5A00");
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage(exception.Message, "#E04D42");
        }
    }

    [RelayCommand]
    private void ShowProfiles()
    {
        IsHomeView = false;
        IsSaveGamesView = false;
        IsGameplayView = false;
        IsSettingsView = false;
        IsDiagnosticsView = false;
        IsProfilesView = true;
        RefreshProfileLibrary();
        UpdateViewVisibility();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        IsHomeView = false;
        IsSaveGamesView = false;
        IsGameplayView = false;
        IsProfilesView = false;
        IsDiagnosticsView = false;
        IsSettingsView = true;
        UpdateViewVisibility();
    }

    [RelayCommand]
    private void ShowDiagnostics()
    {
        IsHomeView = false;
        IsSaveGamesView = false;
        IsGameplayView = false;
        IsProfilesView = false;
        IsSettingsView = false;
        IsDiagnosticsView = true;
        UpdateViewVisibility();
    }

    [RelayCommand]
    private void AdvanceOnboarding()
    {
        if (OnboardingStep < 2)
        {
            OnboardingStep++;
            OnPropertyChanged(nameof(OnboardingTitle));
            OnPropertyChanged(nameof(OnboardingDescription));
            OnPropertyChanged(nameof(OnboardingActionLabel));
            return;
        }

        IsOnboardingVisible = false;
        _onboardingCompleted?.Invoke();
    }

    [RelayCommand]
    private void SkipOnboarding()
    {
        IsOnboardingVisible = false;
        _onboardingCompleted?.Invoke();
    }

    public string CreateDiagnosticsReport() => DiagnosticsReportBuilder.Build(
        ProductName,
        Phase,
        DetectionStatus,
        InstallationDetails,
        InstallationPath,
        UserDataPath,
        BinarySettingsPath,
        BinarySettingsStatus,
        SystemDiagnostics,
        HardwareDiagnostics,
        ConfigurationFiles,
        PakFiles,
        Notices);

    [RelayCommand]
    private void LoadHardwareRecommendation()
    {
        BuiltInGraphicsPresetViewModel? preset = BuiltInGraphicsPresets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, HardwareDiagnostics.Recommendation.PresetName, StringComparison.Ordinal));
        if (preset is null)
        {
            return;
        }

        if (HasPendingChanges || IsReviewingChanges)
        {
            ShowMessage("Apply or discard current changes before using a hardware recommendation.", "#D6BC84");
            return;
        }

        if (CanStageHardwareRecommendation)
        {
            LoadBuiltInGraphicsPreset(preset);
            if (HasPendingChanges)
            {
                OpenReview();
            }
        }
    }

    [RelayCommand]
    private async Task RunDetailedHardwareDetectionAsync()
    {
        if (!CanRunDetailedHardwareDetection)
        {
            return;
        }

        IsDetailedHardwareDetectionRunning = true;
        try
        {
            HardwareScanMessage = "Checking hardware details…";
            ShowMessage(HardwareScanMessage, "#D6BC84");
            HardwareSnapshot detailedSnapshot = await Task.Run(() => _hardwareProbe.Inspect(includeDetailedGraphics: true));
            HardwareDiagnostics = HardwareDiagnosticsViewModel.FromSnapshot(detailedSnapshot);
            _detailedHardwareSnapshot = detailedSnapshot;
            HasAcknowledgedDetailedHardwareScan = true;
            _detailedHardwareScanCompleted?.Invoke(detailedSnapshot);
            HardwareScanMessage = detailedSnapshot.HasGraphicsMemory
                ? "Hardware details checked. AEC can now offer a conservative graphics recommendation."
                : "Hardware details checked. Windows did not report dedicated GPU memory, so AEC will not guess a graphics recommendation.";
            ShowMessage(HardwareScanMessage, detailedSnapshot.HasGraphicsMemory ? "#B4D941" : "#D6BC84");
        }
        finally
        {
            IsDetailedHardwareDetectionRunning = false;
            OnPropertyChanged(nameof(CanStageHardwareRecommendation));
            OnPropertyChanged(nameof(CanRunDetailedHardwareDetection));
            OnPropertyChanged(nameof(CanShowHardwareScanAction));
        }
    }

    [RelayCommand]
    private void StartCreatingProfile()
    {
        if (IsAnyOperationRunning)
        {
            return;
        }

        IsCreatingProfile = true;
        NewProfileName = "";
        NewProfileDescription = "";
    }

    [RelayCommand]
    private void CreateProfileFromGraphics()
    {
        ShowProfiles();
        StartCreatingProfile();
    }

    [RelayCommand]
    private void CancelCreatingProfile() => IsCreatingProfile = false;

    [RelayCommand]
    private void CreateProfile()
    {
        if (IsAnyOperationRunning)
        {
            return;
        }

        var graphics = new List<ProfileSetting>();
        foreach ((string id, SettingEditorViewModel editor) in _editors)
        {
            if (!EditableSettingsCatalog.IsDefined(editor.Key))
            {
                ShowMessage($"{FindSettingName(id)} cannot be included because this setting is not supported by profiles yet.", "#D6BC84");
                return;
            }
            if (editor.TryGetCustomProfileValue(out string? value))
            {
                graphics.Add(new ProfileSetting(editor.Key, value!));
            }
        }
        if (graphics.Count == 0)
        {
            ShowMessage("There are no custom graphics values to save yet.", "#D6BC84");
            return;
        }

        try
        {
            string version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
            var profile = new UserProfile(
                UserProfile.CurrentSchemaVersion,
                NewProfileName.Trim(),
                string.IsNullOrWhiteSpace(NewProfileDescription) ? null : NewProfileDescription.Trim(),
                DateTimeOffset.UtcNow,
                version,
                graphics,
                [],
                []);
            StoredUserProfile saved = _profileLibrary.Save(profile);
            RefreshProfileLibrary();
            IsCreatingProfile = false;
            ShowMessage($"Saved profile: {saved.Profile.Name}", "#B4D941");
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"The profile was not saved: {exception.Message}", "#E04D42");
        }
    }

    public void ImportProfile(string path)
    {
        if (IsAnyOperationRunning)
        {
            return;
        }

        try
        {
            UserProfile profile = _profileLibrary.ReadExternal(path);
            ImportedProfile = new ImportedProfileViewModel(profile, Path.GetFileName(path));
            ShowMessage("Profile checked. Choose whether to add it to your library or load it for review.", "#B4D941");
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"The profile was not imported: {exception.Message}", "#E04D42");
        }
    }

    [RelayCommand]
    private void AddImportedProfileToLibrary()
    {
        if (ImportedProfile is null || IsAnyOperationRunning)
        {
            return;
        }

        try
        {
            StoredUserProfile saved = _profileLibrary.Save(ImportedProfile.Profile);
            RefreshProfileLibrary();
            ImportedProfile = null;
            ShowMessage($"Added to My profiles: {saved.Profile.Name}", "#B4D941");
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"The profile was not saved: {exception.Message}", "#E04D42");
        }
    }

    [RelayCommand]
    private void LoadImportedProfile()
    {
        if (ImportedProfile is not null)
        {
            LoadProfileForReview(ImportedProfile.Profile);
        }
    }

    [RelayCommand]
    private void LoadProfile(UserProfileRowViewModel? profile)
    {
        if (profile is null || IsAnyOperationRunning)
        {
            return;
        }

        try
        {
            LoadProfileForReview(_profileLibrary.Read(profile.Id));
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"The saved profile could not be loaded: {exception.Message}", "#E04D42");
        }
    }

    [RelayCommand]
    private void CompareProfile(UserProfileRowViewModel? profile)
    {
        if (profile is null)
        {
            return;
        }

        try
        {
            UserProfile source = _profileLibrary.Read(profile.Id);
            ProfileComparisonName = source.Name;
            var profileSettings = source.Graphics.ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);
            var resetKeys = _editors.Values
                .Where(editor => editor.ShowOverrideToggle && editor.HasCurrentOverride && !profileSettings.ContainsKey(editor.Key))
                .Select(editor => editor.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            ProfileComparisonRows = profileSettings.Keys
                .Concat(resetKeys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(key =>
            {
                FeatureSettingSnapshot? current = _allFeatureGroups
                    .SelectMany(group => group.Settings)
                    .FirstOrDefault(candidate => string.Equals(candidate.TechnicalKey, key, StringComparison.OrdinalIgnoreCase));
                TryGetEditorByTechnicalKey(key, out SettingEditorViewModel? editor);
                return new ProfileComparisonRowViewModel(
                    current?.Name ?? key,
                    editor is null
                        ? "Not available for this game setup"
                        : editor.FormatValue(editor.GetProfileComparisonValue()),
                    profileSettings.TryGetValue(key, out ProfileSetting? setting)
                        ? editor?.FormatValue(setting.Value) ?? setting.Value
                        : "→ Game default");
            }).ToArray();
            OnPropertyChanged(nameof(HasProfileComparison));
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"The saved profile could not be compared: {exception.Message}", "#E04D42");
        }
    }

    [RelayCommand]
    private void ClearProfileComparison()
    {
        ProfileComparisonName = null;
        ProfileComparisonRows = [];
        OnPropertyChanged(nameof(HasProfileComparison));
    }

    public UserProfile? GetProfileForExport(string id)
    {
        try
        {
            return _profileLibrary.Read(id);
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"The saved profile could not be exported: {exception.Message}", "#E04D42");
            return null;
        }
    }

    public void ReportProfileFileError(string action) =>
        ShowMessage($"The profile {action} could not be completed.", "#E04D42");

    public void ReportDiagnosticsCopyError() =>
        ShowMessage("Diagnostics could not be copied to the clipboard.", "#E04D42");

    [RelayCommand]
    private void RequestProfileDeletion(UserProfileRowViewModel? profile)
    {
        if (profile is null || IsAnyOperationRunning)
        {
            return;
        }

        ProfilePendingDeletion = profile;
    }

    [RelayCommand]
    private void CancelProfileDeletion() => ProfilePendingDeletion = null;

    [RelayCommand]
    private void ConfirmProfileDeletion()
    {
        UserProfileRowViewModel? profile = ProfilePendingDeletion;
        if (profile is null || IsAnyOperationRunning)
        {
            return;
        }

        try
        {
            _profileLibrary.Delete(profile.Id);
            ProfilePendingDeletion = null;
            RefreshProfileLibrary();
            ShowMessage($"Deleted profile: {profile.Name}", "#B4D941");
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"The saved profile could not be deleted: {exception.Message}", "#E04D42");
        }
    }

    [RelayCommand]
    private void DuplicateProfile(UserProfileRowViewModel? profile)
    {
        if (profile is null || IsAnyOperationRunning)
        {
            return;
        }

        try
        {
            UserProfile duplicate = profile.Profile with
            {
                Name = profile.Name + " copy",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            StoredUserProfile saved = _profileLibrary.Save(duplicate);
            RefreshProfileLibrary();
            ShowMessage($"Created copy: {saved.Profile.Name}", "#B4D941");
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"The profile could not be duplicated: {exception.Message}", "#E04D42");
        }
    }

    [RelayCommand]
    private void RequestProfileRename(UserProfileRowViewModel? profile)
    {
        if (profile is null || IsAnyOperationRunning)
        {
            return;
        }

        ProfilePendingRename = profile;
        RenamedProfileName = profile.Name;
    }

    [RelayCommand]
    private void CancelProfileRename()
    {
        ProfilePendingRename = null;
        RenamedProfileName = "";
    }

    [RelayCommand]
    private void ConfirmProfileRename()
    {
        UserProfileRowViewModel? profile = ProfilePendingRename;
        if (profile is null || !CanConfirmProfileRename)
        {
            return;
        }

        try
        {
            UserProfile renamed = profile.Profile with { Name = RenamedProfileName.Trim() };
            StoredUserProfile saved = _profileLibrary.Save(renamed);
            try
            {
                _profileLibrary.Delete(profile.Id);
            }
            catch (Exception exception) when (IsExpectedUserOperationException(exception))
            {
                RefreshProfileLibrary();
                ProfilePendingRename = null;
                ShowMessage($"Created renamed copy {saved.Profile.Name}, but kept the original: {exception.Message}", "#D6BC84");
                return;
            }

            RefreshProfileLibrary();
            ProfilePendingRename = null;
            RenamedProfileName = "";
            ShowMessage($"Renamed profile: {saved.Profile.Name}", "#B4D941");
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"The profile could not be renamed: {exception.Message}", "#E04D42");
        }
    }

    private void OnChildPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SaveManagerViewModel.IsBusy))
        {
            NotifyMutationAvailability();
        }

        if (e.PropertyName is nameof(SaveManagerViewModel.Slots) or nameof(SaveManagerViewModel.BackupHealthSummary))
        {
            OnPropertyChanged(nameof(HomeSavesSummary));
        }
    }
    private void UpdateViewVisibility()
    {
        OnPropertyChanged(nameof(ShowHomeView));
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowSaveGamesView));
        OnPropertyChanged(nameof(ShowGameplayView));
        OnPropertyChanged(nameof(ShowProfilesView));
        OnPropertyChanged(nameof(ShowSettingsView));
        OnPropertyChanged(nameof(ShowDiagnosticsView));
        OnPropertyChanged(nameof(IsGraphicsSectionActive));
        OnPropertyChanged(nameof(IsSettingsSectionActive));
        OnPropertyChanged(nameof(PageContextLabel));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    [RelayCommand]
    private void ShowGraphics()
    {
        IsHomeView = false;
        IsSaveGamesView = false;
        IsGameplayView = false;
        IsProfilesView = false;
        IsSettingsView = false;
        IsDiagnosticsView = false;
        UpdateViewVisibility();
    }

    [RelayCommand]
    private void ShowHome()
    {
        IsSaveGamesView = false;
        IsGameplayView = false;
        IsProfilesView = false;
        IsSettingsView = false;
        IsDiagnosticsView = false;
        IsHomeView = true;
        UpdateViewVisibility();
    }

    partial void OnIsHomeViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHomeView));
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    partial void OnIsGameplayViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowGameplayView));
        OnPropertyChanged(nameof(ShowProfilesView));
        OnPropertyChanged(nameof(ShowSettingsView));
        OnPropertyChanged(nameof(ShowDiagnosticsView));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    partial void OnIsGameplayAdvancedModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsGameplaySimpleMode));
    }

    partial void OnIsProfilesViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowProfilesView));
        OnPropertyChanged(nameof(ShowSettingsView));
        OnPropertyChanged(nameof(ShowDiagnosticsView));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    partial void OnIsSettingsViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowSettingsView));
        OnPropertyChanged(nameof(ShowDiagnosticsView));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    partial void OnIsDiagnosticsViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowDiagnosticsView));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    partial void OnIsHighContrastEnabledChanged(bool value)
    {
        _highContrastChanged?.Invoke(value);
    }

    partial void OnIsDiscordRichPresenceEnabledChanged(bool value) =>
        _discordRichPresenceChanged?.Invoke(value);

    partial void OnIsExperimentalGraphicsSettingsEnabledChanged(bool value)
    {
        _experimentalGraphicsSettingsChanged?.Invoke(value);
        ApplyViewMode();
    }

    partial void OnIsExperimentalGameplaySettingsEnabledChanged(bool value)
    {
        _experimentalGameplaySettingsChanged?.Invoke(value);
        NotifyGameplayRangeChanged();
        UpdateGameplayControlRanges();
    }

    partial void OnIncludeClanInGameplayPatchChanged(bool value)
    {
        CloseReview();
        NotifyGameplayStateChanged();
    }

    partial void OnIsGraphicsPresetsExpandedChanged(bool value) =>
        OnPropertyChanged(nameof(GraphicsPresetsToggleLabel));

    partial void OnIsGameplayPresetsExpandedChanged(bool value) =>
        OnPropertyChanged(nameof(GameplayPresetsToggleLabel));

    partial void OnHasAcknowledgedDetailedHardwareScanChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowHardwareScanAction));
        OnPropertyChanged(nameof(DetailedHardwareActionLabel));
    }

    partial void OnHardwareScanMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasHardwareScanMessage));

    partial void OnUserProfilesChanged(IReadOnlyList<UserProfileRowViewModel> value) =>
        OnPropertyChanged(nameof(HasUserProfiles));

    partial void OnImportedProfileChanged(ImportedProfileViewModel? value) =>
        OnPropertyChanged(nameof(HasImportedProfile));

    partial void OnProfilePendingDeletionChanging(UserProfileRowViewModel? value)
    {
        if (ProfilePendingDeletion is not null && !ReferenceEquals(ProfilePendingDeletion, value))
        {
            ProfilePendingDeletion.IsPendingDeletion = false;
        }
    }

    partial void OnProfilePendingDeletionChanged(UserProfileRowViewModel? value)
    {
        if (value is not null)
        {
            value.IsPendingDeletion = true;
        }

        OnPropertyChanged(nameof(HasProfilePendingDeletion));
    }

    partial void OnProfilePendingRenameChanged(UserProfileRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasProfilePendingRename));
        OnPropertyChanged(nameof(CanConfirmProfileRename));
    }

    partial void OnNewProfileNameChanged(string value) =>
        OnPropertyChanged(nameof(CanSaveProfile));

    partial void OnRenamedProfileNameChanged(string value) =>
        OnPropertyChanged(nameof(CanConfirmProfileRename));

    [RelayCommand]
    private void DiscardChanges()
    {
        CloseReview();
        foreach (SettingEditorViewModel editor in _editors.Values)
        {
            editor.Reset();
        }

        UpdatePendingChanges();
        ShowMessage("Pending changes discarded. No files were changed.", "#7A877A");
    }

    [RelayCommand]
    private void RestoreGameDefaults()
    {
        if (!CanRestoreGameDefaults)
        {
            ShowMessage(
                _editors.Values.Any(editor => editor.HasActiveOverride)
                    ? "Apply or discard the current work before restoring game defaults."
                    : "No configurator overrides are active.",
                "#7A877A");
            return;
        }

        foreach (SettingEditorViewModel editor in _editors.Values.Where(editor => editor.HasActiveOverride))
        {
            editor.UseGameDefault();
        }

        UpdatePendingChanges();
        OpenReview();
        ShowMessage(
            "Review the complete removal of configurator overrides, then confirm.",
            "#FF5A00");
    }

    [RelayCommand]
    private void OpenReview()
    {
        if (_snapshot is null || !HasPendingChanges || IsAnyOperationRunning)
        {
            return;
        }

        try
        {
            SettingChangeRequest[] requests = [.. _editors
                .Where(pair => pair.Value.HasChanges)
                .Select(pair => pair.Value.CreateRequest(FindSettingName(pair.Key)))];
            _reviewPlan = _settingsEditor.CreatePlan(_snapshot, requests);
            _reviewIsToolChangeRemoval = false;
            ReviewChanges = _reviewPlan.Changes
                .Select(change => new ChangeReviewRowViewModel(
                    change.DisplayName,
                    $"{change.FileName} · {change.Key}",
                    FormatReviewValue(change.Key, change.Before),
                    FormatReviewValue(change.Key, change.After)))
                .ToArray();
            IsReviewingChanges = true;
            ShowMessage("Check every value, then confirm the write.", "#FF5A00");
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, "#E04D42");
        }
    }

    [RelayCommand]
    private void RemoveToolChanges()
    {
        if (_snapshot is null || !CanRemoveToolChanges)
        {
            return;
        }

        try
        {
            _reviewPlan = _settingsEditor.CreateRemoveToolChangesPlan(_snapshot);
            _reviewIsToolChangeRemoval = true;
            ReviewChanges = _reviewPlan.Changes
                .Select(change => new ChangeReviewRowViewModel(
                    change.DisplayName,
                    $"{change.FileName} | {change.Key}",
                    FormatReviewValue(change.Key, change.Before),
                    FormatReviewValue(change.Key, change.After)))
                .ToArray();
            OnPropertyChanged(nameof(ReviewSummary));
            OnPropertyChanged(nameof(ReviewDescription));
            OnPropertyChanged(nameof(ConfirmReviewLabel));
            IsReviewingChanges = true;
            ShowMessage("Review the tool-managed files before removing those changes.", "#FF5A00");
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, "#E04D42");
        }
    }

    [RelayCommand]
    private void CancelReview()
    {
        CloseReview();
        ShowMessage("Review closed. Your pending values are unchanged.", "#7A877A");
    }

    [RelayCommand]
    private async Task ConfirmApplyAsync()
    {
        using IDisposable? mutation = _mutationGate.TryEnter();
        if (mutation is null)
        {
            return;
        }
        SettingsChangePlan? plan = _reviewPlan;
        if (plan is null || !IsReviewingChanges)
        {
            return;
        }

        bool isToolChangeRemoval = plan.IsToolChangeRemoval;
        bool isGameplay = _reviewIsGameplay;
        bool removesGameplayPak = _reviewRemovesGameplayPak;
        _reviewPlan = null;
        _reviewIsToolChangeRemoval = false;
        _reviewIsGameplay = false;
        _reviewRemovesGameplayPak = false;
        OnPropertyChanged(nameof(ReviewSummary));
        OnPropertyChanged(nameof(ReviewDescription));
        OnPropertyChanged(nameof(ConfirmReviewLabel));
        IsReviewingChanges = false;
        ReviewChanges = [];
        IsBusy = true;
        ShowMessage(
            isGameplay
                ? removesGameplayPak ? "Removing the verified AEC gameplay PAK..." : "Building and installing the reviewed gameplay PAK..."
                : isToolChangeRemoval ? "Removing verified tool changes..." : "Applying the reviewed changes...",
            "#FF5A00");
        SettingsOperationResult result;
        try
        {
            result = await Task.Run(() => isGameplay
                ? _gameplayEditor.Apply(plan)
                : _settingsEditor.Apply(plan));
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"No changes were kept: {exception.Message}", "#E04D42");
            return;
        }
        finally
        {
            IsBusy = false;
        }
        if (ShouldRefreshAfter(result) && !await RefreshFromDiskAsync())
        {
            ShowMessage("The operation finished, but the configuration could not be refreshed from disk.", "#E04D42");
            return;
        }

        ShowMessage(FormatOperationMessage(result), GetOperationAccent(result));
    }

    [RelayCommand]
    private async Task RevertLastAsync()
    {
        if (_snapshot is null || HasPendingChanges || IsReviewingChanges)
        {
            return;
        }

        using IDisposable? mutation = _mutationGate.TryEnter();
        if (mutation is null)
        {
            return;
        }

        IsBusy = true;
        ShowMessage("Restoring the last configurator backup...", "#FF5A00");
        SettingsOperationResult result;
        try
        {
            result = await Task.Run(() => _settingsEditor.RevertLast(_snapshot));
        }
        catch (Exception exception)
        {
            ShowMessage($"Nothing was restored: {exception.Message}", "#E04D42");
            return;
        }
        finally
        {
            IsBusy = false;
        }
        if (ShouldRefreshAfter(result) && !await RefreshFromDiskAsync())
        {
            ShowMessage("The backup was restored, but the configuration could not be refreshed.", "#E04D42");
            return;
        }

        ShowMessage(FormatOperationMessage(result), GetOperationAccent(result));
    }

    private static bool ShouldRefreshAfter(SettingsOperationResult result) =>
        result.Succeeded || result.Status == SettingsOperationStatus.PartialRollbackRequired;

    private static string GetOperationAccent(SettingsOperationResult result) => result.Status switch
    {
        SettingsOperationStatus.PartialRollbackRequired => "#D6BC84",
        SettingsOperationStatus.Failed => "#E04D42",
        _ => result.Succeeded ? "#B4D941" : "#E04D42",
    };

    private static string FormatOperationMessage(SettingsOperationResult result) =>
        result.Status == SettingsOperationStatus.PartialRollbackRequired &&
        !result.Message.Contains("manual recovery required", StringComparison.OrdinalIgnoreCase)
            ? $"Manual recovery required. {result.Message}"
            : result.Message;

    partial void OnIsAdvancedModeChanged(bool value)
    {
        if (!value)
        {
            SearchText = string.Empty;
        }

        OnPropertyChanged(nameof(IsSimpleMode));
        ApplyViewMode();
    }

    partial void OnIsSaveGamesViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowSaveGamesView));
        OnPropertyChanged(nameof(ShowDiagnosticsView));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    partial void OnSaveManagerChanging(SaveManagerViewModel? oldValue, SaveManagerViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnChildPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnChildPropertyChanged;
    }

    partial void OnSaveManagerChanged(SaveManagerViewModel? value)
    {
        OnPropertyChanged(nameof(IsSaveManagerAvailable));
        OnPropertyChanged(nameof(IsAnyOperationRunning));
        OnPropertyChanged(nameof(IsSaveManagerUnavailable));
    }

    private CancellationTokenSource? _searchDebounceSource;
    private Task? _searchDebounceTask;

    partial void OnSearchTextChanged(string value)
    {
        CancellationTokenSource? previous = _searchDebounceSource;
        previous?.Cancel();
        previous?.Dispose();
        _searchDebounceSource = new CancellationTokenSource();

        CancellationToken token = _searchDebounceSource.Token;
        _searchDebounceTask = DebouncedSearchApplyAsync(token);
    }

    partial void OnGraphicsFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsAllGraphicsFilter));
        OnPropertyChanged(nameof(IsModifiedGraphicsFilter));
        OnPropertyChanged(nameof(IsGameDefaultsGraphicsFilter));
        ApplyViewMode();
    }

    private async Task DebouncedSearchApplyAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
            if (Avalonia.Application.Current is null)
            {
                // Headless tests do not have an Avalonia application or message pump.
                ApplyViewMode();
                return;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(ApplyViewMode);
        }
        catch (OperationCanceledException)
        {
            // A newer search request replaced this one.
        }
    }
    partial void OnCanRevertLastChanged(bool value) => OnPropertyChanged(nameof(CanUndo));

    partial void OnHasRemovableToolChangesChanged(bool value) => OnPropertyChanged(nameof(CanRemoveToolChanges));

    partial void OnIsBusyChanged(bool value)
    {
        NotifyMutationAvailability();
        OnPropertyChanged(nameof(HomeTitle));
    }

    private void OnMutationGateChanged(object? sender, EventArgs e) =>
        NotifyMutationAvailability();

    private void NotifyMutationAvailability()
    {
        OnPropertyChanged(nameof(IsAnyOperationRunning));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRemoveToolChanges));
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanRestoreGameDefaults));
        OnPropertyChanged(nameof(CanReviewGameplay));
        OnPropertyChanged(nameof(CanResetGameplay));
        OnPropertyChanged(nameof(CanEditGameplay));
        OnPropertyChanged(nameof(HasCustomProfileSettings));
        OnPropertyChanged(nameof(CanSaveProfile));
        OnPropertyChanged(nameof(CanConfirmProfileRename));
        OnPropertyChanged(nameof(CanStageHardwareRecommendation));
        OnPropertyChanged(nameof(CanRunDetailedHardwareDetection));
        OnPropertyChanged(nameof(CanShowHardwareScanAction));
    }

    partial void OnIsReviewingChangesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRemoveToolChanges));
        OnPropertyChanged(nameof(ShowPendingActions));
        OnPropertyChanged(nameof(ShowBottomBar));
        OnPropertyChanged(nameof(ShowReviewActions));
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanRestoreGameDefaults));
        OnPropertyChanged(nameof(CanReviewGameplay));
        OnPropertyChanged(nameof(CanResetGameplay));
        OnPropertyChanged(nameof(CanEditGameplay));
    }

    partial void OnReviewChangesChanged(IReadOnlyList<ChangeReviewRowViewModel> value) =>
        OnPropertyChanged(nameof(ReviewSummary));

    private void SetDetection(string status, string statusColor, string dotColor)
    {
        DetectionStatus = status;
        DetectionColor = statusColor;
        DetectionDotColor = dotColor;
        OnPropertyChanged(nameof(DetectionStatus));
        OnPropertyChanged(nameof(HomeTitle));
    }

    private async Task<bool> RefreshFromDiskAsync()
    {
        CloseReview();
        _lastRefreshRecoveredOperation = false;
        _lastSaveRecoveryMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(HomeTitle));
        DetectionStatus = "Scanning game files";
        ShowMessage("Reading the installation and settings...", "#FF5A00");
        try
        {
            Task<GameInspectionSnapshot> inspectionTask = Task.Run(_inspector.Inspect);
            Task<HardwareSnapshot> hardwareTask = Task.Run(() => _hardwareProbe.Inspect());
            HardwareSnapshot ordinaryHardware = await hardwareTask;
            HardwareDiagnostics = HardwareDiagnosticsViewModel.FromSnapshot(_detailedHardwareSnapshot ?? ordinaryHardware);
            OnPropertyChanged(nameof(CanStageHardwareRecommendation));
            GameInspectionSnapshot snapshot = await inspectionTask;
            if (await Task.Run(() => _settingsEditor.RecoverInterruptedChanges(snapshot)))
            {
                _lastRefreshRecoveredOperation = true;
                snapshot = await Task.Run(_inspector.Inspect);
            }
            bool canKeepChildState = _verifiedGameContext?.Matches(snapshot) == true &&
                SaveManager is not null;
            _saveGamesRefreshFailed = false;
            _snapshot = snapshot;
            UpdateGameplayCatalog(snapshot);
            _verifiedGameContext = VerifyGameContext(snapshot);
            if (snapshot.HasErrors)
            {
                SetDetection("Ancestors detected with problems", "#D6BC84", "#D6BC84");
            }
            else if (_verifiedGameContext is not null)
            {
                SetDetection("Ancestors is ready", "#B4D941", "#B4D941");
            }
            else if (snapshot.IsGameDetected)
            {
                SetDetection("Ancestors detected but not supported for editing", "#D6BC84", "#D6BC84");
            }
            else if (snapshot.Notices.Any(notice => string.Equals(notice.Code, "game.multiple-installations", StringComparison.Ordinal)))
            {
                SetDetection("Multiple Ancestors installations detected", "#D6BC84", "#D6BC84");
            }
            else
            {
                SetDetection("Ancestors installation not detected", "#7A877A", "#7A877A");
            }
            InstallationPath = snapshot.Installation?.InstallDirectory ?? "Not detected";
            InstallationDetails = snapshot.Installation is null
                ? snapshot.Notices.Any(notice => string.Equals(notice.Code, "game.multiple-installations", StringComparison.Ordinal))
                    ? "More than one installation was found. AEC will not choose one automatically."
                    : "Store and build unknown"
                : FormatInstallation(snapshot.Installation);
            UserDataPath = snapshot.UserDataDirectory ?? "Not detected";
            BinarySettingsPath = snapshot.BinarySettingsFile?.FullPath ?? "Not detected";
            BinarySettingsStatus = snapshot.BinarySettingsFile?.FormatStatus ?? "Not inspected";
            LogDetection(
                snapshot.HasErrors
                    ? "problems"
                    : _verifiedGameContext is not null
                        ? "ready"
                        : snapshot.IsGameDetected
                            ? "unsupported"
                            : "not-found");

            _allFeatureGroups = ReadableSettingsCatalog.CreateFeatureGroups(snapshot);
            OnPropertyChanged(nameof(CustomOverrideCount));
            OnPropertyChanged(nameof(GamePresetName));
            OnPropertyChanged(nameof(HomeGraphicsSummary));
            OnPropertyChanged(nameof(HomeGameplaySummary));
            NotifyExternalPakFolderState();
            RebuildEditors();
            UpdatePendingChanges();
            ApplyViewMode();
            LoadTechnicalDetails(snapshot);
            CanRevertLast = _settingsEditor.CanRevertLast(snapshot);
            HasRemovableToolChanges = _settingsEditor.CanRemoveToolChanges(snapshot);
            if (canKeepChildState)
            {
                _saveGamesRefreshFailed = !await SaveManager!.RefreshSilentlyAsync();
                if (!_saveGamesRefreshFailed)
                {
                    _lastSaveRecoveryMessage = SaveManager.LastRecoveryMessage;
                }
            }
            else
            {
                SaveManager?.Dispose();
                SaveManager = await CreateSaveManagerAsync();
                SaveManager?.Activate();
                OnPropertyChanged(nameof(HomeSavesSummary));
                _saveGamesRefreshFailed = _verifiedGameContext is not null && SaveManager is null;
                _lastSaveRecoveryMessage = SaveManager?.LastRecoveryMessage;
            }
            return true;
        }
        catch (Exception exception)
        {
            _snapshot = null;
            _verifiedGameContext = null;
            _saveGamesRefreshFailed = false;
            SetDetection("Scan failed", "#E04D42", "#E04D42");
            InstallationPath = "Not available";
            InstallationDetails = "The previous result was cleared";
            UserDataPath = "Not available";
            BinarySettingsPath = "Not available";
            BinarySettingsStatus = "Not inspected";
            FeatureGroups = [];
            ConfigurationFiles = [];
            Settings = [];
            PakFiles = [];
            Notices = [new NoticeRowViewModel("Error", exception.Message)];
            CanRevertLast = false;
            HasRemovableToolChanges = false;
            // Clear editor and writer state after a failed scan.
            foreach (SettingEditorViewModel editor in _editors.Values)
            {
                editor.Changed -= OnEditorChanged;
            }
            _editors.Clear();
            _allFeatureGroups = [];
            PendingChanges = [];
            SaveManager?.Dispose();
            SaveManager = null;
            OnPropertyChanged(nameof(HomeSavesSummary));
            NotifyExternalPakFolderState();
            ShowMessage($"Scan failed: {exception.Message}", "#E04D42");
            LogDetection("failed: " + exception.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HomeTitle));
        }
    }

    private static VerifiedGameContext? VerifyGameContext(GameInspectionSnapshot snapshot) =>
        VerifiedGameContext.TryCreateFromSnapshot(snapshot);

    private string GetRefreshMessage(bool isReload)
    {
        if (_lastRefreshRecoveredOperation && _lastSaveRecoveryMessage is not null)
        {
            return isReload
                ? "Interrupted settings and save operations were recovered safely and the configuration was reloaded."
                : "Interrupted settings and save operations were recovered safely.";
        }

        if (_lastRefreshRecoveredOperation)
        {
            return _saveGamesRefreshFailed
                ? "An interrupted tool operation was recovered, but save games could not be refreshed."
                : isReload
                    ? "An interrupted tool operation was recovered safely and the configuration was reloaded."
                    : "An interrupted tool operation was recovered safely.";
        }

        if (_lastSaveRecoveryMessage is not null)
        {
            return isReload
                ? $"{_lastSaveRecoveryMessage} Configuration reloaded from disk."
                : _lastSaveRecoveryMessage;
        }

        if (_saveGamesRefreshFailed)
        {
            return isReload
                ? "Configuration reloaded, but save games could not be refreshed."
                : "Configuration loaded, but save games could not be refreshed.";
        }

        return isReload
            ? "Configuration reloaded from disk."
            : "Configuration loaded. No files were changed.";
    }

    private bool IsCurrentContextVerified() =>
        _verifiedGameContext is not null && _gameContextVerifier.Verify(_verifiedGameContext);

    private async Task<SaveManagerViewModel?> CreateSaveManagerAsync()
    {
        if (_verifiedGameContext is not { } context)
        {
            return null;
        }

        try
        {
            ISaveGameManager manager = _saveManagerFactory(context);
            SaveGamesSnapshot snapshot = await Task.Run(manager.Inspect);
            var watchdog = new SaveGameWatchdog(
                context,
                _gameContextVerifier,
                message => AppDiagnostics.Logger?.Write(message));
            var viewModel = new SaveManagerViewModel(
                manager,
                context.UserDataDirectory,
                watchdog,
                dispatchToUi: null,
                mutationGate: _mutationGate,
                storeName: context.Store switch
                {
                    StoreKind.Gog => "GOG",
                    StoreKind.EpicGames => "Epic Games",
                    StoreKind.Heroic => "Heroic",
                    StoreKind.Steam => "Steam",
                    _ => context.Store.ToString()
                });
            viewModel.Refresh(snapshot);
            return viewModel;
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"Could not load save games: {exception.Message}", "#E04D42");
            return null;
        }
    }

    private void RebuildEditors()
    {
        foreach (SettingEditorViewModel editor in _editors.Values)
        {
            editor.Changed -= OnEditorChanged;
        }

        _editors.Clear();
        foreach (FeatureSettingSnapshot setting in _allFeatureGroups.SelectMany(group => group.Settings))
        {
            if (setting.Editor is null)
            {
                continue;
            }

            var editor = new SettingEditorViewModel(setting.Editor);
            editor.Changed += OnEditorChanged;
            _editors.Add(setting.Id, editor);
        }
    }

    private void ApplyViewMode()
    {
        ViewModeTitle = IsAdvancedMode ? "Advanced" : "Simple";
        ViewModeDescription = IsAdvancedMode
            ? "Editable controls and useful technical values"
            : "The settings that make the clearest visual difference";

        string query = SearchText.Trim();
        if (query.Length == 0)
        {
            foreach (FeatureGroupRowViewModel existingGroup in FeatureGroups)
            {
                _groupExpansionStates[existingGroup.Id] = existingGroup.IsExpanded;
            }
        }

        foreach (FeatureGroupRowViewModel oldGroup in FeatureGroups)
        {
            oldGroup.Dispose();
        }

        FeatureGroups = _allFeatureGroups
            .Where(group => IsAdvancedMode || group.IsEssential)
            .Select(group => CreateGroupRow(group, query))
            .Where(group => group.Settings.Count > 0)
            .ToArray();
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    private FeatureGroupRowViewModel CreateGroupRow(
        FeatureGroupSnapshot group,
        string query)
    {
        FeatureSettingRowViewModel[] settings = [.. group.Settings
            .Where(setting => IsAdvancedMode
                ? SettingDefinitionCatalog.IsShownInAdvancedMode(setting)
                : SettingDefinitionCatalog.IsShownInSimpleMode(setting.Id))
            .Where(setting => IsExperimentalGraphicsSettingsEnabled || !SettingDefinitionCatalog.IsExperimental(setting.Id))
            .Where(setting => MatchesSearch(group, setting, query))
            .Where(setting => MatchesGraphicsFilter(_editors.GetValueOrDefault(setting.Id)))
            .Select(setting => new FeatureSettingRowViewModel(
                setting.Name,
                setting.Value,
                setting.Description,
                setting.Source,
                setting.TechnicalKey ?? setting.Source,
                GetAccentColor(setting.State),
                true,
                IsAdvancedMode,
                setting.PresetValues?
                    .Select(value => new SettingPresetValueRowViewModel(value.Name, value.Value))
                    .ToArray() ?? [],
                setting.ActivePresetName,
                _editors.GetValueOrDefault(setting.Id),
                SettingDefinitionCatalog.IsExperimental(setting.Id)))];

        return new FeatureGroupRowViewModel(
            group.Id,
            group.Category,
            IsAdvancedMode ? group.Name : group.SimpleName ?? group.Name,
            IsAdvancedMode ? group.Summary : group.SimpleSummary ?? group.Summary,
            group.Description,
            GetAccentColor(group.State),
            settings.Length == 1 ? "1 setting" : $"{settings.Length} settings",
            settings,
            IsAdvancedMode,
            query.Length > 0 || _groupExpansionStates.GetValueOrDefault(group.Id));
    }

    private static bool MatchesSearch(
        FeatureGroupSnapshot group,
        FeatureSettingSnapshot setting,
        string query) =>
        query.Length == 0 ||
        group.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        group.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        setting.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        setting.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            setting.TechnicalKey?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private bool MatchesGraphicsFilter(SettingEditorViewModel? editor) => GraphicsFilter switch
    {
        "Modified" => editor?.HasActiveOverride == true || editor?.HasChanges == true,
        "Game defaults" => editor?.HasActiveOverride != true && editor?.HasChanges != true,
        _ => true,
    };

    private void LoadTechnicalDetails(GameInspectionSnapshot snapshot)
    {
        ConfigurationFiles = snapshot.ConfigurationFiles
            .Select(file => new ConfigurationFileRowViewModel(
                file.Name,
                $"{FormatBytes(file.SizeBytes ?? 0)} · {file.Settings.Count} readable settings",
                file.ReadError ?? "Read successfully"))
            .ToArray();

        Settings = snapshot.ConfigurationFiles
            .SelectMany(file => file.Settings.Select(setting => new SettingRowViewModel(
                file.Name,
                string.IsNullOrEmpty(setting.Section)
                    ? setting.Key
                    : $"[{setting.Section}] {setting.Key}",
                setting.Value,
                $"Line {setting.LineNumber}")))
            .ToArray();

        PakFiles = snapshot.PakFiles
            .Select(file => new PakFileRowViewModel(
                file.Name,
                $"{FormatBytes(file.SizeBytes)} · {file.LastWriteTimeUtc.ToLocalTime():g}",
                file.Classification switch
                {
                    PakClassification.BaseGame => "Known base-game package",
                    PakClassification.AecOwned => "AEC-managed package",
                    PakClassification.PatchStyle => "Patch-style package; origin not assumed",
                    _ => "Unclassified package",
                }))
            .ToArray();

        Notices = snapshot.Notices
            .Select(notice => new NoticeRowViewModel(
                notice.Severity.ToString(),
                notice.Message))
            .ToArray();

        OnPropertyChanged(nameof(SettingCount));
    }

    private void OnEditorChanged(object? sender, EventArgs eventArgs)
    {
        CloseReview();
        UpdatePendingChanges();
        if (GraphicsFilter != "All")
        {
            ApplyViewMode();
        }
        if (HasPendingChanges)
        {
            ShowMessage("Review the pending values, then apply or discard them.", "#FF5A00");
        }
    }

    private void UpdatePendingChanges()
    {
        PendingChanges = _editors
            .Where(pair => pair.Value.HasChanges)
            .Select(pair => new PendingChangeRowViewModel(
                FindSettingName(pair.Key),
                pair.Value.DesiredSummary))
            .ToArray();
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(PendingSummary));
        OnPropertyChanged(nameof(PendingDetails));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRemoveToolChanges));
        OnPropertyChanged(nameof(ShowPendingActions));
        OnPropertyChanged(nameof(CanRestoreGameDefaults));
        OnPropertyChanged(nameof(HasCustomProfileSettings));
        OnPropertyChanged(nameof(CanSaveProfile));
    }

    private string FormatReviewValue(string key, string? rawValue)
    {
        SettingEditorViewModel? editor = _editors.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal));
        return editor?.FormatValue(rawValue) ?? rawValue ?? "Game default";
    }

    private void RefreshProfileLibrary()
    {
        try
        {
            UserProfiles = _profileLibrary.List()
                .Select(profile => new UserProfileRowViewModel(
                    profile.Id,
                    profile.Profile.Name,
                    profile.Profile.Description ?? "No description",
                    ProfileContents(profile.Profile),
                    profile.Profile))
                .ToArray();
            if (_profileLibrary.UnreadableProfileCount > 0)
            {
                string count = _profileLibrary.UnreadableProfileCount.ToString(CultureInfo.CurrentCulture);
                ShowMessage($"{count} local profile(s) could not be read and were left untouched.", "#D6BC84");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            UserProfiles = [];
            ShowMessage("Your local profile library could not be read.", "#D6BC84");
        }
    }

    private void UpdateGameplayCatalog(GameInspectionSnapshot snapshot)
    {
        foreach (GameplayDifficultyControlViewModel control in AllGameplayControls)
        {
            control.PropertyChanged -= OnGameplayControlChanged;
        }

        bool isSupported = GameplayDifficultyCatalog.Supports(snapshot);
        GameplayDifficultyPresets = isSupported ? GameplayDifficultyCatalog.CreatePresets() : [];
        GameplaySimpleControls = isSupported ? GameplayDifficultyCatalog.CreateSimpleControls() : [];
        GameplayAdvancedControls = isSupported ? GameplayDifficultyCatalog.CreateAdvancedControls() : [];
        UpdateGameplayControlRanges();
        GameplayState = _gameplayEditor.Inspect(snapshot);
        if (isSupported && GameplayState.Kind != GameplayDifficultyStateKind.Unverified)
        {
            ApplyGameplaySettingsToControls(GameplayState.Settings);
        }
        foreach (GameplayDifficultyControlViewModel control in AllGameplayControls)
        {
            control.PropertyChanged += OnGameplayControlChanged;
        }
        GameplayResearchValues = isSupported ? GameplayDifficultyCatalog.CreateAdvancedValues() : [];
        GameplayReadiness = GameplayState.Kind == GameplayDifficultyStateKind.Unverified && isSupported
            ? new GameplayReadinessViewModel(
                "Installed gameplay PAK needs attention",
                GameplayState.Description + ". AEC will not overwrite or remove it.",
                "#E04D42",
                true)
            : GameplayDifficultyCatalog.AssessReadiness(snapshot);
        SetGameplayDraftStatus(isSupported
            ? GameplayState.Kind == GameplayDifficultyStateKind.Active
                ? "Installed gameplay difficulty"
                : $"Steam build {GameplayDifficultyCatalog.SupportedSteamBuildId} · game default"
            : "Gameplay difficulty is available only for verified Steam build 5495393 with matching stock PAK signatures");
        OnPropertyChanged(nameof(HasGameplayDifficultyPresets));
        OnPropertyChanged(nameof(HasGameplaySimpleControls));
        OnPropertyChanged(nameof(HasGameplayAdvancedControls));
        OnPropertyChanged(nameof(HasGameplayResearchValues));
        NotifyGameplayStateChanged();
    }

    private void OnGameplayControlChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(GameplayDifficultyControlViewModel.MultiplierPercent))
        {
            CloseReview();
            SetGameplayDraftStatus("Custom gameplay difficulty");
            NotifyGameplayStateChanged();
        }
    }

    private void SetGameplayDraftStatus(string draft)
    {
        GameplayDraftStatus = HasGameplayPendingChanges
            ? $"{draft} · pending review"
            : GameplayState.Description;
        OnPropertyChanged(nameof(GameplayDraftStatus));
        OnPropertyChanged(nameof(HomeGameplaySummary));
    }

    private GameplayDifficultySettings CurrentGameplaySettings => new(
        GameplayPercent("food"),
        GameplayPercent("water"),
        GameplayPercent("sleep"),
        GameplayPercent("fall-damage"),
        GameplayPercent("bleeding"),
        GameplayPercent("poison"),
        GameplayPercent("energy-recovery"),
        GameplayPercent("wound-sleep-healing"),
        GameplayPercent("wound-stamina-penalty"),
        GameplayPercent("poison-recovery"),
        GameplayPercent("rest-delay"),
        GameplayPercent("exhaustion-threshold"),
        GameplayPercent("exhaustion-penalty"),
        GameplayPercent("wound-recovery-duration"),
        GameplayPercent("poison-stamina-penalty"),
        IncludeClanInGameplayPatch);

    private int GameplayPercent(string id) =>
        AllGameplayControls.FirstOrDefault(control => string.Equals(control.Id, id, StringComparison.Ordinal))?.MultiplierPercent ?? 100;

    private void ApplyGameplaySettingsToControls(GameplayDifficultySettings settings)
    {
        IncludeClanInGameplayPatch = settings.IncludeClan;
        foreach (GameplayDifficultyControlViewModel control in AllGameplayControls)
        {
            control.MultiplierPercent = control.Id switch
            {
                "food" => settings.FoodPercent,
                "water" => settings.WaterPercent,
                "sleep" => settings.SleepPercent,
                "fall-damage" => settings.FallDamagePercent,
                "bleeding" => settings.BleedingPercent,
                "poison" => settings.PoisonPercent,
                "energy-recovery" => settings.EnergyRecoveryPercent,
                "wound-sleep-healing" => settings.WoundSleepHealingPercent,
                "wound-stamina-penalty" => settings.WoundStaminaPenaltyPercent,
                "poison-recovery" => settings.PoisonRecoveryPercent,
                "rest-delay" => settings.RestDelayPercent,
                "exhaustion-threshold" => settings.ExhaustionThresholdPercent,
                "exhaustion-penalty" => settings.ExhaustionPenaltyPercent,
                "wound-recovery-duration" => settings.WoundRecoveryDurationPercent,
                "poison-stamina-penalty" => settings.PoisonStaminaPenaltyPercent,
                _ => 100,
            };
        }
    }

    private IEnumerable<GameplayDifficultyControlViewModel> AllGameplayControls =>
        GameplaySimpleControls.Concat(GameplayAdvancedControls);

    private void NotifyGameplayStateChanged()
    {
        OnPropertyChanged(nameof(HasGameplayPendingChanges));
        OnPropertyChanged(nameof(CanReviewGameplay));
        OnPropertyChanged(nameof(CanResetGameplay));
        OnPropertyChanged(nameof(CanEditGameplay));
        OnPropertyChanged(nameof(GameplayReviewButtonLabel));
        OnPropertyChanged(nameof(HomeGameplaySummary));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    private void NotifyGameplayRangeChanged()
    {
        OnPropertyChanged(nameof(GameplayMinimumPercent));
        OnPropertyChanged(nameof(GameplayMaximumPercent));
        OnPropertyChanged(nameof(GameplayControlRangeLabel));
    }

    private void UpdateGameplayControlRanges()
    {
        int max = GameplayMaximumPercent;
        foreach (GameplayDifficultyControlViewModel control in AllGameplayControls)
        {
            control.MaxPercent = max;
        }
    }

    private static string ProfileContents(UserProfile profile)
    {
        var sections = new List<string>(3);
        if (profile.Graphics.Count > 0) sections.Add("Graphics");
        if (profile.Display.Count > 0) sections.Add("Display");
        if (profile.Gameplay.Count > 0) sections.Add("Gameplay");
        return string.Join(" · ", sections);
    }

    private static UserProfile CreateBuiltInProfile(string name, IReadOnlyList<ProfileSetting> graphics) =>
        new(
            UserProfile.CurrentSchemaVersion,
            name,
            "Built-in AEC graphics preset.",
            DateTimeOffset.UnixEpoch,
            "1.0.0",
            graphics,
            [],
            []);

    private static UserProfile CreateHardwareBaselineProfile(
        string name,
        GameGraphicsQuality quality,
        IReadOnlyList<ProfileSetting> hardwareOverrides)
    {
        var graphics = new List<ProfileSetting>
        {
            new(SystemSaveSettingKeys.ViewDistanceQuality, quality.ToString()),
            new(SystemSaveSettingKeys.PostProcessingQuality, quality.ToString()),
            new(SystemSaveSettingKeys.ShadowQuality, quality.ToString()),
            new(SystemSaveSettingKeys.TextureQuality, quality.ToString()),
            new(SystemSaveSettingKeys.VisualEffectsQuality, quality.ToString()),
            new(SystemSaveSettingKeys.FoliageQuality, quality.ToString()),
        };
        graphics.AddRange(hardwareOverrides);
        return CreateBuiltInProfile(name, graphics);
    }

    private void LoadProfileForReview(UserProfile profile)
    {
        if (_snapshot is null || IsAnyOperationRunning)
        {
            ShowMessage("Reload the game settings before loading a profile.", "#D6BC84");
            return;
        }
        if (HasPendingChanges || IsReviewingChanges)
        {
            ShowMessage("Apply or discard the pending changes before loading a profile.", "#D6BC84");
            return;
        }
        if (profile.Gameplay.Count > 0)
        {
            ShowMessage("Gameplay profiles are not available yet. No settings were loaded.", "#D6BC84");
            return;
        }
        if (profile.Display.Count > 0)
        {
            ShowMessage("Display settings in profiles are not available yet. No settings were loaded.", "#D6BC84");
            return;
        }

        var candidates = new List<(SettingEditorViewModel Editor, ProfileSetting Setting)>();
        foreach (ProfileSetting setting in profile.Graphics)
        {
            if (!TryGetEditorByTechnicalKey(setting.Key, out SettingEditorViewModel? editor) || editor is null ||
                !editor.CanApplyProfileValue(setting.Value))
            {
                ShowMessage($"{profile.Name} contains a setting that is not supported by this game setup. No settings were loaded.", "#E04D42");
                return;
            }
            candidates.Add((editor, setting));
        }

        var profileKeys = new HashSet<string>(
            profile.Graphics.Select(setting => setting.Key),
            StringComparer.OrdinalIgnoreCase);
        foreach (SettingEditorViewModel editor in _editors.Values)
        {
            if (editor.ShowOverrideToggle && editor.HasCurrentOverride && !profileKeys.Contains(editor.Key))
            {
                editor.UseGameDefault();
            }
        }

        foreach ((SettingEditorViewModel editor, ProfileSetting setting) in candidates)
        {
            _ = editor.TryApplyProfileValue(setting.Value);
        }

        ShowGraphics();
        UpdatePendingChanges();
        if (HasPendingChanges)
        {
            ShowMessage($"Loaded {profile.Name}. Review the pending changes before applying them.", "#FF5A00");
        }
        else
        {
            ShowMessage($"{profile.Name} already matches your current setup. No changes are needed.", "#B4D941");
        }
    }

    private void LoadBuiltInTweakForReview(UserProfile tweak)
    {
        if (_snapshot is null || IsAnyOperationRunning)
        {
            ShowMessage("Reload the game settings before loading a graphics tweak.", "#D6BC84");
            return;
        }
        if (HasPendingChanges || IsReviewingChanges)
        {
            ShowMessage("Apply or discard the pending changes before loading a graphics tweak.", "#D6BC84");
            return;
        }

        var candidates = new List<(SettingEditorViewModel Editor, ProfileSetting Setting)>();
        foreach (ProfileSetting setting in tweak.Graphics)
        {
            if (!TryGetEditorByTechnicalKey(setting.Key, out SettingEditorViewModel? editor) || editor is null ||
                !editor.CanApplyProfileValue(setting.Value))
            {
                ShowMessage($"{tweak.Name} contains a setting that is not supported by this game setup. No settings were loaded.", "#E04D42");
                return;
            }
            candidates.Add((editor, setting));
        }

        foreach ((SettingEditorViewModel editor, ProfileSetting setting) in candidates)
        {
            _ = editor.TryApplyProfileValue(setting.Value);
        }

        ShowGraphics();
        UpdatePendingChanges();
        ShowMessage(
            HasPendingChanges
                ? $"Loaded {tweak.Name}. Only its listed settings were staged for review."
                : $"{tweak.Name} already matches your current setup. No changes are needed.",
            HasPendingChanges ? "#FF5A00" : "#B4D941");
    }

    private void CloseReview()
    {
        if (_reviewPlan is not null)
        {
            if (_reviewIsGameplay)
            {
                _gameplayEditor.DiscardPlan(_reviewPlan);
            }
            else
            {
                _settingsEditor.DiscardPlan(_reviewPlan);
            }
            _reviewPlan = null;
        }

        _reviewIsToolChangeRemoval = false;
        _reviewIsGameplay = false;
        _reviewRemovesGameplayPak = false;
        ReviewChanges = [];
        IsReviewingChanges = false;
        OnPropertyChanged(nameof(ReviewSummary));
        OnPropertyChanged(nameof(ReviewDescription));
        OnPropertyChanged(nameof(ConfirmReviewLabel));
    }

    private string FindSettingName(string settingId) =>
        _allFeatureGroups
            .SelectMany(group => group.Settings)
            .First(setting => string.Equals(setting.Id, settingId, StringComparison.Ordinal))
            .Name;

    private void NotifyExternalPakFolderState()
    {
        OnPropertyChanged(nameof(ExternalPakCount));
        OnPropertyChanged(nameof(HasExternalPaks));
        OnPropertyChanged(nameof(ExternalPakHelpText));
        OnPropertyChanged(nameof(GamePakFolderPath));
        OnPropertyChanged(nameof(CanOpenGamePakFolder));
    }

    private static string? TryGetGamePakFolder(string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return null;
        }

        try
        {
            string root = Path.GetFullPath(installDirectory);
            string pakFolder = Path.GetFullPath(Path.Combine(root, "Ancestors", "Content", "Paks"));
            string relative = Path.GetRelativePath(root, pakFolder);
            return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                ? null
                : pakFolder;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool TryOpenDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                _ = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                return true;
            }

            if (OperatingSystem.IsLinux())
            {
                var startInfo = new ProcessStartInfo { FileName = "xdg-open", UseShellExecute = false };
                startInfo.ArgumentList.Add(path);
                _ = Process.Start(startInfo);
                return true;
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or
            InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }

        return false;
    }

    private bool TryGetEditorByTechnicalKey(string key, out SettingEditorViewModel? editor)
    {
        editor = null;
        editor = _editors.Values.FirstOrDefault(candidate => string.Equals(
            candidate.Key,
            key,
            StringComparison.OrdinalIgnoreCase));
        return editor is not null;
    }

    private void LogDetection(string result)
    {
        string store = _snapshot?.Installation?.Store.ToString() ?? "unknown";
        string data = string.IsNullOrWhiteSpace(_snapshot?.UserDataDirectory)
            ? "missing user-data"
            : "user-data found";
        AppDiagnostics.Logger?.Write("Detection: store=" + store + " " + data + " => " + result);
    }

    private void ShowMessage(string message, string accent)
    {
        OperationMessage = message;
        OperationAccent = accent;
    }

    private sealed class EmptyUserProfileLibrary : IUserProfileLibrary
    {
        public static readonly EmptyUserProfileLibrary Instance = new();

        public int UnreadableProfileCount => 0;

        public IReadOnlyList<StoredUserProfile> List() => [];

        public UserProfile Read(string id) =>
            throw new NotSupportedException("The profile library is not available.");

        public StoredUserProfile Save(UserProfile profile) =>
            throw new NotSupportedException("The profile library is not available.");

        public void Delete(string id) =>
            throw new NotSupportedException("The profile library is not available.");

        public UserProfile ReadExternal(string path) =>
            throw new NotSupportedException("The profile library is not available.");
    }

    private static string GetAccentColor(ReadableSettingState state) => state switch
    {
        ReadableSettingState.Enabled => "#B4D941",
        ReadableSettingState.Disabled => "#7A877A",
        ReadableSettingState.Modified => "#FF5A00",
        _ => "#687668",
    };

    private static bool IsExpectedUserOperationException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or
        ArgumentException or NotSupportedException or System.Text.DecoderFallbackException or
        System.Text.Json.JsonException;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{size:0.##} {units[unit]}");
    }

    private static string FormatInstallation(GameInstallationSnapshot installation)
    {
        string store = installation.Store switch
        {
            StoreKind.EpicGames => "Epic Games",
            StoreKind.Gog => "GOG",
            StoreKind.Heroic => "Heroic",
            _ => installation.Store.ToString(),
        };
        string layer = installation.CompatibilityLayer == CompatibilityLayerKind.Proton
            ? "  Proton"
            : string.Empty;
        string identity = installation.Store switch
        {
            StoreKind.EpicGames or StoreKind.Gog when installation.ContentSignatureReadFailed =>
                "Content signature could not be read",
            StoreKind.EpicGames or StoreKind.Gog when string.Equals(
                installation.ContentSignature,
                AncestorsGameProfile.SupportedContentSignature,
                StringComparison.Ordinal) => "Content signature verified",
            StoreKind.EpicGames or StoreKind.Gog when installation.ContentSignature is not null =>
                "Content signature not recognized",
            StoreKind.EpicGames or StoreKind.Gog => "Content signature unavailable",
            StoreKind.Heroic => "Detection only · store identity unverified",
            StoreKind.Steam when installation.BuildId is not null => "Steam build " + installation.BuildId,
            _ when installation.ContentSignature is not null => "Recognized PAK index signature",
            _ => "Build unknown",
        };
        return $"{store}  {installation.Host}{layer}  {identity}";
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (GameplayDifficultyControlViewModel control in AllGameplayControls)
        {
            control.PropertyChanged -= OnGameplayControlChanged;
        }
        CancellationTokenSource? searchSource = _searchDebounceSource;
        Task? searchTask = _searchDebounceTask;
        searchSource?.Cancel();
        if (searchTask is null || searchTask.IsCompleted)
        {
            searchSource?.Dispose();
        }
        else
        {
            _ = searchTask.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        AppDiagnostics.Logger?.Write($"Search debounce failed during shutdown: {completed.Exception}");
                    }

                    searchSource?.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        _mutationGate.Changed -= OnMutationGateChanged;
        _searchDebounceSource = null;
        _searchDebounceTask = null;
        SaveManager?.Dispose();
        SaveManager = null;
    }
}
