using System.Globalization;
using System.Text;

namespace AncestorsEnhanced.Infrastructure.Logging;

/// <summary>
/// Minimal append-only file logger used for crash diagnostics and support tickets.
/// Writes to the tool's log directory without throwing when the filesystem is unwritable.
/// The stream is kept open for the lifetime of the logger to avoid reopening the file
/// on every message.
/// </summary>
public sealed class AppLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly string _logPath;
    private StreamWriter? _writer;
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
                StreamWriter writer = _writer ??= OpenWriter();
                string line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}");
                writer.WriteLine(line);
                writer.Flush();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private StreamWriter OpenWriter()
    {
        string directory = Path.GetDirectoryName(_logPath)
            ?? throw new InvalidOperationException("The log directory is missing.");
        Directory.CreateDirectory(directory);
        var stream = new FileStream(
            _logPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read);
        return new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }

        GC.SuppressFinalize(this);
    }
}