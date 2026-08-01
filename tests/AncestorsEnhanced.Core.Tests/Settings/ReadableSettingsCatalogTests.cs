using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Core.Settings;

namespace AncestorsEnhanced.Core.Tests.Settings;

public sealed class ReadableSettingsCatalogTests
{
    [Fact]
    public void CreateTranslatesVerifiedOverridesIntoReadableValues()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot(
            [
                new IniSettingSnapshot("SystemSettings", "r.MotionBlurQuality", "0", 1),
                new IniSettingSnapshot("SystemSettings", "r.MaxAnisotropy", "16", 2),
                new IniSettingSnapshot("SystemSettings", "r.ViewDistanceScale", "1.20", 3),
                new IniSettingSnapshot(
                    "/Script/MoviePlayer.MoviePlayerSettings",
                    "!StartupMovies",
                    "ClearArray",
                    4),
            ],
            [
                new PakFileSnapshot(
                    "pakchunk99-WindowsNoEditor_P.pak",
                    "vignette.pak",
                    913,
                    DateTimeOffset.UnixEpoch,
                    PakClassification.PatchStyle,
                    "06F74C5E4BF70D2748614D8C74405B4C96FB4E50F103A66827C4E2041B2801A0"),
            ]);

        IReadOnlyList<ReadableSettingSnapshot> settings = ReadableSettingsCatalog.Create(snapshot);

        ReadableSettingSnapshot motionBlur = Assert.Single(settings, setting => setting.Id == "motion-blur");
        Assert.Equal("Off", motionBlur.Value);
        Assert.Equal(ReadableSettingState.Disabled, motionBlur.State);
        ReadableSettingSnapshot textureFiltering =
            Assert.Single(settings, setting => setting.Id == "anisotropic-filtering");
        Assert.Equal("16×", textureFiltering.Value);
        Assert.Equal(1, textureFiltering.Percentage);
        ReadableSettingSnapshot viewDistance =
            Assert.Single(settings, setting => setting.Id == "view-distance");
        Assert.Equal("120%", viewDistance.Value);
        Assert.Null(viewDistance.Percentage);
        Assert.Equal("Skipped", Assert.Single(settings, setting => setting.Id == "startup-videos").Value);
        Assert.Equal("50% intensity", Assert.Single(settings, setting => setting.Id == "vignette").Value);
    }

    [Fact]
    public void CreateUsesTheLastDuplicateIniEntry()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot(
            [
                new IniSettingSnapshot("SystemSettings", "r.MotionBlurQuality", "4", 1),
                new IniSettingSnapshot("SystemSettings", "r.MotionBlurQuality", "0", 2),
            ],
            []);

        ReadableSettingSnapshot motionBlur = Assert.Single(
            ReadableSettingsCatalog.Create(snapshot),
            setting => setting.Id == "motion-blur");

        Assert.Equal("Off", motionBlur.Value);
    }

    [Fact]
    public void CreateDoesNotGuessAMissingOverride()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot([], []);

        ReadableSettingSnapshot motionBlur = Assert.Single(
            ReadableSettingsCatalog.Create(snapshot),
            setting => setting.Id == "motion-blur");

        Assert.Equal("Game default", motionBlur.Value);
        Assert.Equal(ReadableSettingState.Unknown, motionBlur.State);
        Assert.Null(motionBlur.Percentage);
    }

    private static GameInspectionSnapshot CreateSnapshot(
        IReadOnlyList<IniSettingSnapshot> iniSettings,
        IReadOnlyList<PakFileSnapshot> pakFiles)
    {
        return new GameInspectionSnapshot(
            DateTimeOffset.UnixEpoch,
            null,
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
            []);
    }
}
