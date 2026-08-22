using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

public sealed class SaveGameCheckpointStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ancestors-enhanced-save-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CreateStoresContentAndManifestValidatesSha256()
    {
        string userData = CreateUserData();
        var store = new SaveGameCheckpointStore(() => FixedTime, maxCheckpointsPerSlot: 50);
        Directory.CreateDirectory(Path.Combine(userData, "SaveGames", "Savegame0.sav.mod"));
        byte[] content = TestSaveFactory.Create(1, 2, 3, 4, 5);
        File.WriteAllBytes(Path.Combine(userData, "SaveGames", "Savegame0.sav"), content);

        string checkpointId = store.Create(userData, 0, content);

        Assert.NotEmpty(checkpointId);
        byte[] stored = SaveGameCheckpointStore.Read(userData, 0, checkpointId);
        Assert.Equal(content, stored);
    }

    [Fact]
    public void CreateEnforcesTheCheckpointCapByDeletingTheOldest()
    {
        string userData = CreateUserData();
        byte[] content = TestSaveFactory.Create(1, 2, 3);
        File.WriteAllBytes(Path.Combine(userData, "SaveGames", "Savegame0.sav"), content);
        DateTimeOffset first = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset second = new(2026, 8, 1, 12, 0, 1, TimeSpan.Zero);
        DateTimeOffset third = new(2026, 8, 1, 12, 0, 2, TimeSpan.Zero);
        var store = new SaveGameCheckpointStore(() => first, maxCheckpointsPerSlot: 2);

        string firstId = store.Create(userData, 0, content);
        var secondStore = new SaveGameCheckpointStore(() => second, maxCheckpointsPerSlot: 2);
        string secondId = secondStore.Create(userData, 0, content);
        var thirdStore = new SaveGameCheckpointStore(() => third, maxCheckpointsPerSlot: 2);
        string thirdId = thirdStore.Create(userData, 0, content);

        IReadOnlyList<SaveGameCheckpoint> remaining = SaveGameCheckpointStore.ListCheckpoints(userData, 0);
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, checkpoint => checkpoint.Id == firstId);
        Assert.Contains(remaining, checkpoint => checkpoint.Id == secondId);
        Assert.Contains(remaining, checkpoint => checkpoint.Id == thirdId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RetentionNeverDeletesTheCheckpointThatWasJustPublished(int cap)
    {
        string userData = CreateUserData();
        string[] ids = cap == 1
            ? [CheckpointId('f'), CheckpointId('0')]
            : [CheckpointId('e'), CheckpointId('f'), CheckpointId('0')];
        var pendingIds = new Queue<string>(ids);
        var store = new SaveGameCheckpointStore(
            () => FixedTime,
            cap,
            _ => pendingIds.Dequeue());

        string publishedId = string.Empty;
        foreach (int value in Enumerable.Range(1, ids.Length))
        {
            publishedId = store.Create(userData, 0, TestSaveFactory.Create((byte)value));
        }

        IReadOnlyList<SaveGameCheckpoint> remaining =
            SaveGameCheckpointStore.ListCheckpoints(userData, 0);
        Assert.Equal(cap, remaining.Count);
        Assert.Contains(remaining, checkpoint => checkpoint.Id == publishedId);
        Assert.True(File.Exists(SaveGamePaths.GetCheckpointPath(userData, 0, publishedId)));
    }

    [Fact]
    public void ReadRefusesACorruptedStoredFile()
    {
        string userData = CreateUserData();
        byte[] content = TestSaveFactory.Create(9, 9, 9);
        var store = new SaveGameCheckpointStore(() => FixedTime, maxCheckpointsPerSlot: 50);

        string checkpointId = store.Create(userData, 0, content);
        string checkpointPath = SaveGamePaths.GetCheckpointPath(userData, 0, checkpointId);
        File.WriteAllBytes(checkpointPath, [1, 1, 1]);

        Assert.Throws<InvalidDataException>(() => SaveGameCheckpointStore.Read(userData, 0, checkpointId));
    }

    [Fact]
    public void CreateLeavesNoTemporaryDirectoryBehind()
    {
        string userData = CreateUserData();
        var store = new SaveGameCheckpointStore(() => FixedTime, maxCheckpointsPerSlot: 50);

        string checkpointId = store.Create(userData, 0, TestSaveFactory.Create(1, 2, 3));

        string slotRoot = SaveGamePaths.GetSlotRoot(userData, 0);
        Assert.DoesNotContain(Directory.EnumerateDirectories(slotRoot), dir =>
            Path.GetFileName(dir).EndsWith(".tmp", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(slotRoot, checkpointId)));
    }

    [Fact]
    public void OrphanedTemporaryDirectoryDoesNotBlockNewCheckpoints()
    {
        string userData = CreateUserData();
        string slotRoot = SaveGamePaths.GetSlotRoot(userData, 0);
        Directory.CreateDirectory(Path.Combine(slotRoot, ".orphaned.tmp"));

        var store = new SaveGameCheckpointStore(() => FixedTime, maxCheckpointsPerSlot: 50);
        string checkpointId = store.Create(userData, 0, TestSaveFactory.Create(1, 2, 3));

        Assert.Single(SaveGameCheckpointStore.ListCheckpoints(userData, 0));
        Assert.True(File.Exists(SaveGamePaths.GetCheckpointPath(userData, 0, checkpointId)));
    }

    [Fact]
    public void TemporaryDirectoryWithAValidLookingManifestIsNeverListed()
    {
        string userData = CreateUserData();
        string slotRoot = SaveGamePaths.GetSlotRoot(userData, 0);
        string temp = Path.Combine(slotRoot, ".20260801-120000-000-aaaaaaaaaaaa.tmp");
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "checkpoint.json"),
            "{\"createdAtUtc\":\"2026-08-01T12:00:00+00:00\",\"sizeBytes\":3,\"sha256\":\"00\",\"origin\":\"Manual\"}");

        Assert.Empty(SaveGameCheckpointStore.ListCheckpoints(userData, 0));
    }

    [Fact]
    public void BrokenCheckpointCandidateDoesNotHideValidCheckpoints()
    {
        string userData = CreateUserData();
        var store = new SaveGameCheckpointStore(() => FixedTime, maxCheckpointsPerSlot: 50);
        string valid = store.Create(userData, 0, TestSaveFactory.Create(1, 2, 3));
        string broken = Path.Combine(
            SaveGamePaths.GetSlotRoot(userData, 0), "20260801-120001-000-bbbbbbbbbbbb");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "checkpoint.json"), "{ not-json");

        IReadOnlyList<SaveGameCheckpoint> checkpoints = SaveGameCheckpointStore.ListCheckpoints(userData, 0);

        SaveGameCheckpoint only = Assert.Single(checkpoints);
        Assert.Equal(valid, only.Id);
    }

    [Fact]
    public void RetentionNeverDeletesAnUnverifiableCheckpointDirectory()
    {
        string userData = CreateUserData();
        string slotRoot = SaveGamePaths.GetSlotRoot(userData, 0);
        string broken = Path.Combine(
            slotRoot, "20260801-115959-000-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "checkpoint.json"), "{ not-json");
        var store = new SaveGameCheckpointStore(() => FixedTime, maxCheckpointsPerSlot: 1);

        _ = store.Create(userData, 0, TestSaveFactory.Create(1, 2, 3));

        Assert.True(Directory.Exists(broken));
        Assert.Single(SaveGameCheckpointStore.ListCheckpoints(userData, 0));
    }

    [Fact]
    public void ListingUsesMetadataWhileRestoreStillValidatesTheStoredSave()
    {
        string userData = CreateUserData();
        byte[] content = TestSaveFactory.Create(4, 5, 6);
        var store = new SaveGameCheckpointStore(() => FixedTime, maxCheckpointsPerSlot: 50);
        string checkpointId = store.Create(userData, 0, content);
        string checkpointPath = SaveGamePaths.GetCheckpointPath(userData, 0, checkpointId);
        byte[] corrupted = File.ReadAllBytes(checkpointPath);
        corrupted[^1] ^= 0x01;
        File.WriteAllBytes(checkpointPath, corrupted);

        SaveGameCheckpoint listed = Assert.Single(
            SaveGameCheckpointStore.ListCheckpoints(userData, 0));

        Assert.Equal(checkpointId, listed.Id);
        Assert.Throws<InvalidDataException>(() =>
            SaveGameCheckpointStore.Read(userData, 0, checkpointId));
    }

    [Fact]
    public void CheckpointDoesNotRequireTheCheatSchemaParser()
    {
        string userData = CreateUserData();
        byte[] content = SnappyBlockCodec.EncodeLiteral([1, 2, 3, 4, 5]);
        Assert.Throws<InvalidDataException>(() =>
            SaveGameSchemaAnalyzer.Parse(SnappyBlockCodec.Decode(content)));
        var store = new SaveGameCheckpointStore(() => FixedTime, maxCheckpointsPerSlot: 50);

        string checkpointId = store.Create(userData, 0, content);

        Assert.Equal(content, SaveGameCheckpointStore.Read(userData, 0, checkpointId));
    }

    private static readonly DateTimeOffset FixedTime = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private static string CheckpointId(char suffix) =>
        $"20260801-120000-000-{new string(suffix, 32)}";

    private string CreateUserData()
    {
        string userData = Path.Combine(_temporaryDirectory, "Saved");
        Directory.CreateDirectory(Path.Combine(userData, "SaveGames"));
        return userData;
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
