using Avalonia.Media;

namespace AncestorsEnhanced.App.ViewModels;

public sealed record FeatureSettingRowViewModel(
    string Name,
    string Value,
    string Description,
    string Source,
    string TechnicalKey,
    string AccentColor,
    bool ShowDescription,
    bool ShowTechnicalDetails,
    IReadOnlyList<SettingPresetValueRowViewModel> PresetValues,
    string? ActivePresetName,
    SettingEditorViewModel? Editor,
    bool IsExperimental = false)
{
    public IBrush AccentBrush => StatusPresentation.BrushForLegacyAccent(AccentColor);

    public bool IsEditable => Editor is not null;

    public bool IsReadOnly => Editor is null;

    public bool HasPresetValues => PresetValues.Count > 0;

    public bool HasInspectionFailure => string.Equals(Value, "Not verified", StringComparison.Ordinal);

    public string ValueLabel => Editor?.HasChanges == true
        ? "Pending change"
        : Editor is { ShowOverrideToggle: false }
        ? "Current game value"
        : Editor?.HasCurrentOverride == true
        ? "Custom override"
        : Editor?.ShowUnknownGameValue == true && !HasPresetValues
            ? "Game controlled"
        : ActivePresetName is not null
            ? $"{ActivePresetName} game preset"
            : HasPresetValues
                ? "Game preset value unknown"
                : HasInspectionFailure
                    ? "Inspection status"
                    : "Game default";

    public string ReadOnlyLabel => HasInspectionFailure ? Source : "Read only";

    public string PresetExplanation => ActivePresetName is null
        ? "The game selects one of these values, but its active preset could not be read safely."
        : $"Active preset: {ActivePresetName}. The list below compares all three game presets.";
}

public sealed record SettingPresetValueRowViewModel(string Name, string Value);
