namespace AncestorsEnhanced.Core.Editing;

public sealed record SettingChangeRequest(
    string SettingId,
    string DisplayName,
    string FileName,
    string Section,
    string Key,
    string? Value);
