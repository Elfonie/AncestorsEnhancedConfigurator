using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace AncestorsEnhanced.Infrastructure.Platform;

/// <summary>
/// Delivers a local, payload-free activation request from a second launch to the
/// already running desktop instance. The mutex/lock remains the authority for
/// ownership; this pipe only asks the owner to bring its window forward.
/// </summary>
public sealed class SingleInstanceActivationListener : IDisposable
{
    private readonly string _pipeName;
    private readonly Action _activate;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenerTask;
    private bool _disposed;

    public SingleInstanceActivationListener(string identifier, Action activate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(activate);
        _pipeName = BuildPipeName(identifier);
        _activate = activate;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _listenerTask ??= Task.Run(ListenAsync);
    }

    public static bool TryActivateExistingInstance(string identifier, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        try
        {
            using var client = new NamedPipeClientStream(
                ".", BuildPipeName(identifier), PipeDirection.Out, PipeOptions.None);
            client.Connect((int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue));
            client.WriteByte(1);
            client.Flush();
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string BuildPipeName(string identifier)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identifier));
        return "ancestors-enhanced-" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);

                // The protocol intentionally accepts no arguments or file paths.
                // A connected client can only request the existing window to activate.
                if (server.ReadByte() >= 0)
                {
                    _activate();
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (IOException) when (!_cancellation.IsCancellationRequested)
            {
                // A malformed/disconnected local client must not end the listener.
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
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
