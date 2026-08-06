namespace AncestorsEnhanced.Core.SaveGames;

public sealed class ToolSettings
{
    public bool IsWatchdogEnabled { get; set; }

    public int WatchdogIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// True when the free-camera toggle itself added ConsoleKeys=F10 to Input.ini.
    /// Used to remove only that exact tool-owned entry when the toggle is disabled.
    /// </summary>
    public bool FreeCameraF10Owned { get; set; }
}
