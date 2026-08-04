using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;

namespace AncestorsEnhanced.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDiagnostics.Logger?.Write(StartupLine());
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            AppDiagnostics.Logger?.Write($"Unhandled exception: {eventArgs.ExceptionObject}");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            AppDiagnostics.Logger?.Write($"Unobserved task exception: {eventArgs.Exception}");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static string StartupLine()
    {
        string version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        string os = RuntimeInformation.OSDescription;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Ancestors Enhanced Configurator {version} starting on {os}");
    }
}