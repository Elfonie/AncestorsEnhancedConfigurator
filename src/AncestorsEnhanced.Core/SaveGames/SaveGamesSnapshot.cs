namespace AncestorsEnhanced.Core.SaveGames;

public sealed record SaveGamesSnapshot(
    DateTimeOffset InspectedAtUtc,
    string? UserDataDirectory,
    IReadOnlyList<SaveGameSlotSnapshot> Slots);
