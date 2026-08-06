using System.Text;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.Tests.SystemSave;

public sealed class SnappyBlockCodecTests
{
    [Fact]
    public void DecodeSupportsOverlappingCopies()
    {
        byte[] compressed = [11, 20, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', (byte)' ', 5, 6];

        Assert.Equal("hello hello", Encoding.ASCII.GetString(SnappyBlockCodec.Decode(compressed)));
    }

    [Fact]
    public void LiteralEncodingRoundTripsLargePayload()
    {
        byte[] payload = [.. Enumerable.Range(0, 12104).Select(index => (byte)(index % 251))];

        byte[] encoded = SnappyBlockCodec.EncodeLiteral(payload);

        Assert.Equal(payload, SnappyBlockCodec.Decode(encoded));
    }
    [Fact]
    public void DecodeHonoursACustomMaximumLength()
    {
        byte[] payload = Enumerable.Range(0, 5000).Select(index => (byte)(index % 251)).ToArray();
        byte[] encoded = SnappyBlockCodec.EncodeLiteral(payload);

        Assert.Throws<InvalidDataException>(() => SnappyBlockCodec.Decode(encoded, 1000));
        Assert.Equal(payload, SnappyBlockCodec.Decode(encoded, 5000));
    }

    [Fact]
    public void TruncatedInputIsRejected()
    {
        byte[] encoded = SnappyBlockCodec.EncodeLiteral([1, 2, 3, 4]);

        Assert.Throws<InvalidDataException>(() => SnappyBlockCodec.Decode(encoded.AsSpan(0, encoded.Length - 1)));
    }

    [Fact]
    public void InvalidCopyOffsetZeroIsRejected()
    {
        // expectedLength=3, then a type-2 copy with offset 0.
        byte[] malformed = [3, 0x04, 0x00, 0x00];

        Assert.Throws<InvalidDataException>(() => SnappyBlockCodec.Decode(malformed));
    }

    [Fact]
    public void MalformedVarintIsRejected()
    {
        // 6 bytes all with continuation bit set: too long.
        byte[] malformed = [0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00];

        Assert.Throws<InvalidDataException>(() => SnappyBlockCodec.Decode(malformed));
    }

}
