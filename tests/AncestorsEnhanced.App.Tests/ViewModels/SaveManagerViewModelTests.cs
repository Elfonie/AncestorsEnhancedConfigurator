using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.SaveGames;

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
    public void RefreshShowsSaveRestoreRecoveryInsteadOfGenericLoadedMessage()
    {
        var viewModel = new SaveManagerViewModel(
            new FakeSaveGameManager(new SaveGamesSnapshot(
                DateTimeOffset.UnixEpoch,
                "user-data",
                Slots())),
            "user-data");
        const string Recovery = "Recovered an interrupted save restore safely.";

        viewModel.Refresh(new SaveGamesSnapshot(
            DateTimeOffset.UnixEpoch,
            "user-data",
            Slots(),
            Recovery));

        Assert.Equal(Recovery, viewModel.StatusMessage);
        Assert.Equal("#B4D941", viewModel.StatusAccent);
    }

    [Fact]
    public void BackupHealthSummarizesReadableSlotsAndFlagsInspectionProblems()
    {
        var viewModel = new SaveManagerViewModel(
            new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", [])),
            "user-data");
        var readable = new SaveGameSlotSnapshot(
            "0", "Savegame0.sav", "path-0", true, 42, DateTimeOffset.UnixEpoch,
            [new SaveGameCheckpoint("cp-1", DateTimeOffset.UnixEpoch, "0", 42, "hash", "Manual")]);
        var broken = new SaveGameSlotSnapshot(
            "1", "Savegame1.sav", "path-1", true, null, null, [], "Unreadable save");

        viewModel.Refresh(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", [readable, broken]));

        Assert.True(viewModel.HasBackupHealthWarning);
        Assert.Contains("1 readable save slot", viewModel.BackupHealthSummary, StringComparison.Ordinal);
        Assert.Contains("1 checkpoint", viewModel.BackupHealthSummary, StringComparison.Ordinal);
        Assert.Contains("1 slot(s) need attention", viewModel.BackupHealthSummary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cheat:MaxNeuronalEnergy")]
    [InlineData("Cheat:MaxNeeds")]
    [InlineData("Cheat:HealClan")]
    public void LegacyModifiedCheckpointOriginsRemainReadable(string origin)
    {
        var checkpoint = new SaveGameCheckpoint(
            "checkpoint",
            DateTimeOffset.UnixEpoch,
            "0",
            1,
            "hash",
            origin);
        var viewModel = new SaveGameCheckpointViewModel(
            checkpoint,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => true);

        Assert.Equal("Legacy modified checkpoint", viewModel.OriginLabel);
    }

    [Fact]
    public void CheckpointMetadataIsReportedWithoutChangingTheCheckpoint()
    {
        var checkpoint = new SaveGameCheckpoint(
            "checkpoint", DateTimeOffset.UnixEpoch, "0", 1, "hash", "Manual");
        CheckpointMetadata? saved = null;
        var viewModel = new SaveGameCheckpointViewModel(
            checkpoint,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => true,
            metadataChanged: metadata => saved = metadata);

        viewModel.Title = "Before evolution";
        viewModel.Note = "Keep this safe";
        viewModel.IsFavorite = true;

        Assert.Equal("Before evolution", viewModel.DisplayTitle);
        Assert.True(viewModel.HasNote);
        Assert.Equal("Before evolution", saved!.Title);
        Assert.Equal("Keep this safe", saved.Note);
        Assert.True(saved.IsFavorite);
        Assert.Equal("checkpoint", checkpoint.Id);
    }

    [Fact]
    public void CheckpointMetadataIsNotReportedDuringViewModelConstruction()
    {
        var checkpoint = new SaveGameCheckpoint("checkpoint", DateTimeOffset.UnixEpoch, "0", 1, "hash", "Manual");
        int notifications = 0;

        _ = new SaveGameCheckpointViewModel(
            checkpoint,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => true,
            metadata: new CheckpointMetadata("Pinned", "Keep", true),
            metadataChanged: _ => notifications++);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void CheckpointFiltersSearchMetadataAndOrigin()
    {
        var checkpoints = new[]
        {
            new SaveGameCheckpoint("manual", DateTimeOffset.UnixEpoch, "0", 1, "manual", "Manual"),
            new SaveGameCheckpoint("auto", DateTimeOffset.UnixEpoch, "0", 1, "auto", "AutoBackup"),
        };
        var slot = new SaveGameSlotSnapshot("0", "Savegame0.sav", "path-0", true, 1, DateTimeOffset.UnixEpoch, checkpoints);
        var viewModel = new SaveGameSlotViewModel(
            slot,
            () => Task.CompletedTask,
            _ => () => Task.CompletedTask,
            _ => () => Task.CompletedTask,
            metadataProvider: checkpoint => checkpoint.Id == "manual"
                ? new CheckpointMetadata("Before evolution", "Important", false)
                : null);

        viewModel.SetCheckpointFilter("evolution", "All");
        Assert.Single(viewModel.VisibleCheckpoints);
        Assert.Equal("manual", viewModel.VisibleCheckpoints[0].Id);

        viewModel.SetCheckpointFilter("", "AutoBackup");
        Assert.Single(viewModel.VisibleCheckpoints);
        Assert.Equal("auto", viewModel.VisibleCheckpoints[0].Id);
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
        Assert.True(viewModel.ShowHomeView);
        Assert.False(viewModel.ShowGraphicsView);
        Assert.False(viewModel.ShowSaveGamesView);

        viewModel.ShowSaveGamesCommand.Execute(null);

        Assert.True(viewModel.IsSaveGamesView);
        Assert.True(viewModel.ShowSaveGamesView);
        Assert.False(viewModel.ShowGraphicsView);

        viewModel.ShowGraphicsCommand.Execute(null);

        Assert.False(viewModel.IsSaveGamesView);
        Assert.True(viewModel.ShowGraphicsView);

        viewModel.ShowHomeCommand.Execute(null);

        Assert.True(viewModel.ShowHomeView);
        Assert.False(viewModel.ShowGraphicsView);
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
    public async Task CommittedWarningUsesWarningAccent()
    {
        var manager = new WarningSaveGameManager();
        var viewModel = new SaveManagerViewModel(
            manager,
            "user-data",
            watchdog: null);

        SaveGameOperationResult result = await viewModel.RunLoad("0", "checkpoint");

        Assert.Equal(SaveOperationCommitState.CommittedWithWarning, result.CommitState);
        Assert.Equal("#D6BC84", viewModel.StatusAccent);
        Assert.Equal("Loaded with a timestamp warning.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task FailedOperationWithSafetyCheckpointRefreshesAndUsesWarningAccent()
    {
        var manager = new SafetyCheckpointWarningManager();
        var viewModel = new SaveManagerViewModel(manager, "user-data", watchdog: null);

        SaveGameOperationResult result = await viewModel.RunLoad("0", "checkpoint");

        Assert.False(result.Succeeded);
        Assert.Equal(SaveOperationCommitState.CommittedWithWarning, result.CommitState);
        Assert.Equal(1, manager.InspectCount);
        Assert.Equal("Restore stopped; safety checkpoint created.", viewModel.StatusMessage);
        Assert.Equal("#D6BC84", viewModel.StatusAccent);
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

    [Fact]
    public void RefreshReusesUnchangedCheckpointViewModelsAndKeepsMetadataEditingState()
    {
        var checkpoint = new SaveGameCheckpoint("cp-1", DateTimeOffset.UnixEpoch, "0", 1, "checkpoint", "Manual");
        var snapshot = new SaveGamesSnapshot(
            DateTimeOffset.UnixEpoch,
            "user-data",
            [new SaveGameSlotSnapshot("0", "Savegame0.sav", "path-0", true, 42, DateTimeOffset.UnixEpoch, [checkpoint])]);
        var viewModel = new SaveManagerViewModel(new FakeSaveGameManager(snapshot), "user-data", watchdog: null);

        viewModel.Refresh(snapshot);
        SaveGameCheckpointViewModel original = Assert.Single(viewModel.Slots[0].Checkpoints);
        original.ToggleMetadataEditorCommand.Execute(null);
        original.Title = "Keep me";

        viewModel.Refresh(snapshot);

        SaveGameCheckpointViewModel refreshed = Assert.Single(viewModel.Slots[0].Checkpoints);
        Assert.Same(original, refreshed);
        Assert.True(refreshed.IsMetadataEditorVisible);
        Assert.Equal("Keep me", refreshed.Title);
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
    public void TogglingWatchdogReconcilesToTheLatestRequestedState()
    {
        var watchdog = new FakeWatchdog();
        var viewModel = new SaveManagerViewModel(new FakeSaveGameManager(
            new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots())), "user-data-tmp", watchdog);

        Assert.False(watchdog.StartCount > 0);
        viewModel.Activate();
        viewModel.IsWatchdogEnabled = true;
        Assert.True(SpinWait.SpinUntil(() => watchdog.StartCount == 1, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, watchdog.StopCount);

        viewModel.IsWatchdogEnabled = false;
        Assert.Equal(1, watchdog.StartCount);
        Assert.True(SpinWait.SpinUntil(() => watchdog.StopCount == 1, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void LoadingEnabledSettingsDoesNotQueueAStopAfterActivate()
    {
        string userData = TempUserData();
        Directory.CreateDirectory(userData);
        try
        {
            File.WriteAllText(Path.Combine(userData, "AncestorsEnhanced_ToolSettings.json"),
                "{\"IsWatchdogEnabled\":true,\"WatchdogIntervalMinutes\":5,\"KeepRunningInTrayWhenClosing\":true}");
            var watchdog = new FakeWatchdog();
            using var viewModel = new SaveManagerViewModel(
                new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, userData, Slots())),
                userData,
                watchdog);

            viewModel.Activate();

            Assert.True(SpinWait.SpinUntil(() => watchdog.StartCount == 1, TimeSpan.FromSeconds(2)));
            Assert.False(SpinWait.SpinUntil(() => watchdog.StopCount > 0, TimeSpan.FromMilliseconds(250)));
        }
        finally
        {
            Directory.Delete(userData, recursive: true);
        }
    }

    [Fact]
    public void OffThenOnReconcilesToEnabledEvenWhenThePreviousStopIsBlocked()
    {
        using var stopEntered = new ManualResetEventSlim();
        using var releaseStop = new ManualResetEventSlim();
        var watchdog = new FakeWatchdog
        {
            StopEntered = stopEntered,
            ReleaseStop = releaseStop,
        };
        using var viewModel = new SaveManagerViewModel(
            new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots())),
            "user-data-tmp",
            watchdog);
        viewModel.Activate();
        viewModel.IsWatchdogEnabled = true;
        Assert.True(SpinWait.SpinUntil(() => watchdog.StartCount == 1, TimeSpan.FromSeconds(2)));

        viewModel.IsWatchdogEnabled = false;
        Assert.True(stopEntered.Wait(TimeSpan.FromSeconds(2)));
        viewModel.IsWatchdogEnabled = true;
        releaseStop.Set();

        Assert.True(SpinWait.SpinUntil(() => watchdog.StartCount >= 2, TimeSpan.FromSeconds(2)));
        Assert.True(watchdog.IsRunning);
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
        private int _startCount;
        private int _stopCount;

        public int StartCount => Volatile.Read(ref _startCount);
        public int StopCount => Volatile.Read(ref _stopCount);
        public bool IsRunning => StartCount > StopCount;

        public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);

        public int SuppressedSlot { get; private set; } = -1;

        public ManualResetEventSlim? StopEntered { get; init; }

        public ManualResetEventSlim? ReleaseStop { get; init; }

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

        public void Start() => Interlocked.Increment(ref _startCount);

        public void StopWatch()
        {
            StopEntered?.Set();
            _ = ReleaseStop?.Wait(TimeSpan.FromSeconds(10));
            Interlocked.Increment(ref _stopCount);
        }

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

    private sealed class WarningSaveGameManager : ISaveGameManager
    {
        public SaveGamesSnapshot Inspect() =>
            new(DateTimeOffset.UnixEpoch, "user-data", []);

        public SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual") =>
            new(false, "not used");

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) =>
            new(
                true,
                "Loaded with a timestamp warning.",
                CommitState: SaveOperationCommitState.CommittedWithWarning);

        public SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId) =>
            new(false, "not used");
    }

    private sealed class SafetyCheckpointWarningManager : ISaveGameManager
    {
        public int InspectCount { get; private set; }

        public SaveGamesSnapshot Inspect()
        {
            InspectCount++;
            return new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, "user-data", Slots());
        }

        public SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual") =>
            new(false, "not used");

        public SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId) => new(
            false,
            "Restore stopped; safety checkpoint created.",
            "safety-checkpoint",
            SaveOperationCommitState.CommittedWithWarning);

        public SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId) =>
            new(false, "not used");
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
            Assert.True(File.Exists(settingsPath));
            ToolSettings settings = System.Text.Json.JsonSerializer.Deserialize<ToolSettings>(
                File.ReadAllBytes(settingsPath))!;
            Assert.Equal(10, settings.WatchdogIntervalMinutes);
            Assert.True(settings.KeepRunningInTrayWhenClosing);
        }
        finally
        {
            Directory.Delete(userData, recursive: true);
        }
    }

    [Fact]
    public void TrayClosePreferenceIsSavedAndLoaded()
    {
        string userData = TempUserData();
        Directory.CreateDirectory(userData);
        try
        {
            using (var viewModel = new SaveManagerViewModel(
                new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, userData, Slots())),
                userData,
                watchdog: null))
            {
                Assert.True(viewModel.KeepRunningInTrayWhenClosing);
                viewModel.KeepRunningInTrayWhenClosing = false;
            }

            using var reloaded = new SaveManagerViewModel(
                new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, userData, Slots())),
                userData,
                watchdog: null);

            Assert.False(reloaded.KeepRunningInTrayWhenClosing);
        }
        finally
        {
            Directory.Delete(userData, recursive: true);
        }
    }

    [Fact]
    public void NullToolSettingsAreReportedAndPreserved()
    {
        string userData = TempUserData();
        Directory.CreateDirectory(userData);
        string settingsPath = Path.Combine(userData, "AncestorsEnhanced_ToolSettings.json");
        File.WriteAllText(settingsPath, "null");
        try
        {
            using var viewModel = new SaveManagerViewModel(
                new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, userData, Slots())),
                userData,
                watchdog: null);

            Assert.True(viewModel.HasToolSettingsWarning);
            Assert.False(viewModel.CanConfigureAutoBackup);
            Assert.Equal("null", File.ReadAllText(settingsPath));
        }
        finally
        {
            Directory.Delete(userData, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedToolSettingsArePreservedUntilExplicitReset()
    {
        string userData = TempUserData();
        Directory.CreateDirectory(userData);
        string settingsPath = Path.Combine(userData, "AncestorsEnhanced_ToolSettings.json");
        const string InvalidSettings = "{ definitely not valid json";
        File.WriteAllText(settingsPath, InvalidSettings);
        var viewModel = new SaveManagerViewModel(
            new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, userData, Slots())),
            userData,
            watchdog: null);
        try
        {
            Assert.True(viewModel.HasToolSettingsWarning);
            Assert.False(viewModel.CanConfigureAutoBackup);
            Assert.True(viewModel.CanResetToolSettings);
            Assert.Equal(InvalidSettings, File.ReadAllText(settingsPath));

            viewModel.ResetToolSettingsCommand.Execute(null);
            await WaitUntilAsync(
                () => File.Exists(settingsPath) && !viewModel.HasToolSettingsWarning,
                TimeSpan.FromSeconds(5),
                "Reset tool settings were not persisted.");

            string archived = Assert.Single(Directory.EnumerateFiles(
                userData,
                "AncestorsEnhanced_ToolSettings.json.invalid-*.bak"));
            Assert.Equal(InvalidSettings, File.ReadAllText(archived));
            using System.Text.Json.JsonDocument _ = System.Text.Json.JsonDocument.Parse(
                File.ReadAllBytes(settingsPath));
            Assert.True(viewModel.CanConfigureAutoBackup);
        }
        finally
        {
            viewModel.Dispose();
            Directory.Delete(userData, recursive: true);
        }
    }

    [Fact]
    public async Task QueuedGameProcessRefreshDoesNotOverlapASlowProbe()
    {
        string userData = TempUserData();
        Directory.CreateDirectory(userData);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        int probes = 0;
        var viewModel = new SaveManagerViewModel(
            new FakeSaveGameManager(new SaveGamesSnapshot(DateTimeOffset.UnixEpoch, userData, Slots())),
            userData,
            watchdog: null,
            dispatchToUi: action => action(),
            mutationGate: null,
            gameRunningProbe: () =>
            {
                Interlocked.Increment(ref probes);
                started.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                completed.Set();
                return true;
            });
        try
        {
            viewModel.QueueGameRunningStateRefresh();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            viewModel.QueueGameRunningStateRefresh();

            await Task.Delay(100);
            Assert.Equal(1, Volatile.Read(ref probes));

            release.Set();
            Assert.True(await Task.Run(() => completed.Wait(TimeSpan.FromSeconds(5))));
            await WaitUntilAsync(() => viewModel.IsGameRunning, TimeSpan.FromSeconds(5), "Game state was not refreshed.");
        }
        finally
        {
            release.Set();
            viewModel.Dispose();
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
