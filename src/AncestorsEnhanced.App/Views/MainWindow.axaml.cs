using System.ComponentModel;
using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.Profiles;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AncestorsEnhanced.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _subscribedViewModel;
    private readonly Dictionary<TextBlock, IBrush?> _standardTextForegrounds = [];
    private readonly Dictionary<Border, (IBrush? Background, IBrush? BorderBrush)> _standardBorderBrushes = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as MainViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            if (_subscribedViewModel.IsOnboardingVisible)
            {
                Dispatcher.UIThread.Post(() => this.FindControl<Button>("OnboardingPrimaryButton")?.Focus(NavigationMethod.Tab));
            }
            ApplyHighContrastOverrides(_subscribedViewModel.IsHighContrastEnabled);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsHighContrastEnabled) &&
            DataContext is MainViewModel themeViewModel)
        {
            ApplyHighContrastOverrides(themeViewModel.IsHighContrastEnabled);
        }

        if (e.PropertyName == nameof(MainViewModel.IsReviewingChanges) &&
            DataContext is MainViewModel { IsReviewingChanges: true })
        {
            _restoreFocus = FocusManager?.GetFocusedElement() ?? _restoreFocus;
            Dispatcher.UIThread.Post(() =>
            {
                if (ReviewOverlay is { IsVisible: true })
                {
                    ReviewOverlay.Focus(NavigationMethod.Tab);
                    var cancel = this.FindControl<Button>("ReviewCancelButton");
                    if (cancel is not null)
                    {
                        cancel.Focus(NavigationMethod.Tab);
                    }
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsReviewingChanges) &&
                 DataContext is MainViewModel { IsReviewingChanges: false } &&
                 _restoreFocus is InputElement onboardingFocusTarget)
        {
            Dispatcher.UIThread.Post(() => onboardingFocusTarget.Focus(NavigationMethod.Tab));
            _restoreFocus = null;
        }
        else if (e.PropertyName == nameof(MainViewModel.IsOnboardingVisible) &&
                 DataContext is MainViewModel { IsOnboardingVisible: true })
        {
            _restoreFocus = FocusManager?.GetFocusedElement() ?? _restoreFocus;
            Dispatcher.UIThread.Post(() =>
            {
                var primary = this.FindControl<Button>("OnboardingPrimaryButton");
                primary?.Focus(NavigationMethod.Tab);
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsOnboardingVisible) &&
                 DataContext is MainViewModel { IsOnboardingVisible: false } &&
                 _restoreFocus is InputElement focusTarget)
        {
            Dispatcher.UIThread.Post(() => focusTarget.Focus(NavigationMethod.Tab));
            _restoreFocus = null;
        }
    }

    private IInputElement? _restoreFocus;

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || GetActiveDialog() is not Control dialog)
        {
            return;
        }

        List<Control> focusableControls = dialog.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.Focusable && control.IsTabStop && control.IsEffectivelyVisible && control.IsEffectivelyEnabled)
            .ToList();
        if (focusableControls.Count == 0)
        {
            return;
        }

        e.Handled = true;
        Control? focusedControl = FocusManager?.GetFocusedElement() as Control;
        int currentIndex = focusedControl is null ? -1 : focusableControls.IndexOf(focusedControl);
        int nextIndex;
        if (currentIndex < 0)
        {
            nextIndex = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? focusableControls.Count - 1 : 0;
        }
        else
        {
            int direction = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            nextIndex = (currentIndex + direction + focusableControls.Count) % focusableControls.Count;
        }

        focusableControls[nextIndex].Focus(NavigationMethod.Tab);
    }

    private Border? GetActiveDialog()
    {
        if (OnboardingOverlay is { IsVisible: true })
        {
            return this.FindControl<Border>("OnboardingDialog");
        }

        return ReviewOverlay is { IsVisible: true }
            ? this.FindControl<Border>("ReviewDialog")
            : null;
    }

    private void ApplyHighContrastOverrides(bool enabled)
    {
        if (enabled)
        {
            foreach (TextBlock textBlock in this.GetVisualDescendants().OfType<TextBlock>())
            {
                if (!_standardTextForegrounds.ContainsKey(textBlock))
                {
                    _standardTextForegrounds[textBlock] = textBlock.Foreground;
                }
                textBlock.Foreground = Brushes.White;
            }

            foreach (Border border in this.GetVisualDescendants().OfType<Border>())
            {
                if (!_standardBorderBrushes.ContainsKey(border))
                {
                    _standardBorderBrushes[border] = (border.Background, border.BorderBrush);
                }
                border.Background = Brushes.Black;
                border.BorderBrush = Brushes.White;
            }
            return;
        }

        foreach ((TextBlock textBlock, IBrush? foreground) in _standardTextForegrounds)
        {
            textBlock.Foreground = foreground;
        }
        _standardTextForegrounds.Clear();

        foreach ((Border border, (IBrush? background, IBrush? borderBrush)) in _standardBorderBrushes)
        {
            border.Background = background;
            border.BorderBrush = borderBrush;
        }
        _standardBorderBrushes.Clear();
    }

    private async void ImportProfileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || StorageProvider is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import Ancestors Enhanced profile",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Ancestors Enhanced profile")
                        {
                            Patterns = ["*.aecprofile"],
                        },
                    ],
                });
            IStorageFile? file = files.Count > 0 ? files[0] : null;
            if (file is not null && file.TryGetLocalPath() is string path)
            {
                viewModel.ImportProfile(path);
            }
            else if (file is not null)
            {
                viewModel.ReportProfileFileError("import");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            viewModel.ReportProfileFileError("import");
        }
    }

    private async void ExportProfileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: UserProfileRowViewModel row } ||
            DataContext is not MainViewModel viewModel ||
            StorageProvider is null)
        {
            return;
        }

        UserProfile? profile = viewModel.GetProfileForExport(row.Id);
        if (profile is null)
        {
            return;
        }

        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export Ancestors Enhanced profile",
                    SuggestedFileName = SanitizeFileName(profile.Name) + ".aecprofile",
                    DefaultExtension = "aecprofile",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Ancestors Enhanced profile")
                        {
                            Patterns = ["*.aecprofile"],
                        },
                    ],
                });
            if (file is null)
            {
                return;
            }

            await using Stream stream = await file.OpenWriteAsync();
            byte[] content = UserProfileCodec.Serialize(profile);
            await stream.WriteAsync(content);
            await stream.FlushAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            viewModel.ReportProfileFileError("export");
        }
    }

    private void LoadStoredProfileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: UserProfileRowViewModel row } &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.LoadProfileCommand.Execute(row);
        }
    }

    private async void CopyDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || Clipboard is null)
        {
            return;
        }

        try
        {
            await Clipboard.SetTextAsync(viewModel.CreateDiagnosticsReport());
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            viewModel.ReportDiagnosticsCopyError();
        }
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string value = string.Concat(name.Select(character => invalid.Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(value) ? "AncestorsEnhancedProfile" : value;
    }
}
