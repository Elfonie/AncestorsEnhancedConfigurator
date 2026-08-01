using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class FeatureGroupRowViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    private bool _isExpanded;

    public FeatureGroupRowViewModel(
        string id,
        string category,
        string name,
        string summary,
        string description,
        string accentColor,
        string settingCount,
        IReadOnlyList<FeatureSettingRowViewModel> settings,
        bool showDescription,
        bool isExpanded)
    {
        Id = id;
        Category = category;
        Name = name;
        Summary = summary;
        Description = description;
        AccentColor = accentColor;
        SettingCount = settingCount;
        Settings = settings;
        ShowDescription = showDescription;
        IsExpanded = isExpanded;
    }

    public string Id { get; }

    public string Category { get; }

    public string Name { get; }

    public string Summary { get; }

    public string Description { get; }

    public string AccentColor { get; }

    public string SettingCount { get; }

    public IReadOnlyList<FeatureSettingRowViewModel> Settings { get; }

    public bool ShowDescription { get; }

    public string Chevron => IsExpanded ? "⌃" : "⌄";

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
