namespace AncestorsEnhanced.Core.Settings;

public sealed record FeatureGroupSnapshot(
    string Id,
    string Category,
    string Name,
    string Summary,
    string Description,
    bool IsEssential,
    ReadableSettingState State,
    IReadOnlyList<FeatureSettingSnapshot> Settings,
    string? SimpleSummary = null,
    string? SimpleName = null);
