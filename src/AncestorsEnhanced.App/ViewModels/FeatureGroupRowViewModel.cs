using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class FeatureGroupRowViewModel : ViewModelBase, IDisposable
{
    private bool _hasResettableChanges;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    public partial bool IsExpanded { get; set; }

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
        foreach (FeatureSettingRowViewModel setting in settings)
        {
            if (setting.Editor is { } editor)
            {
                editor.Changed += OnEditorChanged;
            }
        }
        RefreshResettableChanges();
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

    public bool HasResettableChanges => _hasResettableChanges;

    public string Chevron => IsExpanded ? "⌃" : "⌄";

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    private void OnEditorChanged(object? sender, EventArgs e) => RefreshResettableChanges();

    private void RefreshResettableChanges()
    {
        bool value = Settings.Any(setting => setting.Editor is { } editor && (editor.HasActiveOverride || editor.HasChanges));
        if (_hasResettableChanges != value)
        {
            _hasResettableChanges = value;
            OnPropertyChanged(nameof(HasResettableChanges));
        }
    }

    public void Dispose()
    {
        foreach (FeatureSettingRowViewModel setting in Settings)
        {
            if (setting.Editor is { } editor)
            {
                editor.Changed -= OnEditorChanged;
            }
        }
        GC.SuppressFinalize(this);
    }
}
