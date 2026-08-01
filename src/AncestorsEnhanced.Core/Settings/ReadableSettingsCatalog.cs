using System.Globalization;
using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Core.Settings;

public static class ReadableSettingsCatalog
{
    private const string SystemSettingsSection = "SystemSettings";
    private const string MoviePlayerSection = "/Script/MoviePlayer.MoviePlayerSettings";
    private const string KnownHalfVignettePakSha256 =
        "06F74C5E4BF70D2748614D8C74405B4C96FB4E50F103A66827C4E2041B2801A0";

    public static IReadOnlyList<ReadableSettingSnapshot> Create(GameInspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        List<ReadableSettingSnapshot> settings =
        [
            CreateQualitySwitch(
                snapshot,
                "motion-blur",
                "Motion blur",
                "r.MotionBlurQuality",
                "Blurs moving objects and camera motion. Quality 0 disables it completely."),
            CreateQualitySwitch(
                snapshot,
                "depth-of-field",
                "Depth of field",
                "r.DepthOfFieldQuality",
                "Blurs areas outside the camera's focus. Quality 0 disables it completely."),
            CreateIntegerScale(
                snapshot,
                "anisotropic-filtering",
                "Texture filtering",
                "r.MaxAnisotropy",
                value => value <= 1 ? "Off" : $"{value}×",
                "Keeps ground and surface textures sharp when viewed at an angle.",
                maximum: 16),
            CreateDecimalScale(
                snapshot,
                "image-sharpening",
                "Image sharpening",
                "r.Tonemapper.Sharpen",
                value => value == 0 ? "Off" : value.ToString("0.00", CultureInfo.InvariantCulture),
                "Adds a controlled sharpening pass after temporal anti-aliasing."),
            CreateDecimalScale(
                snapshot,
                "taa-frame-weight",
                "TAA response",
                "r.TemporalAACurrentFrameWeight",
                value => value.ToString("0.00", CultureInfo.InvariantCulture),
                "Controls how strongly temporal anti-aliasing uses the current frame.",
                maximum: 1),
            CreateDecimalScale(
                snapshot,
                "view-distance",
                "View distance",
                "r.ViewDistanceScale",
                value => $"{value * 100:0}%",
                "Scales how far distant objects remain visible."),
            CreateIntegerScale(
                snapshot,
                "texture-memory",
                "Texture memory budget",
                "r.Streaming.PoolSize",
                value => value >= 1024 ? $"{value / 1024d:0.##} GB" : $"{value} MB",
                "Maximum memory reserved for streamed textures."),
            CreateBoolean(
                snapshot,
                "gradient-smoothing",
                "Color-gradient smoothing",
                "r.Tonemapper.GrainQuantization",
                "Adds subtle dithering to reduce visible color banding."),
            CreateBoolean(
                snapshot,
                "light-shafts",
                "Light shafts",
                "r.LightShaftQuality",
                "Controls sunbeams and similar atmospheric light shafts."),
            CreateStartupVideoSetting(snapshot),
            CreateVignetteSetting(snapshot),
        ];

        return settings;
    }

    private static ReadableSettingSnapshot CreateQualitySwitch(
        GameInspectionSnapshot snapshot,
        string id,
        string name,
        string key,
        string description)
    {
        IniSettingSnapshot? entry = FindSetting(snapshot, SystemSettingsSection, key);
        if (!TryParseInt(entry?.Value, out int value))
        {
            return CreateUnknown(id, "Visual effects", name, description);
        }

        return new ReadableSettingSnapshot(
            id,
            "Visual effects",
            name,
            value == 0 ? "Off" : $"On · quality {value}",
            description,
            "Engine.ini override",
            value == 0 ? ReadableSettingState.Disabled : ReadableSettingState.Enabled,
            Math.Clamp(value / 4d, 0, 1));
    }

    private static ReadableSettingSnapshot CreateBoolean(
        GameInspectionSnapshot snapshot,
        string id,
        string name,
        string key,
        string description)
    {
        IniSettingSnapshot? entry = FindSetting(snapshot, SystemSettingsSection, key);
        if (!TryParseInt(entry?.Value, out int value))
        {
            return CreateUnknown(id, "Visual effects", name, description);
        }

        bool enabled = value > 0;
        return new ReadableSettingSnapshot(
            id,
            "Visual effects",
            name,
            enabled ? "On" : "Off",
            description,
            "Engine.ini override",
            enabled ? ReadableSettingState.Enabled : ReadableSettingState.Disabled,
            enabled ? 1 : 0);
    }

