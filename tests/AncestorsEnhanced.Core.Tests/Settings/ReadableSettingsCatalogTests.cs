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
                    PakClassification.PatchStyle),
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
            "50% of game default",
            FindSetting(FindGroup(groups, "vignette"), "environment-vignette").Value);
        Assert.Equal("Videos skipped", FindGroup(groups, "convenience").Summary);
    }

    [Fact]
    public void VerifiedStockVignetteShowsItsOriginalStrength()
    {
        IReadOnlyList<FeatureGroupSnapshot> groups =
            ReadableSettingsCatalog.CreateFeatureGroups(CreateSnapshot(
                [],
                [],
                buildId: "5495393",
                vignette: new VignetteModSnapshot(null, true, "Game asset verified")));

        FeatureSettingSnapshot vignette = FindSetting(
            FindGroup(groups, "vignette"),
            "environment-vignette");
        Assert.Equal("Game Default (100%)", vignette.Value);
        Assert.Equal("100", vignette.Editor!.GameControlledValue);
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
    public void VerifiedStockAdvancedValuesExposeReviewedEditors()
    {
        IReadOnlyList<FeatureGroupSnapshot> groups =
            ReadableSettingsCatalog.CreateFeatureGroups(CreateSnapshot([], [], buildId: "5495393"));

        string[] ids =
        [
            "dof-rings",
            "dof-gather-bokeh",
            "shadow-csm-resolution",
            "ao-quality",
            "fog-grid-pixel-size",
            "capsule-shadows",
            "translucency-volume",
            "sss-scale",
        ];

        foreach (string id in ids)
        {
            FeatureSettingSnapshot setting = Assert.Single(groups.SelectMany(group => group.Settings), setting => setting.Id == id);
            Assert.NotNull(setting.Editor);
            Assert.True(setting.Editor!.CanSetCustomValue);
        }
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
    public void CreateFeatureGroupsOnlyReadsAnOverrideFromItsOwningIniFile()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot(
            [new IniSettingSnapshot("SystemSettings", "r.MotionBlurQuality", "4", 1)],
            []) with
        {
            ConfigurationFiles =
            [
                new ConfigurationFileSnapshot(
                    "Engine.ini",
                    "Engine.ini",
                    true,
                    0,
                    DateTimeOffset.UnixEpoch,
                    [new IniSettingSnapshot("SystemSettings", "r.MotionBlurQuality", "4", 1)],
                    null),
                new ConfigurationFileSnapshot(
                    "Scalability.ini",
                    "Scalability.ini",
                    true,
                    0,
                    DateTimeOffset.UnixEpoch,
                    [new IniSettingSnapshot("SystemSettings", "r.MotionBlurQuality", "0", 1)],
                    null),
            ],
        };

        FeatureSettingSnapshot motionBlur = FindSetting(
            FindGroup(ReadableSettingsCatalog.CreateFeatureGroups(snapshot), "motion-blur"),
            "motion-blur-quality");

        Assert.Equal("Level 4", motionBlur.Value);
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
            medium => Assert.Equal(new SettingPresetValueSnapshot("Medium", "High"), medium),
            high => Assert.Equal(new SettingPresetValueSnapshot("High", "High"), high));
        Assert.NotNull(motionBlur.Editor);
        Assert.Equal("0", motionBlur.Editor.DefaultValue);
        Assert.Null(motionBlur.Editor.CurrentOverride);
        Assert.Equal("Game preset", bloom.Value);
        Assert.Contains("active level not read", bloom.Source, StringComparison.Ordinal);
        Assert.Equal(ReadableSettingState.Unknown, bloom.State);
    }

    [Theory]
    [InlineData(StoreKind.EpicGames, "epic-release-2026")]
    [InlineData(StoreKind.Gog, null)]
    public void CreateFeatureGroupsShowsPresetValuesForSignatureVerifiedStores(
        StoreKind store,
        string? buildId)
    {
        GameInspectionSnapshot snapshot = WithInstallation(
            CreateSnapshot([], []),
            store,
            buildId,
            AncestorsGameProfile.SupportedContentSignature);

        FeatureSettingSnapshot motionBlur = FindSetting(
            FindGroup(ReadableSettingsCatalog.CreateFeatureGroups(snapshot), "motion-blur"),
            "motion-blur-quality");

        Assert.Collection(
            motionBlur.PresetValues!,
            low => Assert.Equal(new SettingPresetValueSnapshot("Low", "Off"), low),
            medium => Assert.Equal(new SettingPresetValueSnapshot("Medium", "High"), medium),
            high => Assert.Equal(new SettingPresetValueSnapshot("High", "High"), high));
        Assert.Contains("Verified Ancestors preset table", motionBlur.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFeatureGroupsDoesNotUsePresetValuesForContradictorySteamEvidence()
    {
        GameInspectionSnapshot snapshot = WithInstallation(
            CreateSnapshot([], []),
            StoreKind.Steam,
            "wrong-build",
            AncestorsGameProfile.SupportedContentSignature);

        FeatureSettingSnapshot motionBlur = FindSetting(
            FindGroup(ReadableSettingsCatalog.CreateFeatureGroups(snapshot), "motion-blur"),
            "motion-blur-quality");

        Assert.Null(motionBlur.PresetValues);
        Assert.Equal("Game preset", motionBlur.Value);
    }

    [Fact]
    public void NonPresetRendererValuesAreReportedAsGameControlled()
    {
        IReadOnlyList<FeatureGroupSnapshot> groups =
            ReadableSettingsCatalog.CreateFeatureGroups(
                CreateSnapshot([], [], buildId: "5495393"));

        FeatureGroupSnapshot clarity = FindGroup(groups, "image-clarity");
        FeatureSettingSnapshot sharpening = FindSetting(clarity, "image-sharpening");
        FeatureSettingSnapshot frameWeight = FindSetting(clarity, "taa-frame-weight");

        Assert.Equal("Game controlled", sharpening.Value);
        Assert.Equal("Game controlled", frameWeight.Value);
        Assert.Null(sharpening.Editor!.GameControlledValue);
        Assert.Contains("not stored", sharpening.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFeatureGroupsUsesDecodedActiveCategoryPresets()
    {
        var graphics = new SystemGraphicsSettingsSnapshot(
            2560,
            1440,
            1680,
            1050,
            1.05,
            GameGraphicsQuality.High,
            GameGraphicsQuality.High,
            GameGraphicsQuality.Low,
            GameGraphicsQuality.Medium,
            GameGraphicsQuality.High,
            GameGraphicsQuality.High,
            GameGraphicsQuality.High,
            120,
            true);
        IReadOnlyList<FeatureGroupSnapshot> groups =
            ReadableSettingsCatalog.CreateFeatureGroups(
                CreateSnapshot([], [], buildId: "5495393", graphics: graphics));

        FeatureSettingSnapshot foliage = FindSetting(
            FindGroup(groups, "view-distance-foliage"),
            "foliage-density");
        FeatureSettingSnapshot bloom = FindSetting(
            FindGroup(groups, "post-processing"),
            "bloom-quality");
        FeatureSettingSnapshot shadow = FindSetting(
            FindGroup(groups, "shadows-lighting"),
            "shadow-quality");
        FeatureSettingSnapshot depthOfField = FindSetting(
            FindGroup(groups, "depth-of-field"),
            "dof-quality");

        Assert.Equal("150%", foliage.Value);
        Assert.Equal("High", foliage.ActivePresetName);
        Assert.Equal("Level 4", bloom.Value);
        Assert.Equal("Low", bloom.ActivePresetName);
        Assert.Equal("Level 4", shadow.Value);
        Assert.Equal("Medium", shadow.ActivePresetName);
        Assert.Equal("Off", depthOfField.Value);
        Assert.Equal("Low", depthOfField.ActivePresetName);
        Assert.Equal("0", depthOfField.Editor!.GameControlledValue);
        Assert.Equal("High base · Custom", FindGroup(groups, "game-menu-settings").Summary);
    }

    private static FeatureGroupSnapshot FindGroup(
        IReadOnlyList<FeatureGroupSnapshot> groups,
        string id) => Assert.Single(groups, group => group.Id == id);

    private static FeatureSettingSnapshot FindSetting(
        FeatureGroupSnapshot group,
        string id) => Assert.Single(group.Settings, setting => setting.Id == id);

    private static GameInspectionSnapshot WithInstallation(
        GameInspectionSnapshot snapshot,
        StoreKind store,
        string? buildId,
        string? contentSignature,
        bool contentSignatureReadFailed = false) => snapshot with
        {
            Installation = new GameInstallationSnapshot(
                store,
                HostKind.Windows,
                CompatibilityLayerKind.None,
                "library",
                "install",
                buildId,
                ExecutableExists: true,
                contentSignature,
                contentSignatureReadFailed),
        };

    private static GameInspectionSnapshot CreateSnapshot(
        IReadOnlyList<IniSettingSnapshot> iniSettings,
        IReadOnlyList<PakFileSnapshot> pakFiles,
        string? buildId = null,
        VignetteModSnapshot? vignette = null,
        SystemGraphicsSettingsSnapshot? graphics = null)
    {
        return new GameInspectionSnapshot(
            DateTimeOffset.UnixEpoch,
            buildId is null
                ? null
                : new GameInstallationSnapshot(
                    StoreKind.Steam,
                    HostKind.Windows,
                    CompatibilityLayerKind.None,
                    "library",
                    "install",
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
                    iniSettings.Where(setting => !string.Equals(
                        setting.Section,
                        "/Script/MoviePlayer.MoviePlayerSettings",
                        StringComparison.OrdinalIgnoreCase)).ToArray(),
                    null),
                new ConfigurationFileSnapshot(
                    "Game.ini",
                    "Game.ini",
                    Exists: true,
                    0,
                    DateTimeOffset.UnixEpoch,
                    iniSettings.Where(setting => string.Equals(
                        setting.Section,
                        "/Script/MoviePlayer.MoviePlayerSettings",
                        StringComparison.OrdinalIgnoreCase)).ToArray(),
                    null),
            ],
            graphics is null
                ? null
                : new BinarySettingsFileSnapshot(
                    "System.sav",
                    "System.sav",
                    true,
                    1,
                    DateTimeOffset.UnixEpoch,
                    "Decoded and verified",
                    graphics),
            pakFiles,
            [],
            vignette);
    }
}
