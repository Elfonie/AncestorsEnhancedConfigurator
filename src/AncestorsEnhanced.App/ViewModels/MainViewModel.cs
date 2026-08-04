using System.Globalization;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Core.Settings;
using AncestorsEnhanced.Infrastructure.SaveGames;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyGameInspector _inspector;
    private readonly IGameSettingsEditor _settingsEditor;
    private readonly Func<string?, ISaveGameManager> _saveManagerFactory;
    private readonly Dictionary<string, SettingEditorViewModel> _editors =
        new(StringComparer.Ordinal);
    private IReadOnlyList<FeatureGroupSnapshot> _allFeatureGroups = [];
    private GameInspectionSnapshot? _snapshot;
    private SettingsChangePlan? _reviewPlan;

    [ObservableProperty]
    public partial string DetectionStatus { get; set; } = "Not checked yet";

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
    public partial string OperationAccent { get; set; } = "#8FA1AD";

    [ObservableProperty]
    public partial bool CanRevertLast { get; set; }

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
        Func<string?, ISaveGameManager>? saveManagerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(settingsEditor);
        _inspector = inspector;
        _settingsEditor = settingsEditor;
        _saveManagerFactory = saveManagerFactory ?? (d => string.IsNullOrWhiteSpace(d) ? throw new InvalidOperationException("The user-data directory is required.") : new SafeSaveGameManager(d));

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

    public bool ShowPendingActions => HasPendingChanges && !IsReviewingChanges;

    public bool ShowReviewActions => IsReviewingChanges;

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

    public string ReviewSummary => ReviewChanges.Count == 1
        ? "Review 1 change before writing"
        : $"Review {ReviewChanges.Count} changes before writing";

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
            ShowMessage("Configuration loaded. No files were changed.", "#8FA1AD");
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
            ShowMessage("Apply or discard the pending changes before refreshing.", "#D6BC84");
            return;
        }

        if (await RefreshFromDiskAsync())
        {
            ShowMessage("Configuration reloaded from disk.", "#62C9A7");
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
        ShowMessage("Pending changes discarded. No files were changed.", "#8FA1AD");
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
                "#8FA1AD");
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
            "#78AEE8");
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
            ReviewChanges = _reviewPlan.Changes
                .Select(change => new ChangeReviewRowViewModel(
                    change.DisplayName,
                    $"{change.FileName} · {change.Key}",
                    change.Before ?? "Game default",
                    change.After ?? "Game default"))
                .ToArray();
            IsReviewingChanges = true;
            ShowMessage("Check every value, then confirm the write.", "#78AEE8");
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage(exception.Message, "#D6BC84");
        }
    }

    [RelayCommand]
    private void CancelReview()
    {
        CloseReview();
        ShowMessage("Review closed. Your pending values are unchanged.", "#8FA1AD");
    }

    [RelayCommand]
    private async Task ConfirmApplyAsync()
    {
        SettingsChangePlan? plan = _reviewPlan;
        if (plan is null || !IsReviewingChanges)
        {
            return;
        }

        _reviewPlan = null;
        IsReviewingChanges = false;
        ReviewChanges = [];
        IsBusy = true;
        ShowMessage("Applying the reviewed changes...", "#78AEE8");
        SettingsOperationResult result;
        try
        {
            result = await Task.Run(() => _settingsEditor.Apply(plan));
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"No changes were kept: {exception.Message}", "#D6BC84");
            return;
        }
        finally
        {
            IsBusy = false;
        }
        if (result.Succeeded)
        {
            await RefreshFromDiskAsync();
        }

        ShowMessage(result.Message, result.Succeeded ? "#62C9A7" : "#D6BC84");
    }

    [RelayCommand]
    private async Task RevertLastAsync()
    {
        if (_snapshot is null || HasPendingChanges || IsReviewingChanges)
        {
            return;
        }

        IsBusy = true;
        ShowMessage("Restoring the last configurator backup...", "#78AEE8");
        SettingsOperationResult result;
        try
        {
            result = await Task.Run(() => _settingsEditor.RevertLast(_snapshot));
        }
        finally
        {
            IsBusy = false;
        }
        if (result.Succeeded)
        {
            await RefreshFromDiskAsync();
        }

        ShowMessage(result.Message, result.Succeeded ? "#62C9A7" : "#D6BC84");
    }

    partial void OnIsAdvancedModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSimpleMode));
        ApplyViewMode();
    }

    partial void OnIsSaveGamesViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowSaveGamesView));
    }

    partial void OnIsCheatViewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCheatView));
        OnPropertyChanged(nameof(ShowGraphicsView));
        OnPropertyChanged(nameof(ShowSaveGamesView));
    }

    partial void OnSaveManagerChanged(SaveManagerViewModel? value)
    {
        OnPropertyChanged(nameof(IsSaveManagerAvailable));
        OnPropertyChanged(nameof(IsSaveManagerUnavailable));
    }

    partial void OnSearchTextChanged(string value) => ApplyViewMode();

    partial void OnCanRevertLastChanged(bool value) => OnPropertyChanged(nameof(CanUndo));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanRestoreGameDefaults));
    }

    partial void OnIsReviewingChangesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(ShowPendingActions));
        OnPropertyChanged(nameof(ShowReviewActions));
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanRestoreGameDefaults));
    }

    partial void OnReviewChangesChanged(IReadOnlyList<ChangeReviewRowViewModel> value) =>
        OnPropertyChanged(nameof(ReviewSummary));

    private async Task<bool> RefreshFromDiskAsync()
    {
        CloseReview();
        IsBusy = true;
        DetectionStatus = "Scanning game files";
        ShowMessage("Reading the installation and settings...", "#78AEE8");
        try
        {
            GameInspectionSnapshot snapshot = await Task.Run(_inspector.Inspect);
            _snapshot = snapshot;
            DetectionStatus = snapshot.IsGameDetected
                ? snapshot.HasErrors
                    ? "Ancestors detected with problems"
                    : "Ancestors is ready"
                : "Ancestors installation not detected";
            InstallationPath = snapshot.Installation?.InstallDirectory ?? "Not detected";
            InstallationDetails = snapshot.Installation is null
                ? "Store and build unknown"
                : FormatInstallation(snapshot.Installation);
            UserDataPath = snapshot.UserDataDirectory ?? "Not detected";
            BinarySettingsPath = snapshot.BinarySettingsFile?.FullPath ?? "Not detected";
            BinarySettingsStatus = snapshot.BinarySettingsFile?.FormatStatus ?? "Not inspected";
            LogDetection("detected");

            _allFeatureGroups = ReadableSettingsCatalog.CreateFeatureGroups(snapshot);
            RebuildEditors();
            ApplyViewMode();
            LoadTechnicalDetails(snapshot);
            CanRevertLast = _settingsEditor.CanRevertLast(snapshot);
            SaveManager?.Dispose();
            SaveManager = await CreateSaveManagerAsync(snapshot.UserDataDirectory);
            Cheat = CreateCheat(snapshot.UserDataDirectory);
            UpdatePendingChanges();
            return true;
        }
        catch (Exception exception)
        {
            _snapshot = null;
            DetectionStatus = "Scan failed";
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
            ShowMessage($"Scan failed: {exception.Message}", "#D6BC84");
            LogDetection("failed: "+exception.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<SaveManagerViewModel?> CreateSaveManagerAsync(string? userDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(userDataDirectory))
        {
            return null;
        }

        ISaveGameManager manager = _saveManagerFactory(userDataDirectory);
        var watchdog = new SaveGameWatchdog(userDataDirectory);
        var viewModel = new SaveManagerViewModel(manager, userDataDirectory, watchdog);
        try
        {
            SaveGamesSnapshot snapshot = await Task.Run(manager.Inspect);
            viewModel.Refresh(snapshot);
            return viewModel;
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            return new SaveManagerViewModel(manager, userDataDirectory, watchdog);
        }
    }


    private static CheatViewModel? CreateCheat(string? userDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(userDataDirectory))
        {
            return null;
        }

        var injector = new SaveGameCheatInjector();
        var service = new SaveGameCheatService(injector, userDataDirectory);
        return new CheatViewModel(service);
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

        string query = IsAdvancedMode ? SearchText.Trim() : "";
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
                IsAdvancedMode,
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
            settings.Length == 1 ? "1 option" : $"{settings.Length} options",
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
            ShowMessage("Review the pending values, then apply or discard them.", "#D6BC84");
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

        ReviewChanges = [];
        IsReviewingChanges = false;
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
        ReadableSettingState.Enabled => "#62C9A7",
        ReadableSettingState.Disabled => "#8FA1AD",
        ReadableSettingState.Modified => "#78AEE8",
        _ => "#C3A66A",
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
        return $"{store}  {installation.Host}{layer}  " +
               $"Build {installation.BuildId ?? "content verified"}";
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SaveManager?.Dispose();
        SaveManager = null;
        Cheat = null;
    }
}


