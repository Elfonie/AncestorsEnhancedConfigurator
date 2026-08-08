using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

/// <summary>F170 - invalid slot numbers are rejected at the store boundary.</summary>
public sealed class F170InvalidSlotTests
{
    [Theory]
    [InlineData("-1")]
    [InlineData("5")]
    [InlineData("999")]
    [InlineData("abc")]
    public void InvalidSlotNumbersAreRejected(string slotNumber)
    {
        string userData = Path.Combine(Path.GetTempPath(), $"ae-f170-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(userData);
            var manager = new SafeSaveGameManager(userData, new SaveGameManagerOptions());

            Assert.Throws<InvalidOperationException>(() => manager.CreateCheckpoint(slotNumber));
            Assert.Throws<InvalidOperationException>(() => manager.DeleteCheckpoint(slotNumber, "20260101-000000-000-0000000000000000"));
            Assert.Throws<InvalidOperationException>(() => manager.LoadCheckpoint(slotNumber, "20260101-000000-000-0000000000000000"));
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
