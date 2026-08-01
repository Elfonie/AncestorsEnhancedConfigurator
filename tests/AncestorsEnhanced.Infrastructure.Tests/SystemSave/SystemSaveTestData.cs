using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.Tests.SystemSave;

internal static class SystemSaveTestData
{
    public static byte[] Create()
    {
        byte[] scalability = Combine(
            UnrealTaggedProperties.EncodeEnum("PostProcessing", "Low"),
            UnrealTaggedProperties.EncodeEnum("Shadows", "Medium"),
            UnrealTaggedProperties.EncodeTerminator());
        byte[] graphics = Combine(
            UnrealTaggedProperties.EncodeIntPoint("FullScreenResolution", 1280, 720),
            UnrealTaggedProperties.EncodeIntPoint("WindowedResolution", 1680, 1050),
            UnrealTaggedProperties.EncodeFloat("Brightness", 1.05f),
            UnrealTaggedProperties.EncodeInt("QualityLevel", 2),
            UnrealTaggedProperties.EncodeStruct("ScalabilitySetting", "ScalabilitySetting", scalability),
            UnrealTaggedProperties.EncodeInt("FrameRateLimit", 3),
            UnrealTaggedProperties.EncodeBool("QualitySettingIsCustom", true),
            UnrealTaggedProperties.EncodeTerminator());
        byte[] options = Combine(
            UnrealTaggedProperties.EncodeStruct("GraphicOptions", "GraphicsOptions", graphics),
            UnrealTaggedProperties.EncodeTerminator());
        byte[] root = Combine(
            UnrealTaggedProperties.EncodeStruct("Options", "PanacheOptions", options),
            UnrealTaggedProperties.EncodeTerminator());
        return SnappyBlockCodec.EncodeLiteral(root);
    }

    private static byte[] Combine(params byte[][] values)
    {
        byte[] result = new byte[values.Sum(value => value.Length)];
        int offset = 0;
        foreach (byte[] value in values)
        {
            value.CopyTo(result, offset);
            offset += value.Length;
        }

        return result;
    }
}
