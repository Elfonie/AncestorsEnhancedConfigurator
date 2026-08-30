using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;

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
        File.WriteAllBytes(slotPath, TestSaveFactory.Create(1, 2, 3));
        var watchdog = new SaveGameWatchdog(userData);

        watchdog.Start();
        try
        {
            File.WriteAllBytes(slotPath, TestSaveFactory.Create(4, 5, 6, 7));
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

    [Fact]
    public void UnchangedSaveDoesNotCreateACheckpointOrRaiseAnEvent()
    {
        string userData = CreateUserData();
        string slotPath = SaveGamePaths.GetSlotPath(userData, 0);
        File.WriteAllBytes(slotPath, TestSaveFactory.Create(1, 2, 3));
        var watchdog = new SaveGameWatchdog(userData);
        int events = 0;
        watchdog.CheckpointCreated += (_, _) => events++;

        watchdog.Start();
        try
        {
            // First change creates a checkpoint.
            File.WriteAllBytes(slotPath, TestSaveFactory.Create(4, 5, 6, 7));
            WaitFor(() => SaveGameCheckpointStore.ListCheckpoints(userData, 0).Count == 1);

            // Writing back the identical content must not create a backup or raise an event.
            File.SetLastWriteTimeUtc(slotPath, DateTimeOffset.UtcNow.UtcDateTime);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int countAfterFirst = SaveGameCheckpointStore.ListCheckpoints(userData, 0).Count;
            Thread.Sleep(700);
            File.SetLastWriteTimeUtc(slotPath, DateTimeOffset.UtcNow.UtcDateTime);

            Assert.Equal(countAfterFirst, SaveGameCheckpointStore.ListCheckpoints(userData, 0).Count);
            Assert.Equal(1, events);
        }
        finally
        {
            watchdog.StopWatch();
        }
    }

    [Fact]
    public void RepeatedChangeWithinCooldownCreatesOnlyOneCheckpoint()
    {
        string userData = CreateUserData();
        string slotPath = SaveGamePaths.GetSlotPath(userData, 0);
        File.WriteAllBytes(slotPath, TestSaveFactory.Create(1, 2, 3));
        var watchdog = new SaveGameWatchdog(userData) { Cooldown = TimeSpan.FromMinutes(5) };

        watchdog.Start();
        try
        {
            File.WriteAllBytes(slotPath, TestSaveFactory.Create(4, 5, 6, 7));
            WaitFor(() => SaveGameCheckpointStore.ListCheckpoints(userData, 0).Count == 1);
            Thread.Sleep(100);
            File.WriteAllBytes(slotPath, TestSaveFactory.Create(8, 9, 10, 11));
            Thread.Sleep(700);

            Assert.Single(SaveGameCheckpointStore.ListCheckpoints(userData, 0));
        }
        finally
        {
            watchdog.StopWatch();
        }
    }

    [Fact]
    public void RestoreUnderMutationLeaseDoesNotPublishAnIntermediateAutoCheckpoint()
    {
        string userData = CreateUserData();
        string slotPath = SaveGamePaths.GetSlotPath(userData, 0);
        byte[] original = TestSaveFactory.Create(1, 2, 3);
        byte[] changed = TestSaveFactory.Create(8, 9, 10, 11);
        File.WriteAllBytes(slotPath, original);
        var manager = new SafeSaveGameManager(
            userData,
            () => DateTimeOffset.UtcNow,
            () => false,
            new SaveGameManagerOptions(MaxCheckpointsPerSlot: 50));
        string checkpoint = manager.CreateCheckpoint("0").CreatedCheckpointId!;
        var watchdog = new SaveGameWatchdog(userData) { Cooldown = TimeSpan.Zero };

        watchdog.Start();
        try
        {
            // Both the temporary live change and restore occur in one operation-aware
            // lease. Events remain dirty until the restored content can be reconciled.
            using (watchdog.BeginSlotMutation(0))
            {
                File.WriteAllBytes(slotPath, changed);
                SaveGameOperationResult restored = manager.LoadCheckpoint("0", checkpoint);
                Assert.True(restored.Succeeded, restored.Message);
            }

            watchdog.WaitForIdle();
            Assert.Equal(original, File.ReadAllBytes(slotPath));
            IReadOnlyList<SaveGameCheckpoint> checkpoints = SaveGameCheckpointStore.ListCheckpoints(userData, 0);
            Assert.Equal(2, checkpoints.Count);
            Assert.DoesNotContain(checkpoints, checkpoint => checkpoint.Origin == "AutoBackup");
        }
        finally
        {
            watchdog.StopWatch();
        }
    }

    [Fact]
    public void SuccessfulBackupResetsRetryBudgetForTheNextFailureEpisode()
    {
        string userData = CreateUserData();
        string slotPath = SaveGamePaths.GetSlotPath(userData, 0);
        File.WriteAllBytes(slotPath, TestSaveFactory.Create(1, 2, 3));
        Queue<SaveGameOperationResult> outcomes = new(
        [
            new(false, "Slot is currently being written or corrupt; skipped backup.", IsTransientFailure: true),
            new(true, "Checkpoint saved.", "first"),
            new(false, "Slot is currently being written or corrupt; skipped backup.", IsTransientFailure: true),
            new(false, "Slot is currently being written or corrupt; skipped backup.", IsTransientFailure: true),
            new(false, "Slot is currently being written or corrupt; skipped backup.", IsTransientFailure: true),
            new(true, "Checkpoint saved.", "second"),
        ]);
        var watchdog = new SaveGameWatchdog(userData, _ => outcomes.Dequeue()) { Cooldown = TimeSpan.Zero };
        int successes = 0;
        watchdog.CheckpointCreated += (_, _) => Interlocked.Increment(ref successes);
        watchdog.Start();
        try
        {
            File.WriteAllBytes(slotPath, TestSaveFactory.Create(4, 5, 6));
            WaitFor(() => Volatile.Read(ref successes) == 1);

            File.WriteAllBytes(slotPath, TestSaveFactory.Create(7, 8, 9));
            WaitFor(() => Volatile.Read(ref successes) == 2);
            Assert.Empty(outcomes);
        }
        finally
        {
            watchdog.StopWatch();
        }
    }

    [Fact]
    public async Task StopAndRestartDoNotLoseOrDuplicateWorkerGenerations()
    {
        string userData = CreateUserData();
        string slotPath = SaveGamePaths.GetSlotPath(userData, 0);
        File.WriteAllBytes(slotPath, TestSaveFactory.Create(1));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        var watchdog = new SaveGameWatchdog(userData, _ =>
        {
            int call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
            return new SaveGameOperationResult(true, "Checkpoint saved.", $"cp-{call}");
        })
        {
            Cooldown = TimeSpan.FromDays(1),
        };

        watchdog.Start();
        watchdog.BeginSlotMutation(0).Dispose();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task stopping = Task.Run(watchdog.StopWatch);
        await Task.Delay(100);
        release.Set();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));

        watchdog.Start();
        watchdog.BeginSlotMutation(0).Dispose();
        WaitFor(() => Volatile.Read(ref calls) == 2);
        await Task.Delay(300);
        watchdog.StopWatch();

        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public void StopFlushesADirtySlotThatIsWaitingForCooldown()
    {
        string userData = CreateUserData();
        int calls = 0;
        using var watchdog = new SaveGameWatchdog(
            userData,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return new SaveGameOperationResult(true, "Checkpoint saved.", "final");
            })
        {
            Cooldown = TimeSpan.FromMinutes(5),
        };

        watchdog.Start();
        using (watchdog.BeginSlotMutation(0))
        {
        }
        Thread.Sleep(100);

        watchdog.StopWatch();

        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        string userData = CreateUserData();
        var watchdog = new SaveGameWatchdog(userData);
        watchdog.Start();

        watchdog.Dispose();
        watchdog.Dispose();

        Assert.False(watchdog.IsRunning);
    }

    [Fact]
    public void CheckpointSubscriberFailuresAreWrittenToDiagnostics()
    {
        string userData = CreateUserData();
        var diagnostics = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var watchdog = new SaveGameWatchdog(
            userData,
            _ => new SaveGameOperationResult(true, "Checkpoint saved.", "checkpoint"),
            diagnostics.Enqueue)
        {
            Cooldown = TimeSpan.Zero,
        };
        watchdog.CheckpointCreated += (_, _) => throw new InvalidOperationException("UI handler failed");
        watchdog.Start();

        watchdog.BeginSlotMutation(0).Dispose();

        WaitFor(() => diagnostics.Any(message => message.Contains("CheckpointCreated subscriber failed", StringComparison.Ordinal)));
        Assert.Contains(diagnostics, message => message.Contains("UI handler failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SlotMutationDoesNotStartWorkerBeforeWatchdogIsEnabled()
    {
        string userData = CreateUserData();
        int calls = 0;
        using var watchdog = new SaveGameWatchdog(
            userData,
            _ =>
            {
                Interlocked.Increment(ref calls);
                return new SaveGameOperationResult(true, "Checkpoint saved.", "cp-1");
            });

        watchdog.BeginSlotMutation(0).Dispose();
        await Task.Delay(750);

        Assert.False(watchdog.IsRunning);
        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public void StopWatchWaitsForSlowWorkerCompletion()
    {
        string userData = CreateUserData();
        string slotPath = SaveGamePaths.GetSlotPath(userData, 0);
        File.WriteAllBytes(slotPath, TestSaveFactory.Create(1, 2, 3));

        var workerStarted = new ManualResetEventSlim();
        var workerCanFinish = new ManualResetEventSlim();
        int checkpointCount = 0;

        using var watchdog = new SaveGameWatchdog(
            userData,
            _ =>
            {
                workerStarted.Set();
                workerCanFinish.Wait(TimeSpan.FromSeconds(5));
                Interlocked.Increment(ref checkpointCount);
                return new SaveGameOperationResult(true, "Checkpoint saved.", "cp-1");
            })
        {
            Cooldown = TimeSpan.Zero,
        };

        watchdog.Start();
        // Trigger worker
        watchdog.BeginSlotMutation(0).Dispose();

        // Wait until worker is actively inside the checkpoint creation delegate
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        // Unblock worker after 500ms
        Task.Run(async () =>
        {
            await Task.Delay(500);
            workerCanFinish.Set();
        });

        // StopWatch must block until the worker has finished
        watchdog.StopWatch();

        Assert.Equal(1, Volatile.Read(ref checkpointCount));
        Assert.False(watchdog.IsRunning);
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
