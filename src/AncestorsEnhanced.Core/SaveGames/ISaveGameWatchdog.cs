namespace AncestorsEnhanced.Core.SaveGames;

public interface ISaveGameWatchdog
{
    bool IsRunning { get; }

    TimeSpan Cooldown { get; set; }

    void Start();

    void StopWatch();

    void SuppressSlot(int slotNumber, TimeSpan duration);

    event EventHandler<string>? CheckpointCreated;

    /// <summary>Raised when the filesystem watcher reports an error.</summary>
    event EventHandler<string>? WatcherError;
}
