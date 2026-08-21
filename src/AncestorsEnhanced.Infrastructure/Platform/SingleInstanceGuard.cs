using System.Globalization;
using System.Runtime.InteropServices;

namespace AncestorsEnhanced.Infrastructure.Platform;

/// <summary>
/// Best-effort single-instance guard: a named mutex on Windows and an exclusive
/// lock file on Unix. The OS releases the lock when the process exits, even after a
/// crash, so no permanent lock remains.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex? _mutex;
    private FileStream? _lockFile;
    private bool _acquired;
    private bool _disposed;

    public SingleInstanceGuard(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        if (OperatingSystem.IsWindows())
        {
            _mutex = new Mutex(initiallyOwned: false, identifier);
            try
            {
                _acquired = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                _acquired = true;
            }
        }
        else
        {
            string lockPath = BuildLockPath(identifier);
            try
            {
                string lockDirectory = Path.GetDirectoryName(lockPath)!;
                Directory.CreateDirectory(lockDirectory);
                if (File.GetAttributes(lockDirectory).HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new IOException("The runtime lock directory is a symbolic link.");
                }
                File.SetUnixFileMode(
                    lockDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                if (File.Exists(lockPath) && File.GetAttributes(lockPath).HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new IOException("The runtime lock file is a symbolic link.");
                }
                _lockFile = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                _acquired = true;
            }
            catch (IOException)
            {
                _acquired = false;
            }
            catch (UnauthorizedAccessException)
            {
                _acquired = false;
            }
        }
    }

    public bool IsAcquired => _acquired;

    public static string BuildLockPath(string identifier)
    {
        string? runtimeDirectory = System.Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        string root = !string.IsNullOrWhiteSpace(runtimeDirectory)
            ? Path.Combine(runtimeDirectory, "ancestors-enhanced")
            : Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "AncestorsEnhanced",
                "Runtime");
        string fileName = new string(
            identifier.Select(character =>
                char.IsLetterOrDigit(character) || character == '-' || character == '.' || character == '_'
                    ? character
                    : '_').ToArray());
        return Path.Combine(root, "." + fileName + ".lock");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_acquired)
        {
            _mutex?.Dispose();
            _lockFile?.Dispose();
            _lockFile = null;
            GC.SuppressFinalize(this);
            return;
        }

        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch (Exception exception) when (
            exception is ApplicationException or ObjectDisposedException)
        {
        }

        try
        {
            _lockFile?.Dispose();
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException)
        {
        }

        _lockFile = null;
        _acquired = false;
        GC.SuppressFinalize(this);
    }
}
