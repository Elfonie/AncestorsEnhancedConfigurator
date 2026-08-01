using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Core.Editing;

public interface IGameSettingsEditor
{
    SettingsChangePlan CreatePlan(
        GameInspectionSnapshot snapshot,
        IReadOnlyList<SettingChangeRequest> requests);

    SettingsOperationResult Apply(SettingsChangePlan plan);

    bool CanRevertLast(GameInspectionSnapshot snapshot);

    SettingsOperationResult RevertLast(GameInspectionSnapshot snapshot);
}
