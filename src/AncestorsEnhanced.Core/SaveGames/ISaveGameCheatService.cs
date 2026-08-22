namespace AncestorsEnhanced.Core.SaveGames;

/// <summary>Outcome of applying and saving a cheat injection.</summary>
public sealed class CheatApplyResult
{
    public CheatApplyResult(bool succeeded, string message, string? checkpointId = null)
    {
        Succeeded = succeeded;
        Message = message;
        CheckpointId = checkpointId;
    }

    public bool Succeeded { get; }

    public string Message { get; }

    public string? CheckpointId { get; }
}

/// <summary>Reads a slot save and stores an experimental mutation as a new checkpoint.</summary>
public interface ISaveGameCheatService
{
    CheatApplyResult Apply(CheatKind kind, string slotNumber);
}
