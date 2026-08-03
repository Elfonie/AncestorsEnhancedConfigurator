using AncestorsEnhanced.Infrastructure.SaveGames;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

public sealed class SaveGameWatchdogTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ancestors-enhanced-watchdog-tests-{Guid.NewGuid():N}");

    [Fact]
    public void StartEndTogglesTheWatcher()
    {
        string userData = CreateUserData();
        var watchdog = new SaveGameWatchdog(userData);

        Assert.False(watchdog.IsRunning);
        watchdog.Start();
        Assert.True(watchdog.IsRunning);
        watchdog.StopWatch();
        Assert.False(watchdog.IsRunning);
    }

    [Fact]
    public void ModifiedSaveCreatesACheckpoint()
    {
        string userData = CreateUserData();
        string slotPath = SaveGamePaths.GetSlotPath(userData, 0);
        File.WriteAllBytes(slotPath, [1, 2, 3]);
        var watchdog = new SaveGameWatchdog(userData);

        watchdog.Start();
        try
        {
            File.WriteAllBytes(slotPath, [4, 5, 6, 7]);
            WaitFor(() => SaveGameCheckpointStore.ListCheckpoints(userData, 0).Count == 1);
            Assert.Single(SaveGameCheckpointStore.ListCheckpoints(userData, 0));
        }
        finally
        {
            watchdog.StopWatch();
        }
    }


    [Fact]
    public void CooldownCanBeReadAndWritten()
    {
        string userData = CreateUserData();
        var watchdog = new SaveGameWatchdog(userData);
        Assert.Equal(TimeSpan.FromMinutes(5), watchdog.Cooldown);

        watchdog.Cooldown = TimeSpan.FromMinutes(20);

        Assert.Equal(TimeSpan.FromMinutes(20), watchdog.Cooldown);
    }

    private string CreateUserData()
    {
        string userData = Path.Combine(_temporaryDirectory, "Saved");
        Directory.CreateDirectory(Path.Combine(userData, "SaveGames"));
        return userData;
    }

    private static void WaitFor(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("The watchdog did not create a checkpoint in time.");
            }

            Thread.Sleep(100);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}

