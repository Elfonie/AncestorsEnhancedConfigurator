using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.Settings;

namespace AncestorsEnhanced.Core.Tests.Settings;

public sealed class ReadableSettingsCatalogTests
{
    [Fact]
    public void CreateFeatureGroupsTranslatesVerifiedOverrides()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot(
            [
                new IniSettingSnapshot("SystemSettings", "r.MotionBlurQuality", "0", 1),
                new IniSettingSnapshot("SystemSettings", "r.MaxAnisotropy", "16", 2),
                new IniSettingSnapshot("SystemSettings", "r.ViewDistanceScale", "1.20", 3),
                new IniSettingSnapshot("SystemSettings", "r.Streaming.PoolSize", "4096", 4),
                new IniSettingSnapshot(
                    "/Script/MoviePlayer.MoviePlayerSettings",
                    "!StartupMovies",
                    "ClearArray",
                    5),
            ],
            [
                new PakFileSnapshot(
                    "pakchunk99-WindowsNoEditor_P.pak",
                    "vignette.pak",
                    913,
                    DateTimeOffset.UnixEpoch,
                    PakClassification.PatchStyle,
                    "06F74C5E4BF70D2748614D8C74405B4C96FB4E50F103A66827C4E2041B2801A0"),
            ],
            vignette: new VignetteModSnapshot(50, true, "Managed graphics patch"));

        IReadOnlyList<FeatureGroupSnapshot> groups =
            ReadableSettingsCatalog.CreateFeatureGroups(snapshot);

        FeatureGroupSnapshot motionBlur = FindGroup(groups, "motion-blur");
        Assert.Equal("Off", motionBlur.Summary);
        Assert.Equal("Off", FindSetting(motionBlur, "motion-blur-quality").Value);

        FeatureGroupSnapshot textures = FindGroup(groups, "textures");
        Assert.Equal("16× · 4 GB", textures.Summary);
        Assert.Equal("16×", FindSetting(textures, "anisotropic-filtering").Value);

