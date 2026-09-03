using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;

namespace AncestorsEnhanced.App.ViewModels;

public partial class FeatureGroupRowViewModel : ViewModelBase, IDisposable
{
    private bool _hasResettableChanges;
    private int _changedSettingCount;
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

    public IBrush AccentBrush => StatusPresentation.BrushForLegacyAccent(AccentColor);

    public string SettingCount { get; }

    public IReadOnlyList<FeatureSettingRowViewModel> Settings { get; }

    public bool ShowDescription { get; }

    public bool HasResettableChanges => _hasResettableChanges;

    public bool HasChangedSettings => _changedSettingCount > 0;

    public string ChangeBadge => _changedSettingCount == 1 ? "1 changed" : $"{_changedSettingCount} changed";

    public string Chevron => IsExpanded ? "⌃" : "⌄";

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    private void OnEditorChanged(object? sender, EventArgs e) => RefreshResettableChanges();

    private void RefreshResettableChanges()
    {
        int changedCount = Settings.Count(setting => setting.Editor is { } editor && (editor.HasActiveOverride || editor.HasChanges));
        bool value = changedCount > 0;
        if (_hasResettableChanges != value)
        {
            _hasResettableChanges = value;
            OnPropertyChanged(nameof(HasResettableChanges));
        }

        if (_changedSettingCount != changedCount)
        {
            _changedSettingCount = changedCount;
            OnPropertyChanged(nameof(HasChangedSettings));
            OnPropertyChanged(nameof(ChangeBadge));
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
