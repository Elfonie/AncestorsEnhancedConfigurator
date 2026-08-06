using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

public sealed class SaveGameWatchdog : ISaveGameWatchdog
{
    private readonly SafeSaveGameManager _manager;
    private readonly string _userDataDirectory;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, CancellationTokenSource> _debounces = new();
    private readonly Dictionary<int, DateTimeOffset> _lastBackupTimes = new();
    private readonly Dictionary<int, DateTimeOffset> _suppressedUntil = new();
    private TimeSpan _cooldown = TimeSpan.FromMinutes(5);
    private bool _stopped;
    private FileSystemWatcher? _watcher;

    public SaveGameWatchdog(string userDataDirectory)
    {
        _userDataDirectory = userDataDirectory;
        _manager = new SafeSaveGameManager(userDataDirectory);
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
            var watcher = new FileSystemWatcher(
                SaveGamePaths.GetSaveGamesDirectory(_userDataDirectory))
            {
                Filter = "Savegame*.sav",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                             NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnWatcherError;
            _watcher = watcher;
        }
    }

    public void StopWatch()
    {
        lock (_gate)
        {
            if (_watcher is null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnChanged;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
            _stopped = true;
        }

        CancelAllDebounces();
    }

    public void SuppressSlot(int slotNumber, TimeSpan duration)
    {
        lock (_gate)
        {
            _suppressedUntil[slotNumber] = DateTimeOffset.UtcNow + duration;
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
                // A watcher that cannot be restarted is still reported as an error;
                // the UI keeps the failed state visible.
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

            if (_debounces.TryGetValue(slot, out CancellationTokenSource? existing))
            {
                // Nicht hier disposen: der Task, der dieses Token besitzt,
                // disposet es nach seinem Abschluss selbst (finally).
                existing.Cancel();
            }

            var source = new CancellationTokenSource();
            _debounces[slot] = source;
            _ = Task.Run(() => DebouncedCheckpointAsync(slot, source, source.Token));
        }
    }

    private async Task DebouncedCheckpointAsync(int slot, CancellationTokenSource source, CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
            lock (_gate)
            {
                if (_stopped)
                {
                    return;
                }
            }

            if (IsSuppressed(slot))
            {
                return;
            }

            if (!CanBackup(slot))
            {
                return;
            }

            SaveGameOperationResult result = _manager.CreateCheckpoint(slot.ToString(CultureInfo.InvariantCulture), "AutoBackup");
            if (result.Succeeded && result.CreatedCheckpointId is not null)
            {
                RecordBackupTime(slot);
                CheckpointCreated?.Invoke(this, slot.ToString(CultureInfo.InvariantCulture));
            }
        }
        catch (OperationCanceledException)
        {
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
                if (_debounces.TryGetValue(slot, out CancellationTokenSource? current) &&
                    ReferenceEquals(current, source))
                {
                    _debounces.Remove(slot);
                }
            }

            // Diese Source gehört diesem Task und ist jetzt verbraucht.
            source.Dispose();
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

    private void CancelAllDebounces()
    {
        Dictionary<int, CancellationTokenSource> current;
        lock (_gate)
        {
            current = new Dictionary<int, CancellationTokenSource>(_debounces);
            _debounces.Clear();
            _lastBackupTimes.Clear();
            _suppressedUntil.Clear();
        }

        foreach (CancellationTokenSource source in current.Values)
        {
            source.Cancel();
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