using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;

namespace AncestorsEnhanced.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version", StringComparer.Ordinal))
        {
            string bareVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            WriteCommandLineOutput($"Ancestors Enhanced Configurator {bareVersion}");
            return;
        }

        AppDiagnostics.Logger?.Write(StartupLine());
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            AppDiagnostics.Logger?.Write($"Unhandled exception: {eventArgs.ExceptionObject}");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppDiagnostics.Logger?.Write($"Unobserved task exception: {eventArgs.Exception}");
            eventArgs.SetObserved();
        };

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
    {
        AppBuilder app = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
#if DEBUG
        // Trace logging is a debug-only aid and must not be enabled in release builds.
        app = app.LogToTrace();
#endif
        return app;
    }

    private static void WriteCommandLineOutput(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value + Environment.NewLine);
        using Stream output = Console.OpenStandardOutput();
        if (output.CanWrite)
        {
            output.Write(bytes);
            output.Flush();
        }
    }

    private static string StartupLine()
    {
        string version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        string os = RuntimeInformation.OSDescription;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Ancestors Enhanced Configurator {version} starting on {os}");
    }
}
