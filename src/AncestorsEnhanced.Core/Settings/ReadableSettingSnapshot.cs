namespace AncestorsEnhanced.Core.Settings;

public sealed record ReadableSettingSnapshot(
    string Id,
    string Category,
    string Name,
    string Value,
    string Description,
    string Source,
    ReadableSettingState State,
    double? Percentage);
