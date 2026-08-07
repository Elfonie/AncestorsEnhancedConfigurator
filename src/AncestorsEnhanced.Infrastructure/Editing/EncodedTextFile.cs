using System.Text;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal sealed record EncodedTextFile(
    string Text,
    Encoding Encoding,
    byte[] Preamble)
{
    public static EncodedTextFile Decode(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        (Encoding Encoding, byte[] Preamble) format = DetectEncoding(content);
        string text = format.Encoding.GetString(content, format.Preamble.Length, content.Length - format.Preamble.Length);
        return new EncodedTextFile(text, format.Encoding, format.Preamble);
    }

    public byte[] Encode(string text)
    {
        byte[] body = Encoding.GetBytes(text);
        if (Preamble.Length == 0)
        {
            return body;
        }

        byte[] result = new byte[Preamble.Length + body.Length];
        Preamble.CopyTo(result, 0);
        body.CopyTo(result, Preamble.Length);
        return result;
    }

    private static (Encoding Encoding, byte[] Preamble) DetectEncoding(byte[] content)
    {
        if (content.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), Encoding.UTF8.Preamble.ToArray());
        }

        // UTF-32 must be rejected before UTF-16: a UTF-32LE BOM (FF FE 00 00) starts
        // with the same two bytes as the UTF-16LE BOM. The editing pipeline only
        // supports UTF-8 and UTF-16, so refuse UTF-32 explicitly rather than
        // mis-classifying it as UTF-16 and corrupting the file.
        if (IsUtf32Preamble(content))
        {
            throw new InvalidDataException("The file uses UTF-32 encoding, which is not supported.");
        }

        if (content.AsSpan().StartsWith(Encoding.Unicode.Preamble))
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), Encoding.Unicode.Preamble.ToArray());
        }

        if (content.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), Encoding.BigEndianUnicode.Preamble.ToArray());
        }

        return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), []);
    }

    private static bool IsUtf32Preamble(byte[] content)
    {
        // UTF-32LE BOM: FF FE 00 00. UTF-32BE BOM: 00 00 FE FF.
        if (content.Length < 4)
        {
            return false;
        }

        return (content[0] == 0xFF && content[1] == 0xFE && content[2] == 0x00 && content[3] == 0x00) ||
               (content[0] == 0x00 && content[1] == 0x00 && content[2] == 0xFE && content[3] == 0xFF);
    }
}
