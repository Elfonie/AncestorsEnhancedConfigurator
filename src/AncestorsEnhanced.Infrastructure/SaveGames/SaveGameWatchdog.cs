using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

/// <summary>
/// Watches the savegames directory and creates checkpoints when a slot save changes.
/// Guarantees at most one queued backup per slot and waits for in-flight backup tasks
/// when stopped/disposed, so stopping never races a running create (F002). Changes that
/// arrive while a backup is already queued or in its cooldown are marked dirty and
/// backed up once afterwards instead of being discarded (I-1).
/// </summary>
public sealed class SaveGameWatchdog : ISaveGameWatchdog, IDisposable
{
    private readonly Func<int, SaveGameOperationResult> _createCheckpoint;
    private readonly string _userDataDirectory;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, Task> _running = new();
    private readonly Dictionary<int, bool> _pending = new();
    private readonly Dictionary<int, DateTimeOffset> _lastBackupTimes = new();
    private readonly Dictionary<int, int> _retryAttempts = new();
    private readonly Dictionary<int, int> _activeMutations = new();
    private TimeSpan _cooldown = TimeSpan.FromMinutes(5);
    private CancellationTokenSource _stopCancellation = new();
    private bool _stopped;
    private FileSystemWatcher? _watcher;

    /// <summary>Binds to a verified game context; the user-data path comes from the context (F078).</summary>
    public SaveGameWatchdog(VerifiedGameContext context, GameContextVerifier verifier)
        : this(context.UserDataDirectory, () => verifier.Verify(context))
    {
    }

    public SaveGameWatchdog(string userDataDirectory, Func<bool>? revalidate = null)
    {
        _userDataDirectory = userDataDirectory;
        var manager = new SafeSaveGameManager(userDataDirectory, null, revalidate);
        _createCheckpoint = slot => manager.CreateCheckpoint(
            slot.ToString(CultureInfo.InvariantCulture), "AutoBackup");
    }

    internal SaveGameWatchdog(string userDataDirectory, Func<int, SaveGameOperationResult> createCheckpoint)
    {
        _userDataDirectory = userDataDirectory;
        _createCheckpoint = createCheckpoint ?? throw new ArgumentNullException(nameof(createCheckpoint));
    }

    public event EventHandler<string>? CheckpointCreated;

    public event EventHandler<string>? WatcherError;

    public bool IsRunning => _watcher is not null;

    public TimeSpan Cooldown
    {
        get
        {
            lock (_gate)
            {
                return _cooldown;
            }
        }
        set
        {
            lock (_gate)
            {
                _cooldown = value;
            }
        }
    }

