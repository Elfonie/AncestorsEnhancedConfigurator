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
    private FileSystemWatcher? _watcher;

    public SaveGameWatchdog(string userDataDirectory)
    {
        _userDataDirectory = userDataDirectory;
        _manager = new SafeSaveGameManager(userDataDirectory);
    }

    public event EventHandler<string>? CheckpointCreated;

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

            var watcher = new FileSystemWatcher(
                SaveGamePaths.GetSaveGamesDirectory(_userDataDirectory))
            {
                Filter = "Savegame*.sav",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Changed += OnChanged;
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
            _watcher.Dispose();
            _watcher = null;
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

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        if (!TryGetSlotNumber(args.Name, out int slot))
        {
            return;
        }

        lock (_gate)
        {
            if (_debounces.TryGetValue(slot, out CancellationTokenSource? existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            var source = new CancellationTokenSource();
            _debounces[slot] = source;
            _ = Task.Run(() => DebouncedCheckpointAsync(slot, source.Token));
        }
    }

    private async Task DebouncedCheckpointAsync(int slot, CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
            if (IsSuppressed(slot))
            {
                return;
            }

            if (!CanBackup(slot))
            {
                return;
            }

            SaveGameOperationResult result = _manager.CreateCheckpoint(slot.ToString(CultureInfo.InvariantCulture));
            if (result.Succeeded)
            {
                RecordBackupTime(slot);
                CheckpointCreated?.Invoke(this, slot.ToString(CultureInfo.InvariantCulture));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (_debounces.TryGetValue(slot, out CancellationTokenSource? source) &&
                    source.IsCancellationRequested)
                {
                    _debounces.Remove(slot);
                }
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
            source.Dispose();
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
