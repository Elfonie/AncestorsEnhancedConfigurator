using System.ComponentModel;
using System.Globalization;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
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
    private readonly GameContextVerifier _gameContextVerifier;
    private VerifiedGameContext? _verifiedGameContext;
    private bool _saveGamesRefreshFailed;
    private readonly Dictionary<string, SettingEditorViewModel> _editors =
        new(StringComparer.Ordinal);
    private IReadOnlyList<FeatureGroupSnapshot> _allFeatureGroups = [];
    private GameInspectionSnapshot? _snapshot;
    private SettingsChangePlan? _reviewPlan;
    private bool _reviewIsToolChangeRemoval;

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
    public partial bool IsCheatView { get; set; }

    [ObservableProperty]
    public partial CheatViewModel? Cheat { get; set; }

    public MainViewModel(
        IReadOnlyGameInspector inspector,
        IGameSettingsEditor settingsEditor,
        Func<VerifiedGameContext, ISaveGameManager>? saveManagerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(settingsEditor);
        _inspector = inspector;
        _gameContextVerifier = new GameContextVerifier(inspector);
        _settingsEditor = settingsEditor;
        _saveManagerFactory = saveManagerFactory ?? (context => new SafeSaveGameManager(context, _gameContextVerifier));

        ProductName = "Ancestors Enhanced Configurator";
        string version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.8.0";
        Phase = $"{version} Graphics, Saves and Cheats";
    }

    public string ProductName { get; }

    public string Phase { get; }

    public bool ShowGraphicsView => !IsSaveGamesView && !IsCheatView;

    public bool ShowSaveGamesView => IsSaveGamesView;

    public bool ShowCheatView => IsCheatView;

    public bool IsCheatAvailable => Cheat is not null;

    public bool IsCheatUnavailable => Cheat is null;

    public bool IsSaveManagerAvailable => SaveManager is not null;

    public bool IsSaveManagerUnavailable => SaveManager is null;

    public bool HasPendingChanges => PendingChanges.Count > 0;

    public bool CanUndo => CanRevertLast && !HasPendingChanges && !IsReviewingChanges && !IsBusy;

    public bool CanRemoveToolChanges =>
        HasRemovableToolChanges && !HasPendingChanges && !IsReviewingChanges && !IsBusy;

    public bool ShowPendingActions => HasPendingChanges && !IsReviewingChanges;

    public bool ShowReviewActions => IsReviewingChanges;

    public bool ShowBottomBar => ShowGraphicsView || HasPendingChanges || IsReviewingChanges;

    public bool IsAnyOperationRunning =>
        IsBusy ||
        (SaveManager?.IsBusy ?? false) ||
        (Cheat?.IsBusy ?? false);
    public bool CanEditSettings => !IsReviewingChanges && !IsBusy;

    public bool CanRestoreGameDefaults =>
        !IsBusy &&
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
        ? ReviewChanges.Count == 1
            ? "Remove tool changes from 1 file"
            : $"Remove tool changes from {ReviewChanges.Count} files"
        : ReviewChanges.Count == 1
        ? "Review 1 change before writing"
        : $"Review {ReviewChanges.Count} changes before writing";

    public string ReviewDescription => _reviewIsToolChangeRemoval
        ? "Only unchanged files managed by this tool will be returned to their captured original state"
        : "Check the old and new values before anything is written";

    public string ConfirmReviewLabel => _reviewIsToolChangeRemoval ? "Confirm removal" : "Confirm & Apply";

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
        if (await RefreshFromDiskAsync())
        {
            ShowMessage(
                _saveGamesRefreshFailed
                    ? "Configuration loaded, but save games could not be refreshed."
                    : "Configuration loaded. No files were changed.",
                _saveGamesRefreshFailed ? "#D6BC84" : "#7A877A");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
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
                _saveGamesRefreshFailed
                    ? "Configuration reloaded, but save games could not be refreshed."
                    : "Configuration reloaded from disk.",
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
        IsCheatView = false;
        IsSaveGamesView = true;
        UpdateViewVisibility();
    }

    [RelayCommand]
    private void ShowCheat()
    {
        IsSaveGamesView = false;
        IsCheatView = true;
        UpdateViewVisibility();
    }

    private void OnChildPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SaveManagerViewModel.IsBusy) ||
            e.PropertyName == nameof(CheatViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(IsAnyOperationRunning));
        }

        if (sender is CheatViewModel cheat &&
            e.PropertyName == nameof(CheatViewModel.IsGameRunning) &&
            SaveManager is not null)
        {
            SaveManager.IsGameRunning = cheat.IsGameRunning;
        }

        if (sender is SaveManagerViewModel saves &&
            e.PropertyName == nameof(SaveManagerViewModel.Slots) &&
            Cheat is not null)
        {
            Cheat.UpdateSlotAvailability(saves.Slots);
        }
    }
    private void UpdateViewVisibility()
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowSaveGamesView));
        OnPropertyChanged(nameof(ShowCheatView));
    }

    [RelayCommand]
    private void ShowGraphics()
    {
        IsCheatView = false;
        IsSaveGamesView = false;
        UpdateViewVisibility();
    }

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
        if (_snapshot is null || !HasPendingChanges)
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
            ShowMessage("Review the tool-managed files before removing those changes.", "#E04D42");
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
        if (result.Succeeded && !await RefreshFromDiskAsync())
        {
            ShowMessage($"Changes were applied, but the configuration could not be refreshed.", "#E04D42");
            return;
        }

        ShowMessage(result.Message, result.Succeeded ? "#B4D941" : "#E04D42");
    }

    [RelayCommand]
    private async Task RevertLastAsync()
    {
        if (_snapshot is null || HasPendingChanges || IsReviewingChanges)
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
        if (result.Succeeded && !await RefreshFromDiskAsync())
        {
            ShowMessage("The backup was restored, but the configuration could not be refreshed.", "#E04D42");
            return;
        }

        ShowMessage(result.Message, result.Succeeded ? "#B4D941" : "#E04D42");
    }

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

    partial void OnIsCheatViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCheatView));
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

    partial void OnCheatChanging(CheatViewModel? oldValue, CheatViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnChildPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnChildPropertyChanged;
    }

    partial void OnCheatChanged(CheatViewModel? value)
    {
        OnPropertyChanged(nameof(IsCheatAvailable));
        OnPropertyChanged(nameof(IsCheatUnavailable));
        OnPropertyChanged(nameof(IsAnyOperationRunning));
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
                // Headless-Unit-Test ohne Avalonia-Application/Message-Pump.
                ApplyViewMode();
                return;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(ApplyViewMode);
        }

        catch (OperationCanceledException)

        {

            // Ein neuerer Tastendruck hat diese Suche abgelöst.

        }

    }


    partial void OnCanRevertLastChanged(bool value) => OnPropertyChanged(nameof(CanUndo));

    partial void OnHasRemovableToolChangesChanged(bool value) => OnPropertyChanged(nameof(CanRemoveToolChanges));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRemoveToolChanges));
        OnPropertyChanged(nameof(IsAnyOperationRunning));
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanRestoreGameDefaults));
    }

    partial void OnIsReviewingChangesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRemoveToolChanges));
        OnPropertyChanged(nameof(ShowPendingActions));
        OnPropertyChanged(nameof(ShowBottomBar));
        OnPropertyChanged(nameof(ShowReviewActions));
        OnPropertyChanged(nameof(ShowBottomBar));
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
        IsBusy = true;
        DetectionStatus = "Scanning game files";
        ShowMessage("Reading the installation and settings...", "#FF5A00");
        try
        {
            GameInspectionSnapshot snapshot = await Task.Run(_inspector.Inspect);
            if (await Task.Run(() => _settingsEditor.RecoverInterruptedChanges(snapshot)))
            {
                snapshot = await Task.Run(_inspector.Inspect);
            }
            bool canKeepChildState = _verifiedGameContext?.Matches(snapshot) == true &&
                SaveManager is not null && Cheat is not null;
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
            LogDetection("detected");

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
            }
            else
            {
                SaveManager?.Dispose();
                SaveManager = await CreateSaveManagerAsync();
                SaveManager?.Activate();
                Cheat?.Dispose();
                Cheat = CreateCheat();
                Cheat?.Start();
                _saveGamesRefreshFailed = _verifiedGameContext is not null && SaveManager is null;
            }
            if (Cheat is not null && SaveManager is not null)
            {
                SaveManager.IsGameRunning = Cheat.IsGameRunning;
                Cheat.UpdateSlotAvailability(SaveManager.Slots);
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
            // Clear the whole internal editor state and writer services so no stale
            // writable editor remains active after a failed scan (F004/F077).
            foreach (SettingEditorViewModel editor in _editors.Values)
            {
                editor.Changed -= OnEditorChanged;
            }
            _editors.Clear();
            _allFeatureGroups = [];
            PendingChanges = [];
            SaveManager?.Dispose();
            SaveManager = null;
            Cheat?.Dispose();
            Cheat = null;
            ShowMessage($"Scan failed: {exception.Message}", "#E04D42");
            LogDetection("failed: "+exception.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static VerifiedGameContext? VerifyGameContext(GameInspectionSnapshot snapshot) =>
        VerifiedGameContext.TryCreateFromSnapshot(snapshot);

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
            var viewModel = new SaveManagerViewModel(manager, context.UserDataDirectory, watchdog);
            viewModel.Refresh(snapshot);
            return viewModel;
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"Could not load save games: {exception.Message}", "#E04D42");
            return null;
        }
    }


    private CheatViewModel? CreateCheat()
    {
        if (_verifiedGameContext is not { } context)
        {
            return null;
        }

        var service = new SaveGameCheatService(context, _gameContextVerifier);
        return new CheatViewModel(
            service,
            async (slot, checkpointId) =>
            {
                if (SaveManager is null)
                {
                    return new SaveGameOperationResult(false, "Save manager is not available; reload first.");
                }

                return await SaveManager.RunLoad(slot, checkpointId);
            });
    }    private void RebuildEditors()
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
            ShowMessage("Review the pending values, then apply or discard them.", "#E04D42");
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

    private void LogDetection(string result)
    {
        string store = _snapshot?.Installation?.Store.ToString() ?? "unknown";
        string data = string.IsNullOrWhiteSpace(_snapshot?.UserDataDirectory)
            ? "missing user-data"
            : "user-data found";
        AppDiagnostics.Logger?.Write("Self-test: store=" + store + " " + data + " => " + result);
    }

    private void ShowMessage(string message, string accent)
    {
        OperationMessage = message;
        OperationAccent = accent;
    }

    private static string GetAccentColor(ReadableSettingState state) => state switch
    {
        ReadableSettingState.Enabled => "#B4D941",
        ReadableSettingState.Disabled => "#7A877A",
        ReadableSettingState.Modified => "#FF5A00",
        _ => "#687668",
    };

    private static bool IsExpectedUserOperationException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
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
            _ => installation.Store.ToString(),
        };
        string layer = installation.CompatibilityLayer == CompatibilityLayerKind.Proton
            ? "  Proton"
            : string.Empty;
        string identity = installation.BuildId is not null
            ? "Build " + installation.BuildId
            : installation.ContentSignature is not null
                ? "Recognized PAK index signature"
                : "Build unknown";
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
        _searchDebounceSource = null;
        _searchDebounceTask = null;
        SaveManager?.Dispose();
        SaveManager = null;
        Cheat?.Dispose();
Cheat = null;
    }
}


