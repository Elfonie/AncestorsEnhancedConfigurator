namespace AncestorsEnhanced.App.ViewModels;

public sealed record FeatureSettingRowViewModel(
    string Name,
    string Value,
    string Description,
    string Source,
    string TechnicalDetails,
    string AccentColor,
    bool ShowTechnicalDetails,
    SettingEditorViewModel? Editor)
{
    public bool IsEditable => Editor is not null;
}