        Assert.Equal(
            "120%",
            FindSetting(FindGroup(groups, "view-distance-foliage"), "view-distance").Value);
        Assert.Equal(
            "50% of original",
            FindSetting(FindGroup(groups, "vignette"), "environment-vignette").Value);
        Assert.Equal("Videos skipped", FindGroup(groups, "convenience").Summary);
    }

    [Fact]
    public void CreateFeatureGroupsSeparatesImportantAndAdvancedSettings()
    {
        IReadOnlyList<FeatureGroupSnapshot> groups =
            ReadableSettingsCatalog.CreateFeatureGroups(CreateSnapshot([], []));

        FeatureGroupSnapshot motionBlur = FindGroup(groups, "motion-blur");
        Assert.True(motionBlur.IsEssential);
        Assert.False(FindSetting(motionBlur, "motion-blur-scale").IsAdvanced);
        Assert.True(FindSetting(motionBlur, "motion-blur-max").IsAdvanced);

        FeatureGroupSnapshot effects = FindGroup(groups, "effects-materials");
        Assert.False(effects.IsEssential);
        Assert.Contains(effects.Settings, setting => setting.TechnicalKey == "r.SSR.Quality");

        FeatureGroupSnapshot postProcessing = FindGroup(groups, "post-processing");
        Assert.Equal(15, postProcessing.Settings.Count);
        Assert.Contains(postProcessing.Settings, setting => setting.TechnicalKey == "r.BloomQuality");
        Assert.Contains(
            postProcessing.Settings,
            setting => setting.TechnicalKey == "r.Tonemapper.GrainQuantization");

        Assert.Equal(15, FindGroup(groups, "depth-of-field").Settings.Count);
        Assert.Equal(8, FindGroup(groups, "textures").Settings.Count);
        Assert.Equal(18, FindGroup(groups, "shadows-lighting").Settings.Count);
        Assert.Equal(13, effects.Settings.Count);
    }

    [Fact]
    public void CreateFeatureGroupsUsesTheLastDuplicateIniEntry()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot(
            [
                new IniSettingSnapshot("SystemSettings", "r.MotionBlurQuality", "4", 1),
                new IniSettingSnapshot("SystemSettings", "r.MotionBlurQuality", "0", 2),
            ],
            []);

        FeatureSettingSnapshot motionBlur = FindSetting(
            FindGroup(ReadableSettingsCatalog.CreateFeatureGroups(snapshot), "motion-blur"),
            "motion-blur-quality");

        Assert.Equal("Off", motionBlur.Value);
    }

    [Fact]
    public void CreateFeatureGroupsDoesNotGuessMissingScalabilityValues()
    {
        IReadOnlyList<FeatureGroupSnapshot> groups =
            ReadableSettingsCatalog.CreateFeatureGroups(CreateSnapshot([], []));

        FeatureSettingSnapshot quality = FindSetting(
            FindGroup(groups, "motion-blur"),
            "motion-blur-quality");
        FeatureSettingSnapshot bloom = FindSetting(
            FindGroup(groups, "post-processing"),
            "bloom-quality");

        Assert.Equal("Game preset", quality.Value);
        Assert.Equal(ReadableSettingState.Unknown, quality.State);
        Assert.Equal("Game preset", bloom.Value);
        Assert.Equal(ReadableSettingState.Unknown, bloom.State);
    }

    [Fact]
    public void CreateFeatureGroupsShowsVerifiedPresetValuesForSupportedBuild()
    {
        IReadOnlyList<FeatureGroupSnapshot> groups =
            ReadableSettingsCatalog.CreateFeatureGroups(
                CreateSnapshot([], [], buildId: "5495393"));

        FeatureSettingSnapshot motionBlur = FindSetting(
            FindGroup(groups, "motion-blur"),
            "motion-blur-quality");
        FeatureSettingSnapshot bloom = FindSetting(
            FindGroup(groups, "post-processing"),
            "bloom-quality");

        Assert.Equal("Game preset", motionBlur.Value);
        Assert.Collection(
            motionBlur.PresetValues!,
            low => Assert.Equal(new SettingPresetValueSnapshot("Low", "Off"), low),
            medium => Assert.Equal(new SettingPresetValueSnapshot("Medium", "Quality 3"), medium),
            high => Assert.Equal(new SettingPresetValueSnapshot("High", "Quality 3"), high));
        Assert.NotNull(motionBlur.Editor);
        Assert.Equal("0", motionBlur.Editor.DefaultValue);
        Assert.Null(motionBlur.Editor.CurrentOverride);
        Assert.Equal("Game preset", bloom.Value);
        Assert.Contains("active level not read", bloom.Source, StringComparison.Ordinal);
        Assert.Equal(ReadableSettingState.Unknown, bloom.State);
    }

    private static FeatureGroupSnapshot FindGroup(
        IReadOnlyList<FeatureGroupSnapshot> groups,
        string id) => Assert.Single(groups, group => group.Id == id);

    private static FeatureSettingSnapshot FindSetting(
        FeatureGroupSnapshot group,
        string id) => Assert.Single(group.Settings, setting => setting.Id == id);

    private static GameInspectionSnapshot CreateSnapshot(
        IReadOnlyList<IniSettingSnapshot> iniSettings,
        IReadOnlyList<PakFileSnapshot> pakFiles,
        string? buildId = null,
        VignetteModSnapshot? vignette = null)
    {
        return new GameInspectionSnapshot(
            DateTimeOffset.UnixEpoch,
            buildId is null
                ? null
                : new GameInstallationSnapshot(
                    StoreKind.Steam,
                    HostKind.Windows,
                    CompatibilityLayerKind.None,
                    "store",
                    "library",
                    "install",
                    "Ancestors-Win64-Shipping.exe",
                    buildId,
                    ExecutableExists: true),
            null,
            [
                new ConfigurationFileSnapshot(
                    "Engine.ini",
                    "Engine.ini",
                    Exists: true,
                    0,
                    DateTimeOffset.UnixEpoch,
                    iniSettings,
                    null),
            ],
            null,
            pakFiles,
            [],
            vignette);
    }
}
