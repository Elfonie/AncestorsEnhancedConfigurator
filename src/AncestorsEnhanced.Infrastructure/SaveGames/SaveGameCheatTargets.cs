using AncestorsEnhanced.Core.SaveGames;

namespace AncestorsEnhanced.Infrastructure.SaveGames;

/// <summary>
/// Canonical cheat target definitions (F027). Every target is a structural
/// <see cref="CheatTargetSpec"/> with a unique schema path and authorised match count.
/// Both the injector and the post-reparse verification resolve the same exact paths, so
/// an equally named property elsewhere in the tree is never accepted by accident.
/// The paths below are the synthetic schema cases already verified by tests; real-world
/// paths for saves without such a fixture remain unverified and therefore fail closed.
/// </summary>
internal static class SaveGameCheatTargets
{
    public static IReadOnlyList<CheatTargetSpec> CheatTargetsFor(CheatKind kind) => kind switch
    {
        CheatKind.MaxNeuronalEnergy =>
        [
            new CheatTargetSpec(
                "<save>/RPGData/NeuronalEnergySources",
                "NeuronalEnergySources",
                "ArrayProperty",
                "FloatProperty",
                IsArray: true,
                999_999.0f),
        ],
        CheatKind.MaxNeeds => VitalityScalars(
            ["RegimenStamina", "Energy", "Stamina"],
            1_000.0f),
        CheatKind.HealClan => VitalityScalars(
            ["Energy", "Stamina"],
            1.0f,
            includeHealth: true),
        CheatKind.ForceMutations => [],
        _ => [],
    };

    private static CheatTargetSpec[] VitalityScalars(
        string[] names,
        float value,
        bool includeHealth = false)
    {
        var specs = new List<CheatTargetSpec>();
        foreach (string name in names)
        {
            specs.Add(Scalar(
                $"<save>/PlayerControllerData/CharacterData/VitalityData/{name}",
                name,
                value));
        }

        if (includeHealth)
        {
            specs.Add(Scalar(
                "<save>/PlayerControllerData/CharacterData/HealthData/Health",
                "Health",
                value));
        }

        return specs.ToArray();
    }

    private static CheatTargetSpec Scalar(string schemaPath, string name, float value) =>
        new(schemaPath, name, "FloatProperty", null, IsArray: false, value);
}
