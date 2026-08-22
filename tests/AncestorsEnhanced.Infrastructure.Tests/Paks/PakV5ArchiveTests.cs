using System.Buffers.Binary;
using System.Security.Cryptography;
using AncestorsEnhanced.Infrastructure.Paks;

#pragma warning disable CA5350

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

    [Fact]
    public void DuplicateIndexPathsAreRejected()
    {
        byte[] pak = PakV5Archive.BuildFiles(
        [
            ("A.uasset", new byte[] { 1 }),
            ("B.uasset", new byte[] { 2 }),
        ]);
        long indexOffset = BitConverter.ToInt64(pak, pak.Length - 36);
        long indexSize = BitConverter.ToInt64(pak, pak.Length - 28);
        byte[] needle = System.Text.Encoding.UTF8.GetBytes("B.uasset\0");
        int secondName = pak.AsSpan((int)indexOffset, (int)indexSize).IndexOf(needle);
        Assert.True(secondName >= 0);
        pak[(int)indexOffset + secondName] = (byte)'A';
        SHA1.HashData(pak.AsSpan((int)indexOffset, (int)indexSize)).CopyTo(pak.AsSpan(pak.Length - 20));

        Assert.Throws<InvalidDataException>(() => PakV5Archive.ContainsFile(pak, "A.uasset"));
    }

    [Fact]
    public void EntryPayloadCannotOverlapTheIndex()
    {
        byte[] pak = PakV5Archive.BuildSingleFile("A.uasset", [1, 2, 3]);
        long indexOffset = BitConverter.ToInt64(pak, pak.Length - 36);
        long indexSize = BitConverter.ToInt64(pak, pak.Length - 28);
        byte[] needle = System.Text.Encoding.UTF8.GetBytes("A.uasset\0");
        int name = pak.AsSpan((int)indexOffset, (int)indexSize).IndexOf(needle);
        Assert.True(name >= 0);
        int indexedOffset = (int)indexOffset + name + needle.Length;
        BinaryPrimitives.WriteInt64LittleEndian(pak.AsSpan(indexedOffset, 8), indexOffset - 1);
        SHA1.HashData(pak.AsSpan((int)indexOffset, (int)indexSize)).CopyTo(pak.AsSpan(pak.Length - 20));

        Assert.Throws<InvalidDataException>(() => PakV5Archive.ReadFile(pak, "A.uasset"));
    }
}
#pragma warning restore CA5350
