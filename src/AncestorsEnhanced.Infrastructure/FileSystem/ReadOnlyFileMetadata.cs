namespace AncestorsEnhanced.Infrastructure.FileSystem;

internal sealed record ReadOnlyFileMetadata(
    string Name,
    string FullPath,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc);
