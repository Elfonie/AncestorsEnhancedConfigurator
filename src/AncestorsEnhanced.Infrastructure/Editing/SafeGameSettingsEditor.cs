using System.Diagnostics;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Infrastructure.Editing;

public sealed class SafeGameSettingsEditor : IGameSettingsEditor
{
    private readonly SettingsChangePlanner _planner;
    private readonly SettingsTransaction _transaction;

    public SafeGameSettingsEditor()
        : this(
            () => DateTimeOffset.UtcNow,
            IsAncestorsRunning,
            GameEditingGuard.IsExpectedNativeUserDataDirectory)
    {
    }

    internal SafeGameSettingsEditor(
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning)
        : this(utcNow, isGameRunning, _ => true)
    {
    }

    internal SafeGameSettingsEditor(
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning,
        Func<string, bool> isExpectedUserDataDirectory)
    {
        _planner = new SettingsChangePlanner(utcNow, isExpectedUserDataDirectory);
        _transaction = new SettingsTransaction(
            utcNow,
            isGameRunning,
            isExpectedUserDataDirectory);
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

    private static bool IsAncestorsRunning()
    {
        try
        {
            return Process.GetProcessesByName("Ancestors-Win64-Shipping").Length > 0 ||
                   Process.GetProcessesByName("Ancestors").Length > 0;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
