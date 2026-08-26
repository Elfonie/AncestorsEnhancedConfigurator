using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.Core.Editing;

public interface IGameplayDifficultyEditor
{
    GameplayDifficultyState Inspect(GameInspectionSnapshot snapshot);

    SettingsChangePlan CreatePlan(
        GameInspectionSnapshot snapshot,
        GameplayDifficultySettings settings);

    SettingsOperationResult Apply(SettingsChangePlan plan);

    void DiscardPlan(SettingsChangePlan plan);
}
