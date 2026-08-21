using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

internal static class TestSaveFactory
{
    public static byte[] Create(params byte[] seed)
    {
        int value = 17;
        foreach (byte item in seed)
        {
            value = unchecked(value * 31 + item);
        }

        byte[] property = UnrealTaggedProperties.EncodeInt("TestValue", value);
        byte[] terminator = UnrealTaggedProperties.EncodeTerminator();
        byte[] payload = new byte[property.Length + terminator.Length];
        property.CopyTo(payload, 0);
        terminator.CopyTo(payload, property.Length);
        return SnappyBlockCodec.EncodeLiteral(payload);
    }
}
