using System.Globalization;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Core.Settings;

public static class ReadableSettingsCatalog
{
    private const string SystemSettingsSection = "SystemSettings";
    private const string MoviePlayerSection = "/Script/MoviePlayer.MoviePlayerSettings";
    private const string KnownHalfVignettePakSha256 =
        "06F74C5E4BF70D2748614D8C74405B4C96FB4E50F103A66827C4E2041B2801A0";

    public static IReadOnlyList<FeatureGroupSnapshot> CreateFeatureGroups(
        GameInspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return
        [
            CreateMotionBlurGroup(snapshot),
            CreateDepthOfFieldGroup(snapshot),
            CreateVignetteGroup(snapshot),
            CreateImageClarityGroup(snapshot),
            CreateViewDistanceGroup(snapshot),
            CreateTextureGroup(snapshot),
            CreatePostProcessingGroup(snapshot),
            CreateShadowGroup(snapshot),
            CreateEffectsGroup(snapshot),
            CreateGameMenuGroup(snapshot),
            CreateConvenienceGroup(snapshot),
        ];
    }

    private static FeatureGroupSnapshot CreateMotionBlurGroup(GameInspectionSnapshot snapshot)
    {
        FeatureSettingSnapshot quality = Quality(
            snapshot,
            "motion-blur-quality",
            "Quality and activation",
            "r.MotionBlurQuality",
            "Quality 0 disables motion blur; levels 1 to 4 select its rendering quality.",
            maximum: 4);
        FeatureSettingSnapshot scale = Decimal(
            snapshot,
            "motion-blur-scale",
            "Strength multiplier",
            "r.MotionBlur.Scale",
            value => Percent(value),
            "Multiplies the strength chosen by the game's cameras and post-process volumes.",
            missingValue: "100% (engine default)",
            missingSource: "No custom multiplier",
            maximum: 1);

        return new FeatureGroupSnapshot(
            "motion-blur",
            "Camera effects",
            "Motion blur",
            quality.Value,
            "Camera and object motion blur, with quality and strength kept separate.",
            IsEssential: true,
            quality.State,
            [
                quality,
                scale,
                Decimal(
                    snapshot,
                    "motion-blur-amount",
                    "Absolute strength override",
                    "r.MotionBlur.Amount",
                    value => value < 0 ? "Game controlled" : value.ToString("0.##", CultureInfo.InvariantCulture),
                    "Replaces the game's chosen strength instead of scaling it. -1 leaves it game controlled.",
                    missingValue: "Game controlled",
                    isAdvanced: true),
                Decimal(
                    snapshot,
                    "motion-blur-max",
                    "Maximum blur length",
                    "r.MotionBlur.Max",
                    value => value < 0 ? "Game controlled" : $"{value:0.##}% of screen width",
                    "Limits the maximum visible distortion caused by motion blur.",
                    missingValue: "Game controlled",
                    isAdvanced: true),
            ]);
    }

