using AncestorsEnhanced.Infrastructure.Logging;

namespace AncestorsEnhanced.App;

/// <summary>
/// Process-wide diagnostics holder. Created once at startup so crash and detection
/// information can be written to a single log file for support tickets.
/// </summary>
public static class AppDiagnostics
{
    private static readonly Lazy<AppLogger?> LazyLogger = new(CreateLogger);

    public static AppLogger? Logger => LazyLogger.Value;

    private static AppLogger? CreateLogger()
    {
        try
        {
            return new AppLogger(AppLogger.DefaultLogDirectory());
        }
        catch (Exception)
        {
            return null;
        }
    }
}