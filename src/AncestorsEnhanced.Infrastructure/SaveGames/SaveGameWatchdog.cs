using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

/// <summary>
/// Watches the savegames directory and creates checkpoints when a slot save changes.
/// Guarantees at most one queued backup per slot and waits for in-flight backup tasks
/// when stopped/disposed, so stopping never races a running create (F002). Changes that
/// arrive while a backup is already queued or in its cooldown are marked dirty and
/// backed up once afterwards instead of being discarded (I-1).
/// </summary>
public sealed class SaveGameWatchdog : ISaveGameWatchdog
{
    private readonly SafeSaveGameManager _manager;
    private readonly string _userDataDirectory;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, Task> _running = new();
    private readonly Dictionary<int, bool> _pending = new();
    private readonly Dictionary<int, DateTimeOffset> _lastBackupTimes = new();
    private readonly Dictionary<int, DateTimeOffset> _suppressedUntil = new();
    private TimeSpan _cooldown = TimeSpan.FromMinutes(5);
    private bool _stopped;
    private FileSystemWatcher? _watcher;

    public SaveGameWatchdog(string userDataDirectory, Func<bool>? revalidate = null)
    {
        _userDataDirectory = userDataDirectory;
        _manager = new SafeSaveGameManager(userDataDirectory, null, revalidate);
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
            _pending.Clear();
            _lastBackupTimes.Clear();
            _suppressedUntil.Clear();
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

    public void SuppressSlot(int slotNumber, TimeSpan duration)
    {
        lock (_gate)
        {
            _suppressedUntil[slotNumber] = DateTimeOffset.UtcNow + duration;
        }
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
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        if (!TryGetSlotNumber(args.Name, out int slot))
        {
            return;
        }

        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            if (_running.ContainsKey(slot))
            {
                // A backup for this slot is already queued/running. Remember the change
                // and let the running task pick it up once, instead of spawning a second
                // parallel job (guarantee: at most one queued backup per slot).
                _pending[slot] = true;
                return;
            }

            Task task = BackupSlotAsync(slot);
            _running[slot] = task;
        }
    }

    private async Task BackupSlotAsync(int slot)
    {
        try
        {
            await Task.Delay(500).ConfigureAwait(false);

            // Process changes that arrived while this job was running. Each iteration
            // handles one pending change; loop until the slot is quiescent.
            while (true)
            {
                lock (_gate)
                {
                    if (_stopped)
                    {
                        return;
                    }
                }

                if (!IsSuppressed(slot) && CanBackup(slot))
                {
                    SaveGameOperationResult result = _manager.CreateCheckpoint(
                        slot.ToString(CultureInfo.InvariantCulture),
                        "AutoBackup");
                    if (result.Succeeded && result.CreatedCheckpointId is not null)
                    {
                        RecordBackupTime(slot);
                        CheckpointCreated?.Invoke(this, slot.ToString(CultureInfo.InvariantCulture));
                    }
                }

                lock (_gate)
                {
                    if (_stopped || !_pending.TryGetValue(slot, out bool dirty) || !dirty)
                    {
                        _pending.Remove(slot);
                        break;
                    }

                    _pending.Remove(slot);
                }

                // A change arrived while the first backup was being written; schedule one
                // more pass with a fresh debounce so in-progress writes can settle.
                await Task.Delay(500).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException)
        {
            WatcherError?.Invoke(this, $"Auto-backup failed for slot {slot}: {exception.Message}");
        }
        catch (Exception exception)
        {
            WatcherError?.Invoke(this, $"Auto-backup failed for slot {slot}: {exception.Message}");
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(slot);
            }
        }
    }

    private bool IsSuppressed(int slot)
    {
        lock (_gate)
        {
            if (!_suppressedUntil.TryGetValue(slot, out DateTimeOffset until))
            {
                return false;
            }

            if (DateTimeOffset.UtcNow < until)
            {
                return true;
            }

            _suppressedUntil.Remove(slot);
            return false;
        }
    }

    private bool CanBackup(int slot)
    {
        lock (_gate)
        {
            if (!_lastBackupTimes.TryGetValue(slot, out DateTimeOffset last))
            {
                return true;
            }

            return DateTimeOffset.UtcNow >= last + _cooldown;
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

        if (!fileName.StartsWith("Savegame", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string number = fileName["Savegame".Length..^".sav".Length];
        return int.TryParse(number, out slotNumber) &&
               slotNumber >= 0 &&
               slotNumber < SaveGamePaths.SlotCount;
    }
}