    private static FeatureGroupSnapshot CreateDepthOfFieldGroup(GameInspectionSnapshot snapshot)
    {
        FeatureSettingSnapshot quality = Quality(
            snapshot,
            "dof-quality",
            "Quality and activation",
            "r.DepthOfFieldQuality",
            "Quality 0 disables depth of field; higher levels change rendering quality, not a simple intensity.",
            maximum: 4);

        return new FeatureGroupSnapshot(
            "depth-of-field",
            "Camera effects",
            "Depth of field",
            quality.Value,
            "Focus blur controlled by the renderer and by game-specific camera assets.",
            IsEssential: true,
            quality.State,
            [
                quality,
                Informational(
                    "dof-strength",
                    "Effect strength",
                    "Controlled by game camera assets",
                    "Ancestors uses separate default and progression-related DOF camera animations. No safe global intensity override has been verified.",
                    "Camera assets",
                    "DepthOfFieldScale"),
                Integer(
                    snapshot,
                    "dof-rings",
                    "Gather sample rings",
                    "r.DOF.Gather.RingCount",
                    value => value.ToString(CultureInfo.InvariantCulture),
                    "Controls the number of sampling rings used for higher-quality blur gathering.",
                    isAdvanced: true),
                Integer(
                    snapshot,
                    "dof-accumulator-quality",
                    "Gather accumulator quality",
                    "r.DOF.Gather.AccumulatorQuality",
                    FormatQualityLevel,
                    "Controls the quality of the accumulator used by the gather stage.",
                    isAdvanced: true),
                Integer(
                    snapshot,
                    "dof-postfilter-method",
                    "Gather post-filter method",
                    "r.DOF.Gather.PostfilterMethod",
                    value => value switch
                    {
                        0 => "Off",
                        1 => "Median 3×3",
                        2 => "Maximum 3×3",
                        _ => $"Method {value}",
                    },
                    "Selects the small post-filter applied after depth-of-field gathering.",
                    isAdvanced: true),
                Boolean(
                    snapshot,
                    "dof-gather-bokeh",
                    "Gather bokeh simulation",
                    "r.DOF.Gather.EnableBokehSettings",
                    "Applies bokeh settings during the gather stage.",
                    isAdvanced: true),
                Boolean(
                    snapshot,
                    "dof-scatter-bokeh",
                    "Scatter bokeh simulation",
                    "r.DOF.Scatter.EnableBokehSettings",
                    "Applies bokeh settings while scattering bright out-of-focus areas.",
                    isAdvanced: true),
                Integer(
                    snapshot,
                    "dof-foreground-compositing",
                    "Foreground scatter compositing",
                    "r.DOF.Scatter.ForegroundCompositing",
                    value => value == 0 ? "Off" : $"Mode {value}",
                    "Controls how scattered foreground blur is composited.",
                    isAdvanced: true),
                Integer(
                    snapshot,
                    "dof-background-compositing",
                    "Background scatter compositing",
                    "r.DOF.Scatter.BackgroundCompositing",
                    value => value == 0 ? "Off" : $"Mode {value}",
                    "Controls how scattered background blur is composited.",
                    isAdvanced: true),
                Decimal(
                    snapshot,
                    "dof-scatter-sprite-ratio",
                    "Maximum scattered-sprite ratio",
                    "r.DOF.Scatter.MaxSpriteRatio",
                    Percent,
                    "Limits how much of the image may use scattered bokeh sprites.",
                    isAdvanced: true,
                    maximum: 1),
                Integer(
                    snapshot,
                    "dof-recombine-quality",
                    "Recombine quality",
                    "r.DOF.Recombine.Quality",
                    value => $"Level {value}",
                    "Controls the final slight-out-of-focus recombination pass.",
                    isAdvanced: true),
                Boolean(
                    snapshot,
                    "dof-recombine-bokeh",
                    "Recombine bokeh simulation",
                    "r.DOF.Recombine.EnableBokehSettings",
                    "Applies bokeh settings during the final recombination pass.",
                    isAdvanced: true),
                Integer(
                    snapshot,
                    "dof-temporal-quality",
                    "DOF temporal accumulation",
                    "r.DOF.TemporalAAQuality",
                    FormatQualityLevel,
                    "Controls temporal accumulation quality used to stabilize depth of field.",
                    isAdvanced: true),
                Decimal(
                    snapshot,
                    "dof-foreground-radius",
                    "Maximum foreground radius",
                    "r.DOF.Kernel.MaxForegroundRadius",
                    Invariant,
                    "Limits the foreground blur radius in screen space.",
                    isAdvanced: true),
                Decimal(
                    snapshot,
                    "dof-background-radius",
                    "Maximum background radius",
                    "r.DOF.Kernel.MaxBackgroundRadius",
                    Invariant,
                    "Limits the background blur radius in screen space.",
                    isAdvanced: true),
            ]);
    }

    private static FeatureGroupSnapshot CreateVignetteGroup(GameInspectionSnapshot snapshot)
    {
        PakFileSnapshot? knownPatch = snapshot.PakFiles.FirstOrDefault(pak =>
            string.Equals(pak.Sha256, KnownHalfVignettePakSha256, StringComparison.OrdinalIgnoreCase));
        bool hasUnknownPatch = snapshot.PakFiles.Any(pak =>
            pak.Classification == PakClassification.PatchStyle);

        FeatureSettingSnapshot environment = knownPatch is not null
            ? new FeatureSettingSnapshot(
                "environment-vignette",
                "Environmental vignette",
                "50% of original",
                "The day/night curve is reduced from 0.40-0.80 to 0.20-0.40; it is not a constant value of 0.50.",
                "Verified patch fingerprint",
                "VL01E01_Vignette_Intensity",
                ReadableSettingState.Modified,
                IsAdvanced: false,
                Percentage: 0.5)
            : new FeatureSettingSnapshot(
                "environment-vignette",
                "Environmental vignette",
                hasUnknownPatch ? "Unknown custom package" : "Game controlled",
                "The game's day/night system varies vignette intensity over time.",
                hasUnknownPatch ? "Unrecognized patch package" : "No verified patch",
                "VL01E01_Vignette_Intensity",
                ReadableSettingState.Unknown,
                IsAdvanced: false);

        return new FeatureGroupSnapshot(
            "vignette",
            "Camera effects",
            "Vignette",
            environment.Value,
            "Environmental edge darkening, separate from temporary gameplay status effects.",
            IsEssential: true,
            environment.State,
            [
                environment,
                Informational(
                    "gameplay-screen-effects",
                    "Gameplay status effects",
                    "Unchanged",
                    "Damage, panic, hunger, tiredness, cold, poison, sprint and outline use separate post-process materials.",
                    "Gameplay post-process assets",
                    "GameplayPostProcessManager",
                    isAdvanced: true),
            ]);
    }

