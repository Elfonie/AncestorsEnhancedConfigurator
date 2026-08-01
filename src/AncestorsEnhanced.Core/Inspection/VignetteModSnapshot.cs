namespace AncestorsEnhanced.Core.Inspection;

public sealed record VignetteModSnapshot(
    decimal? Percent,
    bool IsEditable,
    string Status,
    string? ActivePatchPath = null);
