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

    [ObservableProperty]
    private string _detectionStatus = "Not checked yet";

    [ObservableProperty]
    private string _inspectionTime = "";

    [ObservableProperty]
    private string _installationPath = "Not detected";

    [ObservableProperty]
    private string _installationDetails = "Steam build unknown";

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
    private string _operationMessage = "Ready. Nothing is written until you choose Apply changes.";

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
        Phase = "0.3 · safe editing preview";
        RefreshFromDisk();
    }

    public string ProductName { get; }

    public string Phase { get; }

    public bool HasPendingChanges => PendingChanges.Count > 0;

    public bool CanUndo => CanRevertLast && !HasPendingChanges;

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
        foreach (SettingEditorViewModel editor in _editors.Values)
        {
            editor.Reset();
        }

        UpdatePendingChanges();
        ShowMessage("Pending changes discarded. No files were changed.", "#8FA1AD");
    }

    [RelayCommand]
    private void ApplyChanges()
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
            SettingsChangePlan plan = _settingsEditor.CreatePlan(_snapshot, requests);
            SettingsOperationResult result = _settingsEditor.Apply(plan);
            if (!result.Succeeded)
            {
                ShowMessage(result.Message, "#D6BC84");
                return;
            }

            RefreshFromDisk();
            ShowMessage(result.Message, "#62C9A7");
        }
        catch (InvalidOperationException exception)
        {
            ShowMessage(exception.Message, "#D6BC84");
        }
    }

    [RelayCommand]
    private void RevertLast()
    {
        if (_snapshot is null || HasPendingChanges)
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

    private void RefreshFromDisk()
    {
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
            ? "Steam build unknown"
            : $"Steam · Build {snapshot.Installation.BuildId ?? "unknown"}";
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
}
