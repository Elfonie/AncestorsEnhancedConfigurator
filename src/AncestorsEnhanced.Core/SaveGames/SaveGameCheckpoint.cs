namespace AncestorsEnhanced.Core.SaveGames;

public sealed record SaveGameCheckpoint(
    string Id,
    DateTimeOffset CreatedAtUtc,
    string SlotNumber,
    long SizeBytes,
    string Sha256);
