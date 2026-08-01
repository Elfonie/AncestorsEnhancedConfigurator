using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.Editing;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class SettingEditorViewModelTests
{
    [Fact]
    public void PresenceEditorMapsTheSwitchToAddOrRemove()
    {
        var viewModel = new SettingEditorViewModel(new SettingEditSnapshot(
            "Game.ini",
            "/Script/MoviePlayer.MoviePlayerSettings",
            "!StartupMovies",
            SettingEditorKind.Presence,
            "ClearArray",
            null));

        Assert.True(viewModel.IsPresence);
        Assert.False(viewModel.HasChanges);
        Assert.Equal("Game default", viewModel.ModeLabel);

        viewModel.UseCustomValue = true;
        SettingChangeRequest add = viewModel.CreateRequest("startup", "Startup videos");

        Assert.True(viewModel.HasChanges);
        Assert.Equal("ClearArray", add.Value);
        Assert.Equal("Videos skipped", viewModel.ModeLabel);

        viewModel.Reset();
        Assert.False(viewModel.HasChanges);
        Assert.False(viewModel.UseCustomValue);
    }

    [Fact]
    public void NumberEditorNormalizesEquivalentDecimalValues()
    {
        var viewModel = new SettingEditorViewModel(new SettingEditSnapshot(
            "Engine.ini",
            "SystemSettings",
            "r.ViewDistanceScale",
            SettingEditorKind.Number,
            "1.2",
            "1.200",
            0.5m,
            2m,
            0.05m));

        Assert.False(viewModel.HasChanges);

        viewModel.NumberValue = 1.5m;

        Assert.True(viewModel.HasChanges);
        Assert.Equal(
            "1.5",
            viewModel.CreateRequest("view-distance", "View distance").Value);
    }
}
