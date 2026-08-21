using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.Settings;

namespace AncestorsEnhanced.Core.Editing;

internal sealed record SettingEditorTemplate(
    SettingEditorKind Kind,
    string DefaultValue,
    string FileName = "Engine.ini",
    string Section = "SystemSettings",
    decimal? Minimum = null,
    decimal? Maximum = null,
    decimal? Increment = null,
    IReadOnlyList<SettingChoice>? Choices = null,
    SettingFileTarget Target = SettingFileTarget.Ini,
    string? Unit = null,
    bool IsDirect = false);

public static class EditableSettingsCatalog
{
    private static readonly Dictionary<string, SettingEditorTemplate> Settings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["r.MotionBlurQuality"] = Quality(defaultValue: 0, maximum: 4),
            ["r.MotionBlur.Scale"] = Number(1, 0, 2, 0.05m),
            ["r.MotionBlur.Amount"] = Number(-1, -1, 1, 0.05m),
            ["r.MotionBlur.Max"] = Number(-1, -1, 100, 1),
            ["r.DepthOfFieldQuality"] = Quality(defaultValue: 0, maximum: 4),

            ["r.PostProcessAAQuality"] = Quality(defaultValue: 4, maximum: 6),
            ["r.TemporalAACurrentFrameWeight"] = Number(0.15m, 0.01m, 1, 0.01m),
            ["r.Tonemapper.Sharpen"] = Number(0.4m, 0, 2, 0.05m),

            ["r.ViewDistanceScale"] = Number(1.2m, 0.5m, 2, 0.05m),
            ["foliage.DensityScale"] = Number(1.5m, 0.25m, 3, 0.05m),
            ["grass.DensityScale"] = Number(1.5m, 0.25m, 3, 0.05m),
            ["r.SkeletalMeshLODBias"] = Number(0, -2, 4, 1),

            ["r.MaxAnisotropy"] = Choice(
                "16",
                ("0", "Game default"),
                ("2", "2×"),
                ("4", "4×"),
                ("8", "8×"),
                ("16", "16×")),
            ["r.Streaming.PoolSize"] = Number(4096, 256, 16384, 256),
            ["r.Streaming.MipBias"] = Number(0, -4, 16, 0.25m),
            ["r.Streaming.Boost"] = Number(1, 0.1m, 4, 0.1m),
            ["r.Streaming.LimitPoolSizeToVRAM"] = Toggle(defaultValue: true),
            ["r.Streaming.MaxNumTexturesToStreamPerFrame"] = Number(0, 0, 64, 1),
            ["r.Streaming.AmortizeCPUToGPUCopy"] = Toggle(defaultValue: false),
            ["r.Streaming.MaxEffectiveScreenSize"] = Number(0, 0, 16384, 256),

            ["r.AmbientOcclusionLevels"] = Choice(
                "-1",
                ("-1", "Automatic"),
                ("0", "Off"),
                ("1", "1 level"),
                ("2", "2 levels"),
                ("3", "3 levels"),
                ("4", "4 levels")),
            ["r.BloomQuality"] = Quality(defaultValue: 5, maximum: 5),
            ["r.EyeAdaptationQuality"] = Quality(defaultValue: 2, maximum: 2),
            ["r.SceneColorFringeQuality"] = Quality(defaultValue: 0, maximum: 1),
            ["r.LensFlareQuality"] = Quality(defaultValue: 2, maximum: 3),
            ["r.LightShaftQuality"] = Toggle(defaultValue: true),
            ["r.Tonemapper.GrainQuantization"] = Toggle(defaultValue: true),
            ["r.Tonemapper.Quality"] = Quality(defaultValue: 5, maximum: 5),
            ["r.AmbientOcclusionRadiusScale"] = Number(1.5m, 0.1m, 5, 0.1m),
            ["r.AmbientOcclusionMaxQuality"] = Number(100, 0, 100, 5),
            ["r.AmbientOcclusionMipLevelFactor"] = Number(0.6m, 0.1m, 2, 0.1m),
            ["r.Filter.SizeScale"] = Number(0.8m, 0.1m, 2, 0.05m),

