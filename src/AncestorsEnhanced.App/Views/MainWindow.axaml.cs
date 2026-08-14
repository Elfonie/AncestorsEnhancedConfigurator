using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AncestorsEnhanced.App.ViewModels;

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
}