    private static FeatureGroupSnapshot CreateImageClarityGroup(GameInspectionSnapshot snapshot)
    {
        FeatureSettingSnapshot antiAliasing = Quality(
            snapshot,
            "aa-quality",
            "Anti-aliasing quality",
            "r.PostProcessAAQuality",
            "Selects the anti-aliasing method and quality used by the active game preset.",
            maximum: 6);
        FeatureSettingSnapshot taa = Decimal(
            snapshot,
            "taa-frame-weight",
            "TAA current-frame weight",
            "r.TemporalAACurrentFrameWeight",
            InvariantTwoDecimals,
            "Higher values respond faster but can shimmer more; lower values are steadier but can ghost more.",
            maximum: 1);
        FeatureSettingSnapshot sharpen = Decimal(
            snapshot,
            "image-sharpening",
            "Image sharpening",
            "r.Tonemapper.Sharpen",
            value => value == 0 ? "Off" : InvariantTwoDecimals(value),
            "Adds sharpening after temporal anti-aliasing.");

        return new FeatureGroupSnapshot(
            "image-clarity",
            "Image quality",
            "Image clarity and anti-aliasing",
            CreateCompactSummary(("Sharpen", sharpen), ("TAA", taa)),
            "Controls edge smoothing, temporal response and final image sharpening.",
            IsEssential: true,
            CombineStates(antiAliasing, taa, sharpen),
            [antiAliasing, taa, sharpen]);
    }

    private static FeatureGroupSnapshot CreateViewDistanceGroup(GameInspectionSnapshot snapshot)
    {
        FeatureSettingSnapshot viewDistance = Decimal(
            snapshot,
            "view-distance",
            "View distance",
            "r.ViewDistanceScale",
            Percent,
            "Scales how far distant objects remain visible.");

        return new FeatureGroupSnapshot(
            "view-distance-foliage",
            "World detail",
            "View distance and foliage",
            viewDistance.Value,
            "Object range and vegetation density are separate controls.",
            IsEssential: true,
            CombineStates(viewDistance),
            [
                viewDistance,
                Decimal(
                    snapshot,
                    "foliage-density",
                    "Foliage density",
                    "foliage.DensityScale",
                    Percent,
                    "Changes how much non-grass foliage is spawned; it does not primarily control range."),
                Decimal(
                    snapshot,
                    "grass-density",
                    "Grass density",
                    "grass.DensityScale",
                    Percent,
                    "Changes the amount of grass spawned by the foliage system."),
                Integer(
                    snapshot,
                    "skeletal-lod-bias",
                    "Character and animal LOD bias",
                    "r.SkeletalMeshLODBias",
                    value => value.ToString(CultureInfo.InvariantCulture),
                    "Biases skeletal meshes toward lower or higher detail levels.",
                    isAdvanced: true),
            ]);
    }

