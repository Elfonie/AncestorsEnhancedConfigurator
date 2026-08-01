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
}
