using Avalonia.Interactivity;

namespace AncestorsEnhanced.App.Views;

public partial class AlreadyRunningWindow : Avalonia.Controls.Window
{
    public AlreadyRunningWindow()
    {
        InitializeComponent();
    }

    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();
}
