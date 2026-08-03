namespace AncestorsEnhanced.Core.SaveGames;

public interface ISaveGameWatchdog
{
    bool IsRunning { get; }

    TimeSpan Cooldown { get; set; }

    void Start();

    void StopWatch();

    event EventHandler<string>? CheckpointCreated;
}
