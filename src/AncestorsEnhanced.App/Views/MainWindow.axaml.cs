using System.ComponentModel;
using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.Profiles;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AncestorsEnhanced.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _subscribedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
                 _restoreFocus is InputElement focusTarget)
        {
            Dispatcher.UIThread.Post(() => focusTarget.Focus(NavigationMethod.Tab));
            _restoreFocus = null;
        }
    }

    private IInputElement? _restoreFocus;

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
            viewModel.ReportProfileFileError("copy diagnostics");
        }
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string value = string.Concat(name.Select(character => invalid.Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(value) ? "AncestorsEnhancedProfile" : value;
    }
}
