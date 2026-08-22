namespace AncestorsEnhanced.Core.SaveGames;

public sealed class ToolSettings
{
    public bool IsWatchdogEnabled { get; set; }

    public int WatchdogIntervalMinutes { get; set; } = 5;
}
