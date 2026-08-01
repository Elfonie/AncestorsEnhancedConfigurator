namespace AncestorsEnhanced.Core.Inspection;

public sealed record ConfigurationFileSnapshot(
    string Name,
    string FullPath,
    bool Exists,
    long? SizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    IReadOnlyList<IniSettingSnapshot> Settings,
    string? ReadError);
