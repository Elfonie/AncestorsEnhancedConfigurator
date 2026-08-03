namespace AncestorsEnhanced.Core.SaveGames;

public sealed record SaveGameManagerOptions(
    int MaxCheckpointsPerSlot = 50);
