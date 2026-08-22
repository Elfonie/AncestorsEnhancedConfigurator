using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Core.Settings;

internal sealed record ScalabilityPresetValues(
    string? Low,
    string? Medium,
    string? High)
{
    public IEnumerable<(string Name, string? RawValue)> Enumerate()
    {
        yield return ("Low", Low);
        yield return ("Medium", Medium);
        yield return ("High", High);
    }

    public string? Get(GameGraphicsQuality quality) => quality switch
    {
        GameGraphicsQuality.Low => Low,
        GameGraphicsQuality.Medium => Medium,
        GameGraphicsQuality.High => High,
        _ => null,
    };
}

internal static class AncestorsScalabilityPresetCatalog
{
    public const string SupportedBuildId = AncestorsGameProfile.SupportedSteamBuildId;
    public const string SupportedContentSignature = AncestorsGameProfile.SupportedContentSignature;

    private static readonly Dictionary<string, ScalabilityPresetValues> Values =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["r.PostProcessAAQuality"] = new("0", "3", "4"),

            ["r.SkeletalMeshLODBias"] = new("1", "0", "0"),
            ["r.ViewDistanceScale"] = new("0.8", "0.9", "1.0"),

            ["r.AOQuality"] = new(null, null, "1"),
            ["r.CapsuleShadows"] = new("0", "1", "1"),
            ["r.DistanceFieldAO"] = new("0", "0", "1"),
            ["r.DistanceFieldShadowing"] = new("0", "0", "1"),
            ["r.LightFunctionQuality"] = new("0", "1", "1"),
            ["r.LightMaxDrawDistanceScale"] = new("0", ".5", "1"),
            ["r.Shadow.CSM.MaxCascades"] = new("1", "2", "2"),
            ["r.Shadow.CSM.TransitionScale"] = new("0", "0.25", "0.8"),
            ["r.Shadow.DistanceScale"] = new("0.2", "0.4", "0.4"),
            ["r.Shadow.MaxCSMResolution"] = new("512", "2048", "2048"),
            ["r.Shadow.MaxResolution"] = new("512", "1024", "1024"),
            ["r.Shadow.PreShadowResolutionFactor"] = new("0.5", "0.5", "0.5"),
            ["r.Shadow.RadiusThreshold"] = new("0.06", "0.05", "0.04"),
            ["r.ShadowQuality"] = new("0", "4", "5"),
            ["r.VolumetricFog"] = new("1", "1", "1"),
            ["r.VolumetricFog.GridPixelSize"] = new("16", "16", "16"),
            ["r.VolumetricFog.GridSizeZ"] = new("64", "64", "64"),
            ["r.VolumetricFog.HistoryMissSupersampleCount"] = new("4", "4", "4"),

            ["r.AmbientOcclusionLevels"] = new("0", "-1", "-1"),
            ["r.AmbientOcclusionMaxQuality"] = new("0", "60", "100"),
            ["r.AmbientOcclusionMipLevelFactor"] = new("1.0", "1.0", "0.6"),
            ["r.AmbientOcclusionRadiusScale"] = new("1.2", "1.5", "1.5"),
            ["r.BloomQuality"] = new("4", "4", "5"),
            ["r.DepthOfFieldQuality"] = new("0", "1", "2"),
            ["r.DOF.Gather.AccumulatorQuality"] = new(null, "0", "0"),
            ["r.DOF.Gather.EnableBokehSettings"] = new(null, "0", "0"),
            ["r.DOF.Gather.PostfilterMethod"] = new(null, "2", "2"),
            ["r.DOF.Gather.RingCount"] = new(null, "3", "4"),
            ["r.DOF.Kernel.MaxBackgroundRadius"] = new(null, "0.006", "0.012"),
            ["r.DOF.Kernel.MaxForegroundRadius"] = new(null, "0.006", "0.012"),
            ["r.DOF.Recombine.Quality"] = new(null, "0", "0"),
            ["r.DOF.Scatter.BackgroundCompositing"] = new(null, "0", "1"),
            ["r.DOF.Scatter.EnableBokehSettings"] = new(null, null, "0"),
            ["r.DOF.Scatter.ForegroundCompositing"] = new(null, "0", "1"),
            ["r.DOF.Scatter.MaxSpriteRatio"] = new(null, null, "0.04"),
            ["r.DOF.TemporalAAQuality"] = new(null, "0", "0"),
            ["r.EyeAdaptationQuality"] = new("0", "0", "2"),
            ["r.FastBlurThreshold"] = new("0", "2", "3"),
            ["r.Filter.SizeScale"] = new("0.6", "0.7", "0.8"),
            ["r.LensFlareQuality"] = new("0", "0", "2"),
            ["r.LightShaftQuality"] = new("0", "0", "1"),
            ["r.MotionBlurQuality"] = new("0", "3", "3"),
            ["r.RenderTargetPoolMin"] = new("300", "350", "400"),
            ["r.SceneColorFringeQuality"] = new("0", "0", "1"),
            ["r.Tonemapper.GrainQuantization"] = new("0", "0", "1"),
            ["r.Tonemapper.Quality"] = new("0", "2", "5"),
            ["r.Upscale.Quality"] = new("1", "2", "2"),

            ["r.MaxAnisotropy"] = new("0", "2", "4"),
            ["r.Streaming.AmortizeCPUToGPUCopy"] = new("1", "0", "0"),
            ["r.Streaming.Boost"] = new("0.3", "1", "1"),
            ["r.Streaming.LimitPoolSizeToVRAM"] = new("1", "1", "1"),
            ["r.Streaming.MaxEffectiveScreenSize"] = new("0", "0", "0"),
            ["r.Streaming.MaxNumTexturesToStreamPerFrame"] = new("1", "0", "0"),
            ["r.Streaming.MipBias"] = new("16", "1", "0"),
            ["r.Streaming.PoolSize"] = new("500", "1000", "1500"),

            ["r.DetailMode"] = new("0", "1", "1"),
            ["r.EmitterSpawnRateScale"] = new("0.125", "0.25", "0.5"),
            ["r.MaterialQualityLevel"] = new("0", "2", "1"),
            ["r.ParticleLightQuality"] = new("0", "0", "1"),
            ["r.RefractionQuality"] = new("0", "0", "2"),
            ["r.SceneColorFormat"] = new("3", "3", "3"),
            ["r.SSR.Quality"] = new("0", "0", "2"),
            ["r.SSS.HalfRes"] = new("1", "1", "1"),
            ["r.SSS.Quality"] = new("0", "0", "-1"),
            ["r.SSS.SampleSet"] = new("0", "0", "1"),
            ["r.SSS.Scale"] = new("0", "0.75", "1"),
            ["r.TranslucencyLightingVolumeDim"] = new("24", "32", "48"),
            ["r.TranslucencyVolumeBlur"] = new("0", "0", "1"),

            ["foliage.DensityScale"] = new("1.0", "1.25", "1.5"),
            ["grass.DensityScale"] = new("1.0", "1.25", "1.5"),
        };

    public static bool TryGet(
        GameInstallationSnapshot? installation,
        string key,
        out ScalabilityPresetValues presetValues)
    {
        if (installation is null || !GameIdentity.IsSupported(
                installation.Store,
                installation.BuildId,
                installation.ContentSignature,
                installation.ContentSignatureReadFailed))
        {
            presetValues = null!;
            return false;
        }

        return Values.TryGetValue(key, out presetValues!);
    }
}
