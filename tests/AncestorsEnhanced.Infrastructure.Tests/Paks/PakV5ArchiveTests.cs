using AncestorsEnhanced.Infrastructure.Paks;

namespace AncestorsEnhanced.Infrastructure.Tests.Paks;

public sealed class PakV5ArchiveTests
{
    [Fact]
    public void SingleFileArchiveRoundTrips()
    {
        const string path = "Ancestors/Content/Test.uasset";
        byte[] content = Enumerable.Range(0, 512).Select(value => (byte)value).ToArray();

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
}
