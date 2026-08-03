namespace AncestorsEnhanced.Core.SaveGames;

public sealed record SaveGameOperationResult(
    bool Succeeded,
    string Message,
    string? CreatedCheckpointId = null);
