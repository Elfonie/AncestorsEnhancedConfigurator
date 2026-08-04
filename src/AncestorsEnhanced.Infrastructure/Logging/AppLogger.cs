using System.Globalization;
using System.Text;

namespace AncestorsEnhanced.Infrastructure.Logging;

/// <summary>
/// Minimal append-only file logger used for crash diagnostics and support tickets.
/// Writes to the tool's log directory without throwing when the filesystem is unwritable.
/// </summary>
public sealed class AppLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly string _logPath;
    private bool _disposed;

    public AppLogger(string logDirectory)
    {
        ArgumentNullException.ThrowIfNull(logDirectory);
        _logPath = Path.Combine(logDirectory, "AncestorsEnhanced.log");
    }

    public static string DefaultLogDirectory()
    {
        try
        {
            string? localAppData = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                string directory = Path.Combine(localAppData, "AncestorsEnhanced", "Logs");
                Directory.CreateDirectory(directory);
                return directory;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return Path.GetTempPath();
    }

    public void Write(string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                string line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{System.Environment.NewLine}");
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}