            ["r.ShadowQuality"] = Quality(defaultValue: 4, maximum: 5),
            ["r.Shadow.CSM.MaxCascades"] = Number(2, 1, 4, 1),
            ["r.Shadow.MaxResolution"] = Choice(
                "1024",
                ("256", "256 px"),
                ("512", "512 px"),
                ("1024", "1024 px"),
                ("2048", "2048 px"),
                ("4096", "4096 px")),
            ["r.Shadow.DistanceScale"] = Number(0.4m, 0.1m, 2, 0.05m),
            ["r.DistanceFieldShadowing"] = Toggle(defaultValue: true),
            ["r.DistanceFieldAO"] = Toggle(defaultValue: true),
            ["r.VolumetricFog"] = Toggle(defaultValue: true),

            ["r.RefractionQuality"] = Quality(defaultValue: 2, maximum: 2),
            ["r.SSR.Quality"] = Quality(defaultValue: 2, maximum: 4),
            ["r.DetailMode"] = Quality(defaultValue: 1, maximum: 2),
            ["r.EmitterSpawnRateScale"] = Number(0.5m, 0, 2, 0.05m),
            ["r.ParticleLightQuality"] = Toggle(defaultValue: true),
            ["!StartupMovies"] = new(
                SettingEditorKind.Presence,
                "ClearArray",
                "Game.ini",
                "/Script/MoviePlayer.MoviePlayerSettings"),
            ["bWaitForMoviesToComplete"] = new(
                SettingEditorKind.Presence,
                "False",
                "Game.ini",
                "/Script/MoviePlayer.MoviePlayerSettings"),
            ["mod.VignettePercent"] = new(
                SettingEditorKind.Number,
                "100",
                "AncestorsEnhanced-Vignette_P.pak",
                "GraphicsMods",
                0,
                100,
                5,
                Target: SettingFileTarget.Pak,
                Unit: "%"),
            [SystemSaveSettingKeys.FullscreenResolution] = SystemChoice(
                "1280x720",
                ResolutionChoices()),
            [SystemSaveSettingKeys.WindowedResolution] = SystemChoice(
                "1680x1050",
                ResolutionChoices()),
            [SystemSaveSettingKeys.Brightness] = new(
                SettingEditorKind.Number,
                "1",
                "System.sav",
                "GraphicsOptions",
                0.5m,
                1.5m,
                0.05m,
                Target: SettingFileTarget.SystemSave,
                IsDirect: true),
            [SystemSaveSettingKeys.FrameRateLimit] = SystemChoice(
                "120",
                SystemGraphicsOptionCatalog.FrameRateLimits
                    .Select(value => new SettingChoice(
                        value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        value == 0 ? "Unlimited" : $"{value} FPS"))
                    .ToArray()),
            [SystemSaveSettingKeys.ViewDistanceQuality] = SystemQuality("High"),
            [SystemSaveSettingKeys.PostProcessingQuality] = SystemQuality("High"),
            [SystemSaveSettingKeys.ShadowQuality] = SystemQuality("High"),
            [SystemSaveSettingKeys.TextureQuality] = SystemQuality("High"),
            [SystemSaveSettingKeys.VisualEffectsQuality] = SystemQuality("High"),
            [SystemSaveSettingKeys.FoliageQuality] = SystemQuality("High"),
        };

    public static bool IsDefined(string? key) =>
        key is not null && Settings.ContainsKey(key);

    public static SettingEditSnapshot? Create(
        GameInspectionSnapshot snapshot,
        string key,
        string? currentOverride)
    {
        if (!IsVerifiedEditingTarget(snapshot) ||
            !Settings.TryGetValue(key, out SettingEditorTemplate? template))
        {
            return null;
        }

        IReadOnlyList<SettingChoice>? choices = template.Choices;
        if (template.IsDirect && template.Kind == SettingEditorKind.Choice &&
            currentOverride is not null &&
            choices?.Any(choice => string.Equals(choice.Value, currentOverride, StringComparison.Ordinal)) == false &&
            IsPlausibleResolution(currentOverride))
        {
            choices = [.. choices, new SettingChoice(currentOverride, currentOverride)];
        }

        var editor = new SettingEditSnapshot(
            template.FileName,
            template.Section,
            key,
            template.Kind,
            template.DefaultValue,
            currentOverride,
            template.Minimum,
            template.Maximum,
            template.Increment,
            choices,
            template.Target,
            template.Unit,
            template.IsDirect);
        editor = editor with
        {
            CanSetCustomValue = template.IsDirect || currentOverride is null || IsValidValue(editor, currentOverride),
        };
        return editor.IsDirect && !editor.CanSetCustomValue ? null : editor;
    }

    public static bool TryValidate(
        GameInspectionSnapshot snapshot,
        SettingChangeRequest request,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);

        // Validate against the actual current value from the snapshot so that an
        // unknown/unsupported existing override cannot silently bypass the same
        // restrictions the UI applies.
        string? currentOverride = FindCurrentOverride(snapshot, request);
        SettingEditSnapshot? editor = Create(snapshot, request.Key, currentOverride);
        if (editor is null ||
            !string.Equals(request.FileName, editor.FileName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.Section, editor.Section, StringComparison.OrdinalIgnoreCase))
        {
            error = $"{request.Key} is not editable for this game build.";
            return false;
        }

        if (request.Value is null)
        {
            error = editor.IsDirect ? $"{request.Key} requires a value." : null;
            return !editor.IsDirect;
        }

        if (!editor.CanSetCustomValue)
        {
            error = $"{request.Key} contains an unsupported current value and can only be reset.";
            return false;
        }

        bool valid = IsValidValue(editor, request.Value);

        error = valid ? null : $"{request.Value} is not a valid value for {request.Key}.";
        return valid;
    }

    private static string? FindCurrentOverride(
        GameInspectionSnapshot snapshot,
        SettingChangeRequest request)
    {
        // System.sav-backed settings live in the binary settings file.
        if (string.Equals(
                request.FileName,
                "System.sav",
                StringComparison.OrdinalIgnoreCase))
        {
            return ReadSystemSaveValue(snapshot, request.Key);
        }

        ConfigurationFileSnapshot? file = snapshot.ConfigurationFiles
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, request.FileName, StringComparison.OrdinalIgnoreCase));
        IniSettingSnapshot? entry = file?.Settings
            .LastOrDefault(setting =>
                string.Equals(setting.Key, request.Key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(setting.Section, request.Section, StringComparison.OrdinalIgnoreCase));
        return entry?.Value;
    }

    private static string? ReadSystemSaveValue(
        GameInspectionSnapshot snapshot,
        string key)
    {
        SystemGraphicsSettingsSnapshot? graphics = snapshot.BinarySettingsFile?.GraphicsSettings;
        if (graphics is null)
        {
            return null;
        }

        return graphics is null ? null : GetSystemValue(graphics, key);
    }

    public static bool IsVerifiedEditingTarget(GameInspectionSnapshot snapshot)
    {
        GameInstallationSnapshot? installation = snapshot.Installation;
        if (installation?.ExecutableExists != true)
        {
            return false;
        }

        bool supportedPlatform = installation switch
        {
            { Store: StoreKind.Steam, Host: HostKind.Windows, CompatibilityLayer: CompatibilityLayerKind.None } => true,
            { Store: StoreKind.Steam, Host: HostKind.Linux, CompatibilityLayer: CompatibilityLayerKind.Proton } => true,
            { Store: StoreKind.EpicGames or StoreKind.Gog, Host: HostKind.Windows, CompatibilityLayer: CompatibilityLayerKind.None } => true,
            _ => false,
        };
        if (!supportedPlatform)
        {
            return false;
        }

        // Steam build evidence is store-specific. Epic and GOG are recognised by the
        // verified content signature; a signature read error always fails closed.
        return GameIdentity.IsSupported(
            installation.Store,
            installation.BuildId,
            installation.ContentSignature,
            installation.ContentSignatureReadFailed);
    }

    public static string? GetCurrentSystemValue(GameInspectionSnapshot snapshot, string key)
    {
        SystemGraphicsSettingsSnapshot? graphics = snapshot.BinarySettingsFile?.GraphicsSettings;
        if (graphics is null)
        {
            return null;
        }

        return graphics is null ? null : GetSystemValue(graphics, key);
    }

    /// <summary>Maps a recognised System.sav setting key to its current string value.</summary>
    public static string? GetSystemValue(SystemGraphicsSettingsSnapshot graphics, string key) =>
        key switch
        {
            SystemSaveSettingKeys.FullscreenResolution =>
                $"{graphics.FullscreenWidth}x{graphics.FullscreenHeight}",
            SystemSaveSettingKeys.WindowedResolution =>
                $"{graphics.WindowedWidth}x{graphics.WindowedHeight}",
            SystemSaveSettingKeys.Brightness => Invariant((decimal)graphics.Brightness),
            SystemSaveSettingKeys.FrameRateLimit =>
                graphics.FrameRateLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SystemSaveSettingKeys.ViewDistanceQuality => graphics.ViewDistanceQuality.ToString(),
            SystemSaveSettingKeys.PostProcessingQuality => graphics.PostProcessingQuality.ToString(),
            SystemSaveSettingKeys.ShadowQuality => graphics.ShadowQuality.ToString(),
            SystemSaveSettingKeys.TextureQuality => graphics.TextureQuality.ToString(),
            SystemSaveSettingKeys.VisualEffectsQuality => graphics.VisualEffectsQuality.ToString(),
            SystemSaveSettingKeys.FoliageQuality => graphics.FoliageQuality.ToString(),
            _ => null,
        };

    private static bool IsValidValue(SettingEditSnapshot editor, string value) =>
        editor.Kind switch
        {
            SettingEditorKind.Toggle => value is "0" or "1",
            SettingEditorKind.Choice => editor.Choices?.Any(choice =>
                string.Equals(choice.Value, value, StringComparison.Ordinal)) == true,
            SettingEditorKind.Number => IsValidNumber(editor, value),
            SettingEditorKind.Presence => string.Equals(
                value,
                editor.DefaultValue,
                StringComparison.Ordinal),
            _ => false,
        };

    private static bool IsValidNumber(SettingEditSnapshot editor, string value)
    {
        if (!decimal.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal number) ||
            editor.Minimum is decimal minimum && number < minimum ||
            editor.Maximum is decimal maximum && number > maximum)
        {
            return false;
        }

        if (editor.Increment is not > 0 || editor.Minimum is null)
        {
            return true;
        }

        decimal steps = (number - editor.Minimum.Value) / editor.Increment.Value;
        return decimal.Abs(steps - decimal.Round(steps)) < 0.000001m;
    }

    private static SettingEditorTemplate Toggle(bool defaultValue) =>
        new(SettingEditorKind.Toggle, defaultValue ? "1" : "0");

    private static SettingEditorTemplate Number(
        decimal defaultValue,
        decimal minimum,
        decimal maximum,
        decimal increment) =>
        new(
            SettingEditorKind.Number,
            Invariant(defaultValue),
            Minimum: minimum,
            Maximum: maximum,
            Increment: increment);

    private static SettingEditorTemplate Quality(int defaultValue, int maximum) =>
        Choice(
            defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [.. Enumerable.Range(0, maximum + 1)
                .Select(value => (
                    value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    SelectQualityLabel(value)))]);


    private static string SelectQualityLabel(int value) => value switch
    {
        0 => "Off",
        1 => "Low",
        2 => "Medium",
        3 => "High",
        _ => $"Level {value}",
    };

    private static SettingEditorTemplate Choice(
        string defaultValue,
        params (string Value, string Label)[] choices) =>
        new(
            SettingEditorKind.Choice,
            defaultValue,
            Choices: choices.Select(choice => new SettingChoice(choice.Value, choice.Label)).ToArray());

    private static SettingEditorTemplate SystemChoice(
        string defaultValue,
        IReadOnlyList<SettingChoice> choices) =>
        new(
            SettingEditorKind.Choice,
            defaultValue,
            "System.sav",
            "GraphicsOptions",
            Choices: choices,
            Target: SettingFileTarget.SystemSave,
            IsDirect: true);

    private static SettingEditorTemplate SystemQuality(string defaultValue) =>
        SystemChoice(
            defaultValue,
            [
                new SettingChoice("Low", "Low"),
                new SettingChoice("Medium", "Medium"),
                new SettingChoice("High", "High"),
            ]);

    private static SettingChoice[] ResolutionChoices() =>
        [.. SystemGraphicsOptionCatalog.Resolutions
            .Select(value => new SettingChoice(
                value,
                value.Replace("x", " × ", StringComparison.Ordinal)))];

    private static string Invariant(decimal value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsPlausibleResolution(string value)
    {
        string[] parts = value.Split('x');
        return parts.Length == 2 &&
            int.TryParse(parts[0], out int width) &&
            int.TryParse(parts[1], out int height) &&
            width is >= 320 and <= 16384 && height is >= 200 and <= 8640;
    }
}
