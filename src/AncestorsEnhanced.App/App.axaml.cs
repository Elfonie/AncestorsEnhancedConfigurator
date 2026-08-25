using AncestorsEnhanced.App.Discord;
using AncestorsEnhanced.App.Accessibility;
using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.App.Views;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Infrastructure.Inspection;
using AncestorsEnhanced.Infrastructure.Profiles;
using AncestorsEnhanced.Infrastructure.Platform;
using Avalonia;
using Avalonia.Controls;
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
            if (desktop.Args?.Contains("--already-running", StringComparer.Ordinal) == true)
            {
                desktop.MainWindow = new AlreadyRunningWindow();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var accessibilityPreferences = new AccessibilityPreferencesStore();
            AccessibilityPreferences preferences = accessibilityPreferences.Load();
            AccessibilityTheme.Apply(this, preferences.HighContrastEnabled);

            DiscordRichPresenceService? discordPresence = null;
            var discordTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            discordTimer.Tick += (_, _) => discordPresence?.RunCallbacks();
            bool windowOpened = false;
            void SetDiscordRichPresence(bool enabled)
            {
                if (!enabled)
                {
                    discordTimer.Stop();
                    discordPresence?.Dispose();
                    discordPresence = null;
                    return;
                }

                if (!windowOpened)
                {
                    return;
                }

                discordPresence ??= new DiscordRichPresenceService();
                discordPresence.Start();
                discordTimer.Start();
            }

            var viewModel = new MainViewModel(
                ReadOnlyAncestorsInspector.CreateDefault(),
                new SafeGameSettingsEditor(),
                profileLibrary: new UserProfileLibrary(),
                highContrastEnabled: preferences.HighContrastEnabled,
                highContrastChanged: enabled =>
                {
                    AccessibilityTheme.Apply(this, enabled);
                    preferences = preferences with { HighContrastEnabled = enabled };
                    accessibilityPreferences.TrySave(preferences);
                },
                discordRichPresenceEnabled: preferences.DiscordRichPresenceEnabled,
                discordRichPresenceChanged: enabled =>
                {
                    preferences = preferences with { DiscordRichPresenceEnabled = enabled };
                    accessibilityPreferences.TrySave(preferences);
                    SetDiscordRichPresence(enabled);
                },
                showOnboarding: !preferences.HasCompletedOnboarding,
                onboardingCompleted: () =>
                {
                    preferences = preferences with { HasCompletedOnboarding = true };
                    accessibilityPreferences.TrySave(preferences);
                },
                experimentalGraphicsSettingsEnabled: preferences.ExperimentalGraphicsSettingsEnabled,
                experimentalGraphicsSettingsChanged: enabled =>
                {
                    preferences = preferences with { ExperimentalGraphicsSettingsEnabled = enabled };
                    accessibilityPreferences.TrySave(preferences);
                },
                hasAcknowledgedDetailedHardwareScan: preferences.HasAcknowledgedDetailedHardwareScan,
                detailedHardwareSnapshot: preferences.DetailedHardwareSnapshot,
                detailedHardwareScanCompleted: snapshot =>
                {
                    preferences = preferences with
                    {
                        HasAcknowledgedDetailedHardwareScan = true,
                        DetailedHardwareSnapshot = snapshot,
                    };
                    accessibilityPreferences.TrySave(preferences);
                },
                hardwareProbe: new SystemHardwareProbe());
            var window = new MainWindow
            {
                DataContext = viewModel,
            };
            var trayIcon = new TrayIcon
            {
                Icon = window.Icon,
                ToolTipText = "Ancestors Enhanced Configurator",
                IsVisible = false,
            };
            var trayIcons = new TrayIcons { trayIcon };
            TrayIcon.SetIcons(this, trayIcons);

            bool exitingFromTray = false;
            void ShowWindow()
            {
                trayIcon.IsVisible = false;
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
            }

            var activationListener = new SingleInstanceActivationListener(
                "AncestorsEnhancedConfigurator",
                () => Dispatcher.UIThread.Post(ShowWindow));
            activationListener.Start();

            trayIcon.Clicked += (_, _) => ShowWindow();
            var openMenuItem = new NativeMenuItem { Header = "Open Ancestors Enhanced" };
            openMenuItem.Click += (_, _) => ShowWindow();
            var exitMenuItem = new NativeMenuItem { Header = "Exit" };
            exitMenuItem.Click += (_, _) =>
            {
                exitingFromTray = true;
                trayIcon.IsVisible = false;
                window.Close();
            };
            trayIcon.Menu = new NativeMenu
            {
                Items = { openMenuItem, exitMenuItem },
            };
            window.Closing += (_, eventArgs) =>
            {
                if (!exitingFromTray && viewModel.ShouldKeepRunningInTrayOnClose)
                {
                    eventArgs.Cancel = true;
                    trayIcon.IsVisible = true;
                    window.Hide();
                }
            };
            window.Opened += async (_, _) =>
            {
                windowOpened = true;
                SetDiscordRichPresence(preferences.DiscordRichPresenceEnabled);
                await viewModel.InitializeAsync();
            };
            int retryCount = 0;
            const int MaxAutomaticRetries = 3;
            DispatcherTimer? retryTimer = null;
            window.Closed += (_, _) =>
            {
                retryTimer?.Stop();
                discordTimer.Stop();
                discordPresence?.Dispose();
                activationListener.Dispose();
                trayIcon.Dispose();
                viewModel.Dispose();
            };
            retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            retryTimer.Tick += async (_, _) =>
            {
                if (viewModel.IsAnyOperationRunning || viewModel.HasPendingChanges || viewModel.IsReviewingChanges)
                {
                    return;
                }

                if (!viewModel.CanRetrySaveManagerInitialization)
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
