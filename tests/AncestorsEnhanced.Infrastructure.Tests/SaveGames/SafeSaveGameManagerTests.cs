using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

public sealed class SafeSaveGameManagerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ancestors-enhanced-manager-tests-{Guid.NewGuid():N}");

    [Fact]
    public void InspectListsEverySlotAndExistingCheckpoints()
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(1, 2, 3));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);

        SaveGamesSnapshot snapshot = manager.Inspect();

        Assert.Equal(5, snapshot.Slots.Count);
        SaveGameSlotSnapshot slot0 = snapshot.Slots.Single(slot => slot.SlotNumber == "0");
        Assert.True(slot0.Exists);
        Assert.Equal(TestSaveFactory.Create(1, 2, 3).Length, slot0.SizeBytes);
        SaveGameSlotSnapshot slot1 = snapshot.Slots.Single(slot => slot.SlotNumber == "1");
        Assert.False(slot1.Exists);
    }

    [Fact]
    public void CreateCheckpointBacksUpTheCurrentSave()
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(1, 2, 3, 4));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);

        SaveGameOperationResult result = manager.CreateCheckpoint("0");

        Assert.True(result.Succeeded, result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.CreatedCheckpointId));
        SaveGamesSnapshot snapshot = manager.Inspect();
        SaveGameSlotSnapshot slot0 = snapshot.Slots.Single(slot => slot.SlotNumber == "0");
        Assert.Single(slot0.Checkpoints);
    }

    [Fact]
    public void LoadCheckpointRestoresASavedStateAndMakesASafetyBackup()
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(7, 7, 7));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);
        manager.CreateCheckpoint("0");

        File.WriteAllBytes(SaveGamePaths.GetSlotPath(userData, 0), TestSaveFactory.Create(9, 9, 9, 9));

        SaveGameOperationResult loaded = manager.LoadCheckpoint("0", manager.Inspect()
            .Slots.Single(slot => slot.SlotNumber == "0").Checkpoints[0].Id);

        Assert.True(loaded.Succeeded, loaded.Message);
        Assert.Equal(TestSaveFactory.Create(7, 7, 7), File.ReadAllBytes(SaveGamePaths.GetSlotPath(userData, 0)));

        SaveGamesSnapshot after = manager.Inspect();
        SaveGameSlotSnapshot slot0 = after.Slots.Single(slot => slot.SlotNumber == "0");
        Assert.Equal(2, slot0.Checkpoints.Count);
    }

    [Fact]
    public void CreateCheckpointWorksWhileTheGameIsRunning()
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(1, 2, 3));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: true);

        SaveGameOperationResult result = manager.CreateCheckpoint("0");

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(manager.Inspect().Slots.Single(slot => slot.SlotNumber == "0").Checkpoints);
    }

    [Fact]
    public void LoadRefusesWhileTheGameIsRunning()
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(1, 2, 3));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: true);

        Assert.False(manager.LoadCheckpoint("0", "anything").Succeeded);
    }

    [Fact]
    public void CreateCheckpointRefusesWhenThereIsNoSaveFile()
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(1, 2, 3));
        File.Delete(SaveGamePaths.GetSlotPath(userData, 0));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);

        SaveGameOperationResult result = manager.CreateCheckpoint("0");

        Assert.False(result.Succeeded);
        Assert.Contains("no save", result.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void IdenticalCheckpointIsSkippedButChangedContentIsBackedUp()
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(1, 2, 3, 4));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);

        SaveGameOperationResult first = manager.CreateCheckpoint("0");
        Assert.True(first.Succeeded);
        Assert.Single(manager.Inspect().Slots.Single(slot => slot.SlotNumber == "0").Checkpoints);

        SaveGameOperationResult duplicate = manager.CreateCheckpoint("0");
        Assert.True(duplicate.Succeeded);
        Assert.Contains("unchanged", duplicate.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(manager.Inspect().Slots.Single(slot => slot.SlotNumber == "0").Checkpoints);

        File.WriteAllBytes(SaveGamePaths.GetSlotPath(userData, 0), TestSaveFactory.Create(5, 5, 5, 5, 5));
        SaveGameOperationResult changed = manager.CreateCheckpoint("0");
        Assert.True(changed.Succeeded);
        Assert.Equal(2, manager.Inspect().Slots.Single(slot => slot.SlotNumber == "0").Checkpoints.Count);
    }


    [Fact]
    public void LoadDoesNotAddASafetyBackupWhenCurrentStateMatchesTheCheckpoint()
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(7, 7, 7));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);
        manager.CreateCheckpoint("0");
        string checkpointId = manager.Inspect()
            .Slots.Single(slot => slot.SlotNumber == "0").Checkpoints[0].Id;

        SaveGameOperationResult loaded = manager.LoadCheckpoint("0", checkpointId);

        Assert.True(loaded.Succeeded, loaded.Message);
        Assert.Equal(TestSaveFactory.Create(7, 7, 7), File.ReadAllBytes(SaveGamePaths.GetSlotPath(userData, 0)));
        Assert.Single(manager.Inspect().Slots.Single(slot => slot.SlotNumber == "0").Checkpoints);
    }

    [Fact]
    public void LoadRefusesToOverwriteALiveSaveChangedAfterPreRestore()
    {
        byte[] checkpointContent = TestSaveFactory.Create(1, 2, 3);
        byte[] liveContent = TestSaveFactory.Create(4, 5, 6);
        byte[] foreignContent = TestSaveFactory.Create(7, 8, 9);
        string userData = CreateUserDataWithSave(0, checkpointContent);
        var creator = CreateManager(userData, gameRunning: false);
        string checkpointId = creator.CreateCheckpoint("0").CreatedCheckpointId!;
        string slotPath = SaveGamePaths.GetSlotPath(userData, 0);
        File.WriteAllBytes(slotPath, liveContent);
        var manager = new SafeSaveGameManager(
            userData,
            () => DateTimeOffset.UtcNow,
            () => false,
            new SaveGameManagerOptions(),
            beforeRestoreCommit: () => File.WriteAllBytes(slotPath, foreignContent));

        SaveGameOperationResult result = manager.LoadCheckpoint("0", checkpointId);

        Assert.False(result.Succeeded);
        Assert.Contains("changed after", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(foreignContent, File.ReadAllBytes(slotPath));
    }

    [Fact]
    public void DamagedMatchingCheckpointDoesNotBlockANewBackup()
    {
        byte[] content = TestSaveFactory.Create(1, 2, 3);
        string userData = CreateUserDataWithSave(0, content);
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);
        string damaged = manager.CreateCheckpoint("0").CreatedCheckpointId!;
        File.WriteAllBytes(SaveGamePaths.GetCheckpointPath(userData, 0, damaged), [0, 0, 0]);

        SaveGameOperationResult result = manager.CreateCheckpoint("0");

        Assert.True(result.Succeeded, result.Message);
        Assert.NotEqual(damaged, result.CreatedCheckpointId);
    }


    [Fact]
    public void DeleteCheckpointRemovesTheStoredCheckpoint()
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(1, 2, 3));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);
        string checkpointId = manager.CreateCheckpoint("0").CreatedCheckpointId!;

        SaveGameOperationResult result = manager.DeleteCheckpoint("0", checkpointId);

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(manager.Inspect().Slots.Single(slot => slot.SlotNumber == "0").Checkpoints);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../other")]
    [InlineData(@"..\other")]
    [InlineData("/absolute")]
    [InlineData("C:\\absolute")]
    [InlineData("id.with.dot")]
    [InlineData("id/with/slash")]
    [InlineData("id\\with\\backslash")]
    [InlineData("id:with:colon")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void InvalidCheckpointIdsAreRejectedForLoad(string checkpointId)
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(1, 2, 3));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);

        SaveGameOperationResult result = manager.LoadCheckpoint("0", checkpointId);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../other")]
    [InlineData(@"..\other")]
    [InlineData("/absolute")]
    [InlineData("C:\\absolute")]
    [InlineData("id.with.dot")]
    [InlineData("id/with/slash")]
    [InlineData("id\\with\\backslash")]
    [InlineData("id:with:colon")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void InvalidCheckpointIdsNeverDeleteAnything(string checkpointId)
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(1, 2, 3));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);
        string validId = manager.CreateCheckpoint("0").CreatedCheckpointId!;
        Assert.Single(manager.Inspect().Slots.Single(slot => slot.SlotNumber == "0").Checkpoints);

        SaveGameOperationResult result = manager.DeleteCheckpoint("0", checkpointId);

        Assert.False(result.Succeeded);
        Assert.Single(manager.Inspect().Slots.Single(slot => slot.SlotNumber == "0").Checkpoints);
        Assert.True(Directory.Exists(SaveGamePaths.GetSlotRoot(userData, 0)));
        Assert.True(File.Exists(SaveGamePaths.GetCheckpointPath(userData, 0, validId)));
    }

    [Fact]
    public void GeneratedCheckpointIdsPassStrictValidation()
    {
        string userData = CreateUserDataWithSave(0, TestSaveFactory.Create(1, 2, 3));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);

        SaveGameOperationResult result = manager.CreateCheckpoint("0");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.CreatedCheckpointId);
        SaveGamePaths.ValidateCheckpointId(result.CreatedCheckpointId);
        Assert.Equal(52, result.CreatedCheckpointId.Length);
    }

    private string CreateUserDataWithSave(int slotNumber, byte[] content)
    {
        string userData = Path.Combine(_temporaryDirectory, "Saved");
        Directory.CreateDirectory(Path.Combine(userData, "SaveGames"));
        File.WriteAllBytes(SaveGamePaths.GetSlotPath(userData, slotNumber), content);
        return userData;
    }

    private static SafeSaveGameManager CreateManager(string userData, bool gameRunning) =>
        new(
            userData,
            () => new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            () => gameRunning,
            new SaveGameManagerOptions(MaxCheckpointsPerSlot: 50));

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
