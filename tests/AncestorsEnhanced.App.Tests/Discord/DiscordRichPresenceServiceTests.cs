using AncestorsEnhanced.App.Discord;

namespace AncestorsEnhanced.App.Tests.Discord;

public sealed class DiscordRichPresenceServiceTests
{
    [Fact]
    public void StartPublishesOneStaticActivityWithoutScreenSpecificData()
    {
        var native = new RecordingNative();
        using var service = new DiscordRichPresenceService(native);

        service.Start();
        service.Start();

        DiscordRichPresenceActivity activity = Assert.Single(native.Activities);
        Assert.Equal("Open", activity.Details);
        Assert.Equal("big_logo", activity.LargeImage);
        Assert.Equal("small_logo", activity.SmallImage);
        Assert.DoesNotContain("Graphics", activity.Details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Save", activity.Details, StringComparison.OrdinalIgnoreCase);
        Assert.True(service.IsActive);
        Assert.Equal("Active", service.StatusMessage);
    }

    [Fact]
    public void MissingNativeLibraryDoesNotRetryOrCrashTheApplication()
    {
        var native = new ThrowingNative();
        using var service = new DiscordRichPresenceService(native);

        service.Start();
        service.RunCallbacks();
        service.Start();

        Assert.Equal(1, native.StartAttempts);
        Assert.Equal(0, native.CallbackAttempts);
        Assert.False(service.IsActive);
        Assert.Equal("Unavailable on this system", service.StatusMessage);
    }

    private sealed class RecordingNative : IDiscordRichPresenceNative
    {
        public List<DiscordRichPresenceActivity> Activities { get; } = [];

        public void Start(DiscordRichPresenceActivity activity) => Activities.Add(activity);

        public void RunCallbacks()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingNative : IDiscordRichPresenceNative
    {
        public int StartAttempts { get; private set; }

        public int CallbackAttempts { get; private set; }

        public void Start(DiscordRichPresenceActivity activity)
        {
            StartAttempts++;
            throw new DllNotFoundException();
        }

        public void RunCallbacks() => CallbackAttempts++;

        public void Dispose()
        {
        }
    }
}