    private static ReadableSettingSnapshot CreateIntegerScale(
        GameInspectionSnapshot snapshot,
        string id,
        string name,
        string key,
        Func<int, string> formatValue,
        string description,
        int? maximum = null)
    {
        IniSettingSnapshot? entry = FindSetting(snapshot, SystemSettingsSection, key);
        if (!TryParseInt(entry?.Value, out int value))
        {
            return CreateUnknown(id, "Image quality", name, description);
        }

        return new ReadableSettingSnapshot(
            id,
            "Image quality",
            name,
            formatValue(value),
            description,
            "Engine.ini override",
            value == 0 ? ReadableSettingState.Disabled : ReadableSettingState.Modified,
            maximum is > 0 ? Math.Clamp(value / (double)maximum.Value, 0, 1) : null);
    }

    private static ReadableSettingSnapshot CreateDecimalScale(
        GameInspectionSnapshot snapshot,
        string id,
        string name,
        string key,
        Func<double, string> formatValue,
        string description,
        double? maximum = null)
    {
        IniSettingSnapshot? entry = FindSetting(snapshot, SystemSettingsSection, key);
        if (!TryParseDouble(entry?.Value, out double value))
        {
            return CreateUnknown(id, "Image quality", name, description);
        }

        return new ReadableSettingSnapshot(
            id,
            "Image quality",
            name,
            formatValue(value),
            description,
            "Engine.ini override",
            value == 0 ? ReadableSettingState.Disabled : ReadableSettingState.Modified,
            maximum is > 0 ? Math.Clamp(value / maximum.Value, 0, 1) : null);
    }

    private static ReadableSettingSnapshot CreateStartupVideoSetting(
        GameInspectionSnapshot snapshot)
    {
        IniSettingSnapshot? clearMovies = FindSetting(snapshot, MoviePlayerSection, "!StartupMovies");
        bool skipped = string.Equals(clearMovies?.Value, "ClearArray", StringComparison.OrdinalIgnoreCase);

        return new ReadableSettingSnapshot(
            "startup-videos",
            "Convenience",
            "Startup videos",
            skipped ? "Skipped" : "Game default",
            "Controls whether the startup splash movies are played.",
            skipped ? "Game.ini override" : "No verified override",
            skipped ? ReadableSettingState.Enabled : ReadableSettingState.Unknown,
            skipped ? 1 : null);
    }

    private static ReadableSettingSnapshot CreateVignetteSetting(GameInspectionSnapshot snapshot)
    {
        PakFileSnapshot? knownPatch = snapshot.PakFiles.FirstOrDefault(pak =>
            string.Equals(pak.Sha256, KnownHalfVignettePakSha256, StringComparison.OrdinalIgnoreCase));
        if (knownPatch is not null)
        {
            return new ReadableSettingSnapshot(
                "vignette",
                "Visual effects",
                "Vignette",
                "50% intensity",
                "Darkening around the edge of the image is reduced but retained.",
                "Verified patch fingerprint",
                ReadableSettingState.Modified,
                0.5);
        }

        bool hasUnknownPatch = snapshot.PakFiles.Any(pak =>
            pak.Classification == PakClassification.PatchStyle);
        return new ReadableSettingSnapshot(
            "vignette",
            "Visual effects",
            "Vignette",
            hasUnknownPatch ? "Unknown custom value" : "Game default",
            "Darkening around the edge of the image.",
            hasUnknownPatch ? "Unrecognized patch package" : "No verified override",
            ReadableSettingState.Unknown,
            null);
    }

    private static ReadableSettingSnapshot CreateUnknown(
        string id,
        string category,
        string name,
        string description)
    {
        return new ReadableSettingSnapshot(
            id,
            category,
            name,
            "Game default",
            description,
            "No verified override",
            ReadableSettingState.Unknown,
            null);
    }

    private static IniSettingSnapshot? FindSetting(
        GameInspectionSnapshot snapshot,
        string section,
        string key)
    {
        return snapshot.ConfigurationFiles
            .SelectMany(file => file.Settings)
            .LastOrDefault(setting =>
                string.Equals(setting.Section, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseInt(string? text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDouble(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
