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
        Assert.Equal("Use game default", viewModel.DesiredSummary);

        viewModel.UseCustomValue = true;
        SettingChangeRequest add = viewModel.CreateRequest("Startup videos");

        Assert.True(viewModel.HasChanges);
        Assert.Equal("ClearArray", add.Value);
        Assert.Equal("Skip videos", viewModel.DesiredSummary);

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
            viewModel.CreateRequest("View distance").Value);
    }

    [Fact]
    public void DirectEditorCannotProduceAResetRequest()
    {
        var viewModel = new SettingEditorViewModel(new SettingEditSnapshot(
            "System.sav",
            "GraphicsOptions",
            SystemSaveSettingKeys.FrameRateLimit,
            SettingEditorKind.Choice,
            "120",
            "120",
            Choices:
            [
                new SettingChoice("120", "120 FPS"),
                new SettingChoice("144", "144 FPS"),
            ],
            Target: SettingFileTarget.SystemSave,
            IsDirect: true))
        {
            UseCustomValue = false
        };

        Assert.False(viewModel.ShowOverrideToggle);
        Assert.Equal("120", viewModel.CreateRequest("Frame-rate limit").Value);
    }

    [Fact]
    public void UnsupportedOverrideRemainsAvailableForReset()
    {
        var viewModel = new SettingEditorViewModel(new SettingEditSnapshot(
            "Engine.ini",
            "SystemSettings",
            "r.ViewDistanceScale",
            SettingEditorKind.Number,
            "1.2",
            "invalid",
            0.5m,
            2m,
            0.05m,
            CanSetCustomValue: false));

        Assert.True(viewModel.HasUnsupportedCurrentValue);
        Assert.False(viewModel.IsCustomEditorEnabled);
        Assert.False(viewModel.HasChanges);

        viewModel.UseCustomValue = false;

        Assert.True(viewModel.HasChanges);
        Assert.Null(viewModel.CreateRequest("View distance").Value);
    }

    [Fact]
    public void DisabledOverrideEditorShowsTheResolvedGamePresetValue()
    {
        var viewModel = new SettingEditorViewModel(new SettingEditSnapshot(
            "Engine.ini",
            "SystemSettings",
            "r.DepthOfFieldQuality",
            SettingEditorKind.Choice,
            "0",
            null,
            Choices:
            [
                new SettingChoice("0", "Off"),
                new SettingChoice("1", "Quality 1"),
                new SettingChoice("2", "Quality 2"),
            ],
            GameControlledValue: "2"));

        Assert.False(viewModel.UseCustomValue);
        Assert.True(viewModel.HasKnownGameValue);
        Assert.True(viewModel.ShowValueEditor);
        Assert.False(viewModel.IsCustomEditorEnabled);
        Assert.Equal("Quality 2", viewModel.SelectedChoice!.Label);
        Assert.False(viewModel.HasChanges);
    }

    [Fact]
    public void UnknownPresetValueDoesNotDisplayTheEditorDefault()
    {
        var viewModel = new SettingEditorViewModel(new SettingEditSnapshot(
            "Engine.ini",
            "SystemSettings",
            "r.DepthOfFieldQuality",
            SettingEditorKind.Choice,
            "0",
            null,
            Choices: [new SettingChoice("0", "Off")]));

        Assert.False(viewModel.ShowValueEditor);
        Assert.True(viewModel.ShowUnknownGameValue);
    }
}
