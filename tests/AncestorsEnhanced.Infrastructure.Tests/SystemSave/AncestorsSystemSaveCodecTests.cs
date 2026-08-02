using System.Buffers.Binary;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.Tests.SystemSave;

public sealed class AncestorsSystemSaveCodecTests
{
    [Fact]
    public void ReadTreatsAMissingCustomFlagAsFalse()
    {
        byte[] file = RemoveCustomFlag(VerifiedSystemSaveFixture.Read());
        SystemGraphicsSettingsSnapshot settings = AncestorsSystemSaveCodec.Read(file);

        Assert.False(settings.QualitySettingIsCustom);
        Assert.Equal(GameGraphicsQuality.High, settings.OverallQuality);
        Assert.Equal(GameGraphicsQuality.Low, settings.PostProcessingQuality);
    }

    [Fact]
    public void ReadDecodesVerifiedGraphicsOptions()
    {
        SystemGraphicsSettingsSnapshot settings = AncestorsSystemSaveCodec.Read(
            VerifiedSystemSaveFixture.Read());

        Assert.Equal((1280, 720), (settings.FullscreenWidth, settings.FullscreenHeight));
        Assert.Equal((1680, 1050), (settings.WindowedWidth, settings.WindowedHeight));
        Assert.Equal(1.05, settings.Brightness, 3);
        Assert.Equal(GameGraphicsQuality.High, settings.OverallQuality);
        Assert.Equal(GameGraphicsQuality.Low, settings.PostProcessingQuality);
        Assert.Equal(GameGraphicsQuality.Medium, settings.ShadowQuality);
        Assert.Equal(GameGraphicsQuality.High, settings.FoliageQuality);
        Assert.Equal(120, settings.FrameRateLimit);
        Assert.True(settings.QualitySettingIsCustom);
    }

    [Fact]
    public void ApplyEditsCopyAndPreservesReadableStructure()
    {
        byte[] original = VerifiedSystemSaveFixture.Read();

        byte[] updated = AncestorsSystemSaveCodec.Apply(
            original,
            new Dictionary<string, string>
            {
                [SystemSaveSettingKeys.FullscreenResolution] = "2560x1440",
                [SystemSaveSettingKeys.Brightness] = "1.1",
                [SystemSaveSettingKeys.PostProcessingQuality] = "High",
                [SystemSaveSettingKeys.ShadowQuality] = "High",
                [SystemSaveSettingKeys.FoliageQuality] = "Low",
                [SystemSaveSettingKeys.FrameRateLimit] = "144",
            });
        SystemGraphicsSettingsSnapshot settings = AncestorsSystemSaveCodec.Read(updated);

        Assert.Equal((2560, 1440), (settings.FullscreenWidth, settings.FullscreenHeight));
        Assert.Equal(1.1, settings.Brightness, 3);
        Assert.Equal(GameGraphicsQuality.High, settings.PostProcessingQuality);
        Assert.Equal(GameGraphicsQuality.High, settings.ShadowQuality);
        Assert.Equal(GameGraphicsQuality.Low, settings.FoliageQuality);
        Assert.Equal(144, settings.FrameRateLimit);
        Assert.True(settings.QualitySettingIsCustom);
        Assert.NotEqual(original, updated);
        Assert.Equal(GameGraphicsQuality.Low, AncestorsSystemSaveCodec.Read(original).PostProcessingQuality);
    }

    private static byte[] RemoveCustomFlag(byte[] file)
    {
        byte[] data = SnappyBlockCodec.Decode(file);
        IReadOnlyList<TaggedProperty> root = UnrealTaggedProperties.Read(data, 0, data.Length);
        TaggedProperty options = UnrealTaggedProperties.Require(root, "Options", "StructProperty");
        IReadOnlyList<TaggedProperty> optionValues = UnrealTaggedProperties.Read(
            data,
            options.ValueOffset,
            options.ValueLength);
        TaggedProperty graphics = UnrealTaggedProperties.Require(
            optionValues,
            "GraphicOptions",
            "StructProperty");
        IReadOnlyList<TaggedProperty> graphicValues = UnrealTaggedProperties.Read(
            data,
            graphics.ValueOffset,
            graphics.ValueLength);
        TaggedProperty custom = UnrealTaggedProperties.Require(
            graphicValues,
            "QualitySettingIsCustom",
            "BoolProperty");
        int removedLength = custom.End - custom.Start;
        byte[] updated = new byte[data.Length - removedLength];
        data.AsSpan(0, custom.Start).CopyTo(updated);
        data.AsSpan(custom.End).CopyTo(updated.AsSpan(custom.Start));
        foreach (TaggedProperty container in new[] { options, graphics })
        {
            long size = BinaryPrimitives.ReadInt64LittleEndian(
                updated.AsSpan(container.SizeOffset, sizeof(long)));
            BinaryPrimitives.WriteInt64LittleEndian(
                updated.AsSpan(container.SizeOffset, sizeof(long)),
                size - removedLength);
        }

        return SnappyBlockCodec.EncodeLiteral(updated);
    }

}
