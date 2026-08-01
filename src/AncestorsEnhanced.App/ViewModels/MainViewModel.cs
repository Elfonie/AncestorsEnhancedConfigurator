using System.Globalization;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.Safety;
using AncestorsEnhanced.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IReadOnlyGameInspector _inspector;
    private IReadOnlyList<FeatureGroupSnapshot> _allFeatureGroups = [];

    [ObservableProperty]
    private string _detectionStatus = "Inspection has not run yet";

    [ObservableProperty]
    private string _inspectionTime = "Not inspected";

    [ObservableProperty]
    private string _installationPath = "Not detected";

    [ObservableProperty]
    private string _installationDetails = "Steam build unknown";

    [ObservableProperty]
    private string _userDataPath = "Not detected";

    [ObservableProperty]
    private string _binarySettingsPath = "Not detected";

    [ObservableProperty]
    private string _binarySettingsStatus = "System.sav has not been inspected";

    [ObservableProperty]
    private string _gameMenuSettingsSummary = "Game menu settings have not been inspected";

    [ObservableProperty]
    private IReadOnlyList<FeatureGroupRowViewModel> _featureGroups = [];

    [ObservableProperty]
    private bool _isAdvancedMode;

    [ObservableProperty]
    private string _viewModeTitle = "Simple view";

    [ObservableProperty]
    private string _viewModeDescription = "Important settings and their effective state.";

    [ObservableProperty]
    private IReadOnlyList<ConfigurationFileRowViewModel> _configurationFiles = [];

    [ObservableProperty]
    private IReadOnlyList<SettingRowViewModel> _settings = [];

    [ObservableProperty]
    private IReadOnlyList<PakFileRowViewModel> _pakFiles = [];

    [ObservableProperty]
    private IReadOnlyList<NoticeRowViewModel> _notices = [];

    public MainViewModel(IReadOnlyGameInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        _inspector = inspector;

        ApplicationSafetyProfile safetyProfile = ApplicationSafetyProfile.Foundation;
        ProductName = "Ancestors Enhanced Configurator";
        Phase = "Read-only inspection · 0.2 development";
        SafetyStatus = safetyProfile.IsReadOnly
            ? "Read-only: game-file writes are disabled"
            : "Write operations enabled";

        Refresh();
    }

    public string ProductName { get; }

    public string Phase { get; }

    public string SafetyStatus { get; }

    public int ConfigurationFileCount => ConfigurationFiles.Count;

    public int SettingCount => Settings.Count;

    public int PakFileCount => PakFiles.Count;

    [RelayCommand]
    private void Refresh()
    {
        GameInspectionSnapshot snapshot = _inspector.Inspect();

        DetectionStatus = snapshot.IsGameDetected
            ? snapshot.HasErrors
                ? "Ancestors detected with problems"
                : "Ancestors detected successfully"
            : "Ancestors installation not detected";
        InspectionTime = $"Last checked: {snapshot.InspectedAtUtc.ToLocalTime():G}";
        InstallationPath = snapshot.Installation?.InstallDirectory ?? "Not detected";
        InstallationDetails = snapshot.Installation is null
            ? "Steam build unknown"
            : $"Steam · Build {snapshot.Installation.BuildId ?? "unknown"} · " +
              (snapshot.Installation.ExecutableExists ? "executable verified" : "executable missing");
        UserDataPath = snapshot.UserDataDirectory ?? "Not detected";
        BinarySettingsPath = snapshot.BinarySettingsFile?.FullPath ?? "Not detected";
        BinarySettingsStatus = snapshot.BinarySettingsFile?.FormatStatus ?? "Not inspected";
        GameMenuSettingsSummary = snapshot.BinarySettingsFile?.Exists == true
            ? "The game's own resolution and quality presets were found in System.sav. " +
              "Their custom binary values are not shown until the decoder is verified."
            : "The game's own graphics-settings file has not been created yet.";

        _allFeatureGroups = ReadableSettingsCatalog.CreateFeatureGroups(snapshot);
        ApplyViewMode();

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

    partial void OnIsAdvancedModeChanged(bool value) => ApplyViewMode();

    private void ApplyViewMode()
    {
        ViewModeTitle = IsAdvancedMode ? "Advanced view" : "Simple view";
        ViewModeDescription = IsAdvancedMode
            ? "All verified renderer settings, game-controlled values, and technical sources."
            : "Important visual settings with only the controls that are useful to most players.";

        FeatureGroups = _allFeatureGroups
            .Where(group => IsAdvancedMode || group.IsEssential)
            .Select(group =>
            {
                FeatureSettingSnapshot[] visibleSettings = group.Settings
                    .Where(setting => IsAdvancedMode || !setting.IsAdvanced)
                    .ToArray();

                return new FeatureGroupRowViewModel(
                    group.Category,
                    group.Name,
                    group.Summary,
                    group.Description,
                    GetAccentColor(group.State),
                    visibleSettings.Length == 1
                        ? "1 setting"
                        : $"{visibleSettings.Length} settings",
                    visibleSettings
                        .Select(setting => new FeatureSettingRowViewModel(
                            setting.Name,
                            setting.Value,
                            setting.Description,
                            setting.Source,
                            setting.TechnicalKey is null
                                ? setting.Source
                                : $"{setting.TechnicalKey} · {setting.Source}",
                            GetAccentColor(setting.State),
                            IsAdvancedMode))
                        .ToArray());
            })
            .ToArray();
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

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{size:0.##} {units[unit]}");
    }
}
