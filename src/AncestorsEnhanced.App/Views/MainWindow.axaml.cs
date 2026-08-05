using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AncestorsEnhanced.App.ViewModels;

namespace AncestorsEnhanced.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsReviewingChanges) &&
            DataContext is MainViewModel { IsReviewingChanges: true })
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ReviewOverlay is { IsVisible: true })
                {
                    ReviewOverlay.Focus(NavigationMethod.Pointer);
                }
            });
        }
    }
    private Flyout? _activeDeleteFlyout;

    private void OnDeleteOpenClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Flyout: Flyout flyout })
        {
            _activeDeleteFlyout = flyout;
        }
    }

    private void OnConfirmDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Das transientliche Bestätigungs-Flyout nach der finalen Aktion sofort schließen.
        _activeDeleteFlyout?.Hide();
        _activeDeleteFlyout = null;
    }

}
