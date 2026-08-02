using AncestorsEnhanced.Infrastructure.Paks;

namespace AncestorsEnhanced.Infrastructure.Tests.Paks;

public sealed class PakV5ArchiveTests
{
    [Fact]
    public void SingleFileArchiveRoundTrips()
    {
        const string path = "Ancestors/Content/Test.uasset";
        byte[] content = [.. Enumerable.Range(0, 512).Select(value => (byte)value)];

        byte[] pak = PakV5Archive.BuildSingleFile(path, content);

        Assert.True(PakV5Archive.ContainsFile(pak, path));
        Assert.Equal(content, PakV5Archive.ReadFile(pak, path));
    }

    [Fact]
    public void DamagedIndexIsRejected()
    {
        byte[] pak = PakV5Archive.BuildSingleFile("Test.uasset", [1, 2, 3]);
        pak[^46] ^= 1;

        Assert.Throws<InvalidDataException>(() => PakV5Archive.ReadFile(pak, "Test.uasset"));
    }

    [Fact]
    public void IndexIdentityRejectsAClaimedHashForDamagedIndexData()
    {
        byte[] pak = PakV5Archive.BuildSingleFile("Test.uasset", [1, 2, 3]);
        long indexOffset = BitConverter.ToInt64(pak, pak.Length - 36);
        pak[indexOffset] ^= 1;
        string path = Path.Combine(Path.GetTempPath(), $"aec-pak-{Guid.NewGuid():N}.pak");
        try
        {
            File.WriteAllBytes(path, pak);

            Assert.Throws<InvalidDataException>(() => PakV5Archive.ReadIndexIdentity(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
