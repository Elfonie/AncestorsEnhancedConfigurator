using System.ComponentModel;
using System.Globalization;
using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.Profiles;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Core.Settings;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.SaveGames;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyGameInspector _inspector;
    private readonly IGameSettingsEditor _settingsEditor;
    private readonly Func<VerifiedGameContext, ISaveGameManager> _saveManagerFactory;
    private readonly IUserProfileLibrary _profileLibrary;
    private readonly GameContextVerifier _gameContextVerifier;
    private readonly UiMutationGate _mutationGate = new();
    private VerifiedGameContext? _verifiedGameContext;
    private bool _saveGamesRefreshFailed;
    private bool _lastRefreshRecoveredOperation;
    private string? _lastSaveRecoveryMessage;
    private readonly Dictionary<string, SettingEditorViewModel> _editors =
        new(StringComparer.Ordinal);
    private IReadOnlyList<FeatureGroupSnapshot> _allFeatureGroups = [];
    private GameInspectionSnapshot? _snapshot;
    private SettingsChangePlan? _reviewPlan;
    private bool _reviewIsToolChangeRemoval;
    private readonly Action<bool>? _highContrastChanged;

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
    public partial bool IsSaveGamesView { get; set; }

    [ObservableProperty]
    public partial SaveManagerViewModel? SaveManager { get; set; }

    [ObservableProperty]
    public partial bool IsGameplayView { get; set; }

    [ObservableProperty]
    public partial bool IsGameplayAdvancedMode { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<GameplayDifficultyPresetViewModel> GameplayDifficultyPresets { get; set; } =
    [
        new(
            "Game default",
            "100% across every available control",
            "No gameplay patch is created. This is the reference point for all future percentage changes."),
        new(
            "Explorer (planned)",
            "Lower survival pressure",
            "Will reduce the food, water, sleep and fall-damage categories together after the PAK load path is verified."),
        new(
            "Survival (planned)",
            "Higher survival pressure",
            "Will raise the same simple categories together. It will not silently alter combat, QTEs or animal damage."),
        new(
            "Custom (planned)",
            "Choose each simple category yourself",
            "Each available category will use 10% steps relative to the game default."),
    ];

    [ObservableProperty]
    public partial IReadOnlyList<GameplayDifficultyControlViewModel> GameplaySimpleControls { get; set; } =
    [
        new(
            "Food need",
            "24 portions per day · game default",
            "Higher is harder: the named Food NeededPerDay value defines a larger food requirement."),
        new(
            "Water need",
            "30 portions per day · game default",
            "Higher is harder: the named Liquid NeededPerDay value defines a larger liquid requirement."),
        new(
            "Sleep need",
            "16 portions per day · game default",
            "Higher is harder: the named Sleep NeededPerDay value defines a larger sleep requirement."),
        new(
            "Energy recovery",
            "1.0 energy per second · game default",
            "Higher is easier while energy regeneration is active. Normal stamina and health limits still apply."),
        new(
            "Fall damage",
            "Minor 2.5% · Major 5% · game default",
            "Higher is harder: minor and major falls use separate, named damage values and will remain a paired Simple control."),
    ];

    [ObservableProperty]
    public partial IReadOnlyList<GameplayResearchValueViewModel> GameplayResearchValues { get; set; } =
    [
        new(
            "Energy recovery delay",
            "1.5 seconds",
            "The delay before resting enables energy regeneration. This is not a regeneration rate."),
        new(
            "Cumulative energy-loss threshold",
            "0.50 energy",
            "Recorded energy loss at or beyond this threshold triggers one stamina penalty, then the accumulator resets."),
        new(
            "Cumulative energy-loss stamina penalty",
            "0.15 stamina",
            "The penalty is one absolute stamina subtraction per threshold crossing; excess loss is not carried over."),
        new(
            "Major wound base recovery time",
            "480 minutes",
            "The game multiplies this by one minus the applicable wound-duration ability modifiers, clamped to 0–1."),
        new(
            "Minor wound stamina penalty",
            "0.15 maximum stamina",
            "While wounded, this is a maximum-stamina modifier, not an immediate current-stamina drain."),
        new(
            "Major wound stamina penalty",
            "0.30 maximum stamina",
            "While wounded, this is a maximum-stamina modifier, not an immediate current-stamina drain."),
        new(
            "Major poison stamina penalty",
            "0.25 maximum stamina",
            "While majorly poisoned, this is a maximum-stamina modifier. The minor-poison override is not known."),
    ];

    [ObservableProperty]
    public partial bool IsProfilesView { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsView { get; set; }

    [ObservableProperty]
    public partial bool IsHighContrastEnabled { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<UserProfileRowViewModel> UserProfiles { get; set; } = [];

    [ObservableProperty]
    public partial ImportedProfileViewModel? ImportedProfile { get; set; }

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
        Action<bool>? highContrastChanged = null)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(settingsEditor);
        _inspector = inspector;
        _gameContextVerifier = new GameContextVerifier(inspector);
        _settingsEditor = settingsEditor;
        _saveManagerFactory = saveManagerFactory ?? (context => new SafeSaveGameManager(context, _gameContextVerifier));
        _profileLibrary = profileLibrary ?? EmptyUserProfileLibrary.Instance;
        _highContrastChanged = highContrastChanged;
        IsHighContrastEnabled = highContrastEnabled;
        _mutationGate.Changed += OnMutationGateChanged;

        ProductName = "Ancestors Enhanced Configurator";
        string version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        Phase = $"{version} Graphics, Saves and Gameplay";
    }

    public string ProductName { get; }

    public string Phase { get; }

    public bool ShowGraphicsView => !IsSaveGamesView && !IsGameplayView && !IsProfilesView && !IsSettingsView;

    public bool ShowSaveGamesView => IsSaveGamesView;

    public bool ShowGameplayView => IsGameplayView;

    public bool HasGameplayResearchValues => GameplayResearchValues.Count > 0;

    public bool IsGameplaySimpleMode => !IsGameplayAdvancedMode;

    public bool HasGameplayDifficultyPresets => GameplayDifficultyPresets.Count > 0;

    public bool HasGameplaySimpleControls => GameplaySimpleControls.Count > 0;

    public bool ShowProfilesView => IsProfilesView;

    public bool ShowSettingsView => IsSettingsView;

    public bool HasUserProfiles => UserProfiles.Count > 0;

    public bool HasImportedProfile => ImportedProfile is not null;

    public bool HasCustomProfileSettings => _editors.Values.Any(editor =>
        editor.TryGetCustomProfileValue(out _));

    public bool CanSaveProfile =>
        !IsAnyOperationRunning &&
        HasCustomProfileSettings &&
        !string.IsNullOrWhiteSpace(NewProfileName);

    public bool IsSaveManagerAvailable => SaveManager is not null;

    public bool IsSaveManagerUnavailable => SaveManager is null;

    public bool ShouldKeepRunningInTrayOnClose =>
        SaveManager is { IsWatchdogEnabled: true, KeepRunningInTrayWhenClosing: true };

    public bool HasPendingChanges => PendingChanges.Count > 0;

    public bool CanUndo => CanRevertLast && !HasPendingChanges && !IsReviewingChanges && !IsAnyOperationRunning;

    public bool CanRemoveToolChanges =>
        HasRemovableToolChanges && !HasPendingChanges && !IsReviewingChanges && !IsAnyOperationRunning;

    public bool ShowPendingActions => HasPendingChanges && !IsReviewingChanges;

    public bool ShowReviewActions => IsReviewingChanges;

    public bool ShowBottomBar => ShowGraphicsView || HasPendingChanges || IsReviewingChanges;

    public bool IsAnyOperationRunning =>
        IsBusy ||
        _mutationGate.IsBusy ||
        (SaveManager?.IsBusy ?? false);
    public bool CanEditSettings => !IsReviewingChanges && !IsAnyOperationRunning;

    public bool CanRestoreGameDefaults =>
        !IsAnyOperationRunning &&
        !HasPendingChanges &&
        !IsReviewingChanges &&
        _editors.Values.Any(editor => editor.HasActiveOverride);

    public bool IsSimpleMode => !IsAdvancedMode;

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

    public string ReviewSummary => _reviewIsToolChangeRemoval
        ? "Remove Ancestors Enhanced from my game"
        : ReviewChanges.Count == 1
        ? "Review 1 change before writing"
        : $"Review {ReviewChanges.Count} changes before writing";

    public string ReviewDescription => _reviewIsToolChangeRemoval
        ? "The listed files will be restored to their state before you first used this Configurator. Save games and other mods are not changed"
        : "Check the old and new values before anything is written";

    public string ConfirmReviewLabel => _reviewIsToolChangeRemoval ? "Remove from my game" : "Confirm & Apply";

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
    private void ShowSaveGames()
    {
        IsGameplayView = false;
        IsProfilesView = false;
        IsSettingsView = false;
        IsSaveGamesView = true;
        UpdateViewVisibility();
    }

    [RelayCommand]
    private void ShowGameplay()
    {
        IsSaveGamesView = false;
        IsProfilesView = false;
        IsSettingsView = false;
        IsGameplayView = true;
        UpdateViewVisibility();
    }

    [RelayCommand]
    private void ShowGameplaySimple() => IsGameplayAdvancedMode = false;

    [RelayCommand]
    private void ShowGameplayAdvanced() => IsGameplayAdvancedMode = true;

    [RelayCommand]
    private void ShowProfiles()
    {
        IsSaveGamesView = false;
        IsGameplayView = false;
        IsSettingsView = false;
        IsProfilesView = true;
        RefreshProfileLibrary();
        UpdateViewVisibility();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        IsSaveGamesView = false;
        IsGameplayView = false;
        IsProfilesView = false;
        IsSettingsView = true;
        UpdateViewVisibility();
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

    private void OnChildPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SaveManagerViewModel.IsBusy))
        {
            NotifyMutationAvailability();
        }
    }
    private void UpdateViewVisibility()
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowSaveGamesView));
        OnPropertyChanged(nameof(ShowGameplayView));
        OnPropertyChanged(nameof(ShowProfilesView));
        OnPropertyChanged(nameof(ShowSettingsView));
    }

    [RelayCommand]
    private void ShowGraphics()
    {
        IsSaveGamesView = false;
        IsGameplayView = false;
        IsProfilesView = false;
        IsSettingsView = false;
        UpdateViewVisibility();
    }

    partial void OnIsGameplayViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowGameplayView));
        OnPropertyChanged(nameof(ShowProfilesView));
        OnPropertyChanged(nameof(ShowSettingsView));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    partial void OnIsGameplayAdvancedModeChanged(bool value) =>
        OnPropertyChanged(nameof(IsGameplaySimpleMode));

    partial void OnIsProfilesViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowProfilesView));
        OnPropertyChanged(nameof(ShowSettingsView));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    partial void OnIsSettingsViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowSettingsView));
        OnPropertyChanged(nameof(ShowBottomBar));
    }

    partial void OnIsHighContrastEnabledChanged(bool value)
    {
        _highContrastChanged?.Invoke(value);
    }

    partial void OnUserProfilesChanged(IReadOnlyList<UserProfileRowViewModel> value) =>
        OnPropertyChanged(nameof(HasUserProfiles));

    partial void OnImportedProfileChanged(ImportedProfileViewModel? value) =>
        OnPropertyChanged(nameof(HasImportedProfile));

    partial void OnNewProfileNameChanged(string value) =>
        OnPropertyChanged(nameof(CanSaveProfile));

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
                    change.Before ?? "Game default",
                    change.After ?? "Game default"))
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
                    change.Before ?? "Game default",
                    change.After ?? "Game default"))
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
        _reviewPlan = null;
        _reviewIsToolChangeRemoval = false;
        OnPropertyChanged(nameof(ReviewSummary));
        OnPropertyChanged(nameof(ReviewDescription));
        OnPropertyChanged(nameof(ConfirmReviewLabel));
        IsReviewingChanges = false;
        ReviewChanges = [];
        IsBusy = true;
        ShowMessage(
            isToolChangeRemoval ? "Removing verified tool changes..." : "Applying the reviewed changes...",
            "#FF5A00");
        SettingsOperationResult result;
        try
        {
            result = await Task.Run(() => _settingsEditor.Apply(plan));
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
        OnPropertyChanged(nameof(HasCustomProfileSettings));
        OnPropertyChanged(nameof(CanSaveProfile));
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
    }

    partial void OnReviewChangesChanged(IReadOnlyList<ChangeReviewRowViewModel> value) =>
        OnPropertyChanged(nameof(ReviewSummary));

    private void SetDetection(string status, string statusColor, string dotColor)
    {
        DetectionStatus = status;
        DetectionColor = statusColor;
        DetectionDotColor = dotColor;
        OnPropertyChanged(nameof(DetectionStatus));
    }

    private async Task<bool> RefreshFromDiskAsync()
    {
        CloseReview();
        _lastRefreshRecoveredOperation = false;
        _lastSaveRecoveryMessage = null;
        IsBusy = true;
        DetectionStatus = "Scanning game files";
        ShowMessage("Reading the installation and settings...", "#FF5A00");
        try
        {
            GameInspectionSnapshot snapshot = await Task.Run(_inspector.Inspect);
            if (await Task.Run(() => _settingsEditor.RecoverInterruptedChanges(snapshot)))
            {
                _lastRefreshRecoveredOperation = true;
                snapshot = await Task.Run(_inspector.Inspect);
            }
            bool canKeepChildState = _verifiedGameContext?.Matches(snapshot) == true &&
                SaveManager is not null;
            _saveGamesRefreshFailed = false;
            _snapshot = snapshot;
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
            else
            {
                SetDetection("Ancestors installation not detected", "#7A877A", "#7A877A");
            }
            InstallationPath = snapshot.Installation?.InstallDirectory ?? "Not detected";
            InstallationDetails = snapshot.Installation is null
                ? "Store and build unknown"
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
            ShowMessage($"Scan failed: {exception.Message}", "#E04D42");
            LogDetection("failed: " + exception.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
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
            var watchdog = new SaveGameWatchdog(context, _gameContextVerifier);
            var viewModel = new SaveManagerViewModel(
                manager,
                context.UserDataDirectory,
                watchdog,
                dispatchToUi: null,
                mutationGate: _mutationGate);
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
            .Where(setting => MatchesSearch(group, setting, query))
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
                _editors.GetValueOrDefault(setting.Id)))];

        return new FeatureGroupRowViewModel(
            group.Id,
            group.Category,
            group.Name,
            IsAdvancedMode ? group.Summary : group.SimpleSummary ?? group.Summary,
            group.Description,
            GetAccentColor(group.State),
            settings.Length == 1 ? "1 setting" : $"{settings.Length} settings",
            settings,
            IsAdvancedMode,
            query.Length > 0);
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
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            UserProfiles = [];
            ShowMessage("Your local profile library could not be read.", "#D6BC84");
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

    private void CloseReview()
    {
        if (_reviewPlan is not null)
        {
            _settingsEditor.DiscardPlan(_reviewPlan);
            _reviewPlan = null;
        }

        _reviewIsToolChangeRemoval = false;
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

        public IReadOnlyList<StoredUserProfile> List() => [];

        public UserProfile Read(string id) =>
            throw new NotSupportedException("The profile library is not available.");

        public StoredUserProfile Save(UserProfile profile) =>
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
        _searchDebounceSource?.Cancel();
        try
        {
            _searchDebounceTask?.Wait(TimeSpan.FromMilliseconds(300));
        }
        catch (AggregateException)
        {
        }
        _searchDebounceSource?.Dispose();
        _mutationGate.Changed -= OnMutationGateChanged;
        _searchDebounceSource = null;
        _searchDebounceTask = null;
        SaveManager?.Dispose();
        SaveManager = null;
    }
}
