namespace AncestorsEnhanced.Core.Editing;

public sealed record SettingChangePreview(
    string DisplayName,
    string FileName,
    string Key,
    string? Before,
    string? After);
