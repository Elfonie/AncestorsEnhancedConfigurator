namespace AncestorsEnhanced.Core.Inspection;

public sealed record PakFileSnapshot(
    string Name,
    string FullPath,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    PakClassification Classification);
