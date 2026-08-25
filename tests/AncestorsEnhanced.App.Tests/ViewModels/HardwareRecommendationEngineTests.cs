using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Infrastructure.Platform;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class HardwareRecommendationEngineTests
{
    [Fact]
    public void RecommendUsesLowVramPresetForFourGiBOrLess()
    {
        HardwareRecommendationViewModel recommendation = HardwareRecommendationEngine.Recommend(Snapshot(vramGiB: 4));

        Assert.Equal("Low VRAM", recommendation.PresetName);
        Assert.True(recommendation.CanStagePreset);
        Assert.Contains("conservative", recommendation.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecommendUsesHighQualityOnlyForHighReportedVramAndSystemMemory()
    {
        HardwareRecommendationViewModel recommendation = HardwareRecommendationEngine.Recommend(Snapshot(vramGiB: 12, memoryGiB: 32));

        Assert.Equal("High Quality", recommendation.PresetName);
        Assert.True(recommendation.CanStagePreset);
        Assert.Contains("not an FPS guarantee", recommendation.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void RecommendRefusesToGuessWithoutReportedGraphicsMemory()
    {
        HardwareRecommendationViewModel recommendation = HardwareRecommendationEngine.Recommend(new HardwareSnapshot(
            "Windows",
            "CPU",
            8,
            4,
            16UL * 1024 * 1024 * 1024,
            [new GraphicsAdapterSnapshot("GPU", null)]));

        Assert.Equal("No automatic preset", recommendation.PresetName);
        Assert.False(recommendation.CanStagePreset);
    }

    private static HardwareSnapshot Snapshot(int vramGiB, int memoryGiB = 16) => new(
        "Windows",
        "CPU",
        8,
        4,
        (ulong)memoryGiB * 1024 * 1024 * 1024,
        [new GraphicsAdapterSnapshot("GPU", (ulong)vramGiB * 1024 * 1024 * 1024)]);
}
