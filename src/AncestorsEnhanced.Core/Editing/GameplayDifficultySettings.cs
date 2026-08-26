namespace AncestorsEnhanced.Core.Editing;

public sealed record GameplayDifficultySettings(
    int FoodPercent,
    int WaterPercent,
    int SleepPercent,
    int FallDamagePercent,
    int BleedingPercent = 100,
    int PoisonPercent = 100,
    int EnergyRecoveryPercent = 100,
    int WoundSleepHealingPercent = 100,
    int WoundStaminaPenaltyPercent = 100,
    int PoisonRecoveryPercent = 100,
    int RestDelayPercent = 100,
    int ExhaustionThresholdPercent = 100,
    int ExhaustionPenaltyPercent = 100,
    int WoundRecoveryDurationPercent = 100,
    int PoisonStaminaPenaltyPercent = 100)
{
    public static GameplayDifficultySettings GameDefault { get; } = new(100, 100, 100, 100, 100, 100, 100, 100, 100);

    public bool IsGameDefault => this == GameDefault;

    public void Validate()
    {
        ValidatePercent(FoodPercent, nameof(FoodPercent));
        ValidatePercent(WaterPercent, nameof(WaterPercent));
        ValidatePercent(SleepPercent, nameof(SleepPercent));
        ValidatePercent(FallDamagePercent, nameof(FallDamagePercent));
        ValidatePercent(BleedingPercent, nameof(BleedingPercent));
        ValidatePercent(PoisonPercent, nameof(PoisonPercent));
        ValidatePercent(EnergyRecoveryPercent, nameof(EnergyRecoveryPercent));
        ValidatePercent(WoundSleepHealingPercent, nameof(WoundSleepHealingPercent));
        ValidatePercent(WoundStaminaPenaltyPercent, nameof(WoundStaminaPenaltyPercent));
        ValidatePercent(PoisonRecoveryPercent, nameof(PoisonRecoveryPercent));
        ValidatePercent(RestDelayPercent, nameof(RestDelayPercent));
        ValidatePercent(ExhaustionThresholdPercent, nameof(ExhaustionThresholdPercent));
        ValidatePercent(ExhaustionPenaltyPercent, nameof(ExhaustionPenaltyPercent));
        ValidatePercent(WoundRecoveryDurationPercent, nameof(WoundRecoveryDurationPercent));
        ValidatePercent(PoisonStaminaPenaltyPercent, nameof(PoisonStaminaPenaltyPercent));
    }

    private static void ValidatePercent(int value, string parameterName)
    {
        if (value is < 10 or > 1000 || value % 10 != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Gameplay difficulty must use 10% steps from 10% through 1000%.");
        }
    }
}
