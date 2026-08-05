using AncestorsEnhanced.Core.SaveGames;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SaveManagerViewModel : ViewModelBase, IDisposable
{
    private readonly ISaveGameManager _manager;
    private readonly ISaveGameWatchdog? _watchdog;
    private readonly string _userDataDirectory;
    private readonly Action<Action> _dispatchToUi;
    private bool _loadingSettings;
    private readonly SemaphoreSlim _settingsWriteLock = new(1, 1);

    private const string ToolSettingsFileName = "AncestorsEnhanced_ToolSettings.json";
    private static readonly System.Text.Json.JsonSerializerOptions JsonSettings =
        new() { WriteIndented = true };

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "No save games loaded yet.";

    [ObservableProperty]
    public partial string StatusAccent { get; set; } = "#7A877A";

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

    public SaveManagerViewModel(
        ISaveGameManager manager,
        string userDataDirectory,
        ISaveGameWatchdog? watchdog = null)
        : this(manager, userDataDirectory, watchdog, dispatchToUi: null)
    {
    }

    public SaveManagerViewModel(
        ISaveGameManager manager,
        string userDataDirectory,
        ISaveGameWatchdog? watchdog,
        Action<Action>? dispatchToUi)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        _userDataDirectory = userDataDirectory;
        _watchdog = watchdog;
        _dispatchToUi = dispatchToUi ?? (action => Dispatcher.UIThread.Post(action));
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
                checkpoint => () => RunLoad(slot.SlotNumber, checkpoint.Id),
                checkpoint => () => RunDelete(slot.SlotNumber, checkpoint.Id)))
            .ToArray();


        StatusMessage = HasSlots ? "Save games loaded successfully." : "No save games loaded yet.";
        StatusAccent = HasSlots ? "#B4D941" : "#7A877A";

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
        StatusAccent = "#FF5A00";
        NotifyState();
        try
        {
            SaveGamesSnapshot snapshot = await Task.Run(_manager.Inspect);
            Refresh(snapshot);
            StatusMessage = "Save games reloaded.";
            StatusAccent = "#B4D941";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = $"Could not reload save games: {exception.Message}";
            StatusAccent = "#D92316";
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
        StatusAccent = "#FF5A00";
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
                StatusAccent = "#B4D941";
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                StatusMessage = $"{result.Message} (checkpoints could not be refreshed: {exception.Message})";
                StatusAccent = "#D92316";
            }
        }
        else
        {
            StatusMessage = result.Message;
            StatusAccent = "#D92316";
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

        if (_watchdog is not null && int.TryParse(slotNumber, out int slot))
        {
            _watchdog.SuppressSlot(slot, TimeSpan.FromSeconds(5));
        }

        await RunOperation(() => _manager.LoadCheckpoint(slotNumber, checkpointId));
    }

    public async Task RunDelete(string slotNumber, string checkpointId)
    {
        if (IsBusy)
        {
            return;
        }

        await RunOperation(() => _manager.DeleteCheckpoint(slotNumber, checkpointId));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _settingsWriteLock.Dispose();
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
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory) || !File.Exists(path))
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
        var settings = new ToolSettings
        {
            IsWatchdogEnabled = IsWatchdogEnabled,
            WatchdogIntervalMinutes = _cooldownMinutes,
        };

        string? directory = Path.GetDirectoryName(ToolSettingsPath());
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        string path = ToolSettingsPath();
        _ = Task.Run(async () =>
        {
            await _settingsWriteLock.WaitAsync();
            try
            {
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(settings, JsonSettings));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
            finally
            {
                _settingsWriteLock.Release();
            }
        });
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreate));
    }

    private void OnWatchdogCheckpointCreated(object? sender, string slotNumber)
    {
        // Der Watchdog feuert vom Thread-Pool; UI-Änderungen gehören auf den UI-Thread.
        // In Tests ohne UI-Loop läuft der Aufruf synchron über CheckAccess.
        void Refresh()
        {
            try
            {
                _ = RefreshAsync();
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                StatusMessage = $"Could not refresh after auto-backup: {exception.Message}";
                StatusAccent = "#D92316";
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            _dispatchToUi(Refresh);
        }
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
