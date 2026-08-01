using AncestorsEnhanced.Core.Safety;

namespace AncestorsEnhanced.Core.Tests.Safety;

public sealed class ApplicationSafetyProfileTests
{
    [Fact]
    public void FoundationProfileIsReadOnlyAndOffline()
    {
        ApplicationSafetyProfile profile = ApplicationSafetyProfile.Foundation;

        Assert.True(profile.IsReadOnly);
        Assert.False(profile.GameFileWritesEnabled);
        Assert.False(profile.NetworkAccessEnabled);
        Assert.False(profile.TelemetryEnabled);
    }

    [Fact]
    public void Version03EnablesOnlyGuardedGameFileWrites()
    {
        ApplicationSafetyProfile profile = ApplicationSafetyProfile.Version03;

        Assert.False(profile.IsReadOnly);
        Assert.True(profile.GameFileWritesEnabled);
        Assert.False(profile.NetworkAccessEnabled);
        Assert.False(profile.TelemetryEnabled);
    }
}