    private static FeatureGroupSnapshot CreateTextureGroup(GameInspectionSnapshot snapshot)
    {
        FeatureSettingSnapshot anisotropy = Integer(
            snapshot,
            "anisotropic-filtering",
            "Texture filtering",
            "r.MaxAnisotropy",
            value => value <= 1 ? "Off" : $"{value}×",
            "Keeps surfaces sharp when viewed at an angle.",
            maximum: 16);
        FeatureSettingSnapshot pool = Integer(
            snapshot,
            "texture-pool",
            "Texture memory budget",
            "r.Streaming.PoolSize",
            FormatMegabytes,
            "Maximum memory reserved for streamed textures.");

        return new FeatureGroupSnapshot(
            "textures",
            "Image quality",
            "Textures and streaming",
            $"{anisotropy.Value} · {pool.Value}",
            "Texture filtering, memory use and background streaming behavior.",
            IsEssential: true,
            CombineStates(anisotropy, pool),
            [
                anisotropy,
                pool,
                Decimal(
                    snapshot,
                    "texture-mip-bias",
                    "Texture mip bias",
                    "r.Streaming.MipBias",
                    Invariant,
                    "Positive values select lower-resolution texture mip levels sooner.",
                    isAdvanced: true),
                Decimal(
                    snapshot,
                    "texture-streaming-boost",
                    "Streaming resolution boost",
                    "r.Streaming.Boost",
                    Percent,
                    "Scales the texture resolution requested by the streaming system.",
                    isAdvanced: true),
                Boolean(
                    snapshot,
                    "texture-limit-vram",
                    "Limit pool to detected VRAM",
                    "r.Streaming.LimitPoolSizeToVRAM",
                    "Prevents the streaming pool from exceeding the detected graphics-memory budget.",
                    isAdvanced: true),
                Integer(
                    snapshot,
                    "texture-stream-count",
                    "Textures streamed per frame",
                    "r.Streaming.MaxNumTexturesToStreamPerFrame",
                    value => value.ToString(CultureInfo.InvariantCulture),
                    "Limits how many textures may be updated in one frame.",
                    isAdvanced: true),
                Boolean(
                    snapshot,
                    "texture-amortize-copy",
                    "Spread streaming updates",
                    "r.Streaming.AmortizeCPUToGPUCopy",
                    "Distributes CPU-to-GPU texture update work over multiple frames.",
                    isAdvanced: true),
                Integer(
                    snapshot,
                    "texture-max-screen-size",
                    "Maximum effective screen size",
                    "r.Streaming.MaxEffectiveScreenSize",
                    value => value == 0 ? "Unlimited by override" : $"{value} px",
                    "Caps the screen size used when calculating wanted texture resolution.",
                    isAdvanced: true),
            ]);
    }

    private static FeatureGroupSnapshot CreatePostProcessingGroup(GameInspectionSnapshot snapshot)
    {
        FeatureSettingSnapshot[] settings =
        [
            Integer(snapshot, "ao-levels", "Ambient occlusion levels", "r.AmbientOcclusionLevels", FormatAutoLevel, "Controls the number of screen-space ambient-occlusion levels. -1 lets the renderer choose."),
            Integer(snapshot, "bloom-quality", "Bloom quality", "r.BloomQuality", FormatQualityLevel, "Controls the quality of glow around bright areas."),
            Integer(snapshot, "eye-adaptation", "Eye adaptation", "r.EyeAdaptationQuality", FormatOffOrQuality, "Controls automatic exposure adaptation when moving between bright and dark areas."),
            Integer(snapshot, "chromatic-aberration", "Chromatic aberration", "r.SceneColorFringeQuality", FormatOffOrQuality, "Controls colored lens fringing, mainly near image edges."),
            Integer(snapshot, "lens-flare", "Lens flares", "r.LensFlareQuality", FormatOffOrQuality, "Controls lens-flare rendering quality."),
            Boolean(snapshot, "light-shafts", "Light shafts", "r.LightShaftQuality", "Controls atmospheric sunbeams and similar light shafts."),
            Boolean(snapshot, "gradient-smoothing", "Color-gradient smoothing", "r.Tonemapper.GrainQuantization", "Adds subtle dithering to reduce visible color banding."),
            Integer(snapshot, "tonemapper-quality", "Tonemapper quality", "r.Tonemapper.Quality", FormatQualityLevel, "Controls quality features in the final tone-mapping pass.", isAdvanced: true),
            Integer(snapshot, "render-target-pool", "Render-target pool minimum", "r.RenderTargetPoolMin", value => $"{value} MB", "Minimum pooled memory reserved for intermediate rendering targets.", isAdvanced: true),
            Decimal(snapshot, "ao-radius", "AO radius scale", "r.AmbientOcclusionRadiusScale", Percent, "Scales the radius used by screen-space ambient occlusion.", isAdvanced: true),
            Integer(snapshot, "ao-max-quality", "AO maximum quality", "r.AmbientOcclusionMaxQuality", value => $"{value}%", "Caps the ambient-occlusion quality selected by the renderer.", isAdvanced: true, maximum: 100),
            Decimal(snapshot, "ao-mip-factor", "AO mip-level factor", "r.AmbientOcclusionMipLevelFactor", Invariant, "Controls how ambient occlusion uses lower-resolution mip levels.", isAdvanced: true),
            Integer(snapshot, "fast-blur-threshold", "Fast-blur threshold", "r.FastBlurThreshold", value => value.ToString(CultureInfo.InvariantCulture), "Selects when a faster Gaussian-blur path is used.", isAdvanced: true),
            Integer(snapshot, "upscale-quality", "Upscale quality", "r.Upscale.Quality", FormatQualityLevel, "Controls the image upscaling filter used by the renderer.", isAdvanced: true),
            Decimal(snapshot, "filter-size", "Post-process filter size", "r.Filter.SizeScale", Percent, "Scales the size of several post-processing filters.", isAdvanced: true),
        ];

        int overrides = CountOverrides(snapshot, settings);
        return new FeatureGroupSnapshot(
            "post-processing",
            "Visual effects",
            "Post-processing",
            overrides == 0 ? "Game preset" : $"{overrides} custom overrides",
            "Lighting and lens-style effects applied after the scene is rendered.",
            IsEssential: true,
            CombineStates(settings),
            settings);
    }

