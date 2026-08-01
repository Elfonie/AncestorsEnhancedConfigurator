using AncestorsEnhanced.Core.Editing;

namespace AncestorsEnhanced.Core.Settings;

public sealed record FeatureSettingSnapshot(
    string Id,
    string Name,
    string Value,
    string Description,
    string Source,
    string? TechnicalKey,
    ReadableSettingState State,
    bool IsAdvanced,
    double? Percentage = null,
    IReadOnlyList<SettingPresetValueSnapshot>? PresetValues = null,
    string? ActivePresetName = null,
    SettingEditSnapshot? Editor = null);

public sealed record SettingPresetValueSnapshot(string Name, string Value);
