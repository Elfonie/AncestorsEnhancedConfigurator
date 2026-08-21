using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Inspection;
using AncestorsEnhanced.Infrastructure.Platform;

namespace AncestorsEnhanced.Infrastructure.Editing;

public sealed class SafeGameSettingsEditor : IGameSettingsEditor
{
    private readonly SettingsChangePlanner _planner;
    private readonly SettingsTransaction _transaction;
    private readonly Func<bool> _isGameRunning;
    private readonly GameContextVerifier? _verifier;

    public SafeGameSettingsEditor()
        : this(
            () => DateTimeOffset.UtcNow,
            GameProcessProbe.IsAncestorsRunning,
            GameEditingGuard.IsExpectedNativeUserDataDirectory,
            new GameContextVerifier(ReadOnlyAncestorsInspector.CreateDefault()))
    {
    }

    internal SafeGameSettingsEditor(
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning)
        : this(utcNow, isGameRunning, _ => true, null)
    {
    }

    internal SafeGameSettingsEditor(
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning,
        Func<string, bool> isExpectedUserDataDirectory)
        : this(utcNow, isGameRunning, isExpectedUserDataDirectory, null)
    {
    }

    internal SafeGameSettingsEditor(
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning,
        Func<string, bool> isExpectedUserDataDirectory,
        GameContextVerifier? verifier)
    {
        _isGameRunning = isGameRunning;
        _verifier = verifier;
        _planner = new SettingsChangePlanner(utcNow, isExpectedUserDataDirectory);
        _transaction = new SettingsTransaction(
            utcNow,
            isGameRunning,
            isExpectedUserDataDirectory,
            verifier is null ? (_ => true) : plan => Revalidate(verifier, plan),
            verifier is null ? (_ => true) : snapshot => RevalidateSnapshot(verifier, snapshot));
    }

    public bool RecoverInterruptedChanges(GameInspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_isGameRunning())
        {
            return false;
        }

        VerifiedGameContext? context = VerifiedGameContext.TryCreateFromSnapshot(snapshot);
        if (context is null || (_verifier is not null && !_verifier.Verify(context)))
        {
            return false;
        }

        return ConfigurationFileOperations.RecoverInterruptedOperations(
            context.UserDataDirectory,
            context.InstallDirectory);
    }

    public SettingsChangePlan CreatePlan(
        GameInspectionSnapshot snapshot,
        IReadOnlyList<SettingChangeRequest> requests) =>
        _transaction.Issue(_planner.Create(snapshot, requests));

    public SettingsOperationResult Apply(SettingsChangePlan plan) => _transaction.Apply(plan);

    public void DiscardPlan(SettingsChangePlan plan) => _transaction.Discard(plan);

    public bool CanRevertLast(GameInspectionSnapshot snapshot) =>
        _transaction.CanRevertLast(snapshot);

    public SettingsOperationResult RevertLast(GameInspectionSnapshot snapshot) =>
        _transaction.RevertLast(snapshot);

    public bool CanRemoveToolChanges(GameInspectionSnapshot snapshot) =>
        _transaction.CanRemoveToolChanges(snapshot);

    public SettingsChangePlan CreateRemoveToolChangesPlan(GameInspectionSnapshot snapshot) =>
        _transaction.IssueToolChangeRemoval(snapshot);

    private static bool Revalidate(GameContextVerifier verifier, SettingsChangePlan plan)
    {
        VerifiedGameContext? current = verifier.Revalidate();
        return current is not null &&
            string.Equals(plan.ContextFingerprint, current.ContextFingerprint, StringComparison.Ordinal);
    }

    private static bool RevalidateSnapshot(GameContextVerifier verifier, GameInspectionSnapshot snapshot)
    {
        VerifiedGameContext? captured = VerifiedGameContext.TryCreateFromSnapshot(snapshot);
        if (captured is null)
        {
            return false;
        }

        return verifier.Verify(captured);
    }

}
