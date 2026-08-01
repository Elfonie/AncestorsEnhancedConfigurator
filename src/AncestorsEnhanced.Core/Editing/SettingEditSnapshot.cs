namespace AncestorsEnhanced.Core.Editing;

public sealed record SettingEditSnapshot(
    string FileName,
    string Section,
    string Key,
    SettingEditorKind Kind,
    string DefaultValue,
    string? CurrentOverride,
    decimal? Minimum = null,
    decimal? Maximum = null,
    decimal? Increment = null,
    IReadOnlyList<SettingChoice>? Choices = null);
