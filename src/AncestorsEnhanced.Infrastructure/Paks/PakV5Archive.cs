using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

#pragma warning disable CA5350

namespace AncestorsEnhanced.Infrastructure.Paks;

internal static class PakV5Archive
{
    private const uint Magic = 0x5A6F12E1;
    private const int Version = 5;
    private const int FooterSize = 45;
    private const int MaximumIndexSize = 64 * 1024 * 1024;
    private const int MaximumFileCount = 500_000;
    private const int MaximumStringLength = 4096;

    public static string ReadIndexIdentity(string path)
    {
        using FileStream stream = File.OpenRead(path);
        PakFooter footer = ReadFooter(stream);
        stream.Position = footer.IndexOffset;
        byte[] index = ReadExact(stream, footer.IndexSize);
        VerifyHash(index, footer.IndexHash, "PAK index");
        return $"PAK{Version}:{Convert.ToHexString(footer.IndexHash)}";
    }

    public static bool ContainsFile(string path, string fileName)
    {
        using FileStream stream = File.OpenRead(path);
        return FindEntry(stream, fileName) is not null;
    }

    public static bool ContainsFile(byte[] pak, string fileName)
    {
        using var stream = new MemoryStream(pak, writable: false);
        return FindEntry(stream, fileName) is not null;
    }

    public static byte[] ReadFile(string path, string fileName)
    {
        using FileStream stream = File.OpenRead(path);
        return ReadFile(stream, fileName, int.MaxValue);
    }

    public static byte[] ReadFile(string path, string fileName, int maximumSize)
    {
        using FileStream stream = File.OpenRead(path);
        return ReadFile(stream, fileName, maximumSize);
    }

    public static byte[] ReadFile(byte[] pak, string fileName)
    {
        using var stream = new MemoryStream(pak, writable: false);
        return ReadFile(stream, fileName, int.MaxValue);
    }

    public static byte[] BuildSingleFile(string fileName, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        return BuildFiles([(fileName, content)]);
    }

    internal static byte[] BuildFiles(IReadOnlyList<(string FileName, byte[] Content)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0 || files.Any(file => string.IsNullOrWhiteSpace(file.FileName)))
        {
            throw new ArgumentException("At least one named PAK entry is required.", nameof(files));
        }
        if (files.Select(file => file.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
        {
            throw new ArgumentException("PAK entry names must be unique.", nameof(files));
        }

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        List<(string Name, PakEntry Entry)> entries = [];
        foreach ((string fileName, byte[] content) in files)
        {
            ArgumentNullException.ThrowIfNull(content);
            PakEntry entry = new(
                output.Position,
                content.Length,
                content.Length,
                0,
                SHA1.HashData(content),
                [],
                false,
                0);
            entries.Add((fileName, entry));
            WriteEntry(writer, entry);
            writer.Write(content);
        }

        long indexOffset = output.Position;

        using var index = new MemoryStream();
        using (var indexWriter = new BinaryWriter(index, Encoding.UTF8, leaveOpen: true))
        {
            WriteString(indexWriter, "../../../");
            indexWriter.Write(entries.Count);
            foreach ((string name, PakEntry entry) in entries)
            {
                WriteString(indexWriter, name);
                WriteEntry(indexWriter, entry);
            }
        }

        byte[] indexBytes = index.ToArray();
        writer.Write(indexBytes);
        writer.Write((byte)0);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(indexOffset);
        writer.Write((long)indexBytes.Length);
        writer.Write(SHA1.HashData(indexBytes));
        return output.ToArray();
    }

    private static byte[] ReadFile(Stream stream, string fileName, int maximumSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSize);
        LocatedPakEntry located = FindEntry(stream, fileName)
            ?? throw new InvalidDataException($"{fileName} was not found in the PAK.");
        PakEntry entry = located.Entry;
        if (entry.Size > maximumSize || entry.UncompressedSize > maximumSize)
        {
            throw new InvalidDataException($"{fileName} is unexpectedly large.");
        }

        stream.Position = entry.Offset;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        PakEntry storedEntry = ReadEntry(reader);
        if (!entry.Matches(storedEntry))
        {
            throw new InvalidDataException("The PAK entry header does not match its index.");
        }
        if (stream.Position > located.IndexOffset)
        {
            throw new InvalidDataException("The PAK entry header overlaps the index.");
        }

        if (entry.Encrypted)
        {
            throw new InvalidDataException("Encrypted PAK entries are not supported.");
        }

        if (entry.CompressionMethod == 0)
        {
            long payloadEnd = CheckedAdd(stream.Position, entry.Size, "PAK entry payload");
            if (payloadEnd > located.IndexOffset)
            {
                throw new InvalidDataException("The PAK entry payload overlaps the index or footer.");
            }
            byte[] content = ReadExact(stream, entry.Size);
            VerifyHash(content, entry.Hash, "PAK entry");
            return content;
        }

        if (entry.CompressionMethod != 1 || entry.Blocks.Count == 0)
        {
            throw new InvalidDataException("The PAK compression method is not supported.");
        }

        if (entry.UncompressedSize > maximumSize)
        {
            throw new InvalidDataException($"{fileName} is unexpectedly large.");
        }

        // Validate the block layout before reading anything: blocks must cover the
        // entry, never overlap, never run backwards, and stay inside the entry area.
        ValidateBlockLayout(entry, located.IndexOffset);

        using var result = new MemoryStream((int)entry.UncompressedSize);
        using var compressedPayload = new MemoryStream((int)entry.Size);
        long written = 0;
        foreach (PakBlock block in entry.Blocks)
        {
            long start = checked(entry.Offset + block.Start);
            long length = checked(block.End - block.Start);
            stream.Position = start;
            byte[] compressed = ReadExact(stream, length);
            compressedPayload.Write(compressed);
            using var compressedStream = new MemoryStream(compressed, writable: false);
            using var zlib = new ZLibStream(compressedStream, CompressionMode.Decompress);
            // Never decompress past the declared uncompressed size.
            written += CopyBounded(zlib, result, entry.UncompressedSize - written);
        }

        if (result.Length != entry.UncompressedSize || written != entry.UncompressedSize)
        {
            throw new InvalidDataException("The decompressed PAK entry has an unexpected size.");
        }

        // UE4 PAK v5 stores the entry SHA-1 over the concatenated compressed
        // blocks. The payload must be authenticated before the decompressed
        // asset is accepted; hashing the result instead rejects valid stock PAKs.
        VerifyHash(compressedPayload.ToArray(), entry.Hash, "compressed PAK entry");
        return result.ToArray();
    }

