using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.App.Views;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.Inspection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

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
            window.Closed += (_, _) => viewModel.Dispose();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
