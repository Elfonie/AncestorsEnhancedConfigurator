using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

/// <summary>Concurrent checkpoint store mutations must be serialized and never corrupt the store.</summary>
public sealed class CheckpointStoreConcurrencyTests
{
    [Fact]
    public async Task ParallelCheckpointCreatesNeverCorruptTheStore()
    {
        string userData = Path.Combine(Path.GetTempPath(), $"ae-f009-{Guid.NewGuid():N}");
        try
        {
            string savePath = SaveGamePaths.GetSlotPath(userData, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            File.WriteAllBytes(savePath, TestSaveFactory.Create(1, 2, 3, 4, 5));

            var manager = new SafeSaveGameManager(
                userData,
                new SaveGameManagerOptions(MaxCheckpointsPerSlot: 200));

            const int parallelCreates = 40;
            SaveGameOperationResult[] results = await Task.WhenAll(
                Enumerable.Range(0, parallelCreates)
                    .Select(_ => Task.Run(() => manager.CreateCheckpoint("0"))));

            // All creates are serialized by the global mutation gate, so the
            // total checkpoint count matches the successes and every checkpoint is valid.
            Assert.All(results, result => Assert.True(result.Succeeded, result.Message));

            IReadOnlyList<SaveGameCheckpoint> checkpoints =
                SaveGameCheckpointStore.ListCheckpoints(userData, 0);
            // Identical saves are de-duplicated, so the count may be lower than the number of.
            // parallel calls (all of which are serialized and all succeeded).
            Assert.InRange(checkpoints.Count, 1, parallelCreates);

            foreach (SaveGameCheckpoint checkpoint in checkpoints)
            {
                // Read re-validates the stored save against the manifest; a corrupted or
                // raced write would throw here.
                _ = SaveGameCheckpointStore.Read(userData, 0, checkpoint.Id);
            }
        }
        finally
        {
            if (Directory.Exists(userData))
            {
                Directory.Delete(userData, recursive: true);
            }
        }
    }
}
