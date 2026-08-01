using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.Tests.SystemSave;

public sealed class AncestorsSystemSaveCodecTests
{
    [Fact]
    public void ReadDecodesVerifiedGraphicsOptions()
    {
        SystemGraphicsSettingsSnapshot settings = AncestorsSystemSaveCodec.Read(SystemSaveTestData.Create());

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
        byte[] original = SystemSaveTestData.Create();

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

}
