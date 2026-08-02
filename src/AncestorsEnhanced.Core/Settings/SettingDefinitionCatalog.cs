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

    public static bool IsShownInSimpleMode(string settingId) =>
        SimpleSettings.Contains(settingId);

    public static bool IsShownInAdvancedMode(FeatureSettingSnapshot setting) =>
        SimpleSettings.Contains(setting.Id) ||
        UsefulReadOnlySettings.Contains(setting.Id) ||
        EditableSettingsCatalog.IsDefined(setting.TechnicalKey);
}