    public void Start()
    {
        SaveGameGuard.ValidateUserData(_userDataDirectory);
        lock (_gate)
        {
            if (_watcher is not null)
            {
                return;
            }

            _stopped = false;
            if (_stopCancellation.IsCancellationRequested)
            {
                _stopCancellation.Dispose();
                _stopCancellation = new CancellationTokenSource();
            }
            string saveDirectory = SaveGamePaths.GetSaveGamesDirectory(_userDataDirectory);
            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            var watcher = new FileSystemWatcher(saveDirectory)
            {
                Filter = "Savegame*.sav",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                             NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            // Register handlers BEFORE enabling events so no change event can be lost
            // between construction and the first raise.
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
    }

    public void StopWatch()
    {
        Dictionary<int, Task> snapshot;
        lock (_gate)
        {
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnChanged;
                _watcher.Created -= OnChanged;
                _watcher.Renamed -= OnRenamed;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;
            }

            _stopped = true;
            _stopCancellation.Cancel();
            _pending.Clear();
            _lastBackupTimes.Clear();
            _retryAttempts.Clear();
            _activeMutations.Clear();
            snapshot = new Dictionary<int, Task>(_running);
        }

        // Wait for in-flight backup tasks so a stop never races a running create (F002).
        Task[] tasks = snapshot.Values.ToArray();
        if (tasks.Length > 0)
        {
            // Wait completely for in-flight backup tasks so a stop never races a
            // running create (F002). The tasks exit quickly once _stopped is set.
            try
            {
                Task.WaitAll(tasks);
            }
            catch (AggregateException)
            {
                // The backing tasks swallow expected exceptions themselves; a leftover
                // exception on an unobserved task is harmless here.
            }
        }

        lock (_gate)
        {
            _running.Clear();
        }
    }

    public IDisposable BeginSlotMutation(int slotNumber)
    {
        lock (_gate)
        {
            _activeMutations[slotNumber] = _activeMutations.TryGetValue(slotNumber, out int count) ? count + 1 : 1;
        }

        return new SlotMutationLease(this, slotNumber);
    }

    /// <summary>
    /// Blocks until no backup task is currently running (used by tests and restore
    /// flows that need a quiescent watchdog). Never throws for task failures.
    /// </summary>
    public void WaitForIdle()
    {
        while (true)
        {
            Task? task;
            lock (_gate)
            {
                task = _running.Count == 0 ? null : _running.Values.First();
            }

            if (task is null)
            {
                return;
            }

            try
            {
                task.Wait(TimeSpan.FromSeconds(10));
            }
            catch (AggregateException)
            {
            }
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        // A rename into the watched pattern behaves like a fresh file the same way
        // as a create would.
        OnChanged(sender, new FileSystemEventArgs(
            WatcherChangeTypes.Renamed,
            args.FullPath,
            args.Name ?? string.Empty));
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        string message = args.GetException()?.Message ?? "Unknown filesystem watcher error";
        WatcherError?.Invoke(this, message);
        RestartWatcher();
    }

    private void RestartWatcher()
    {
        lock (_gate)
        {
            if (_stopped || _watcher is null)
            {
                return;
            }

            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        // FileSystemWatcher can lose an arbitrary number of notifications during an
        // overflow. Reconcile the finite, canonical slot set after it is restarted.
        for (int slot = 0; slot < SaveGamePaths.SlotCount; slot++)
        {
            if (File.Exists(SaveGamePaths.GetSlotPath(_userDataDirectory, slot)))
            {
                MarkDirty(slot);
            }
        }
    }

    public void Dispose()
    {
        StopWatch();
        _stopCancellation.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        if (!TryGetSlotNumber(args.Name, out int slot))
        {
            return;
        }

        MarkDirty(slot);
    }

    private void MarkDirty(int slot)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _pending[slot] = true;
            EnsureWorkerLocked(slot);
        }
    }

    private async Task BackupSlotAsync(int slot)
    {
        try
        {
            await Task.Delay(500, _stopCancellation.Token).ConfigureAwait(false);

            while (true)
            {
                TimeSpan? wait = null;
                lock (_gate)
                {
                    if (_stopped)
                    {
                        return;
                    }

                    if (!_pending.TryGetValue(slot, out bool dirty) || !dirty)
                    {
                        return;
                    }

                    if (_activeMutations.ContainsKey(slot))
                    {
                        wait = TimeSpan.FromMilliseconds(100);
                    }
                    else if (_lastBackupTimes.TryGetValue(slot, out DateTimeOffset last) &&
                             DateTimeOffset.UtcNow < last + _cooldown)
                    {
                        wait = last + _cooldown - DateTimeOffset.UtcNow;
                    }
                    else
                    {
                        // Consume dirty immediately before the mutation. Events racing
                        // the backup set it again and this worker performs another pass.
                        _pending[slot] = false;
                    }
                }

                if (wait is not null)
                {
                    await Task.Delay(wait.Value, _stopCancellation.Token).ConfigureAwait(false);
                    continue;
                }

                SaveGameOperationResult result = _createCheckpoint(slot);
                if (result.Succeeded)
                {
                    lock (_gate)
                    {
                        // Retry budget is per contiguous failure episode, not a
                        // lifetime counter for this save slot.
                        _retryAttempts.Remove(slot);
                    }

                    if (result.CreatedCheckpointId is not null)
                    {
                        RecordBackupTime(slot);
                        CheckpointCreated?.Invoke(this, slot.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else if (!result.Succeeded)
                {
                    TimeSpan? retryDelay = RegisterFailure(slot, result.Message);
                    if (retryDelay is not null)
                    {
                        await Task.Delay(retryDelay.Value, _stopCancellation.Token).ConfigureAwait(false);
                        continue;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stopCancellation.IsCancellationRequested)
        {
            // StopWatch deliberately interrupts debounce/cooldown waits.
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException)
        {
            WatcherError?.Invoke(this, $"Auto-backup failed for slot {slot + 1}: {exception.Message}");
        }
        catch (Exception exception)
        {
            WatcherError?.Invoke(this, $"Auto-backup failed for slot {slot + 1}: {exception.Message}");
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(slot);
                // The final empty check and worker removal must be one state
                // transition. An event that arrives during worker shutdown either sees
                // this worker or causes its replacement, never a stranded dirty slot.
                if (!_stopped && _pending.TryGetValue(slot, out bool dirty) && dirty)
                {
                    EnsureWorkerLocked(slot);
                }
            }
        }
    }

    private TimeSpan? RegisterFailure(int slot, string message)
    {
        lock (_gate)
        {
            if (!IsTransientBackupFailure(message))
            {
                _retryAttempts.Remove(slot);
                WatcherError?.Invoke(this, $"Auto-backup failed for slot {slot + 1}: {message}");
                return null;
            }

            int attempt = _retryAttempts.TryGetValue(slot, out int previous) ? previous + 1 : 1;
            _retryAttempts[slot] = attempt;
            if (attempt > 3)
            {
                _retryAttempts.Remove(slot);
                WatcherError?.Invoke(this, $"Auto-backup failed for slot {slot + 1} after {attempt - 1} retries: {message}");
                return null;
            }

            _pending[slot] = true;
            return TimeSpan.FromMilliseconds(250 * attempt);
        }
    }

    private static bool IsTransientBackupFailure(string message) =>
        message.Contains("being written", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("currently being written", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("corrupt; skipped backup", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("could not be read as a stable version", StringComparison.OrdinalIgnoreCase);

    private void EndSlotMutation(int slot)
    {
        lock (_gate)
        {
            if (!_activeMutations.TryGetValue(slot, out int count))
            {
                return;
            }

            if (count == 1)
            {
                _activeMutations.Remove(slot);
                // Reconcile after restore even if a watcher event was missed.
                _pending[slot] = true;
                EnsureWorkerLocked(slot);
                return;
            }

            _activeMutations[slot] = count - 1;
        }
    }

    private void EnsureWorkerLocked(int slot)
    {
        if (!_running.ContainsKey(slot))
        {
            _running[slot] = BackupSlotAsync(slot);
        }
    }

    private void RecordBackupTime(int slot)
    {
        lock (_gate)
        {
            _lastBackupTimes[slot] = DateTimeOffset.UtcNow;
        }
    }

    private static bool TryGetSlotNumber(string? fileName, out int slotNumber)
    {
        slotNumber = 0;
        if (fileName is null)
        {
            return false;
        }

        for (int slot = 0; slot < SaveGamePaths.SlotCount; slot++)
        {
            if (string.Equals(fileName, SaveGamePaths.GetSlotFileName(slot), StringComparison.Ordinal))
            {
                slotNumber = slot;
                return true;
            }
        }

        return false;
    }

    private sealed class SlotMutationLease(SaveGameWatchdog owner, int slot) : IDisposable
    {
        private SaveGameWatchdog? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndSlotMutation(slot);
    }
}
