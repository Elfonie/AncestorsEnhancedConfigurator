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

        if (args.Contains("--version", StringComparer.Ordinal))
        {
            // No UI, no game files: print product name and version, exit 0.
            string bareVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            Console.WriteLine($"Ancestors Enhanced Configurator {bareVersion}");
            return;
        }

        using var singleInstance = new AncestorsEnhanced.Infrastructure.Platform.SingleInstanceGuard(
            "AncestorsEnhancedConfigurator");
        if (!singleInstance.IsAcquired)
        {
            AppDiagnostics.Logger?.Write("Second instance detected; shutting down.");
            return;
        }

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