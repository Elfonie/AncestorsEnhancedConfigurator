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
                "startup",
                "Startup videos",
                editor.FileName,
                editor.Section,
                editor.Key,
                "ClearArray"),
            out _));
    }

    [Fact]
    public void ExistingUnexpectedValueIsReadableButNotEditable()
    {
        SettingEditSnapshot? editor = EditableSettingsCatalog.Create(
            CreateSnapshot(),
            "!StartupMovies",
            "UnexpectedCommand");

        Assert.Null(editor);
    }

    [Theory]
    [InlineData(StoreKind.Gog, HostKind.Windows, CompatibilityLayerKind.None, true)]
    [InlineData(StoreKind.Steam, HostKind.Linux, CompatibilityLayerKind.Proton, true)]
    [InlineData(StoreKind.Steam, HostKind.Windows, CompatibilityLayerKind.None, false)]
    public void EditingRequiresTheExactVerifiedTarget(
        StoreKind store,
        HostKind host,
        CompatibilityLayerKind compatibilityLayer,
        bool executableExists)
    {
        GameInspectionSnapshot valid = CreateSnapshot();
        GameInspectionSnapshot unsupported = valid with
        {
            Installation = valid.Installation! with
            {
                Store = store,
                Host = host,
                CompatibilityLayer = compatibilityLayer,
                ExecutableExists = executableExists,
            },
        };

        Assert.Null(EditableSettingsCatalog.Create(
            unsupported,
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
                "store",
                "library",
                "install",
                "Ancestors-Win64-Shipping.exe",
                "5495393",
                ExecutableExists: true),
            "user-data",
            [],
            null,
            [],
            []);
}
