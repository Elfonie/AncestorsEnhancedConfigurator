using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.App.Views;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace AncestorsEnhanced.App.Tests.Views;

public sealed class MainWindowUiTests
{
    [Fact]
    public Task OnboardingFocusTrapCyclesBetweenItsOwnActions() => Dispatch(() =>
    {
        var window = new MainWindow { DataContext = CreateViewModel(showOnboarding: true) };
        window.Show();
        try
        {
            Button skip = window.FindControl<Button>("OnboardingSkipButton")!;
            Button primary = window.FindControl<Button>("OnboardingPrimaryButton")!;
            Assert.True(skip.IsVisible);
            Assert.True(primary.IsVisible);

            primary.Focus();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(skip.IsFocused);

            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            Assert.True(primary.IsFocused);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task ReviewFocusTrapCyclesAndEscapeClosesTheActualReview() => Dispatch(async () =>
    {
        MainViewModel viewModel = CreateViewModel(showOnboarding: false);
        await viewModel.InitializeAsync();
        SettingEditorViewModel viewDistance = Assert.Single(
            viewModel.FeatureGroups.SelectMany(group => group.Settings),
            setting => setting.Name == "View distance").Editor!;
        viewDistance.NumberValue = 1.5m;
        viewModel.OpenReviewCommand.Execute(null);

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            Button cancel = window.FindControl<Button>("ReviewCancelButton")!;
            Button confirm = window.FindControl<Button>("ReviewConfirmButton")!;
            Assert.True(cancel.IsVisible);
            Assert.True(confirm.IsVisible);

            confirm.Focus();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.True(cancel.IsFocused);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(viewModel.IsReviewingChanges);
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    });

    [Fact]
    public Task HighContrastUpdatesTheLiveWindowAndCanBeReversed() => Dispatch(() =>
    {
        MainViewModel viewModel = CreateViewModel(showOnboarding: false);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            TextBlock text = window.GetVisualDescendants().OfType<TextBlock>().First();
            IBrush? standardForeground = text.Foreground;

            viewModel.IsHighContrastEnabled = true;
            Assert.Same(Brushes.White, text.Foreground);

            viewModel.IsHighContrastEnabled = false;
            Assert.Same(standardForeground, text.Foreground);
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    });

    private static async Task Dispatch(Func<Task> action) =>
        await HeadlessUnitTestSession.StartNew(typeof(AncestorsEnhanced.App.App)).Dispatch(action, CancellationToken.None);

    private static Task Dispatch(Action action) =>
        HeadlessUnitTestSession.StartNew(typeof(AncestorsEnhanced.App.App)).Dispatch(action, CancellationToken.None);

    private static MainViewModel CreateViewModel(bool showOnboarding) =>
        new(new FixedInspector(CreateSnapshot()), new RecordingEditor(), showOnboarding: showOnboarding);

    private static GameInspectionSnapshot CreateSnapshot() =>
        new(
            DateTimeOffset.UnixEpoch,
            new GameInstallationSnapshot(StoreKind.Steam, HostKind.Windows, CompatibilityLayerKind.None, "library", "install", "5495393", true),
            "user-data",
            [new ConfigurationFileSnapshot("Engine.ini", "Engine.ini", true, 42, DateTimeOffset.UnixEpoch, [new IniSettingSnapshot("SystemSettings", "r.ViewDistanceScale", "1.2", 1)], null)],
            null,
            [],
            []);

    private sealed class FixedInspector(GameInspectionSnapshot snapshot) : IReadOnlyGameInspector
    {
        public GameInspectionSnapshot Inspect() => snapshot;
    }

    private sealed class RecordingEditor : IGameSettingsEditor
    {
        public SettingsChangePlan CreatePlan(GameInspectionSnapshot snapshot, IReadOnlyList<SettingChangeRequest> requests) =>
            new("review", DateTimeOffset.UnixEpoch, "5495393", snapshot.UserDataDirectory!, requests.Select(request => new SettingChangePreview(request.DisplayName, request.FileName, request.Key, "1.2", request.Value)).ToArray(), []);

        public SettingsOperationResult Apply(SettingsChangePlan plan) => SettingsOperationResult.Applied("Applied.", null);
        public void DiscardPlan(SettingsChangePlan plan) { }
        public bool CanRevertLast(GameInspectionSnapshot snapshot) => false;
        public SettingsOperationResult RevertLast(GameInspectionSnapshot snapshot) => SettingsOperationResult.Failed("Nothing to revert.");
    }
}
