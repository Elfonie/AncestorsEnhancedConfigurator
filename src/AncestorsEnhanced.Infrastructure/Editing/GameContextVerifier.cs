using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Inspection;

namespace AncestorsEnhanced.Infrastructure.Editing;

/// <summary>
/// Re-validates the live game context immediately before a mutating operation using the
/// same detection logic the initial scan used (there is deliberately no second,
/// divergent installation detection). If the current reality no longer
/// matches the <see cref="VerifiedGameContext"/> the caller captured at plan/preview
/// time, verification fails without any write having happened.
/// </summary>
public sealed class GameContextVerifier
{
    private readonly IReadOnlyGameInspector _inspector;

    public GameContextVerifier(IReadOnlyGameInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        _inspector = inspector;
    }

    /// <summary>
    /// Re-spools the recogniser and validates the resulting snapshot against the
    /// supplied verified context. Returns <c>true</c> only when the live state is still
    /// the same supported installation with the same user data, identity and layout.
    /// </summary>
    public bool Verify(VerifiedGameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameInspectionSnapshot snapshot;
        try
        {
            snapshot = _inspector.Inspect();
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            return false;
        }

        return context.Matches(snapshot);
    }
    /// <summary>
    /// Re-detects the live installation and returns a fresh verified context, or
    /// <c>null</c> when the current state is no longer a supported Ancestors install
    /// (fail-closed, including a content-signature read error).
    /// </summary>
    public VerifiedGameContext? Revalidate()
    {
        GameInspectionSnapshot snapshot;
        try
        {
            snapshot = _inspector.Inspect();
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            return null;
        }

        return VerifiedGameContext.TryCreateFromSnapshot(snapshot);
    }
}
