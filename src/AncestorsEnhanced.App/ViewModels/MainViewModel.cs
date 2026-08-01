using CommunityToolkit.Mvvm.ComponentModel;
using AncestorsEnhanced.Core.Safety;

namespace AncestorsEnhanced.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        ApplicationSafetyProfile safetyProfile = ApplicationSafetyProfile.Foundation;

        ProductName = "Ancestors Enhanced Configurator";
        Phase = "Foundation · 0.1 development";
        SafetyStatus = safetyProfile.IsReadOnly
            ? "Read-only: game-file writes are disabled"
            : "Write operations enabled";
        Description =
            "The application shell, project boundaries, and automated tests are active. " +
            "Game detection is the next milestone; this build does not access Ancestors files.";
    }

    public string ProductName { get; }

    public string Phase { get; }

    public string SafetyStatus { get; }

    public string Description { get; }
}