    private static FeatureGroupSnapshot CreateShadowGroup(GameInspectionSnapshot snapshot)
    {
        FeatureSettingSnapshot[] settings =
        [
            Integer(snapshot, "shadow-quality", "Shadow quality", "r.ShadowQuality", FormatOffOrQuality, "Controls the main shadow-rendering quality."),
            Integer(snapshot, "shadow-cascades", "Directional-light cascades", "r.Shadow.CSM.MaxCascades", value => value.ToString(CultureInfo.InvariantCulture), "Controls how many cascades are used for directional-light shadows."),
            Integer(snapshot, "shadow-resolution", "Maximum shadow resolution", "r.Shadow.MaxResolution", value => $"{value} px", "Caps the resolution of individual shadow maps."),
            Decimal(snapshot, "shadow-distance", "Shadow distance scale", "r.Shadow.DistanceScale", Percent, "Scales the distance covered by dynamic shadows."),
            Boolean(snapshot, "distance-field-shadows", "Distance-field shadows", "r.DistanceFieldShadowing", "Enables distance-field shadow rendering."),
            Boolean(snapshot, "distance-field-ao", "Distance-field ambient occlusion", "r.DistanceFieldAO", "Enables ambient occlusion derived from mesh distance fields."),
            Boolean(snapshot, "volumetric-fog", "Volumetric fog", "r.VolumetricFog", "Enables volumetric fog rendering."),
            Integer(snapshot, "light-function-quality", "Light-function quality", "r.LightFunctionQuality", FormatOffOrQuality, "Controls projected light-function quality.", isAdvanced: true),
            Integer(snapshot, "shadow-csm-resolution", "CSM maximum resolution", "r.Shadow.MaxCSMResolution", value => $"{value} px", "Caps cascaded shadow-map resolution.", isAdvanced: true),
            Decimal(snapshot, "shadow-radius-threshold", "Shadow radius threshold", "r.Shadow.RadiusThreshold", Invariant, "Controls when small projected shadows stop being rendered.", isAdvanced: true),
            Decimal(snapshot, "shadow-transition", "Cascade transition scale", "r.Shadow.CSM.TransitionScale", Percent, "Scales transitions between cascaded shadow maps.", isAdvanced: true),
            Decimal(snapshot, "preshadow-resolution", "Pre-shadow resolution factor", "r.Shadow.PreShadowResolutionFactor", Percent, "Scales pre-shadow texture resolution.", isAdvanced: true),
            Integer(snapshot, "ao-quality", "Distance-field AO quality", "r.AOQuality", FormatQualityLevel, "Controls distance-field ambient-occlusion quality.", isAdvanced: true),
            Integer(snapshot, "fog-grid-pixel-size", "Volumetric-fog grid pixel size", "r.VolumetricFog.GridPixelSize", value => value.ToString(CultureInfo.InvariantCulture), "Larger values reduce fog-grid resolution and cost.", isAdvanced: true),
            Integer(snapshot, "fog-grid-depth", "Volumetric-fog depth slices", "r.VolumetricFog.GridSizeZ", value => value.ToString(CultureInfo.InvariantCulture), "Controls the number of depth slices in the volumetric-fog grid.", isAdvanced: true),
            Integer(snapshot, "fog-history-samples", "Fog history supersamples", "r.VolumetricFog.HistoryMissSupersampleCount", value => value.ToString(CultureInfo.InvariantCulture), "Controls supersampling when volumetric-fog history is unavailable.", isAdvanced: true),
            Decimal(snapshot, "light-distance", "Light draw-distance scale", "r.LightMaxDrawDistanceScale", Percent, "Scales the maximum draw distance of local lights.", isAdvanced: true),
            Boolean(snapshot, "capsule-shadows", "Capsule shadows", "r.CapsuleShadows", "Enables soft capsule shadows used by supported characters.", isAdvanced: true),
        ];

        int overrides = CountOverrides(snapshot, settings);
        return new FeatureGroupSnapshot(
            "shadows-lighting",
            "Lighting",
            "Shadows, fog and lighting",
            overrides == 0 ? "Game preset" : $"{overrides} custom overrides",
            "The expensive shadow and atmospheric settings are grouped here.",
            IsEssential: true,
            CombineStates(settings),
            settings);
    }

