using AncestorsEnhanced.Infrastructure.Logging;

namespace AncestorsEnhanced.App;

/// <summary>
/// Lazily creates the process-wide diagnostics log used for crash and detection data.
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
