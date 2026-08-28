using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Infrastructure.Platform;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class HardwareRecommendationEngineTests
{
    [Fact]
    public void RecommendUsesLowVramPresetForFourGiBOrLess()
    {
        HardwareRecommendationViewModel recommendation = HardwareRecommendationEngine.Recommend(Snapshot(vramGiB: 4));

        Assert.Equal("Low VRAM Setup", recommendation.PresetName);
        Assert.True(recommendation.CanStagePreset);
        Assert.Contains("protects graphics memory", recommendation.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecommendUsesHighQualityOnlyForHighReportedVramAndSystemMemory()
    {
        HardwareRecommendationViewModel recommendation = HardwareRecommendationEngine.Recommend(Snapshot(vramGiB: 12, memoryGiB: 32));

        Assert.Equal("High Quality Setup", recommendation.PresetName);
        Assert.True(recommendation.CanStagePreset);
        Assert.Contains("not an FPS guarantee", recommendation.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void RecommendTreatsTheUsableReportedSizeOfATwelveGigabyteCardAsHighQuality()
    {
        HardwareRecommendationViewModel recommendation = HardwareRecommendationEngine.Recommend(Snapshot(vramGiB: 11, memoryGiB: 32));

        Assert.Equal("High Quality Setup", recommendation.PresetName);
        Assert.True(recommendation.CanStagePreset);
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
        [new GraphicsAdapterSnapshot("GPU", null, true)]));

        Assert.Equal("No automatic preset", recommendation.PresetName);
        Assert.False(recommendation.CanStagePreset);
    }

    [Fact]
    public void RecommendUsesGpuVramRamAndCpuForTheProfileDecision()
    {
        HardwareRecommendationViewModel recommendation = HardwareRecommendationEngine.Recommend(new HardwareSnapshot(
            "Windows",
            "CPU",
            4,
            4,
            8UL * 1024 * 1024 * 1024,
            [new GraphicsAdapterSnapshot("Example GPU", 8UL * 1024 * 1024 * 1024, true)]));

        Assert.Equal("Performance Setup", recommendation.PresetName);
        Assert.Contains("Example GPU", recommendation.Description, StringComparison.Ordinal);
        Assert.Contains("8 GiB dedicated VRAM", recommendation.Description, StringComparison.Ordinal);
        Assert.Contains("8 GiB system memory", recommendation.Description, StringComparison.Ordinal);
        Assert.Contains("4 logical processors", recommendation.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void RecommendUsesUltraOnlyWhenGpuMemoryRamAndCpuAllMeetTheHighThreshold()
    {
        HardwareRecommendationViewModel recommendation = HardwareRecommendationEngine.Recommend(
            Snapshot(vramGiB: 16, memoryGiB: 32, logicalProcessors: 12));

        Assert.Equal("Ultra Setup", recommendation.PresetName);
        Assert.True(recommendation.CanStagePreset);
    }

    private static HardwareSnapshot Snapshot(int vramGiB, int memoryGiB = 16, int logicalProcessors = 8) => new(
        "Windows",
        "CPU",
        logicalProcessors,
        4,
        (ulong)memoryGiB * 1024 * 1024 * 1024,
        [new GraphicsAdapterSnapshot("GPU", (ulong)vramGiB * 1024 * 1024 * 1024, true)]);
}