    private static FeatureGroupSnapshot CreateEffectsGroup(GameInspectionSnapshot snapshot)
    {
        FeatureSettingSnapshot[] settings =
        [
            Integer(snapshot, "refraction-quality", "Refraction quality", "r.RefractionQuality", FormatOffOrQuality, "Controls distortion through refractive materials."),
            Integer(snapshot, "ssr-quality", "Screen-space reflections", "r.SSR.Quality", FormatOffOrQuality, "Controls reflections derived from the currently visible scene."),
            Integer(snapshot, "translucency-volume", "Translucency lighting volume", "r.TranslucencyLightingVolumeDim", value => $"{value}³", "Sets the resolution of the volume used to light translucent materials."),
            Boolean(snapshot, "translucency-blur", "Translucency volume blur", "r.TranslucencyVolumeBlur", "Filters the translucency lighting volume."),
            Integer(snapshot, "scene-color-format", "Scene-color format", "r.SceneColorFormat", value => $"Format {value}", "Selects the precision and memory format of the intermediate scene color."),
            Integer(snapshot, "detail-mode", "Actor detail mode", "r.DetailMode", FormatQualityLevel, "Controls whether actors marked for higher detail levels are rendered."),
            Integer(snapshot, "material-quality", "Material quality", "r.MaterialQualityLevel", FormatQualityLevel, "Selects quality branches authored inside materials."),
            Decimal(snapshot, "sss-scale", "Subsurface-scattering scale", "r.SSS.Scale", Percent, "Scales subsurface scattering used by skin and similar materials."),
            Integer(snapshot, "sss-quality", "Subsurface-scattering quality", "r.SSS.Quality", FormatQualityLevel, "Controls subsurface-scattering quality."),
            Integer(snapshot, "sss-samples", "Subsurface sample set", "r.SSS.SampleSet", FormatQualityLevel, "Selects the subsurface-scattering sample set."),
            Boolean(snapshot, "sss-half-resolution", "Half-resolution subsurface scattering", "r.SSS.HalfRes", "Uses a lower-resolution subsurface-scattering path."),
            Decimal(snapshot, "particle-rate", "Particle spawn-rate scale", "r.EmitterSpawnRateScale", Percent, "Scales the number of particles spawned by emitters."),
            Integer(snapshot, "particle-lights", "Particle-light quality", "r.ParticleLightQuality", FormatOffOrQuality, "Controls dynamic lights emitted by particles."),
        ];

        int overrides = CountOverrides(snapshot, settings);
        return new FeatureGroupSnapshot(
            "effects-materials",
            "Renderer",
            "Effects and materials",
            overrides == 0 ? "Game preset" : $"{overrides} custom overrides",
            "Advanced material, reflection, translucency and particle controls.",
            IsEssential: false,
            CombineStates(settings),
            settings);
    }

    private static FeatureGroupSnapshot CreateGameMenuGroup(GameInspectionSnapshot snapshot)
    {
        bool exists = snapshot.BinarySettingsFile?.Exists == true;
        string value = exists ? "Current value not readable yet" : "Not available";
        string source = exists ? "System.sav custom binary data" : "System.sav not found";

        string[] settings =
        [
            "Display mode",
            "Windowed resolution",
            "Fullscreen resolution",
            "Vertical synchronization",
            "Frame-rate limit",
            "Brightness",
            "Overall quality level",
            "Custom-quality state",
            "View-distance preset",
            "Shadow preset",
            "Post-processing preset",
            "Texture preset",
            "Visual-effects preset",
            "Foliage preset",
        ];

        FeatureSettingSnapshot[] details = settings
            .Select((name, index) => Informational(
                $"game-menu-{index}",
                name,
                value,
                "The field exists in System.sav, but Ancestors' custom binary format is not yet read reliably enough to show its current value.",
                source,
                "System.sav"))
            .ToArray();

        return new FeatureGroupSnapshot(
            "game-menu-settings",
            "Game settings",
            "Built-in graphics menu",
            exists ? "Current values not readable yet" : "System.sav not found",
            "Settings saved by Ancestors in its custom binary system file.",
            IsEssential: false,
            ReadableSettingState.Unknown,
            details);
    }

