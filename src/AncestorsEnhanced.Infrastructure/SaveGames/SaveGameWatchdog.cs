using System.Globalization;
using System.Diagnostics;
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
    private readonly object _lifecycleGate = new();
    private readonly Dictionary<int, WorkerState> _running = new();
    private readonly Dictionary<int, bool> _pending = new();
    private readonly Dictionary<int, long> _lastBackupTicks = new();
    private readonly Dictionary<int, int> _retryAttempts = new();
    private readonly Dictionary<int, int> _activeMutations = new();
    private TimeSpan _cooldown = TimeSpan.FromMinutes(5);
    private CancellationTokenSource _stopCancellation = new();
    private bool _stopped;
    private bool _disposed;
    private long _generation;
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

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _watcher is not null;
            }
        }
    }

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
            if (value < TimeSpan.Zero || value > TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Cooldown must be between zero and 24 hours.");
            }
            lock (_gate)
            {
                _cooldown = value;
            }
        }
    }

    public void Start()
    {
        lock (_lifecycleGate)
        {
            SaveGameGuard.ValidateUserData(_userDataDirectory);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_watcher is not null)
                {
                    return;
                }
                if (_running.Values.Any(worker => !worker.Task.IsCompleted))
                {
                    throw new InvalidOperationException("The save watcher is still stopping.");
                }
                _running.Clear();

                _stopCancellation.Dispose();
                _stopCancellation = new CancellationTokenSource();
                _generation++;
                _stopped = false;
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
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
        }
    }

    public void StopWatch()
    {
        lock (_lifecycleGate)
        {
            WorkerState[] snapshot;
            long stoppingGeneration;
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
                stoppingGeneration = _generation;
                _stopCancellation.Cancel();
                _pending.Clear();
                _lastBackupTicks.Clear();
                _retryAttempts.Clear();
                _activeMutations.Clear();
                snapshot = _running.Values
                    .Where(worker => worker.Generation == stoppingGeneration)
                    .ToArray();
            }

            Task[] tasks = snapshot.Select(worker => worker.Task).ToArray();
            if (tasks.Length > 0)
            {
                try
                {
                    _ = Task.WaitAll(tasks, TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                }
            }

            lock (_gate)
            {
                foreach ((int slot, WorkerState worker) in _running.ToArray())
                {
                    if (worker.Generation == stoppingGeneration && worker.Task.IsCompleted)
                    {
                        _running.Remove(slot);
                    }
                }
            }
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
                task = _running.Count == 0 ? null : _running.Values.First().Task;
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
        PublishWatcherError(message);
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
                _watcher.Dispose();
                _watcher = null;
                PublishWatcherError($"The save watcher could not be restarted: {exception.Message}");
                return;
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
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }
            StopWatch();
            _disposed = true;
            _stopCancellation.Dispose();
        }
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

    private async Task BackupSlotAsync(int slot, long generation, CancellationToken stopToken)
    {
        try
        {
            await Task.Delay(500, stopToken).ConfigureAwait(false);

            while (true)
            {
                TimeSpan? wait = null;
                lock (_gate)
                {
                    if (_stopped || generation != _generation)
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
                    else if (_lastBackupTicks.TryGetValue(slot, out long last))
                    {
                        TimeSpan elapsed = Stopwatch.GetElapsedTime(last);
                        if (elapsed < _cooldown)
                        {
                            wait = _cooldown - elapsed;
                        }
                        else
                        {
                            _pending[slot] = false;
                        }
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
                    await Task.Delay(wait.Value, stopToken).ConfigureAwait(false);
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
                        if (RecordBackupTime(slot, generation))
                        {
                            PublishCheckpointCreated(slot.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }
                else if (!result.Succeeded)
                {
                    TimeSpan? retryDelay = RegisterFailure(slot, result);
                    if (retryDelay is not null)
                    {
                        await Task.Delay(retryDelay.Value, stopToken).ConfigureAwait(false);
                        continue;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // StopWatch deliberately interrupts debounce/cooldown waits.
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException)
        {
            PublishWatcherError($"Auto-backup failed for slot {slot + 1}: {exception.Message}");
        }
        catch (Exception exception)
        {
            PublishWatcherError($"Auto-backup failed for slot {slot + 1}: {exception.Message}");
        }
        finally
        {
            lock (_gate)
            {
                if (_running.TryGetValue(slot, out WorkerState? worker) && worker.Generation == generation)
                {
                    _running.Remove(slot);
                }
                // The final empty check and worker removal must be one state
                // transition. An event that arrives during worker shutdown either sees
                // this worker or causes its replacement, never a stranded dirty slot.
                if (!_stopped && generation == _generation && _pending.TryGetValue(slot, out bool dirty) && dirty)
                {
                    EnsureWorkerLocked(slot);
                }
            }
        }
    }

    private TimeSpan? RegisterFailure(int slot, SaveGameOperationResult result)
    {
        lock (_gate)
        {
            if (!result.IsTransientFailure)
            {
                _retryAttempts.Remove(slot);
                PublishWatcherError($"Auto-backup failed for slot {slot + 1}: {result.Message}");
                return null;
            }

            int attempt = _retryAttempts.TryGetValue(slot, out int previous) ? previous + 1 : 1;
            _retryAttempts[slot] = attempt;
            if (attempt > 3)
            {
                _retryAttempts.Remove(slot);
                PublishWatcherError($"Auto-backup failed for slot {slot + 1} after {attempt - 1} retries: {result.Message}");
                return null;
            }

            _pending[slot] = true;
            return TimeSpan.FromMilliseconds(250 * attempt);
        }
    }

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
            long generation = _generation;
            Task task = BackupSlotAsync(slot, generation, _stopCancellation.Token);
            _running[slot] = new WorkerState(generation, task);
        }
    }

    private bool RecordBackupTime(int slot, long generation)
    {
        lock (_gate)
        {
            if (_stopped || generation != _generation)
            {
                return false;
            }

            _lastBackupTicks[slot] = Stopwatch.GetTimestamp();
            return true;
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

    private void PublishCheckpointCreated(string slot)
    {
        foreach (EventHandler<string> handler in CheckpointCreated?.GetInvocationList().Cast<EventHandler<string>>() ?? [])
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { handler(this, slot); } catch { }
            });
        }
    }

    private void PublishWatcherError(string message)
    {
        foreach (EventHandler<string> handler in WatcherError?.GetInvocationList().Cast<EventHandler<string>>() ?? [])
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { handler(this, message); } catch { }
            });
        }
    }

    private sealed record WorkerState(long Generation, Task Task);
}
