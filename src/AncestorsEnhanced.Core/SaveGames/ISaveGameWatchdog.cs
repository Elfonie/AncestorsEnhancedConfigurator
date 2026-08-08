namespace AncestorsEnhanced.Core.SaveGames;

public interface ISaveGameWatchdog
{
    bool IsRunning { get; }

    TimeSpan Cooldown { get; set; }

    void Start();

    void StopWatch();

    /// <summary>Marks a restore/write operation so filesystem events are reconciled afterwards.</summary>
    IDisposable BeginSlotMutation(int slotNumber);

    event EventHandler<string>? CheckpointCreated;

    /// <summary>Raised when the filesystem watcher reports an error.</summary>
    event EventHandler<string>? WatcherError;
}
