namespace AncestorsEnhanced.App.ViewModels;

public sealed record FeatureSettingRowViewModel(
    string Name,
    string Value,
    string Description,
    string TechnicalKey,
    string AccentColor,
    bool ShowDescription,
    bool ShowTechnicalDetails,
    IReadOnlyList<SettingPresetValueRowViewModel> PresetValues,
    SettingEditorViewModel? Editor)
{
    public bool IsEditable => Editor is not null;

    public bool IsReadOnly => Editor is null;

    public bool HasPresetValues => PresetValues.Count > 0;

    public string ValueLabel => HasPresetValues ? "Controlled by" : "Current";
}

public sealed record SettingPresetValueRowViewModel(string Name, string Value);
