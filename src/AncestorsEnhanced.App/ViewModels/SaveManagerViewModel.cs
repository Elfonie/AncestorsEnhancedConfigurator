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
    private Task? _pendingSettingsWrite;
    private int _settingsVersion;

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
    public partial bool IsGameRunning { get; set; }

    [ObservableProperty]
    public partial bool IsWatchdogEnabled { get; set; }

    private int _cooldownMinutes = 5;

    public int CooldownMinutes
    {
        get => _cooldownMinutes;
        set
        {
            // Only sane, bounded cooldowns are accepted (1..1440 minutes).
            int normalized = Math.Clamp(value, 1, 1440);
            if (SetProperty(ref _cooldownMinutes, normalized))
            {
                if (_watchdog is not null)
                {
                    _watchdog.Cooldown = TimeSpan.FromMinutes(normalized);
                }

                if (!_loadingSettings)
                {
                    SaveSettings();
                }
            }
        }
    }

    public string SteamCloudWarning { get; } =
        "Steam Cloud: do not choose a conflict option automatically. Compare save dates and sizes first. Local files are the intended version only after a deliberate local restore; when unsure, copy the local saves before deciding.";

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
            _watchdog.WatcherError += OnWatcherError;
        }

        LoadSettings();
    }

    public bool HasSlots => Slots.Any(slot => slot.HasSave);

    public int[] CooldownChoices { get; } = [5, 10, 20];

    public bool HasNoSlots => !HasSlots;

    public bool CanCreate => !IsBusy;

    /// <summary>Starts persisted watchdog settings only after the owner has loaded slots.</summary>
    public void Activate()
    {
        if (IsWatchdogEnabled)
        {
            _watchdog?.Start();
        }
    }

    public void Refresh(SaveGamesSnapshot snapshot)
    {
        var expandedSlots = Slots
            .Where(slot => slot.IsShowingAllCheckpoints)
            .Select(slot => slot.SlotNumber)
            .ToHashSet(StringComparer.Ordinal);

        Slots = snapshot.Slots
            .Select(slot => new SaveGameSlotViewModel(
                slot,
                () => RunCreate(slot.SlotNumber),
                checkpoint => () => RunLoad(slot.SlotNumber, checkpoint.Id),
                checkpoint => () => RunDelete(slot.SlotNumber, checkpoint.Id),
                () => !IsGameRunning,
                expandedSlots.Contains(slot.SlotNumber)))
            .ToArray();


        StatusMessage = HasSlots ? "Save games loaded successfully." : "No save games loaded yet.";
        StatusAccent = HasSlots ? "#B4D941" : "#7A877A";

        NotifyState();
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        foreach (SaveGameSlotViewModel slot in Slots)
        {
            foreach (SaveGameCheckpointViewModel checkpoint in slot.Checkpoints)
            {
                checkpoint.RefreshRestoreAvailability();
            }
        }
    }

    public async Task<bool> RefreshSilentlyAsync()
    {
        try
        {
            Refresh(await Task.Run(_manager.Inspect));
            return true;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = $"Could not reload save games: {exception.Message}";
            StatusAccent = "#E04D42";
            return false;
        }
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
            StatusAccent = "#E04D42";
        }
        finally
        {
            IsBusy = false;
            NotifyState();
        }
    }

    private async Task<SaveGameOperationResult> RunOperation(Func<SaveGameOperationResult> operation)
    {
        IsBusy = true;
        StatusMessage = "Working...";
        StatusAccent = "#FF5A00";
        NotifyState();
        try
        {
            SaveGameOperationResult result;
            try
            {
                result = await Task.Run(operation);
            }
            catch (Exception exception)
            {
                StatusMessage = $"Operation failed: {exception.Message}";
                StatusAccent = "#E04D42";
                NotifyState();
                return new SaveGameOperationResult(false, $"Operation failed: {exception.Message}");
            }

            if (result.Succeeded)
            {
                try
                {
                    // The post-operation refresh must complete while the UI is still
                    // marked busy, so the operation is only released once the view
                    // reflects the new disk state (F006).
                    SaveGamesSnapshot snapshot = await Task.Run(_manager.Inspect);
                    Refresh(snapshot);
                    StatusMessage = result.Message;
                    StatusAccent = "#B4D941";
                }
                catch (Exception exception)
                {
                    StatusMessage = $"{result.Message} (checkpoints could not be refreshed: {exception.Message})";
                    StatusAccent = "#E04D42";
                }
            }
            else
            {
                StatusMessage = result.Message;
                StatusAccent = "#E04D42";
            }

            NotifyState();
            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyState();
        }
    }

    public async Task RunCreate(string slotNumber)
    {
        if (IsBusy)
        {
            return;
        }

        await RunOperation(() => _manager.CreateCheckpoint(slotNumber));
    }

    public async Task<SaveGameOperationResult> RunLoad(string slotNumber, string checkpointId)
    {
        if (IsBusy)
        {
            return new SaveGameOperationResult(false, "Another save operation is already running.");
        }

        using IDisposable? mutation = _watchdog is not null && int.TryParse(slotNumber, out int slot)
            ? _watchdog.BeginSlotMutation(slot)
            : null;
        return await RunOperation(() => _manager.LoadCheckpoint(slotNumber, checkpointId));
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
        try
        {
            _pendingSettingsWrite?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _settingsWriteLock.Dispose();
        if (_watchdog is not null)
        {
            _watchdog.CheckpointCreated -= OnWatchdogCheckpointCreated;
            _watchdog.WatcherError -= OnWatcherError;
            _watchdog.StopWatch();
        }
    }

    private void OnWatcherError(object? sender, string message)
    {
        void Update()
        {
            StatusMessage = $"Auto-backup watch failed: {message}";
            StatusAccent = "#E04D42";
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
        }
        else
        {
            _dispatchToUi(Update);
        }
    }

    partial void OnIsWatchdogEnabledChanged(bool value)
    {
        if (_watchdog is not null)
        {
            if (value && !_loadingSettings)
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
        int version = Interlocked.Increment(ref _settingsVersion);
        _pendingSettingsWrite = WriteSettingsAsync(path, settings, version);
    }

    private async Task WriteSettingsAsync(string path, ToolSettings settings, int version)
    {
        try
        {
            await _settingsWriteLock.WaitAsync();
            try
            {
                // A delayed older task must never overwrite a newer snapshot. The
                // latest queued task remains the one Dispose waits for (F011).
                if (version != Volatile.Read(ref _settingsVersion))
                {
                    return;
                }

                byte[] payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(settings, JsonSettings);
                // Atomic replace: write a temp file then move it over the target,
                // so a crash never leaves a truncated settings file.
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, payload);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                ReportStatus("Could not save tool settings: " + exception.Message, "#E04D42");
            }
            finally
            {
                _settingsWriteLock.Release();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportStatus("Could not save tool settings: " + exception.Message, "#E04D42");
        }
    }

    private void ReportStatus(string message, string accent)
    {
        void Update()
        {
            StatusMessage = message;
            StatusAccent = accent;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
        }
        else
        {
            _dispatchToUi(Update);
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreate));
    }

    private void OnWatchdogCheckpointCreated(object? sender, string slotNumber)
    {
        // Der Watchdog feuert vom Thread-Pool; UI-Änderungen gehören auf den UI-Thread.
        // In Tests ohne UI-Loop läuft der Aufruf synchron über CheckAccess.
        void Refresh() => _ = RefreshAfterWatchdogAsync();

        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            _dispatchToUi(Refresh);
        }
    }

    private async Task RefreshAfterWatchdogAsync()
    {
        try
        {
            // An automatic checkpoint must not activate the full-window busy overlay.
            SaveGamesSnapshot snapshot = await Task.Run(_manager.Inspect);
            Refresh(snapshot);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not refresh after auto-backup: {exception.Message}";
            StatusAccent = "#E04D42";
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