    private static FeatureGroupSnapshot CreateConvenienceGroup(GameInspectionSnapshot snapshot)
    {
        IniSettingSnapshot? clearMovies = FindSetting(snapshot, MoviePlayerSection, "!StartupMovies");
        bool skipped = string.Equals(clearMovies?.Value, "ClearArray", StringComparison.OrdinalIgnoreCase);
        FeatureSettingSnapshot startup = new(
            "startup-videos",
            "Startup splash videos",
            skipped ? "Skipped" : "Game default",
            "Controls whether the startup splash movies are played.",
            skipped ? "Game.ini override" : "No verified override",
            "!StartupMovies",
            skipped ? ReadableSettingState.Enabled : ReadableSettingState.Unknown,
            IsAdvanced: false,
            Editor: EditableSettingsCatalog.Create(snapshot, "!StartupMovies", clearMovies?.Value));

        return new FeatureGroupSnapshot(
            "convenience",
            "Convenience",
            "Startup and convenience",
            skipped ? "Videos skipped" : "Game default",
            "Non-visual quality-of-life options owned by the configurator.",
            IsEssential: true,
            startup.State,
            [startup]);
    }

    private static FeatureSettingSnapshot Quality(
        GameInspectionSnapshot snapshot,
        string id,
        string name,
        string key,
        string description,
        int maximum,
        bool isAdvanced = false) =>
        Integer(
            snapshot,
            id,
            name,
            key,
            FormatOffOrQuality,
            description,
            isAdvanced: isAdvanced,
            maximum: maximum);

    private static FeatureSettingSnapshot Boolean(
        GameInspectionSnapshot snapshot,
        string id,
        string name,
        string key,
        string description,
        bool isAdvanced = false)
    {
        return Integer(
            snapshot,
            id,
            name,
            key,
            value => value > 0 ? "On" : "Off",
            description,
            isAdvanced: isAdvanced,
            maximum: 1);
    }

    private static FeatureSettingSnapshot Integer(
        GameInspectionSnapshot snapshot,
        string id,
        string name,
        string key,
        Func<int, string> format,
        string description,
        string missingValue = "Game preset",
        string missingSource = "No custom override",
        bool isAdvanced = false,
        int? maximum = null)
    {
        IniSettingSnapshot? entry = FindSetting(snapshot, SystemSettingsSection, key);
        SettingEditSnapshot? editor = EditableSettingsCatalog.Create(snapshot, key, entry?.Value);
        if (entry is null)
        {
            FeatureSettingSnapshot? preset = CreatePresetSetting(
                snapshot,
                id,
                name,
                key,
                description,
                isAdvanced,
                rawValue => int.TryParse(
                    rawValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int presetValue)
                    ? format(presetValue)
                    : null);
            if (preset is not null)
            {
                return preset with { Editor = editor };
            }

            return Unknown(id, name, missingValue, description, missingSource, key, isAdvanced)
                with
            { Editor = editor };
        }

        if (!int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return Unknown(
                id,
                name,
                $"Unreadable override: {entry.Value}",
                description,
                "Engine.ini value is not a valid integer",
                key,
                isAdvanced) with
            { Editor = editor };
        }

        return new FeatureSettingSnapshot(
            id,
            name,
            format(value),
            description,
            "Engine.ini override",
            key,
            value == 0 ? ReadableSettingState.Disabled : ReadableSettingState.Modified,
            isAdvanced,
            maximum is > 0 ? Math.Clamp(value / (double)maximum.Value, 0, 1) : null,
            Editor: editor);
    }

    private static FeatureSettingSnapshot Decimal(
        GameInspectionSnapshot snapshot,
        string id,
        string name,
        string key,
        Func<double, string> format,
        string description,
        string missingValue = "Game preset",
        string missingSource = "No custom override",
        bool isAdvanced = false,
        double? maximum = null)
    {
        IniSettingSnapshot? entry = FindSetting(snapshot, SystemSettingsSection, key);
        SettingEditSnapshot? editor = EditableSettingsCatalog.Create(snapshot, key, entry?.Value);
        if (entry is null)
        {
            FeatureSettingSnapshot? preset = CreatePresetSetting(
                snapshot,
                id,
                name,
                key,
                description,
                isAdvanced,
                rawValue => double.TryParse(
                    rawValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double presetValue)
                    ? format(presetValue)
                    : null);
            if (preset is not null)
            {
                return preset with { Editor = editor };
            }

            return Unknown(id, name, missingValue, description, missingSource, key, isAdvanced)
                with
            { Editor = editor };
        }

        if (!double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return Unknown(
                id,
                name,
                $"Unreadable override: {entry.Value}",
                description,
                "Engine.ini value is not a valid number",
                key,
                isAdvanced) with
            { Editor = editor };
        }

        return new FeatureSettingSnapshot(
            id,
            name,
            format(value),
            description,
            "Engine.ini override",
            key,
            value == 0 ? ReadableSettingState.Disabled : ReadableSettingState.Modified,
            isAdvanced,
            maximum is > 0 ? Math.Clamp(value / maximum.Value, 0, 1) : null,
            Editor: editor);
    }

