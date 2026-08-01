namespace AncestorsEnhanced.Core.Editing;

public sealed record SettingsOperationResult(
    bool Succeeded,
    string Message,
    string? ManifestPath = null);
