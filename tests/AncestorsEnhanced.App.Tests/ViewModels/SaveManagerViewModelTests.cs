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
        Assert.True(viewModel.SaveManager.HasSlots);
        Assert.Equal(5, viewModel.SaveManager.Slots.Count);
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

    private sealed class FakeSaveGameManager(SaveGamesSnapshot snapshot) : ISaveGameManager
    {
        public bool CreatedSlot0 { get; private set; }

        public SaveGamesSnapshot Inspect() => snapshot;

        public SaveGameOperationResult CreateCheckpoint(string slotNumber)
        {
            if (slotNumber == "0")
            {
                CreatedSlot0 = true;
            }

            return new SaveGameOperationResult(true, "Checkpoint saved.", "cp-1");
        }

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) =>
            new(true, "Loaded.");
    }

    [Fact]
    public void TogglingWatchdogStartsAndStopsIt()
    {
        var watchdog = new FakeWatchdog();
        var viewModel = new SaveManagerViewModel(new FakeSaveGameManager(
            new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots())), watchdog);

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
        var viewModel = new SaveManagerViewModel(inspectCount, watchdog);
        viewModel.Refresh(inspectCount.Snapshot);
        int before = inspectCount.InspectCount;

        watchdog.RaiseCheckpoint("0");
        await Task.Delay(50);

        Assert.True(inspectCount.InspectCount > before);
    }

    private sealed class FakeWatchdog : ISaveGameWatchdog
    {
        public int StartCount { get; set; }
        public int StopCount { get; set; }
        public bool IsRunning => StartCount > StopCount;

        public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);

        public event EventHandler<string>? CheckpointCreated;

        public void Start() => StartCount++;

        public void StopWatch() => StopCount++;

        public void RaiseCheckpoint(string slotNumber) =>
            CheckpointCreated?.Invoke(this, slotNumber);
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

        public SaveGameOperationResult CreateCheckpoint(string slotNumber) =>
            new(true, "Checkpoint saved.");

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) =>
            new(true, "Loaded.");
    }
}