    private static FeatureSettingSnapshot? CreatePresetSetting(
        GameInspectionSnapshot snapshot,
        string id,
        string name,
        string key,
        string description,
        bool isAdvanced,
        Func<string, string?> format)
    {
        if (!AncestorsScalabilityPresetCatalog.TryGet(
                snapshot.Installation?.BuildId,
                key,
                out ScalabilityPresetValues? presetValues))
        {
            return null;
        }

        (string Name, string Value)[] values = presetValues
            .Enumerate()
            .Select(preset => (
                preset.Name,
                preset.RawValue is null
                    ? "Not set by this preset"
                    : format(preset.RawValue) ?? $"Raw value {preset.RawValue}"))
            .ToArray();

        string[] distinctValues = values
            .Select(value => value.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new FeatureSettingSnapshot(
            id,
            name,
            $"Game preset · {string.Join(" / ", distinctValues)}",
            description,
            $"Ancestors build {AncestorsScalabilityPresetCatalog.SupportedBuildId} preset table; active level not read from System.sav",
            key,
            ReadableSettingState.Unknown,
            isAdvanced,
            Percentage: null,
            PresetDetails: string.Join(" · ", values.Select(value => $"{value.Name}: {value.Value}")));
    }

    private static FeatureSettingSnapshot Informational(
        string id,
        string name,
        string value,
        string description,
        string source,
        string? technicalKey,
        bool isAdvanced = false) =>
        new(
            id,
            name,
            value,
            description,
            source,
            technicalKey,
            ReadableSettingState.Unknown,
            isAdvanced);

    private static FeatureSettingSnapshot Unknown(
        string id,
        string name,
        string value,
        string description,
        string source,
        string key,
        bool isAdvanced) =>
        new(
            id,
            name,
            value,
            description,
            source,
            key,
            ReadableSettingState.Unknown,
            isAdvanced);

    private static IniSettingSnapshot? FindSetting(
        GameInspectionSnapshot snapshot,
        string section,
        string key) =>
        snapshot.ConfigurationFiles
            .SelectMany(file => file.Settings)
            .LastOrDefault(setting =>
                string.Equals(setting.Section, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase));

    private static int CountOverrides(
        GameInspectionSnapshot snapshot,
        IEnumerable<FeatureSettingSnapshot> settings)
    {
        HashSet<string> keys = settings
            .Where(setting => setting.TechnicalKey is not null)
            .Select(setting => setting.TechnicalKey!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return snapshot.ConfigurationFiles
            .SelectMany(file => file.Settings)
            .Where(setting => string.Equals(
                setting.Section,
                SystemSettingsSection,
                StringComparison.OrdinalIgnoreCase))
            .Select(setting => setting.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(keys.Contains);
    }

    private static ReadableSettingState CombineStates(params FeatureSettingSnapshot[] settings)
    {
        if (settings.Any(setting => setting.State == ReadableSettingState.Modified))
        {
            return ReadableSettingState.Modified;
        }

        if (settings.Any(setting => setting.State == ReadableSettingState.Enabled))
        {
            return ReadableSettingState.Enabled;
        }

        if (settings.Any(setting => setting.State == ReadableSettingState.Disabled))
        {
            return ReadableSettingState.Disabled;
        }

        return ReadableSettingState.Unknown;
    }

    private static string CreateCompactSummary(
        params (string Label, FeatureSettingSnapshot Setting)[] values)
    {
        (string Label, FeatureSettingSnapshot Setting)[] custom = values
            .Where(value => value.Setting.State != ReadableSettingState.Unknown)
            .ToArray();
        return custom.Length == 0
            ? "Game preset"
            : string.Join(" · ", custom.Select(value => $"{value.Label} {value.Setting.Value}"));
    }

    private static string FormatOffOrQuality(int value) =>
        value == 0 ? "Off" : $"Quality {value}";

    private static string FormatQualityLevel(int value) => $"Level {value}";

    private static string FormatAutoLevel(int value) =>
        value < 0 ? "Automatic" : value == 0 ? "Off" : $"Level {value}";

    private static string FormatMegabytes(int value) =>
        value >= 1024 ? $"{value / 1024d:0.##} GB" : $"{value} MB";

    private static string Percent(double value) => $"{value * 100:0.##}%";

    private static string Invariant(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string InvariantTwoDecimals(double value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
