using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Core.Tests.Editing;

public sealed class EditableSettingsCatalogTests
{
    [Fact]
    public void StartupMoviesUsesTheReviewedGameIniTarget()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot();

        SettingEditSnapshot editor = Assert.IsType<SettingEditSnapshot>(
            EditableSettingsCatalog.Create(snapshot, "!StartupMovies", null));

        Assert.Equal("Game.ini", editor.FileName);
        Assert.Equal("/Script/MoviePlayer.MoviePlayerSettings", editor.Section);
        Assert.Equal(SettingEditorKind.Presence, editor.Kind);
        Assert.Equal("ClearArray", editor.DefaultValue);
        Assert.True(EditableSettingsCatalog.TryValidate(
            snapshot,
            new SettingChangeRequest(
                "Startup videos",
                editor.FileName,
                editor.Section,
                editor.Key,
                "ClearArray"),
            out _));
    }

    [Fact]
    public void ExistingUnexpectedValueCanOnlyBeReset()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot();
        SettingEditSnapshot editor = Assert.IsType<SettingEditSnapshot>(EditableSettingsCatalog.Create(
            snapshot,
            "!StartupMovies",
            "UnexpectedCommand"));

        Assert.False(editor.CanSetCustomValue);
        Assert.True(EditableSettingsCatalog.TryValidate(
            snapshot,
            new SettingChangeRequest(
                "Startup videos",
                editor.FileName,
                editor.Section,
                editor.Key,
                null),
            out _));
        Assert.False(EditableSettingsCatalog.TryValidate(
            snapshot,
            new SettingChangeRequest(
                "Startup videos",
                editor.FileName,
                editor.Section,
                editor.Key,
                "UnexpectedCommand"),
            out _));
    }

    [Fact]
    public void ChoiceLabelsUseWellFormedUtf8WithoutMojibake()
    {
        // The "x" choice labels must use the real
        // multiplication sign (U+00D7) and never the double-encoded mojibake sequence.
        SettingEditSnapshot editor = Assert.IsType<SettingEditSnapshot>(
            EditableSettingsCatalog.Create(CreateSnapshot(), "r.MaxAnisotropy", null));

        string[] labels = editor.Choices!.Select(choice => choice.Label).ToArray();
        Assert.Contains(labels, label => label.Contains('\u00d7', StringComparison.Ordinal));
        Assert.DoesNotContain(labels, label => label.Contains('\u00c3', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("r.DOF.Gather.RingCount", "3")]
    [InlineData("r.DOF.Gather.AccumulatorQuality", "1")]
    [InlineData("r.DOF.Gather.EnableBokehSettings", "0")]
    [InlineData("r.Shadow.MaxCSMResolution", "4096")]
    [InlineData("r.VolumetricFog.GridPixelSize", "4")]
    [InlineData("r.CapsuleShadows", "1")]
    [InlineData("r.TranslucencyLightingVolumeDim", "64")]
    [InlineData("r.SSS.Scale", "0.75")]
    public void VerifiedStockAdvancedControlsAcceptOnlyCataloguedValues(string key, string value)
    {
        GameInspectionSnapshot snapshot = CreateSnapshot();
        SettingEditSnapshot editor = Assert.IsType<SettingEditSnapshot>(
            EditableSettingsCatalog.Create(snapshot, key, null));

        Assert.True(EditableSettingsCatalog.TryValidate(
            snapshot,
            new SettingChangeRequest("Advanced setting", editor.FileName, editor.Section, key, value),
            out _));
        Assert.False(EditableSettingsCatalog.TryValidate(
            snapshot,
            new SettingChangeRequest("Advanced setting", editor.FileName, editor.Section, key, "not-a-stock-choice"),
            out _));
    }

    [Theory]
    [InlineData(StoreKind.EpicGames, HostKind.Windows, CompatibilityLayerKind.None)]
    [InlineData(StoreKind.Gog, HostKind.Windows, CompatibilityLayerKind.None)]
    [InlineData(StoreKind.Steam, HostKind.Linux, CompatibilityLayerKind.Proton)]
    public void EditingSupportsVerifiedStores(
        StoreKind store,
        HostKind host,
        CompatibilityLayerKind compatibilityLayer)
    {
        GameInspectionSnapshot valid = CreateSnapshot();
        GameInspectionSnapshot unsupported = valid with
        {
            Installation = valid.Installation! with
            {
                Store = store,
                Host = host,
                CompatibilityLayer = compatibilityLayer,
                BuildId = store == StoreKind.Steam
                    ? AncestorsGameProfile.SupportedSteamBuildId
                    : store == StoreKind.EpicGames ? "epic-build-version" : null,
                ContentSignature = AncestorsGameProfile.SupportedContentSignature,
            },
        };

        Assert.NotNull(EditableSettingsCatalog.Create(
            unsupported,
            "r.ViewDistanceScale",
            null));
    }

    [Fact]
    public void EpicBuildVersionDoesNotOverrideVerifiedContentIdentity()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot();
        snapshot = snapshot with
        {
            Installation = snapshot.Installation! with
            {
                Store = StoreKind.EpicGames,
                BuildId = "not-a-steam-build-id",
                ContentSignature = AncestorsGameProfile.SupportedContentSignature,
            },
        };

        Assert.NotNull(EditableSettingsCatalog.Create(snapshot, "r.ViewDistanceScale", null));
    }

    [Fact]
    public void SteamBuildMismatchStillRejectsMatchingContent()
    {
        GameInspectionSnapshot snapshot = CreateSnapshot();
        snapshot = snapshot with
        {
            Installation = snapshot.Installation! with
            {
                BuildId = "wrong-steam-build",
                ContentSignature = AncestorsGameProfile.SupportedContentSignature,
            },
        };

        Assert.Null(EditableSettingsCatalog.Create(snapshot, "r.ViewDistanceScale", null));
    }

    [Fact]
    public void EditingRejectsAMissingExecutable()
    {
        GameInspectionSnapshot valid = CreateSnapshot();
        Assert.Null(EditableSettingsCatalog.Create(
            valid with { Installation = valid.Installation! with { ExecutableExists = false } },
            "r.ViewDistanceScale",
            null));
    }

    private static GameInspectionSnapshot CreateSnapshot() =>
        new(
            DateTimeOffset.UnixEpoch,
            new GameInstallationSnapshot(
                StoreKind.Steam,
                HostKind.Windows,
                CompatibilityLayerKind.None,
                "library",
                "install",
                "5495393",
                ExecutableExists: true),
            "user-data",
            [],
            null,
            [],
            []);
}
