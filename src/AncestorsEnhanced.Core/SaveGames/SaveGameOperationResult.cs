namespace AncestorsEnhanced.Core.SaveGames;

/// <summary>
/// Describes how far a save operation got before finishing. This distinguishes a
/// failure before any commit (NotCommitted) from a failure that happened after the
/// target was already changed (CommittedWithWarning), so the UI never reports success
/// or failure for the wrong phase (see RB-6 / F007).
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
    SaveOperationCommitState CommitState = SaveOperationCommitState.NotCommitted);