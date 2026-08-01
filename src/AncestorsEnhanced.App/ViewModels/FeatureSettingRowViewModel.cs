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
    string? ActivePresetName,
    SettingEditorViewModel? Editor)
{
    public bool IsEditable => Editor is not null;

    public bool IsReadOnly => Editor is null;

    public bool HasPresetValues => PresetValues.Count > 0;

    public string ValueLabel => HasPresetValues && ActivePresetName is null ? "Controlled by" : "Current";

    public string PresetExplanation => ActivePresetName is null
        ? "The game selects one of these values from its Low, Medium or High preset. The active preset is not currently readable."
        : $"Current game preset: {ActivePresetName}. The list below shows what Low, Medium and High would use.";
}

public sealed record SettingPresetValueRowViewModel(string Name, string Value);
