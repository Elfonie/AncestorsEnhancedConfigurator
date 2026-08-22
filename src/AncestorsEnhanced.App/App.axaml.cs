using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.App.Views;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.Inspection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AncestorsEnhanced.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel(
                ReadOnlyAncestorsInspector.CreateDefault(),
                new SafeGameSettingsEditor());
            var window = new MainWindow
            {
                DataContext = viewModel,
            };
            window.Opened += async (_, _) => await viewModel.InitializeAsync();
            int retryCount = 0;
            const int MaxAutomaticRetries = 3;
            DispatcherTimer? retryTimer = null;
            window.Closed += (_, _) =>
            {
                retryTimer?.Stop();
                viewModel.Dispose();
            };
            retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            retryTimer.Tick += async (_, _) =>
            {
                if (viewModel.IsAnyOperationRunning || viewModel.HasPendingChanges || viewModel.IsReviewingChanges)
                {
                    return;
                }

                bool allAvailable = !viewModel.IsCheatUnavailable && !viewModel.IsSaveManagerUnavailable;
                if (allAvailable)
                {
                    retryTimer.Stop();
                    return;
                }

                // Only a few automatic retries; afterwards the user uses the
                // visible Reload / Scan-again buttons.
                if (retryCount >= MaxAutomaticRetries)
                {
                    retryTimer.Stop();
                    return;
                }

                retryCount++;
                await viewModel.RefreshCommand.ExecuteAsync(null);
            };
            retryTimer.Start();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
