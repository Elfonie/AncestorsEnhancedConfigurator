namespace AncestorsEnhanced.Core.SaveGames;

public sealed record SaveGameSlotSnapshot(
    string SlotNumber,
    string FileName,
    string FullPath,
    bool Exists,
    long? SizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    IReadOnlyList<SaveGameCheckpoint> Checkpoints,
    string? ErrorMessage = null);
