using AncestorsEnhanced.Infrastructure.Platform;

namespace AncestorsEnhanced.Infrastructure.Tests.Platform;

public sealed class SingleInstanceActivationTests
{
    [Fact]
    public async Task LocalActivationRequestReachesTheExistingListener()
    {
        string identifier = "aec-test-" + Guid.NewGuid().ToString("N");
        using var activated = new ManualResetEventSlim();
        using var listener = new SingleInstanceActivationListener(identifier, activated.Set);
        listener.Start();

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
}
