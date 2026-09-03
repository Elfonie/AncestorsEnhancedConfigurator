using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AncestorsEnhanced.App;
/// <summary>
/// Small app-owned dialogs for preference save failures and fatal errors. Built in code
/// (no XAML) and styled exclusively with DynamicResource brushes so they always follow the
/// active theme, including high contrast.
/// </summary>
internal static class AppDialogs
{
    private static Window? OwnerWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>
    /// Non-blocking notice shown when preferences could not be persisted. The change stays
    /// active for this session; the user is told that the next start will fall back.
    /// </summary>
    public static void ShowPreferenceSaveWarning()
    {
        RunOnUIThread(() =>
        {
            Window window = CreateDialog(
                "Ancestors Enhanced Configurator",
                out StackPanel content);
            content.Children.Add(Text("Could not save application preferences.", FontWeight.SemiBold));
            content.Children.Add(Text(
                "Your change works for this session, but it could not be written to disk. "
                + "The next start falls back to the previous settings. Check that your user "
                + "profile folder is writable and not managed by sync software that blocks writes.",
                FontWeight.Normal,
                "SecondaryTextBrush"));
            content.Children.Add(SingleButton("OK", window));

            Show(window, OwnerWindow, modal: false);
        });
    }

    /// <summary>
    /// Blocking fatal-error dialog shown before a controlled shutdown. <paramref name="shutdown"/>
    /// is invoked when the dialog closes.
    /// </summary>
    public static void ShowFatalError(Exception exception, Action shutdown)
    {
        RunOnUIThread(() =>
        {
            string details =
                exception.GetType().FullName + Environment.NewLine
                + exception.Message + Environment.NewLine
                + exception.StackTrace;

            Window window = CreateDialog("Ancestors Enhanced Configurator - unexpected error", out StackPanel content);
            content.Children.Add(Text("An unexpected error occurred.", FontWeight.SemiBold));
            content.Children.Add(Text(
                "Ancestors Enhanced Configurator must close to stay safe. Pending unconfirmed "
                + "changes are discarded; file changes you already confirmed are unaffected.",
                FontWeight.Normal,
                "SecondaryTextBrush"));

            var detailsBox = new TextBox
            {
                Text = details,
                IsReadOnly = true,
                AcceptsReturn = true,
                Height = 130,
                FontFamily = new FontFamily("Consolas, 'Cascadia Mono', monospace"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            };
            content.Children.Add(detailsBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
            };
            var copyButton = new Button { Content = "Copy details" };
            copyButton.Click += async (_, _) =>
            {
                try
                {
                    if (window.Clipboard is { } clipboard)
                    {
                        await clipboard.SetTextAsync(details);
                    }
                }
                catch (Exception clipboardFailure)
                {
                    AppDiagnostics.Logger?.Write($"Could not copy fatal error details: {clipboardFailure}");
                }
            };
            var closeButton = new Button
            {
                Content = "Close",
                Classes = { "PrimaryAction" },
            };
            closeButton.Click += (_, _) => window.Close();
            buttons.Children.Add(copyButton);
            buttons.Children.Add(closeButton);
            content.Children.Add(buttons);

            window.Closed += (_, _) =>
            {
                try
                {
                    shutdown();
                }
                catch (Exception shutdownFailure)
                {
                    AppDiagnostics.Logger?.Write($"Controlled shutdown failed: {shutdownFailure}");
                    Environment.Exit(1);
                }
            };

            Show(window, null, modal: false);
        });
    }

    private static void RunOnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private static Window CreateDialog(string title, out StackPanel content)
    {
        content = new StackPanel { Spacing = 10 };
        return new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            MinWidth = 420,
            MaxWidth = 560,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer
            {
                Content = new Border
                {
                    Background = Application.Current?.FindResource("SurfaceBrush") as IBrush,
                    Padding = new Thickness(18),
                    Child = content,
                },
            },
        };
    }

    private static TextBlock Text(string value, FontWeight weight, string? brushKey = "PrimaryTextBrush") => new()
    {
        Text = value,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
        Foreground = brushKey is null ? null : Application.Current?.FindResource(brushKey) as IBrush,
    };

    private static Button SingleButton(string label, Window window)
    {
        var button = new Button
        {
            Content = label,
            Classes = { "PrimaryAction" },
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        button.Click += (_, _) => window.Close();
        return button;
    }

    private static void Show(Window window, Window? owner, bool modal)
    {
        if (owner is not null && owner.IsVisible)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        if (modal && owner is not null)
        {
            window.ShowDialog(owner);
        }
        else
        {
            window.Show();
            window.Activate();
        }
    }
}
