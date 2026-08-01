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
        byte[] payload = Enumerable.Range(0, 12104).Select(index => (byte)(index % 251)).ToArray();

        byte[] encoded = SnappyBlockCodec.EncodeLiteral(payload);

        Assert.Equal(payload, SnappyBlockCodec.Decode(encoded));
    }
}
