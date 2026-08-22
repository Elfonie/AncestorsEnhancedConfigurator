namespace AncestorsEnhanced.Core.SaveGames;

/// <summary>
/// Describes whether a save operation changed persistent state and whether a warning
/// occurred after that change was committed.
/// </summary>
public enum SaveOperationCommitState
{
    NotCommitted,
    Committed,
    CommittedWithWarning,
}

public sealed record SaveGameOperationResult(
    bool Succeeded,
    string Message,
    string? CreatedCheckpointId = null,
    SaveOperationCommitState CommitState = SaveOperationCommitState.NotCommitted,
    bool IsTransientFailure = false);