    /// <summary>
    /// Copies at most <paramref name="maxBytes"/> bytes from the stream. Throws as soon
    /// as an extra byte would exceed the declared uncompressed size.
    /// </summary>
    private static long CopyBounded(Stream source, Stream destination, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        byte[] buffer = new byte[81920];
        long copied = 0;
        while (copied < maxBytes)
        {
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, maxBytes - copied));
            if (read == 0)
            {
                break;
            }

            destination.Write(buffer, 0, read);
            copied += read;
        }

        if (copied > maxBytes)
        {
            throw new InvalidDataException("The decompressed PAK entry exceeded its declared size.");
        }

        // Even when the budget was reached exactly, the source must be exhausted: an
        // overlong zlib stream that a bounded read stops before is still detected.
        if (source.ReadByte() >= 0)
        {
            throw new InvalidDataException("The decompressed PAK entry exceeded its declared size.");
        }

        return copied;
    }

    private static void ValidateBlockLayout(PakEntry entry, long indexOffset)
    {
        long headerSize = checked(57L + (entry.Blocks.Count * 16L));
        long payloadEnd = CheckedAdd(headerSize, entry.Size, "compressed PAK entry");
        long previousEnd = headerSize;
        foreach (PakBlock block in entry.Blocks)
        {
            if (block.Start != previousEnd || block.End < block.Start || block.End > payloadEnd)
            {
                throw new InvalidDataException("The PAK compression block layout is invalid.");
            }
            long absoluteEnd = CheckedAdd(entry.Offset, block.End, "PAK compression block");
            if (absoluteEnd > indexOffset)
            {
                throw new InvalidDataException("A PAK compression block overlaps the index or footer.");
            }

            previousEnd = block.End;
        }

        if (previousEnd != payloadEnd)
        {
            throw new InvalidDataException("The PAK compression blocks do not cover the declared payload.");
        }
    }

    private static LocatedPakEntry? FindEntry(Stream stream, string fileName)
    {
        PakFooter footer = ReadFooter(stream);
        stream.Position = footer.IndexOffset;
        byte[] indexBytes = ReadExact(stream, footer.IndexSize);
        VerifyHash(indexBytes, footer.IndexHash, "PAK index");

        using var index = new MemoryStream(indexBytes, writable: false);
        using var reader = new BinaryReader(index, Encoding.UTF8, leaveOpen: false);
        _ = ReadString(reader);
        int count = reader.ReadInt32();
        if (count is < 0 or > MaximumFileCount)
        {
            throw new InvalidDataException("The PAK file count is invalid.");
        }

        // Unreal's mount lookup is case-insensitive.  Treat casing-only duplicates
        // and lookups the same way so a conflicting override cannot be missed.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PakEntry? match = null;
        for (int indexNumber = 0; indexNumber < count; indexNumber++)
        {
            string name = ReadString(reader);
            PakEntry entry = ReadEntry(reader);
            if (!names.Add(name))
            {
                throw new InvalidDataException($"The PAK index contains duplicate path {name}.");
            }
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                match = entry;
            }
        }

        if (index.Position != index.Length)
        {
            throw new InvalidDataException("The PAK index contains trailing data.");
        }
        return match is null ? null : new LocatedPakEntry(match, footer.IndexOffset);
    }

    private static PakFooter ReadFooter(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < FooterSize)
        {
            throw new InvalidDataException("The PAK is too small.");
        }

        stream.Position = stream.Length - FooterSize;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        bool encryptedIndex = reader.ReadByte() != 0;
        uint magic = reader.ReadUInt32();
        int version = reader.ReadInt32();
        long indexOffset = reader.ReadInt64();
        long indexSize = reader.ReadInt64();
        byte[] indexHash = reader.ReadBytes(20);
        long expectedFooterOffset;
        try
        {
            expectedFooterOffset = checked(indexOffset + indexSize);
        }
        catch (OverflowException)
        {
            throw new InvalidDataException("The PAK footer contains an invalid index range.");
        }

        if (encryptedIndex || magic != Magic || version != Version ||
            indexOffset < 0 || indexSize is < 0 or > MaximumIndexSize ||
            expectedFooterOffset != stream.Length - FooterSize ||
            indexHash.Length != 20)
        {
            throw new InvalidDataException("The PAK footer is invalid or unsupported.");
        }

        return new PakFooter(indexOffset, indexSize, indexHash);
    }

    private static PakEntry ReadEntry(BinaryReader reader)
    {
        long offset = reader.ReadInt64();
        long size = reader.ReadInt64();
        long uncompressedSize = reader.ReadInt64();
        int compressionMethod = reader.ReadInt32();
        byte[] hash = reader.ReadBytes(20);
        List<PakBlock> blocks = [];
        if (compressionMethod != 0)
        {
            int count = reader.ReadInt32();
            if (count is < 0 or > 100_000)
            {
                throw new InvalidDataException("The PAK compression block count is invalid.");
            }

            for (int index = 0; index < count; index++)
            {
                long start = reader.ReadInt64();
                long end = reader.ReadInt64();
                if (start < 0 || end < start)
                {
                    throw new InvalidDataException("A PAK compression block is invalid.");
                }

                blocks.Add(new PakBlock(start, end));
            }
        }

        bool encrypted = reader.ReadByte() != 0;
        uint blockSize = reader.ReadUInt32();
        if (offset < 0 || size < 0 || uncompressedSize < 0 || hash.Length != 20)
        {
            throw new InvalidDataException("A PAK entry is invalid.");
        }

        return new PakEntry(
            offset,
            size,
            uncompressedSize,
            compressionMethod,
            hash,
            blocks,
            encrypted,
            blockSize);
    }

    private static void WriteEntry(BinaryWriter writer, PakEntry entry)
    {
        writer.Write(entry.Offset);
        writer.Write(entry.Size);
        writer.Write(entry.UncompressedSize);
        writer.Write(entry.CompressionMethod);
        writer.Write(entry.Hash);
        if (entry.CompressionMethod != 0)
        {
            writer.Write(entry.Blocks.Count);
            foreach (PakBlock block in entry.Blocks)
            {
                writer.Write(block.Start);
                writer.Write(block.End);
            }
        }

        writer.Write(entry.Encrypted ? (byte)1 : (byte)0);
        writer.Write(entry.BlockSize);
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length == 0)
        {
            return string.Empty;
        }

        if (length is > MaximumStringLength or < -MaximumStringLength)
        {
            throw new InvalidDataException("A PAK string is too long.");
        }

        if (length > 0)
        {
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length || bytes[^1] != 0)
            {
                throw new InvalidDataException("A PAK string is invalid.");
            }

            return Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
        }

        int byteCount = checked(-length * 2);
        byte[] wideBytes = reader.ReadBytes(byteCount);
        if (wideBytes.Length != byteCount || wideBytes[^1] != 0 || wideBytes[^2] != 0)
        {
            throw new InvalidDataException("A wide PAK string is invalid.");
        }

        return Encoding.Unicode.GetString(wideBytes, 0, wideBytes.Length - 2);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length + 1);
        writer.Write(bytes);
        writer.Write((byte)0);
    }

    private static byte[] ReadExact(Stream stream, long length)
    {
        if (length is < 0 or > int.MaxValue)
        {
            throw new InvalidDataException("The requested PAK entry is too large.");
        }

        byte[] bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static long CheckedAdd(long left, long right, string label)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw new InvalidDataException($"The {label} range is invalid.");
        }
    }

    private static void VerifyHash(byte[] content, byte[] expected, string label)
    {
        if (!SHA1.HashData(content).AsSpan().SequenceEqual(expected))
        {
            throw new InvalidDataException($"The {label} hash is invalid.");
        }
    }

    private sealed record PakFooter(long IndexOffset, long IndexSize, byte[] IndexHash);

    private sealed record LocatedPakEntry(PakEntry Entry, long IndexOffset);

    private sealed record PakBlock(long Start, long End);

    private sealed record PakEntry(
        long Offset,
        long Size,
        long UncompressedSize,
        int CompressionMethod,
        byte[] Hash,
        IReadOnlyList<PakBlock> Blocks,
        bool Encrypted,
        uint BlockSize)
    {
        public bool Matches(PakEntry other) =>
            (other.Offset is 0 || Offset == other.Offset) &&
            Size == other.Size &&
            UncompressedSize == other.UncompressedSize &&
            CompressionMethod == other.CompressionMethod &&
            Hash.AsSpan().SequenceEqual(other.Hash) &&
            Blocks.SequenceEqual(other.Blocks) &&
            Encrypted == other.Encrypted &&
            BlockSize == other.BlockSize;
    }
}
#pragma warning restore CA5350
