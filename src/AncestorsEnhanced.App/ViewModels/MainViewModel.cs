using System.Globalization;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IReadOnlyGameInspector _inspector;
    private readonly IGameSettingsEditor _settingsEditor;
    private readonly Dictionary<string, SettingEditorViewModel> _editors =
        new(StringComparer.Ordinal);
    private IReadOnlyList<FeatureGroupSnapshot> _allFeatureGroups = [];
    private GameInspectionSnapshot? _snapshot;
    private SettingsChangePlan? _reviewPlan;

    [ObservableProperty]
    private string _detectionStatus = "Not checked yet";

    [ObservableProperty]
    private string _inspectionTime = "";

    [ObservableProperty]
    private string _installationPath = "Not detected";

    [ObservableProperty]
    private string _installationDetails = "Store and build unknown";

    [ObservableProperty]
    private string _userDataPath = "Not detected";

    [ObservableProperty]
    private string _binarySettingsPath = "Not detected";

    [ObservableProperty]
    private string _binarySettingsStatus = "Not inspected";

    [ObservableProperty]
    private IReadOnlyList<FeatureGroupRowViewModel> _featureGroups = [];

    [ObservableProperty]
    private bool _isAdvancedMode;

    [ObservableProperty]
    private string _viewModeTitle = "Essential settings";

    [ObservableProperty]
    private string _viewModeDescription = "The useful controls first. Changes stay pending until you apply them.";

    [ObservableProperty]
    private IReadOnlyList<PendingChangeRowViewModel> _pendingChanges = [];

    [ObservableProperty]
    private IReadOnlyList<ChangeReviewRowViewModel> _reviewChanges = [];

    [ObservableProperty]
    private bool _isReviewingChanges;

    [ObservableProperty]
    private string _operationMessage = "Ready. Nothing is written until you review and confirm.";

    [ObservableProperty]
    private string _operationAccent = "#8FA1AD";

    [ObservableProperty]
    private bool _canRevertLast;

    [ObservableProperty]
    private IReadOnlyList<ConfigurationFileRowViewModel> _configurationFiles = [];

    [ObservableProperty]
    private IReadOnlyList<SettingRowViewModel> _settings = [];

    [ObservableProperty]
    private IReadOnlyList<PakFileRowViewModel> _pakFiles = [];

    [ObservableProperty]
    private IReadOnlyList<NoticeRowViewModel> _notices = [];

    public MainViewModel(
        IReadOnlyGameInspector inspector,
        IGameSettingsEditor settingsEditor)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(settingsEditor);
        _inspector = inspector;
        _settingsEditor = settingsEditor;

        ProductName = "Ancestors Enhanced Configurator";
        Phase = "0.3 · review and safe editing";
        RefreshFromDisk();
    }

    public string ProductName { get; }

    public string Phase { get; }

    public bool HasPendingChanges => PendingChanges.Count > 0;

    public bool CanUndo => CanRevertLast && !HasPendingChanges && !IsReviewingChanges;

    public bool ShowPendingActions => HasPendingChanges && !IsReviewingChanges;

    public bool ShowReviewActions => IsReviewingChanges;

    public bool CanEditSettings => !IsReviewingChanges;

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

    public int ConfigurationFileCount => ConfigurationFiles.Count;

    public int SettingCount => Settings.Count;

    public int PakFileCount => PakFiles.Count;

    [RelayCommand]
    private void Refresh()
    {
        if (HasPendingChanges)
        {
            ShowMessage("Apply or discard the pending changes before refreshing.", "#D6BC84");
            return;
        }

        RefreshFromDisk();
        ShowMessage("Configuration reloaded from disk.", "#62C9A7");
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
    private void OpenReview()
    {
        if (_snapshot is null || !HasPendingChanges)
        {
            return;
        }

        try
        {
            SettingChangeRequest[] requests = _editors
                .Where(pair => pair.Value.HasChanges)
                .Select(pair => pair.Value.CreateRequest(
                    pair.Key,
                    FindSettingName(pair.Key)))
                .ToArray();
            _reviewPlan = _settingsEditor.CreatePlan(_snapshot, requests);
            ReviewChanges = _reviewPlan.Changes
                .Select(change => new ChangeReviewRowViewModel(
                    change.DisplayName,
                    $"{change.FileName} · {change.Key}",
                    change.Before ?? "Game preset",
                    change.After ?? "Game preset"))
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
    private void ConfirmApply()
    {
        SettingsChangePlan? plan = _reviewPlan;
        if (plan is null || !IsReviewingChanges)
        {
            return;
        }

        _reviewPlan = null;
        IsReviewingChanges = false;
        ReviewChanges = [];
        SettingsOperationResult result;
        try
        {
            result = _settingsEditor.Apply(plan);
        }
        catch (Exception exception) when (IsExpectedUserOperationException(exception))
        {
            ShowMessage($"No changes were kept: {exception.Message}", "#D6BC84");
            return;
        }
        if (result.Succeeded)
        {
            RefreshFromDisk();
        }

        ShowMessage(result.Message, result.Succeeded ? "#62C9A7" : "#D6BC84");
    }

    [RelayCommand]
    private void RevertLast()
    {
        if (_snapshot is null || HasPendingChanges || IsReviewingChanges)
        {
            return;
        }

        SettingsOperationResult result = _settingsEditor.RevertLast(_snapshot);
        if (result.Succeeded)
        {
            RefreshFromDisk();
        }

        ShowMessage(result.Message, result.Succeeded ? "#62C9A7" : "#D6BC84");
    }

    partial void OnIsAdvancedModeChanged(bool value) => ApplyViewMode();

    partial void OnCanRevertLastChanged(bool value) => OnPropertyChanged(nameof(CanUndo));

    partial void OnIsReviewingChangesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(ShowPendingActions));
        OnPropertyChanged(nameof(ShowReviewActions));
        OnPropertyChanged(nameof(CanEditSettings));
    }

    partial void OnReviewChangesChanged(IReadOnlyList<ChangeReviewRowViewModel> value) =>
        OnPropertyChanged(nameof(ReviewSummary));

    private void RefreshFromDisk()
    {
        CloseReview();
        _snapshot = _inspector.Inspect();
        GameInspectionSnapshot snapshot = _snapshot;

        DetectionStatus = snapshot.IsGameDetected
            ? snapshot.HasErrors
                ? "Ancestors detected with problems"
                : "Ancestors is ready"
            : "Ancestors installation not detected";
        InspectionTime = $"Checked {snapshot.InspectedAtUtc.ToLocalTime():G}";
        InstallationPath = snapshot.Installation?.InstallDirectory ?? "Not detected";
        InstallationDetails = snapshot.Installation is null
            ? "Store and build unknown"
            : FormatInstallation(snapshot.Installation);
        UserDataPath = snapshot.UserDataDirectory ?? "Not detected";
        BinarySettingsPath = snapshot.BinarySettingsFile?.FullPath ?? "Not detected";
        BinarySettingsStatus = snapshot.BinarySettingsFile?.FormatStatus ?? "Not inspected";

        _allFeatureGroups = ReadableSettingsCatalog.CreateFeatureGroups(snapshot);
        RebuildEditors();
        ApplyViewMode();
        LoadTechnicalDetails(snapshot);
        CanRevertLast = _settingsEditor.CanRevertLast(snapshot);
        UpdatePendingChanges();
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
        ViewModeTitle = IsAdvancedMode ? "All renderer settings" : "Essential settings";
        ViewModeDescription = IsAdvancedMode
            ? "Every detected renderer value, including technical controls and read-only fields."
            : "The useful controls first. Changes stay pending until you apply them.";

        FeatureGroups = _allFeatureGroups
            .Where(group => IsAdvancedMode || group.IsEssential)
            .Select(group => CreateGroupRow(group, IsAdvancedMode))
            .ToArray();
    }

    private FeatureGroupRowViewModel CreateGroupRow(
        FeatureGroupSnapshot group,
        bool showAdvanced)
    {
        FeatureSettingRowViewModel[] settings = group.Settings
            .Where(setting => showAdvanced || !setting.IsAdvanced)
            .Select(setting => new FeatureSettingRowViewModel(
                setting.Name,
                setting.Value,
                setting.Description,
                setting.Source,
                CreateTechnicalDetails(setting),
                GetAccentColor(setting.State),
                showAdvanced,
                _editors.GetValueOrDefault(setting.Id)))
            .ToArray();

        return new FeatureGroupRowViewModel(
            group.Category,
            group.Name,
            group.Summary,
            group.Description,
            GetAccentColor(group.State),
            settings.Length == 1 ? "1 setting" : $"{settings.Length} settings",
            settings);
    }

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

        OnPropertyChanged(nameof(ConfigurationFileCount));
        OnPropertyChanged(nameof(SettingCount));
        OnPropertyChanged(nameof(PakFileCount));
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

    private void ShowMessage(string message, string accent)
    {
        OperationMessage = message;
        OperationAccent = accent;
    }

    private static string CreateTechnicalDetails(FeatureSettingSnapshot setting)
    {
        string source = setting.TechnicalKey is null
            ? setting.Source
            : $"{setting.TechnicalKey} · {setting.Source}";
        return setting.PresetDetails is null
            ? source
            : $"{source}{Environment.NewLine}{setting.PresetDetails}";
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
}
