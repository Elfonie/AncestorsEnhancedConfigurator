using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Core.Editing;

public interface IGameSettingsEditor
{
    bool RecoverInterruptedChanges(GameInspectionSnapshot snapshot) => false;

    SettingsChangePlan CreatePlan(
        GameInspectionSnapshot snapshot,
        IReadOnlyList<SettingChangeRequest> requests);

    SettingsOperationResult Apply(SettingsChangePlan plan);

    void DiscardPlan(SettingsChangePlan plan);

    bool CanRevertLast(GameInspectionSnapshot snapshot);

    SettingsOperationResult RevertLast(GameInspectionSnapshot snapshot);

    bool CanRemoveToolChanges(GameInspectionSnapshot snapshot) => false;

    SettingsChangePlan CreateRemoveToolChangesPlan(GameInspectionSnapshot snapshot) =>
        throw new NotSupportedException("Removing tool changes is not available.");
}
