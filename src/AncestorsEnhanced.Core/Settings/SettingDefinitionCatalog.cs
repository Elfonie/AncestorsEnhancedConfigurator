namespace AncestorsEnhanced.Core.Settings;

using AncestorsEnhanced.Core.Editing;

public static class SettingDefinitionCatalog
{
    private static readonly HashSet<string> SimpleSettings = new(StringComparer.Ordinal)
    {
        "motion-blur-quality",
        "dof-quality",
        "environment-vignette",
        "aa-quality",
        "image-sharpening",
        "view-distance",
        "foliage-density",
        "grass-density",
        "anisotropic-filtering",
        "bloom-quality",
        "chromatic-aberration",
        "light-shafts",
        "shadow-quality",
        "startup-videos",
        "game-fullscreen-resolution",
        "game-frame-rate",
        "game-shadow-preset",
        "game-post-processing-preset",
        "game-foliage-preset",
    };

    private static readonly HashSet<string> UsefulReadOnlySettings = new(StringComparer.Ordinal)
    {
        "dof-strength",
        "shadow-csm-resolution",
        "game-overall-quality",
        "game-custom-quality",
        "game-menu-unavailable",
    };

    private static readonly HashSet<string> ExperimentalSettings = new(StringComparer.Ordinal)
    {
        "skeletal-lod-bias", "render-target-pool", "ao-radius", "ao-max-quality", "ao-mip-factor",
        "fast-blur-threshold", "filter-size", "light-function-quality", "shadow-radius-threshold",
        "shadow-transition", "preshadow-resolution", "fog-grid-pixel-size", "fog-grid-depth",
        "fog-history-samples", "light-distance", "translucency-volume", "sss-samples",
        "texture-streaming-boost", "texture-mip-bias"
    };

    public static bool IsShownInSimpleMode(string settingId) =>
        SimpleSettings.Contains(settingId);

    public static bool IsShownInAdvancedMode(FeatureSettingSnapshot setting) =>
        SimpleSettings.Contains(setting.Id) ||
        UsefulReadOnlySettings.Contains(setting.Id) ||
        EditableSettingsCatalog.IsDefined(setting.TechnicalKey);

    public static bool IsExperimental(string settingId) => ExperimentalSettings.Contains(settingId);
}
