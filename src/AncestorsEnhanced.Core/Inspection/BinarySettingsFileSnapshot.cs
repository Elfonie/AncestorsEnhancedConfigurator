namespace AncestorsEnhanced.Core.Inspection;

public sealed record BinarySettingsFileSnapshot(
    string Name,
    string FullPath,
    bool Exists,
    long? SizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    string FormatStatus,
    SystemGraphicsSettingsSnapshot? GraphicsSettings = null);
