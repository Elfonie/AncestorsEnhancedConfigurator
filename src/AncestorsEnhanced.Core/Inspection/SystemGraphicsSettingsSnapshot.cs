namespace AncestorsEnhanced.Core.Inspection;

public enum GameGraphicsQuality
{
    Low,
    Medium,
    High,
}

public sealed record SystemGraphicsSettingsSnapshot(
    int FullscreenWidth,
    int FullscreenHeight,
    int WindowedWidth,
    int WindowedHeight,
    double Brightness,
    GameGraphicsQuality OverallQuality,
    GameGraphicsQuality ViewDistanceQuality,
    GameGraphicsQuality PostProcessingQuality,
    GameGraphicsQuality ShadowQuality,
    GameGraphicsQuality TextureQuality,
    GameGraphicsQuality VisualEffectsQuality,
    GameGraphicsQuality FoliageQuality,
    int FrameRateLimitIndex,
    int FrameRateLimit,
    bool QualitySettingIsCustom);
