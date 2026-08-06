namespace AncestorsEnhanced.Infrastructure.SystemSave;

internal static class SnappyBlockCodec
{
    public static byte[] Decode(ReadOnlySpan<byte> input) =>
        Decode(input, DefaultMaximumDecompressedLength);

    /// <summary>Default upper bound for decompressed Snappy payloads (16 MiB).</summary>
    public const int DefaultMaximumDecompressedLength = 16 * 1024 * 1024;

    public static byte[] Decode(ReadOnlySpan<byte> input, int maximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);
        int inputOffset = 0;
        uint expectedLength = ReadVarint(input, ref inputOffset);
        if (expectedLength > maximumLength)
        {
            throw new InvalidDataException("The Snappy payload is unexpectedly large.");
        }

        byte[] output = new byte[expectedLength];
        int outputOffset = 0;
        while (inputOffset < input.Length)
        {
            byte tag = input[inputOffset++];
            int type = tag & 3;
            if (type == 0)
            {
                int encodedLength = tag >> 2;
                int length = encodedLength < 60
                    ? encodedLength + 1
                    : ReadLiteralLength(input, ref inputOffset, encodedLength - 59);
                EnsureAvailable(input.Length, inputOffset, length);
                EnsureAvailable(output.Length, outputOffset, length);
                input.Slice(inputOffset, length).CopyTo(output.AsSpan(outputOffset));
                inputOffset += length;
                outputOffset += length;
                continue;
            }

            int copyLength;
            int copyOffset;
            if (type == 1)
            {
                EnsureAvailable(input.Length, inputOffset, 1);
                copyLength = 4 + ((tag >> 2) & 7);
                copyOffset = ((tag & 0xE0) << 3) | input[inputOffset++];
            }
            else if (type == 2)
            {
                EnsureAvailable(input.Length, inputOffset, 2);
                copyLength = 1 + (tag >> 2);
                copyOffset = input[inputOffset] | (input[inputOffset + 1] << 8);
                inputOffset += 2;
            }
            else
            {
                EnsureAvailable(input.Length, inputOffset, 4);
                copyLength = 1 + (tag >> 2);
                uint offset =
                    input[inputOffset] |
                    ((uint)input[inputOffset + 1] << 8) |
                    ((uint)input[inputOffset + 2] << 16) |
                    ((uint)input[inputOffset + 3] << 24);
                copyOffset = checked((int)offset);
                inputOffset += 4;
            }

            if (copyOffset <= 0 || copyOffset > outputOffset)
            {
                throw new InvalidDataException("Snappy contains an invalid copy offset.");
            }

            EnsureAvailable(output.Length, outputOffset, copyLength);
            for (int index = 0; index < copyLength; index++)
            {
                output[outputOffset] = output[outputOffset - copyOffset];
                outputOffset++;
            }
        }

        if (outputOffset != output.Length)
        {
            throw new InvalidDataException("Snappy ended before its declared payload length.");
        }

        return output;
    }

    public static byte[] EncodeLiteral(ReadOnlySpan<byte> input)
    {
        using var output = new MemoryStream(input.Length + 8);
        WriteVarint(output, checked((uint)input.Length));
        int remaining = input.Length;
        int offset = 0;
        while (remaining > 0)
        {
            int length = Math.Min(remaining, 65536);
            WriteLiteralHeader(output, length);
            output.Write(input.Slice(offset, length));
            offset += length;
            remaining -= length;
        }

        return output.ToArray();
    }

    private static int ReadLiteralLength(ReadOnlySpan<byte> input, ref int offset, int byteCount)
    {
        if (byteCount is < 1 or > 4)
        {
            throw new InvalidDataException("Snappy contains an invalid literal length.");
        }

        EnsureAvailable(input.Length, offset, byteCount);
        uint value = 0;
        for (int index = 0; index < byteCount; index++)
        {
            value |= (uint)input[offset++] << (index * 8);
        }

        return checked((int)value + 1);
    }

    private static uint ReadVarint(ReadOnlySpan<byte> input, ref int offset)
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            EnsureAvailable(input.Length, offset, 1);
            byte current = input[offset++];
            value |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("Snappy has an invalid size prefix.");
    }

    private static void WriteVarint(Stream output, uint value)
    {
        while (value >= 0x80)
        {
            output.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        output.WriteByte((byte)value);
    }

    private static void WriteLiteralHeader(Stream output, int length)
    {
        int value = length - 1;
        if (length <= 60)
        {
            output.WriteByte((byte)(value << 2));
            return;
        }

        int byteCount = value <= byte.MaxValue ? 1 : value <= ushort.MaxValue ? 2 : 4;
        output.WriteByte((byte)((59 + byteCount) << 2));
        for (int index = 0; index < byteCount; index++)
        {
            output.WriteByte((byte)(value >> (index * 8)));
        }
    }

    private static void EnsureAvailable(int total, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > total - count)
        {
            throw new InvalidDataException("System.sav is truncated or malformed.");
        }
    }
}
