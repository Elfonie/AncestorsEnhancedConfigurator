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
        var desktopLifetime = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

        // AEC writes game files, so an unknown UI exception must never leave the app running
        // in a half-broken state. Only genuinely transient failures (cancellations, network
        // chatter) are swallowed; everything else logs, informs the user and shuts down in a
        // controlled way instead of continuing with unknown state.
        MainViewModel? mainViewModel = null;
        Dispatcher.UIThread.UnhandledException += (_, eventArgs) => HandleUnhandledUiException(
            eventArgs,
            desktopLifetime,
            () => mainViewModel);

        if (desktopLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Args?.Contains("--already-running", StringComparer.Ordinal) == true)
            {
                desktop.MainWindow = new AlreadyRunningWindow();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // The process mutex is acquired before Avalonia is initialized. Make
            // the activation endpoint ready just as early, otherwise a second
            // launch can see the mutex but find no pipe yet.
            Action? showExistingWindow = null;
            bool activationRequestedBeforeWindowWasReady = false;
            var activationListener = new SingleInstanceActivationListener(
                "AncestorsEnhancedConfigurator",
                () => Dispatcher.UIThread.Post(() =>
                {
                    if (showExistingWindow is null)
                    {
                        activationRequestedBeforeWindowWasReady = true;
                        return;
                    }

                    showExistingWindow();
                }));
            activationListener.Start();

            var accessibilityPreferencesStore = new AccessibilityPreferencesStore();
            AccessibilityPreferences preferences = accessibilityPreferencesStore.Load();
            AccessibilityTheme.Apply(this, preferences.HighContrastEnabled);

            // Persists preference changes and never loses them silently: a failed write is
            // logged and reported so the user knows the setting will not survive a restart.
            void SavePreferences(AccessibilityPreferences value)
            {
                if (accessibilityPreferencesStore.TrySave(value))
                {
                    return;
                }

                AppDiagnostics.Logger?.Write("Could not save application preferences.");
                AppDialogs.ShowPreferenceSaveWarning();
            }

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
                    mainViewModel?.SetDiscordRichPresenceStatus("Off");
                    return;
                }

                if (!windowOpened)
                {
                    mainViewModel?.SetDiscordRichPresenceStatus("Waiting for AEC to open");
                    return;
                }

                if (discordPresence is null)
                {
                    discordPresence = new DiscordRichPresenceService();
                    discordPresence.StateChanged += () =>
                    {
                        mainViewModel?.SetDiscordRichPresenceStatus(discordPresence.StatusMessage);
                        if (!discordPresence.IsActive)
                        {
                            discordTimer.Stop();
                        }
                    };
                }

                if (discordPresence.Start())
                {
                    discordTimer.Start();
                }

                mainViewModel?.SetDiscordRichPresenceStatus(discordPresence.StatusMessage);
            }

            var viewModel = new MainViewModel(
                ReadOnlyAncestorsInspector.CreateDefault(),
                new SafeGameSettingsEditor(),
                profileLibrary: new UserProfileLibrary(),
                highContrastEnabled: preferences.HighContrastEnabled,
                highContrastChanged: enabled =>
                {
                    AccessibilityTheme.Apply(this, enabled);
                    mainViewModel?.RefreshThemeBindings();
                    preferences = preferences with { HighContrastEnabled = enabled };
                    SavePreferences(preferences);
                },
                discordRichPresenceEnabled: preferences.DiscordRichPresenceEnabled,
                discordRichPresenceChanged: enabled =>
                {
                    preferences = preferences with { DiscordRichPresenceEnabled = enabled };
                    SavePreferences(preferences);
                    SetDiscordRichPresence(enabled);
                },
                applicationPreferencesWarning: accessibilityPreferencesStore.HasUnreadablePreferences
                    ? "AEC could not read the existing application preferences. They are preserved and none of the changed app options can be saved until you reset them."
                    : null,
                resetApplicationPreferences: () =>
                {
                    var defaults = new AccessibilityPreferences();
                    if (!accessibilityPreferencesStore.TryReset(defaults, out string? archivedFileName))
                    {
                        AppDiagnostics.Logger?.Write("Could not reset unreadable application preferences.");
                        return null;
                    }

                    preferences = defaults;
                    AccessibilityTheme.Apply(this, highContrastEnabled: false);
                    SetDiscordRichPresence(enabled: false);
                    return archivedFileName;
                },
                showOnboarding: !preferences.HasCompletedOnboarding,
                onboardingCompleted: () =>
                {
                    preferences = preferences with { HasCompletedOnboarding = true };
                    SavePreferences(preferences);
                },
                experimentalGraphicsSettingsEnabled: preferences.ExperimentalGraphicsSettingsEnabled,
                experimentalGraphicsSettingsChanged: enabled =>
                {
                    preferences = preferences with { ExperimentalGraphicsSettingsEnabled = enabled };
                    SavePreferences(preferences);
                },
                experimentalGameplaySettingsEnabled: preferences.ExperimentalGameplaySettingsEnabled,
                experimentalGameplaySettingsChanged: enabled =>
                {
                    preferences = preferences with { ExperimentalGameplaySettingsEnabled = enabled };
                    SavePreferences(preferences);
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
                    SavePreferences(preferences);
                },
                hardwareProbe: new SystemHardwareProbe());
            mainViewModel = viewModel;
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
            showExistingWindow = ShowWindow;
            if (activationRequestedBeforeWindowWasReady)
            {
                ShowWindow();
            }

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
                discordPresence = null;
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

    private static void HandleUnhandledUiException(
        DispatcherUnhandledExceptionEventArgs eventArgs,
        IClassicDesktopStyleApplicationLifetime? desktopLifetime,
        Func<MainViewModel?> currentViewModel)
    {
        Exception exception = eventArgs.Exception;
        if (FatalErrorPolicy.IsRecoverable(exception))
        {
            AppDiagnostics.Logger?.Write($"Recovered from transient UI exception: {exception}");
            eventArgs.Handled = true;
            return;
        }

        // Handled=true here does NOT mean the app continues. It only hands control of the
        // exit path to this handler so we can inform the user and shut down cleanly instead
        // of crashing with no feedback.
        eventArgs.Handled = true;
        AppDiagnostics.Logger?.Write($"Fatal UI exception: {exception}");
        TryDiscardPendingChanges(currentViewModel);
        AppDialogs.ShowFatalError(
            exception,
            () =>
            {
                if (desktopLifetime is not null)
                {
                    desktopLifetime.Shutdown();
                }
                else
                {
                    Environment.Exit(1);
                }
            });
    }

    /// <summary>
    /// Best-effort rollback before a fatal shutdown: unconfirmed changes only ever live in
    /// memory, so discarding them returns the user to the last confirmed on-disk state.
    /// Confirmed writes are already atomic (temp file + move + journal) and are left alone.
    /// </summary>
    private static void TryDiscardPendingChanges(Func<MainViewModel?> currentViewModel)
    {
        try
        {
            if (currentViewModel() is not { } viewModel)
            {
                return;
            }

            if (viewModel.IsAnyOperationRunning)
            {
                AppDiagnostics.Logger?.Write(
                    "A file operation was still running during the fatal error; its transaction "
                    + "journal keeps the write atomic, so nothing further is attempted.");
                return;
            }

            if (!viewModel.HasPendingChanges && !viewModel.IsReviewingChanges)
            {
                return;
            }

            if (viewModel.DiscardChangesCommand.CanExecute(null))
            {
                viewModel.DiscardChangesCommand.Execute(null);
                AppDiagnostics.Logger?.Write(
                    "Discarded pending unconfirmed changes before the fatal-error shutdown.");
            }
        }
        catch (Exception rollbackFailure)
        {
            AppDiagnostics.Logger?.Write($"Rollback attempt failed: {rollbackFailure}");
        }
    }
}
