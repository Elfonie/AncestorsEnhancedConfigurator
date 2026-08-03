using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

public sealed class SafeSaveGameManagerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ancestors-enhanced-manager-tests-{Guid.NewGuid():N}");

    [Fact]
    public void InspectListsEverySlotAndExistingCheckpoints()
    {
        string userData = CreateUserDataWithSave(0, [1, 2, 3]);
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);

        SaveGamesSnapshot snapshot = manager.Inspect();

        Assert.Equal(5, snapshot.Slots.Count);
        SaveGameSlotSnapshot slot0 = snapshot.Slots.Single(slot => slot.SlotNumber == "0");
        Assert.True(slot0.Exists);
        Assert.Equal(3, slot0.SizeBytes);
        SaveGameSlotSnapshot slot1 = snapshot.Slots.Single(slot => slot.SlotNumber == "1");
        Assert.False(slot1.Exists);
    }

    [Fact]
    public void CreateCheckpointBacksUpTheCurrentSave()
    {
        string userData = CreateUserDataWithSave(0, [1, 2, 3, 4]);
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
        string userData = CreateUserDataWithSave(0, [7, 7, 7]);
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);
        manager.CreateCheckpoint("0");

        File.WriteAllBytes(SaveGamePaths.GetSlotPath(userData, 0), [9, 9, 9, 9]);

        SaveGameOperationResult loaded = manager.LoadCheckpoint("0", manager.Inspect()
            .Slots.Single(slot => slot.SlotNumber == "0").Checkpoints[0].Id);

        Assert.True(loaded.Succeeded, loaded.Message);
        Assert.Equal([7, 7, 7], File.ReadAllBytes(SaveGamePaths.GetSlotPath(userData, 0)));

        SaveGamesSnapshot after = manager.Inspect();
        SaveGameSlotSnapshot slot0 = after.Slots.Single(slot => slot.SlotNumber == "0");
        Assert.Equal(2, slot0.Checkpoints.Count);
    }

    [Fact]
    public void CreateAndLoadRefuseWhileTheGameIsRunning()
    {
        string userData = CreateUserDataWithSave(0, [1, 2, 3]);
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: true);

        Assert.False(manager.CreateCheckpoint("0").Succeeded);
        Assert.False(manager.LoadCheckpoint("0", "anything").Succeeded);
    }

    [Fact]
    public void CreateCheckpointRefusesWhenThereIsNoSaveFile()
    {
        string userData = CreateUserDataWithSave(0, [1, 2, 3]);
        File.Delete(SaveGamePaths.GetSlotPath(userData, 0));
        SafeSaveGameManager manager = CreateManager(userData, gameRunning: false);

        SaveGameOperationResult result = manager.CreateCheckpoint("0");

        Assert.False(result.Succeeded);
        Assert.Contains("no save", result.Message, StringComparison.OrdinalIgnoreCase);
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
