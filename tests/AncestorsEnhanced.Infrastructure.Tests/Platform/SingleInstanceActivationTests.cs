using AncestorsEnhanced.Infrastructure.Platform;
using System.IO.Pipes;

namespace AncestorsEnhanced.Infrastructure.Tests.Platform;

public sealed class SingleInstanceActivationTests
{
    [Fact]
    public async Task LocalActivationRequestReachesTheExistingListener()
    {
        string identifier = "aec-test-" + Guid.NewGuid().ToString("N");
        using var activated = new ManualResetEventSlim();
        using var listener = new SingleInstanceActivationListener(identifier, activated.Set);
        await listener.StartAsync();

        Assert.True(SingleInstanceActivationListener.TryActivateExistingInstance(identifier, TimeSpan.FromSeconds(3)));
        Assert.True(await Task.Run(() => activated.Wait(TimeSpan.FromSeconds(3))));
    }

    [Fact]
    public void PipeNameIsStableAndDoesNotExposeTheRawIdentifier()
    {
        string pipeName = SingleInstanceActivationListener.BuildPipeName("Aec / user specific");

        Assert.StartsWith("ancestors-enhanced-", pipeName, StringComparison.Ordinal);
        Assert.DoesNotContain("user", pipeName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(pipeName, SingleInstanceActivationListener.BuildPipeName("Aec / user specific"));
    }

    [Fact]
    public async Task StalledClientDoesNotBlockShutdownOrLaterActivation()
    {
        string identifier = "aec-test-" + Guid.NewGuid().ToString("N");
        using var activated = new ManualResetEventSlim();
        var listener = new SingleInstanceActivationListener(identifier, activated.Set);
        await listener.StartAsync();

        using var stalledClient = new NamedPipeClientStream(
            ".", SingleInstanceActivationListener.BuildPipeName(identifier), PipeDirection.Out, PipeOptions.Asynchronous);
        await stalledClient.ConnectAsync(1000);

        listener.Dispose();

        using var replacement = new SingleInstanceActivationListener(identifier, activated.Set);
        await replacement.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(SingleInstanceActivationListener.TryActivateExistingInstance(identifier, TimeSpan.FromSeconds(3)));
        Assert.True(await Task.Run(() => activated.Wait(TimeSpan.FromSeconds(3))));
    }
}
