using AncestorsEnhanced.Core.SaveGames;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SaveManagerViewModel : ViewModelBase, IDisposable
{
    private readonly ISaveGameManager _manager;
    private readonly ISaveGameWatchdog? _watchdog;
    private readonly string _userDataDirectory;
    private bool _loadingSettings;

    private const string ToolSettingsFileName = "AncestorsEnhanced_ToolSettings.json";
    private static readonly System.Text.Json.JsonSerializerOptions JsonSettings =
        new() { WriteIndented = true };

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "No save games loaded yet.";

    [ObservableProperty]
    public partial string StatusAccent { get; set; } = "#8FA1AD";

    [ObservableProperty]
    public partial IReadOnlyList<SaveGameSlotViewModel> Slots { get; set; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsWatchdogEnabled { get; set; }

    private int _cooldownMinutes = 5;

    public int CooldownMinutes
    {
        get => _cooldownMinutes;
        set
        {
            if (SetProperty(ref _cooldownMinutes, value))
            {
                if (_watchdog is not null)
                {
                    _watchdog.Cooldown = TimeSpan.FromMinutes(value);
                }

                if (!_loadingSettings)
                {
                    SaveSettings();
                }
            }
        }
    }

    public string SteamCloudWarning { get; } =
    "Steam Cloud Tip: If Steam shows a 'Cloud Conflict' on launch, simply select 'Upload to Steam Cloud (Local files)' to keep your restored save!";

    public SaveManagerViewModel(ISaveGameManager manager, string userDataDirectory, ISaveGameWatchdog? watchdog = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        _userDataDirectory = userDataDirectory;
        _watchdog = watchdog;
        if (_watchdog is not null)
        {
            _watchdog.CheckpointCreated += OnWatchdogCheckpointCreated;
        }

        LoadSettings();
    }

    public bool HasSlots => Slots.Count > 0;

    public int[] CooldownChoices { get; } = [5, 10, 20];

    public bool HasNoSlots => !HasSlots;

    public bool CanCreate => !IsBusy;

    public void Refresh(SaveGamesSnapshot snapshot)
    {
        Slots = snapshot.Slots
            .Select(slot => new SaveGameSlotViewModel(
                slot,
                () => RunCreate(slot.SlotNumber),
                checkpoint => () => RunLoad(slot.SlotNumber, checkpoint.Id)))
            .ToArray();
        NotifyState();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Reloading save games...";
        StatusAccent = "#78AEE8";
        NotifyState();
        try
        {
            SaveGamesSnapshot snapshot = await Task.Run(_manager.Inspect);
            Refresh(snapshot);
            StatusMessage = "Save games reloaded.";
            StatusAccent = "#62C9A7";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = $"Could not reload save games: {exception.Message}";
            StatusAccent = "#D6BC84";
        }
        finally
        {
            IsBusy = false;
            NotifyState();
        }
    }

    private async Task RunOperation(Func<SaveGameOperationResult> operation)
    {
        IsBusy = true;
        StatusMessage = "Working...";
        StatusAccent = "#78AEE8";
        NotifyState();
        SaveGameOperationResult result;
        try
        {
            result = await Task.Run(operation);
        }
        finally
        {
            IsBusy = false;
        }

        if (result.Succeeded)
        {
            try
            {
                SaveGamesSnapshot snapshot = await Task.Run(_manager.Inspect);
                Refresh(snapshot);
                StatusMessage = result.Message;
                StatusAccent = "#62C9A7";
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                StatusMessage = $"{result.Message} (checkpoints could not be refreshed: {exception.Message})";
                StatusAccent = "#D6BC84";
            }
        }
        else
        {
            StatusMessage = result.Message;
            StatusAccent = "#D6BC84";
        }

        NotifyState();
    }

    public async Task RunCreate(string slotNumber)
    {
        if (IsBusy)
        {
            return;
        }

        await RunOperation(() => _manager.CreateCheckpoint(slotNumber));
    }

    public async Task RunLoad(string slotNumber, string checkpointId)
    {
        if (IsBusy)
        {
            return;
        }

        await RunOperation(() => _manager.LoadCheckpoint(slotNumber, checkpointId));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_watchdog is not null)
        {
            _watchdog.CheckpointCreated -= OnWatchdogCheckpointCreated;
            _watchdog.StopWatch();
        }
    }

    partial void OnIsWatchdogEnabledChanged(bool value)
    {
        if (_watchdog is not null)
        {
            if (value)
            {
                _watchdog.Start();
            }
            else
            {
                _watchdog.StopWatch();
            }
        }

        if (!_loadingSettings)
        {
            SaveSettings();
        }
    }

    private string ToolSettingsPath() =>
        Path.Combine(_userDataDirectory, ToolSettingsFileName);

    private void LoadSettings()
    {
        string path = ToolSettingsPath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            ToolSettings? settings = System.Text.Json.JsonSerializer.Deserialize<ToolSettings>(
                File.ReadAllText(path),
                JsonSettings);
            if (settings is null)
            {
                return;
            }

            _loadingSettings = true;
            try
            {
                CooldownMinutes = settings.WatchdogIntervalMinutes;
                IsWatchdogEnabled = settings.IsWatchdogEnabled;
            }
            finally
            {
                _loadingSettings = false;
            }
        }
        catch (Exception exception) when (
            exception is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new ToolSettings
            {
                IsWatchdogEnabled = IsWatchdogEnabled,
                WatchdogIntervalMinutes = _cooldownMinutes,
            };

            string? directory = Path.GetDirectoryName(ToolSettingsPath());
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                ToolSettingsPath(),
                System.Text.Json.JsonSerializer.Serialize(settings, JsonSettings));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreate));
    }

    private async void OnWatchdogCheckpointCreated(object? sender, string slotNumber)
    {
        await RefreshAsync();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasSlots));
        OnPropertyChanged(nameof(HasNoSlots));
        OnPropertyChanged(nameof(CanCreate));
    }

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException;
}
