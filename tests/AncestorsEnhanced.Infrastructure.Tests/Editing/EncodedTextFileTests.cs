using AncestorsEnhanced.Infrastructure.Editing;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

public sealed class EncodedTextFileTests
{
    [Fact]
    public void Utf32LePreambleIsRejectedInsteadOfMisreadAsUtf16()
    {
        // UTF-32LE BOM (FF FE 00 00) starts with the same two bytes as the UTF-16LE
        // BOM; it must be rejected up front rather than decoded as UTF-16.
        byte[] content = [0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00];

        Assert.Throws<InvalidDataException>(() => EncodedTextFile.Decode(content));
    }

    [Fact]
    public void Utf16LeBomIsStillDecoded()
    {
        byte[] content = [0xFF, 0xFE, 0x41, 0x00, 0x42, 0x00];

        EncodedTextFile file = EncodedTextFile.Decode(content);

        Assert.Equal("AB", file.Text);
    }

    [Fact]
    public void PlainUtf8WithoutBomIsDecoded()
    {
        byte[] content = [0x41, 0x42, 0x43];

        EncodedTextFile file = EncodedTextFile.Decode(content);

        Assert.Equal("ABC", file.Text);
    }
}
