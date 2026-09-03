namespace AncestorsEnhanced.App;

/// <summary>
/// Classifies unhandled UI-thread exceptions. AEC writes game files, so an unknown
/// exception must never leave the app running in a half-broken state: only genuinely
/// transient, well-understood failures may be swallowed and recovered from.
/// </summary>
public static class FatalErrorPolicy
{
    /// <summary>
    /// Returns true when the exception is known to be transient and safe to recover from.
    /// Everything else is treated as fatal: log, inform the user, controlled shutdown.
    /// </summary>
    public static bool IsRecoverable(Exception exception) => exception switch
    {
        // Cancelled background work surfacing on the UI thread (refreshes, probes, timers).
        OperationCanceledException => true,

        // Discord Rich Presence and other network chatter failing must not kill the tool.
        System.Net.Sockets.SocketException => true,
        System.Net.WebException => true,
        System.Net.Http.HttpRequestException => true,

        // Unknown programming bug or unexpected UI failure -> fatal.
        _ => false,
    };
}
