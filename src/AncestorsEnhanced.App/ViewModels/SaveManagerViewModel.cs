using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.Platform;
using Avalonia.Media;
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
    private readonly UiMutationGate? _mutationGate;
    private readonly string _storeName;
    private readonly Func<bool> _isGameRunningProbe;
    private readonly DispatcherTimer _gameProcessTimer;
    private bool _loadingSettings;
    private readonly object _settingsWriteGate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Task _settingsWriteTail = Task.CompletedTask;
    private readonly object _watchdogLifecycleGate = new();
    private Task _watchdogLifecycleTail = Task.CompletedTask;
    private int _watchdogLifecycleVersion;
    private int _watchdogLifecycleActivated;
    private int _watchdogDesiredEnabled;
    private CancellationTokenSource? _metadataWriteDebounce;
    private Task? _watchdogRefreshTask;
    private int _gameProcessRefreshInProgress;
    private int _watchdogRefreshVersion;
    private int _settingsVersion;
    private bool _disposed;
    private string? _toolSettingsWarning;
    private Dictionary<string, CheckpointMetadata> _checkpointMetadata = new(StringComparer.Ordinal);

    private const string ToolSettingsFileName = "AncestorsEnhanced_ToolSettings.json";
    private static readonly System.Text.Json.JsonSerializerOptions JsonSettings =
        new() { WriteIndented = true };

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "No save games loaded yet.";

    [ObservableProperty]
    public partial string StatusAccent { get; set; } = "#7A877A";

    public IBrush StatusBrush => StatusPresentation.BrushForLegacyAccent(StatusAccent);

    [ObservableProperty]
    public partial IReadOnlyList<SaveGameSlotViewModel> Slots { get; set; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsGameRunning { get; set; }

    [ObservableProperty]
    public partial bool IsWatchdogEnabled { get; set; }

    [ObservableProperty]
    public partial bool KeepRunningInTrayWhenClosing { get; set; } = true;

    [ObservableProperty]
    public partial string CheckpointSearchText { get; set; } = "";

    [ObservableProperty]
    public partial string CheckpointOriginFilter { get; set; } = "All";

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

    public string CloudWarningTitle => _storeName.Equals("Steam", StringComparison.OrdinalIgnoreCase)
        ? "Steam Cloud saves"
        : $"{_storeName} cloud saves";

    public string CloudWarning =>
        $"Do not choose a {_storeName} cloud conflict option automatically. Compare save dates and sizes first. After a deliberate local restore, verify which copy should remain authoritative.";

    public string SteamCloudWarning => CloudWarning;

    public SaveManagerViewModel(
        ISaveGameManager manager,
        string userDataDirectory,
        ISaveGameWatchdog? watchdog = null)
        : this(manager, userDataDirectory, watchdog, dispatchToUi: null, mutationGate: null, gameRunningProbe: null)
    {
    }

    public SaveManagerViewModel(
        ISaveGameManager manager,
        string userDataDirectory,
        ISaveGameWatchdog? watchdog,
        Action<Action>? dispatchToUi)
        : this(manager, userDataDirectory, watchdog, dispatchToUi, mutationGate: null, gameRunningProbe: null)
    {
    }

    internal SaveManagerViewModel(
        ISaveGameManager manager,
        string userDataDirectory,
        ISaveGameWatchdog? watchdog,
        Action<Action>? dispatchToUi,
        UiMutationGate? mutationGate,
        string storeName = "Steam",
        Func<bool>? gameRunningProbe = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        _userDataDirectory = userDataDirectory;
        _watchdog = watchdog;
        _mutationGate = mutationGate;
        _storeName = string.IsNullOrWhiteSpace(storeName) ? "Game" : storeName;
        _isGameRunningProbe = gameRunningProbe ?? GameProcessProbe.IsAncestorsRunning;
        _dispatchToUi = dispatchToUi ?? (action => Dispatcher.UIThread.Post(action));
        _gameProcessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _gameProcessTimer.Tick += OnGameProcessTimerTick;
        if (_mutationGate is not null)
        {
            _mutationGate.Changed += OnMutationGateChanged;
        }
        if (_watchdog is not null)
        {
            _watchdog.CheckpointCreated += OnWatchdogCheckpointCreated;
            _watchdog.WatcherError += OnWatcherError;
        }

        LoadSettings();
    }

    public bool HasSlots => Slots.Any(slot => slot.HasSave);

    public string[] CheckpointOriginFilters { get; } = ["All", "Manual", "AutoBackup", "PreRestore"];

    public bool HasNoSlots => !HasSlots;

    public string BackupHealthSummary { get; private set; } = "No checkpoint health data loaded yet.";

    public bool HasBackupHealthWarning { get; private set; }

    public string BackupHealthAccent { get; private set; } = "#7A877A";

    public IBrush BackupHealthBrush => StatusPresentation.BrushForLegacyAccent(BackupHealthAccent);

    public string? LastRecoveryMessage { get; private set; }

    public bool CanCreate => CanMutate;

    public bool CanMutate => !IsBusy && !(_mutationGate?.IsBusy ?? false);

    public string? ToolSettingsWarning
    {
        get => _toolSettingsWarning;
        private set
        {
            if (SetProperty(ref _toolSettingsWarning, value))
            {
                OnPropertyChanged(nameof(HasToolSettingsWarning));
                OnPropertyChanged(nameof(CanConfigureAutoBackup));
                OnPropertyChanged(nameof(CanResetToolSettings));
            }
        }
    }

    public bool HasToolSettingsWarning => !string.IsNullOrWhiteSpace(ToolSettingsWarning);

    public bool CanConfigureAutoBackup => CanMutate && !HasToolSettingsWarning;

    public bool CanResetToolSettings => CanMutate && HasToolSettingsWarning;

    /// <summary>Starts persisted watchdog settings only after the owner has loaded slots.</summary>
    public void Activate()
    {
        RefreshGameRunningState();
        _gameProcessTimer.Start();
        Volatile.Write(ref _watchdogLifecycleActivated, 1);
        Volatile.Write(ref _watchdogDesiredEnabled, IsWatchdogEnabled ? 1 : 0);
        if (IsWatchdogEnabled)
        {
            QueueWatchdogReconciliation();
        }
    }

    public void Refresh(SaveGamesSnapshot snapshot)
    {
        LastRecoveryMessage = snapshot.RecoveryMessage;
        var expandedSlots = Slots
            .Where(slot => slot.IsShowingAllCheckpoints)
            .Select(slot => slot.SlotNumber)
            .ToHashSet(StringComparer.Ordinal);
        var existingCheckpointsBySlot = Slots.ToDictionary(
            slot => slot.SlotNumber,
            slot => (IReadOnlyDictionary<string, SaveGameCheckpointViewModel>)slot.Checkpoints
                .ToDictionary(checkpoint => checkpoint.Id, StringComparer.Ordinal),
            StringComparer.Ordinal);

        Slots = snapshot.Slots
            .Select(slot => new SaveGameSlotViewModel(
                slot,
                () => RunCreate(slot.SlotNumber),
                checkpoint => () => RunLoad(slot.SlotNumber, checkpoint.Id),
                checkpoint => () => RunDelete(slot.SlotNumber, checkpoint.Id),
                () => !IsGameRunning,
                () => CanMutate,
                expandedSlots.Contains(slot.SlotNumber),
                metadataProvider: checkpoint => GetCheckpointMetadata(checkpoint),
                metadataChanged: SaveCheckpointMetadata,
                existingCheckpoints: existingCheckpointsBySlot.GetValueOrDefault(slot.SlotNumber)))
            .ToArray();

        RemoveMetadataForMissingCheckpoints(snapshot);
        ApplyCheckpointFilter();


        StatusMessage = snapshot.RecoveryMessage ??
            (HasSlots ? "Save games loaded successfully." : "No save games loaded yet.");
        StatusAccent = snapshot.RecoveryMessage is not null || HasSlots ? "#B4D941" : "#7A877A";

        int readableSlots = snapshot.Slots.Count(slot => slot.Exists && slot.ErrorMessage is null);
        int checkpointCount = snapshot.Slots.Sum(slot => slot.Checkpoints.Count);
        int unreadableSlots = snapshot.Slots.Count(slot => slot.ErrorMessage is not null);
        HasBackupHealthWarning = unreadableSlots > 0;
        BackupHealthAccent = unreadableSlots > 0
            ? "#E04D42"
            : checkpointCount > 0
                ? "#B4D941"
                : "#7A877A";
        BackupHealthSummary = HasBackupHealthWarning
            ? $"{readableSlots} readable save slot(s) · {checkpointCount} checkpoint(s) · {unreadableSlots} slot(s) need attention"
            : $"{readableSlots} readable save slot(s) · {checkpointCount} checkpoint(s) found during the latest scan";
        OnPropertyChanged(nameof(BackupHealthSummary));
        OnPropertyChanged(nameof(HasBackupHealthWarning));
        OnPropertyChanged(nameof(BackupHealthAccent));
        OnPropertyChanged(nameof(BackupHealthBrush));

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

    partial void OnStatusAccentChanged(string value)
    {
        OnPropertyChanged(nameof(StatusBrush));
    }

    public void RefreshThemeBindings()
    {
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(BackupHealthBrush));
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
            LastRecoveryMessage = null;
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
            StatusMessage = snapshot.RecoveryMessage ?? "Save games reloaded.";
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
        using IDisposable? mutation = _mutationGate?.TryEnter();
        if (_mutationGate is not null && mutation is null)
        {
            return new SaveGameOperationResult(false, "Another operation is already running.");
        }
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

            if (result.Succeeded || result.CommitState != SaveOperationCommitState.NotCommitted)
            {
                try
                {
                    // Keep the UI busy until it reflects the new disk state.
                    SaveGamesSnapshot snapshot = await Task.Run(_manager.Inspect);
                    Refresh(snapshot);
                    StatusMessage = result.Message;
                    StatusAccent = !result.Succeeded ||
                                   result.CommitState == SaveOperationCommitState.CommittedWithWarning
                        ? "#D6BC84"
                        : "#B4D941";
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
        if (!CanMutate)
        {
            return;
        }

        await RunOperation(() => _manager.CreateCheckpoint(slotNumber));
    }

    public async Task<SaveGameOperationResult> RunLoad(string slotNumber, string checkpointId)
    {
        if (!CanMutate)
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
        if (!CanMutate)
        {
            return;
        }

        await RunOperation(() => _manager.DeleteCheckpoint(slotNumber, checkpointId));
    }

    public void Dispose()
    {
        // Persist the in-memory metadata before the disposed guard closes the
        // queue, otherwise a title/note typed just before exit is lost.
        _metadataWriteDebounce?.Cancel();
        _metadataWriteDebounce?.Dispose();
        _metadataWriteDebounce = null;
        SaveSettings(waitForCompletion: true);

        Volatile.Write(ref _watchdogLifecycleActivated, 0);
        Volatile.Write(ref _watchdogDesiredEnabled, 0);
        QueueWatchdogReconciliation();

        Task settingsWrite;
        Task watchdogLifecycle;
        lock (_settingsWriteGate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            settingsWrite = _settingsWriteTail;
        }
        lock (_watchdogLifecycleGate)
        {
            watchdogLifecycle = _watchdogLifecycleTail;
        }
        GC.SuppressFinalize(this);
        _gameProcessTimer.Stop();
        _gameProcessTimer.Tick -= OnGameProcessTimerTick;
        if (_watchdog is not null)
        {
            _watchdog.CheckpointCreated -= OnWatchdogCheckpointCreated;
            _watchdog.WatcherError -= OnWatcherError;
        }
        if (_mutationGate is not null)
        {
            _mutationGate.Changed -= OnMutationGateChanged;
        }
        _lifetimeCancellation.Cancel();
        Task allPending;
        try
        {
            Task[] pending = new Task?[] { settingsWrite, _watchdogRefreshTask, watchdogLifecycle }
                .OfType<Task>()
                .ToArray();
            allPending = Task.WhenAll(pending);
            _ = allPending.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            allPending = Task.CompletedTask;
        }

        if (allPending.IsCompleted)
        {
            _lifetimeCancellation.Dispose();
        }
        else
        {
            _ = allPending.ContinueWith(
                _ =>
                {
                    _lifetimeCancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void OnGameProcessTimerTick(object? sender, EventArgs eventArgs) => QueueGameRunningStateRefresh();

    internal void QueueGameRunningStateRefresh()
    {
        if (Interlocked.CompareExchange(ref _gameProcessRefreshInProgress, 1, 0) != 0)
        {
            return;
        }

        _ = RefreshGameRunningStateAsync();
    }

    private async Task RefreshGameRunningStateAsync()
    {
        try
        {
            bool running = await Task.Run(() =>
            {
                try { return _isGameRunningProbe(); }
                catch (Exception) { return true; }
            });
            IsGameRunning = running;
        }
        finally
        {
            Volatile.Write(ref _gameProcessRefreshInProgress, 0);
        }
    }

    private void RefreshGameRunningState()
    {
        try
        {
            IsGameRunning = _isGameRunningProbe();
        }
        catch (Exception)
        {
            // A failed process query must never make Restore look safer than it is.
            IsGameRunning = true;
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
        if (_loadingSettings)
        {
            // Loading persisted settings establishes desired state only. Activate
            // performs the first actual transition after save slots are ready.
            return;
        }

        Volatile.Write(ref _watchdogDesiredEnabled, value ? 1 : 0);
        QueueWatchdogReconciliation();
        SaveSettings();
    }

    /// <summary>
    /// Serializes all watchdog Start/Stop transitions off the UI thread. A stale
    /// stop can therefore never run after a newer request to enable the watcher.
    /// </summary>
    private void QueueWatchdogReconciliation()
    {
        if (_watchdog is null)
        {
            return;
        }

        int version = Interlocked.Increment(ref _watchdogLifecycleVersion);
        lock (_watchdogLifecycleGate)
        {
            _watchdogLifecycleTail = _watchdogLifecycleTail.ContinueWith(
                _ => Task.Run(() => ReconcileWatchdogState(version), CancellationToken.None),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
        }
    }

    private void ReconcileWatchdogState(int observedVersion)
    {
        while (true)
        {
            bool shouldRun = !_disposed &&
                Volatile.Read(ref _watchdogLifecycleActivated) == 1 &&
                Volatile.Read(ref _watchdogDesiredEnabled) == 1;
            if (shouldRun)
            {
                _watchdog!.Start();
            }
            else
            {
                _watchdog!.StopWatch();
            }

            int currentVersion = Volatile.Read(ref _watchdogLifecycleVersion);
            if (currentVersion == observedVersion)
            {
                return;
            }

            observedVersion = currentVersion;
        }
    }

    partial void OnKeepRunningInTrayWhenClosingChanged(bool value)
    {
        if (!_loadingSettings)
        {
            SaveSettings();
        }
    }

    partial void OnCheckpointSearchTextChanged(string value) => ApplyCheckpointFilter();

    partial void OnCheckpointOriginFilterChanged(string value) => ApplyCheckpointFilter();

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
            FileInfo fileInfo = new(path);
            if (fileInfo.Length > 1024 * 1024)
            {
                ToolSettingsWarning =
                    "Auto-backup settings file exceeds the maximum allowed size (1 MiB). Safe defaults are active.";
                return;
            }

            ToolSettings? settings = System.Text.Json.JsonSerializer.Deserialize<ToolSettings>(
                File.ReadAllText(path),
                JsonSettings);
            if (settings is null)
            {
                ToolSettingsWarning =
                    "Auto-backup settings contain no usable data. Safe defaults are active and the existing settings file was preserved.";
                return;
            }

            _loadingSettings = true;
            try
            {
                CooldownMinutes = settings.WatchdogIntervalMinutes;
                IsWatchdogEnabled = settings.IsWatchdogEnabled;
                KeepRunningInTrayWhenClosing = settings.KeepRunningInTrayWhenClosing;
                _checkpointMetadata = (settings.CheckpointMetadata ?? new Dictionary<string, CheckpointMetadata>())
                    .Where(pair => IsValidMetadataKey(pair.Key) && IsValidMetadata(pair.Value))
                    .ToDictionary(pair => pair.Key, pair => NormalizeMetadata(pair.Value), StringComparer.Ordinal);
            }
            finally
            {
                _loadingSettings = false;
            }
        }
        catch (Exception exception) when (
            exception is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            ToolSettingsWarning =
                "Auto-backup settings could not be read. Safe defaults are active and the existing settings file was preserved. " +
                exception.Message;
        }
    }

    [RelayCommand]
    private void ResetToolSettings()
    {
        if (!CanResetToolSettings)
        {
            return;
        }

        using IDisposable? mutation = _mutationGate?.TryEnter();
        if (_mutationGate is not null && mutation is null)
        {
            return;
        }

        IsBusy = true;
        NotifyState();
        try
        {
            string path = ToolSettingsPath();
            string archivedName = Path.GetFileName(path) +
                $".invalid-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.bak";
            string archivedPath = Path.Combine(Path.GetDirectoryName(path)!, archivedName);
            File.Move(path, archivedPath);

            _loadingSettings = true;
            try
            {
                IsWatchdogEnabled = false;
                CooldownMinutes = 5;
                KeepRunningInTrayWhenClosing = true;
            }
            finally
            {
                _loadingSettings = false;
            }

            ToolSettingsWarning = null;
            SaveSettings();
            StatusMessage = $"Auto-backup settings were reset. The unreadable file was kept as {archivedName}.";
            StatusAccent = "#B4D941";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StatusMessage = "Auto-backup settings could not be reset: " + exception.Message;
            StatusAccent = "#E04D42";
        }
        finally
        {
            IsBusy = false;
            NotifyState();
        }
    }

    private void SaveSettings(bool waitForCompletion = false)
    {
        var settings = new ToolSettings
        {
            IsWatchdogEnabled = IsWatchdogEnabled,
            WatchdogIntervalMinutes = _cooldownMinutes,
            KeepRunningInTrayWhenClosing = KeepRunningInTrayWhenClosing,
            CheckpointMetadata = new Dictionary<string, CheckpointMetadata>(_checkpointMetadata, StringComparer.Ordinal),
        };

        string? directory = Path.GetDirectoryName(ToolSettingsPath());
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        string path = ToolSettingsPath();
        int version = Interlocked.Increment(ref _settingsVersion);
        Task pending;
        lock (_settingsWriteGate)
        {
            if (_disposed)
            {
                return;
            }

            pending = _settingsWriteTail = _settingsWriteTail.ContinueWith(
                _ => WriteSettings(path, settings, version),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }

        // Favorite changes protect retention immediately. Text metadata is
        // queued after a short quiet period to keep typing off the UI thread.
        if (waitForCompletion)
        {
            pending.GetAwaiter().GetResult();
        }
    }

    private void WriteSettings(string path, ToolSettings settings, int version)
    {
        try
        {
            // A delayed older item must never overwrite a newer snapshot. The tail
            // task represents the complete queue, so Dispose always waits for every
            // item rather than only the most recently started task.
            if (version != Volatile.Read(ref _settingsVersion))
            {
                return;
            }

            byte[] payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(settings, JsonSettings);
            string temp = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllBytes(temp, payload);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ReportStatus("Could not save tool settings: " + exception.Message, "#E04D42");
        }
    }

    private void ReportStatus(string message, string accent)
    {
        if (_disposed)
        {
            return;
        }
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
        NotifyMutationAvailability();
    }

    private void OnMutationGateChanged(object? sender, EventArgs e) =>
        NotifyMutationAvailability();

    private void NotifyMutationAvailability()
    {
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanMutate));
        OnPropertyChanged(nameof(CanConfigureAutoBackup));
        OnPropertyChanged(nameof(CanResetToolSettings));
        foreach (SaveGameSlotViewModel slot in Slots)
        {
            slot.RefreshMutationAvailability();
        }
    }

    private void OnWatchdogCheckpointCreated(object? sender, string slotNumber)
    {
        // Watchdog events arrive on a worker thread; UI state changes run on the UI thread.
        void Refresh()
        {
            Interlocked.Increment(ref _watchdogRefreshVersion);
            if (_watchdogRefreshTask is null || _watchdogRefreshTask.IsCompleted)
            {
                _watchdogRefreshTask = RefreshAfterWatchdogAsync(_lifetimeCancellation.Token);
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

    private async Task RefreshAfterWatchdogAsync(CancellationToken token)
    {
        try
        {
            int observed;
            do
            {
                observed = Volatile.Read(ref _watchdogRefreshVersion);
                SaveGamesSnapshot snapshot = await Task.Run(_manager.Inspect, token);
                token.ThrowIfCancellationRequested();
                Refresh(snapshot);
            }
            while (observed != Volatile.Read(ref _watchdogRefreshVersion));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
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
        OnPropertyChanged(nameof(CanMutate));
        OnPropertyChanged(nameof(CanConfigureAutoBackup));
        OnPropertyChanged(nameof(CanResetToolSettings));
    }

    private CheckpointMetadata? GetCheckpointMetadata(SaveGameCheckpoint checkpoint) =>
        _checkpointMetadata.GetValueOrDefault(CheckpointMetadataKey(checkpoint));

    private void SaveCheckpointMetadata(SaveGameCheckpoint checkpoint, CheckpointMetadata metadata)
    {
        string key = CheckpointMetadataKey(checkpoint);
        CheckpointMetadata normalized = NormalizeMetadata(metadata);
        bool favoriteChanged = (_checkpointMetadata.GetValueOrDefault(key)?.IsFavorite ?? false) != normalized.IsFavorite;
        if (string.IsNullOrWhiteSpace(normalized.Title) && string.IsNullOrWhiteSpace(normalized.Note) && !normalized.IsFavorite)
        {
            _checkpointMetadata.Remove(key);
        }
        else
        {
            _checkpointMetadata[key] = normalized;
        }
        if (favoriteChanged)
        {
            _metadataWriteDebounce?.Cancel();
            SaveSettings(waitForCompletion: true);
            return;
        }

        QueueDebouncedMetadataSave();
    }

    private void QueueDebouncedMetadataSave()
    {
        _metadataWriteDebounce?.Cancel();
        _metadataWriteDebounce?.Dispose();
        var cancellation = new CancellationTokenSource();
        _metadataWriteDebounce = cancellation;
        _ = SaveMetadataAfterQuietPeriodAsync(cancellation.Token);
    }

    private async Task SaveMetadataAfterQuietPeriodAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), token);
            if (!token.IsCancellationRequested)
            {
                SaveSettings();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private void RemoveMetadataForMissingCheckpoints(SaveGamesSnapshot snapshot)
    {
        var readableSlots = snapshot.Slots.Where(slot => slot.ErrorMessage is null).ToArray();
        var readableSlotPrefixes = readableSlots
            .Select(slot => slot.SlotNumber + ":")
            .ToArray();

        var active = readableSlots
            .SelectMany(slot => slot.Checkpoints)
            .Select(CheckpointMetadataKey)
            .ToHashSet(StringComparer.Ordinal);

        string[] staleKeys = _checkpointMetadata.Keys
            .Where(key => readableSlotPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)) && !active.Contains(key))
            .ToArray();

        foreach (string key in staleKeys)
        {
            _checkpointMetadata.Remove(key);
        }
        if (staleKeys.Length > 0)
        {
            SaveSettings();
        }
    }

    private void ApplyCheckpointFilter()
    {
        foreach (SaveGameSlotViewModel slot in Slots)
        {
            slot.SetCheckpointFilter(CheckpointSearchText, CheckpointOriginFilter);
        }
    }

    private static string CheckpointMetadataKey(SaveGameCheckpoint checkpoint) =>
        checkpoint.SlotNumber + ":" + checkpoint.Id;

    private static bool IsValidMetadataKey(string key) =>
        key.Length is > 2 and <= 256 && !key.Any(char.IsControl);

    private static bool IsValidMetadata(CheckpointMetadata? metadata) =>
        metadata is not null && (metadata.Title?.Length ?? 0) <= 80 && (metadata.Note?.Length ?? 0) <= 400;

    private static CheckpointMetadata NormalizeMetadata(CheckpointMetadata metadata) => new(
        string.IsNullOrWhiteSpace(metadata.Title) ? null : metadata.Title.Trim(),
        string.IsNullOrWhiteSpace(metadata.Note) ? null : metadata.Note.Trim(),
        metadata.IsFavorite);

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException;
}
