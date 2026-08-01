namespace AncestorsEnhanced.App.ViewModels;

public sealed record FeatureSettingRowViewModel(
    string Name,
    string Value,
    string Description,
    string TechnicalKey,
    string AccentColor,
    bool ShowDescription,
    bool ShowTechnicalDetails,
    SettingEditorViewModel? Editor)
{
    public bool IsEditable => Editor is not null;

    public bool IsReadOnly => Editor is null;
}
