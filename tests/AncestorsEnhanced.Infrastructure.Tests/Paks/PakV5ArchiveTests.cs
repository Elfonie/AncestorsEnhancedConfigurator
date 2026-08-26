using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
    public void RelativeCompressedBlocksRoundTrip()
    {
        const string path = "Ancestors/Content/Test.uasset";
        byte[] content = [.. Enumerable.Range(0, 150_000).Select(value => (byte)(value * 31))];

        byte[] pak = BuildCompressedPak(path, content, 64 * 1024);

        Assert.Equal(content, PakV5Archive.ReadFile(pak, path));
    }

    [Fact]
    public void CompressedArchivePathLookupIsCaseInsensitive()
    {
        byte[] pak = BuildCompressedPak("Ancestors/Content/Test.uasset", [1, 2, 3, 4], 4);

        Assert.True(PakV5Archive.ContainsFile(pak, "ancestors/content/test.uasset"));
        Assert.Equal([1, 2, 3, 4], PakV5Archive.ReadFile(pak, "ANCESTORS/CONTENT/TEST.UASSET"));
    }

    [Fact]
    public void CompressedBlockCannotEscapeTheDeclaredPayload()
    {
        byte[] pak = BuildCompressedPak("Test.uasset", [.. Enumerable.Range(0, 4096).Select(value => (byte)value)], 4096, 1);

        Assert.Throws<InvalidDataException>(() => PakV5Archive.ReadFile(pak, "Test.uasset"));
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

    private static byte[] BuildCompressedPak(
        string path,
        byte[] content,
        int blockSize,
        int firstBlockOffsetAdjustment = 0)
    {
        List<byte[]> compressedBlocks = [];
        for (int offset = 0; offset < content.Length; offset += blockSize)
        {
            int length = Math.Min(blockSize, content.Length - offset);
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(content, offset, length);
            }

            compressedBlocks.Add(compressed.ToArray());
        }

        byte[] compressedPayload = [.. compressedBlocks.SelectMany(block => block)];
        long headerSize = 57L + (compressedBlocks.Count * 16L);
        long nextBlockStart = headerSize + firstBlockOffsetAdjustment;
        var blocks = new List<(long Start, long End)>();
        foreach (byte[] block in compressedBlocks)
        {
            blocks.Add((nextBlockStart, nextBlockStart + block.Length));
            nextBlockStart += block.Length;
        }

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        WriteEntry(writer, 0, compressedPayload.Length, content.Length, content, blocks, (uint)blockSize);
        writer.Write(compressedPayload);
        long indexOffset = output.Position;

        using var index = new MemoryStream();
        using (var indexWriter = new BinaryWriter(index, Encoding.UTF8, leaveOpen: true))
        {
            WriteString(indexWriter, "../../../");
            indexWriter.Write(1);
            WriteString(indexWriter, path);
            WriteEntry(indexWriter, 0, compressedPayload.Length, content.Length, content, blocks, (uint)blockSize);
        }

        byte[] indexBytes = index.ToArray();
        writer.Write(indexBytes);
        writer.Write((byte)0);
        writer.Write(0x5A6F12E1u);
        writer.Write(5);
        writer.Write(indexOffset);
        writer.Write((long)indexBytes.Length);
        writer.Write(SHA1.HashData(indexBytes));
        return output.ToArray();
    }

    private static void WriteEntry(
        BinaryWriter writer,
        long offset,
        long compressedSize,
        long uncompressedSize,
        byte[] compressedPayload,
        List<(long Start, long End)> blocks,
        uint blockSize)
    {
        writer.Write(offset);
        writer.Write(compressedSize);
        writer.Write(uncompressedSize);
        writer.Write(1);
        writer.Write(SHA1.HashData(compressedPayload));
        writer.Write(blocks.Count);
        foreach ((long start, long end) in blocks)
        {
            writer.Write(start);
            writer.Write(end);
        }

        writer.Write((byte)0);
        writer.Write(blockSize);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length + 1);
        writer.Write(bytes);
        writer.Write((byte)0);
    }
}
#pragma warning restore CA5350
