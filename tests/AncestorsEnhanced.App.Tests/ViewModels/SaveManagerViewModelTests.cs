using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.App.ViewModels;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class SaveManagerViewModelTests
{
    [Fact]
    public async Task InitializeBuildsTheSaveManagerAndListsSlots()
    {
        var manager = new FakeSaveGameManager(
            new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots()));
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            _ => manager);

        await viewModel.InitializeAsync();

        Assert.NotNull(viewModel.SaveManager);
        Assert.Equal(5, viewModel.SaveManager.Slots.Count);
        // Alle Test-Slots sind leer (Exists=false) -> korrekt als "keine Saves" gemeldet.
        Assert.False(viewModel.SaveManager.HasSlots);
    }

    [Fact]
    public async Task HasSlotsIsTrueWhenASlotHasASaveFile()
    {
        var slot = new SaveGameSlotSnapshot(
            "0",
            "Savegame0.sav",
            "path-0",
            Exists: true,
            42,
            DateTimeOffset.UnixEpoch,
            []);
        var manager = new FakeSaveGameManager(
            new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", [slot]));
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            _ => manager);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.SaveManager!.HasSlots);
        Assert.Single(viewModel.SaveManager.Slots);
    }


    [Fact]
    public void TabsSwitchBetweenGraphicsAndSaveGames()
    {
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            _ => new FakeSaveGameManager(
                new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots())));

        Assert.False(viewModel.IsSaveGamesView);
        Assert.True(viewModel.ShowGraphicsView);
        Assert.False(viewModel.ShowSaveGamesView);

        viewModel.ShowSaveGamesCommand.Execute(null);

        Assert.True(viewModel.IsSaveGamesView);
        Assert.True(viewModel.ShowSaveGamesView);
        Assert.False(viewModel.ShowGraphicsView);

        viewModel.ShowGraphicsCommand.Execute(null);

        Assert.False(viewModel.IsSaveGamesView);
    }

    [Fact]
    public async Task SlotHasACheckpointThatCanBeLoaded()
    {
        var manager = new FakeSaveGameManager(
            new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots()));
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            _ => manager);
        await viewModel.InitializeAsync();

        SaveGameSlotViewModel slot = viewModel.SaveManager!.Slots.Single(s => s.SlotNumber == "0");
        await slot.CreateCheckpointCommand.ExecuteAsync(null);

        Assert.True(manager.CreatedSlot0);
    }

    [Fact]
    public async Task SaveMutationDisablesAllOtherMutationControls()
    {
        var checkpoint = new SaveGameCheckpoint(
            "cp-1", DateTimeOffset.UnixEpoch, "0", 10, "Manual");
        var slot = new SaveGameSlotSnapshot(
            "0", "Savegame0.sav", "path-0", true, 10, DateTimeOffset.UnixEpoch, [checkpoint]);
        var manager = new BlockingSaveGameManager(
            new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", [slot]));
        var viewModel = new MainViewModel(
            new FixedInspector(CreateSnapshot()),
            new RecordingEditor(),
            _ => manager);
        await viewModel.InitializeAsync();

        Task operation = viewModel.SaveManager!.RunCreate("0");
        Assert.True(manager.Entered.Wait(TimeSpan.FromSeconds(5)));

        SaveGameSlotViewModel shownSlot = Assert.Single(viewModel.SaveManager.Slots);
        SaveGameCheckpointViewModel shownCheckpoint = Assert.Single(shownSlot.Checkpoints);
        Assert.True(viewModel.IsAnyOperationRunning);
        Assert.False(viewModel.CanEditSettings);
        Assert.False(viewModel.SaveManager.CanConfigureAutoBackup);
        Assert.False(shownSlot.CanSaveCheckpoint);
        Assert.False(shownCheckpoint.CanRestore);
        Assert.False(shownCheckpoint.CanDelete);

        manager.Release.Set();
        await operation;

        Assert.False(viewModel.IsAnyOperationRunning);
        Assert.True(viewModel.CanEditSettings);
        Assert.True(viewModel.SaveManager.CanConfigureAutoBackup);
    }

    [Fact]
    public async Task WithoutUserDataTheSaveManagerIsNotShown()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot() with { UserDataDirectory = null };
        var viewModel = new MainViewModel(
            new FixedInspector(snapshot),
            new RecordingEditor(),
            _ => throw new InvalidOperationException("should not be created"));

        await viewModel.InitializeAsync();

        Assert.Null(viewModel.SaveManager);
    }


    [Fact]
    public async Task LoadSuppressesTheWatchdogForThatSlot()
    {
        var watchdog = new FakeWatchdog();
        var viewModel = new SaveManagerViewModel(
            new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots())),
            "user-data",
            watchdog);

        await viewModel.RunLoad("3", "cp-1");

        Assert.Equal(3, watchdog.SuppressedSlot);
    }

    [Fact]
    public void EmptySaveDirectoryReportsNoSlotsWithStatusAccent()
    {
        var manager = new FakeSaveGameManager(
            new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots()));
        var viewModel = new SaveManagerViewModel(
            manager,
            "user-data",
            watchdog: null);

        Assert.True(viewModel.HasNoSlots);
        Assert.False(viewModel.HasSlots);
        Assert.Equal("#7A877A", viewModel.StatusAccent);
        Assert.Contains("No save games", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedOperationSetsErrorAccent()
    {
        var manager = new FailingSaveGameManager();
        var viewModel = new SaveManagerViewModel(
            manager,
            "user-data",
            watchdog: null);

        SaveGameOperationResult result = await viewModel.RunLoad("0", "missing");

        Assert.False(result.Succeeded);
        Assert.Equal("#E04D42", viewModel.StatusAccent);
    }

    [Fact]
    public void RefreshPreservesExpandedCheckpointsAndDisablesRestoreWhileGameRuns()
    {
        SaveGameCheckpoint[] checkpoints = Enumerable.Range(1, 3)
            .Select(index => new SaveGameCheckpoint(
                $"cp-{index}", DateTimeOffset.UnixEpoch, "0", index, $"checkpoint-{index}", "Manual"))
            .ToArray();
        var snapshot = new SaveGamesSnapshot(
            DateTimeOffset.UnixEpoch,
            "user-data",
            [new SaveGameSlotSnapshot("0", "Savegame0.sav", "path-0", true, 42, DateTimeOffset.UnixEpoch, checkpoints)]);
        var viewModel = new SaveManagerViewModel(new FakeSaveGameManager(snapshot), "user-data", watchdog: null);

        viewModel.Refresh(snapshot);
        viewModel.Slots[0].ShowOlderCommand.Execute(null);
        viewModel.IsGameRunning = true;

        Assert.True(viewModel.Slots[0].HasExpandedCheckpoints);
        Assert.All(viewModel.Slots[0].Checkpoints, checkpoint => Assert.False(checkpoint.CanRestore));

        viewModel.Refresh(snapshot);

        Assert.True(viewModel.Slots[0].HasExpandedCheckpoints);
        Assert.All(viewModel.Slots[0].Checkpoints, checkpoint => Assert.False(checkpoint.CanRestore));
    }

    private static string TempUserData() =>
        Path.Combine(Path.GetTempPath(), "aec-sm-" + Guid.NewGuid().ToString("N"));

    private static SaveGameSlotSnapshot[] Slots() =>
        Enumerable.Range(0, 5)
            .Select(slot => new SaveGameSlotSnapshot(
                slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Savegame{slot}.sav",
                $"path-{slot}",
                Exists: false,
                null,
                null,
                []))
            .ToArray();

    private static GameInspectionSnapshot CreateSnapshot() =>
        new(
            DateTimeOffset.UnixEpoch,
            new GameInstallationSnapshot(
                StoreKind.Steam,
                HostKind.Windows,
                CompatibilityLayerKind.None,
                "library",
                "install",
                "5495393",
                ExecutableExists: true),
            "user-data",
            [],
            null,
            [],
            []);

    private sealed class FixedInspector(GameInspectionSnapshot snapshot) : IReadOnlyGameInspector
    {
        public GameInspectionSnapshot Inspect() => snapshot;
    }

    private sealed class RecordingEditor : IGameSettingsEditor
    {
        public SettingsChangePlan CreatePlan(
            GameInspectionSnapshot snapshot,
            IReadOnlyList<SettingChangeRequest> requests) =>
            new(
                "review",
                DateTimeOffset.UnixEpoch,
                "5495393",
                snapshot.UserDataDirectory!,
                [],
                []);

        public SettingsOperationResult Apply(SettingsChangePlan plan) =>
            new(true, "Applied.");

        public void DiscardPlan(SettingsChangePlan plan)
        {
        }

        public bool CanRevertLast(GameInspectionSnapshot snapshot) => false;

        public SettingsOperationResult RevertLast(GameInspectionSnapshot snapshot) =>
            new(false, "Nothing to revert.");
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    public void FormatSizeProducesHumanReadableUnits(long bytes, string expected)
    {
        Assert.Equal(expected, SaveGameSlotViewModel.FormatSize(bytes));
    }

    private sealed class FakeSaveGameManager(SaveGamesSnapshot snapshot) : ISaveGameManager
    {
        public bool CreatedSlot0 { get; private set; }

        public SaveGamesSnapshot Inspect() => snapshot;

        public SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual")
        {
            if (slotNumber == "0")
            {
                CreatedSlot0 = true;
            }

            return new SaveGameOperationResult(true, "Checkpoint saved.", "cp-1");
        }

          public SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId) =>
              new(true, "Deleted.");

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) =>
            new(true, "Loaded.");
    }

    private sealed class BlockingSaveGameManager(SaveGamesSnapshot snapshot) : ISaveGameManager
    {
        public ManualResetEventSlim Entered { get; } = new(false);

        public ManualResetEventSlim Release { get; } = new(false);

        public SaveGamesSnapshot Inspect() => snapshot;

        public SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual")
        {
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10)))
            {
                return new SaveGameOperationResult(false, "Timed out.");
            }
            return new SaveGameOperationResult(true, "Checkpoint saved.", "cp-2");
        }

        public SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId) =>
            new(true, "Deleted.");

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) =>
            new(true, "Loaded.");
    }

    [Fact]
    public void TogglingWatchdogStartsAndStopsIt()
    {
        var watchdog = new FakeWatchdog();
        var viewModel = new SaveManagerViewModel(new FakeSaveGameManager(
            new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots())), "user-data-tmp", watchdog);

        Assert.False(watchdog.StartCount > 0);
        viewModel.IsWatchdogEnabled = true;
        Assert.Equal(1, watchdog.StartCount);
        Assert.Equal(0, watchdog.StopCount);

        viewModel.IsWatchdogEnabled = false;
        Assert.Equal(1, watchdog.StartCount);
        Assert.Equal(1, watchdog.StopCount);
    }

    [Fact]
    public async Task WatchdogCheckpointRefreshReloadsTheList()
    {
        var watchdog = new FakeWatchdog();
        var inspectCount = new CountInspectManager(
            new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots()));
        var viewModel = new SaveManagerViewModel(inspectCount, "user-data", watchdog, action => action());
        viewModel.Refresh(inspectCount.Snapshot);
        int before = inspectCount.InspectCount;

        watchdog.RaiseCheckpoint("0");
        await WaitUntilAsync(
            () => inspectCount.InspectCount > before,
            TimeSpan.FromSeconds(5),
            "The watchdog event did not trigger a save-list refresh.");
    }

    private sealed class FakeWatchdog : ISaveGameWatchdog
    {
        public int StartCount { get; set; }
        public int StopCount { get; set; }
        public bool IsRunning => StartCount > StopCount;

        public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);

        public int SuppressedSlot { get; private set; } = -1;

        public IDisposable BeginSlotMutation(int slotNumber)
        {
            SuppressedSlot = slotNumber;
            return new DisposableAction();
        }

        public event EventHandler<string>? CheckpointCreated;

        public event EventHandler<string>? WatcherError;

        private sealed class DisposableAction : IDisposable
        {
            public void Dispose() { }
        }

        public void Start() => StartCount++;

        public void StopWatch() => StopCount++;

        public void RaiseCheckpoint(string slotNumber) =>
            CheckpointCreated?.Invoke(this, slotNumber);

        public void RaiseWatcherError(string message) =>
            WatcherError?.Invoke(this, message);
    }

    [Fact]
    public async Task WatcherErrorShowsAFailureStatus()
    {
        var watchdog = new FakeWatchdog();
        var viewModel = new SaveManagerViewModel(
            new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots())),
            "user-data",
            watchdog,
            action => action());

        ReadOnlySpan<char> before = viewModel.StatusMessage.AsSpan();
        _ = before.ToString();
        watchdog.RaiseWatcherError("disk full");

        Assert.Equal("#E04D42", viewModel.StatusAccent);
        Assert.Contains("watch failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disk full", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CountInspectManager(SaveGamesSnapshot snapshot) : ISaveGameManager
    {
        public int InspectCount { get; private set; }
        public SaveGamesSnapshot Snapshot => snapshot;

        public SaveGamesSnapshot Inspect()
        {
            InspectCount++;
            return snapshot;
        }

        public SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual") =>
            new(true, "Checkpoint saved.");

          public SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId) =>
              new(true, "Deleted.");

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) =>
            new(true, "Loaded.");
    }

    private sealed class FailingSaveGameManager : ISaveGameManager
    {
        public SaveGamesSnapshot Inspect() =>
            new(DateTimeOffset.UnixEpoch, "user-data", []);

        public SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual") =>
            new(false, "failed");

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) =>
            new(false, "failed");

        public SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId) =>
            new(false, "failed");
    }
    [Fact]
    public void CooldownClampsNegativeAndExtremeValues()
    {
        var viewModel = new SaveManagerViewModel(
            new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots())),
            "user-data",
            watchdog: null);

        viewModel.CooldownMinutes = -5;
        Assert.True(viewModel.CooldownMinutes > 0);
        Assert.Equal(1, viewModel.CooldownMinutes);

        viewModel.CooldownMinutes = 100_000;
        Assert.Equal(1440, viewModel.CooldownMinutes);
    }

    [Fact]
    public void DisposeDrainsTheCompleteSettingsWriteQueue()
    {
        string userData = TempUserData();
        Directory.CreateDirectory(userData);
        try
        {
            var viewModel = new SaveManagerViewModel(
                new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, userData, Slots())),
                userData,
                watchdog: null);
            for (int index = 0; index < 100; index++)
            {
                viewModel.CooldownMinutes = index % 2 == 0 ? 5 : 10;
            }

            viewModel.Dispose();
            viewModel.Dispose();

            Assert.Empty(Directory.EnumerateFiles(userData, "*.tmp"));
            string settingsPath = Path.Combine(userData, "AncestorsEnhanced_ToolSettings.json");
            if (File.Exists(settingsPath))
            {
                using System.Text.Json.JsonDocument _ = System.Text.Json.JsonDocument.Parse(
                    File.ReadAllBytes(settingsPath));
            }
        }
        finally
        {
            Directory.Delete(userData, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string failureMessage)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(failureMessage);
            }

            await Task.Delay(25);
        }
    }
}
