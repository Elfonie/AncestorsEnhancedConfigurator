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
}
