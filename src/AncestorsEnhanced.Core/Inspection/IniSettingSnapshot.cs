namespace AncestorsEnhanced.Core.Inspection;

public sealed record IniSettingSnapshot(
    string Section,
    string Key,
    string Value,
    int LineNumber);
