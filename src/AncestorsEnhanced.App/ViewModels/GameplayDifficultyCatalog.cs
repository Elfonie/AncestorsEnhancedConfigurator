using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.App.ViewModels;

/// <summary>
/// Build-bound presentation data for the exact researched gameplay patch catalog.
/// </summary>
public static class GameplayDifficultyCatalog
{
    public const string SupportedSteamBuildId = AncestorsGameProfile.SupportedSteamBuildId;

    public static IReadOnlyList<GameplayDifficultyPresetViewModel> CreatePresets() =>
    [
        new("Game default", "100% across every available control", "Removes the AEC gameplay PAK and restores unmodified game behavior.", 100),
        new("Explorer", "Lower survival pressure", "Makes food, water and sleep portions more effective, and reduces verified fall, bleeding and poison damage. It does not slow the normal need-bar timer.", 70),
        new("Survival", "Higher survival pressure", "Makes food, water and sleep portions less effective, and raises verified fall, bleeding and poison damage. It does not speed up the normal need-bar timer.", 130),
        new("Hardcore", "Maximum supported simple pressure", "Uses 150% for required portions and each verified hazard category. The normal need-bar timer remains unchanged.", 150),
        new("Custom", "Choose each category yourself", "Starts from game defaults; adjust each available category below in 10% steps.", 100),
    ];

    public static IReadOnlyList<GameplayDifficultyControlViewModel> CreateSimpleControls() =>
    [
        new("food", "Food required", "24 food portions for a full day · game default", "Higher is harder: each food portion restores a smaller share of the hunger meter. Normal hunger-bar decay still lasts one game day.", true),
        new("water", "Water required", "30 water portions for a full day · game default", "Higher is harder: each drink restores a smaller share of the thirst meter. Normal thirst-bar decay still lasts one game day.", true),
        new("sleep", "Sleep required", "16 sleep portions for a full day · game default", "Higher is harder: each sleep portion restores a smaller share of the fatigue meter. Normal fatigue-bar decay still lasts one game day.", true),
        new("fall-damage", "Fall damage", "Minor 2.5% · Major 5% · game default", "Higher is harder: scales the verified minor and major fall-damage values as one paired control.", true),
        new("bleeding", "Bleeding", "Minor 1% · Major 2% health loss · game default", "Higher is harder: scales the verified minor and major bleeding health-loss values as one paired control.", true),
        new("poison", "Poison", "Minor 1% · Major 2% health loss · game default", "Higher is harder: scales the verified minor and major poisoning health-loss values as one paired control.", true),
    ];

    public static IReadOnlyList<GameplayDifficultyControlViewModel> CreateAdvancedControls() =>
    [
        new("energy-recovery", "Energy recovery speed", "1.0 energy per second · game default", "Higher is easier: once the normal rest delay has passed, energy recovers faster. This does not alter the separate exhaustion-damage chain below.", false),
        new("wound-sleep-healing", "Wound healing from sleep", "Minor 10 · Major 10 minutes per sleep portion · game default", "Higher is easier: sleep removes more healing time from both wound states. Wound-cure abilities still apply normally.", false),
        new("wound-stamina-penalty", "Wound stamina penalty", "Minor 15% · Major 30% maximum stamina · game default", "Higher is harder: wounds reduce maximum stamina more. Extreme values can leave a wounded character unable to act.", true),
        new("poison-recovery", "Poison recovery from portions", "Liquid 15 · Sleep 10 minutes per portion · game default", "Higher is easier: drinking water or sleeping removes more healing time from both poisoning states. The base healing duration is unchanged.", false),
        new("rest-delay", "Rest delay after energy use", "1.5 seconds before resting regenerates energy · game default", "Higher is harder: after consuming energy, regeneration starts later. This changes only the delay timer, not the recovery speed.", true),
        new("exhaustion-threshold", "Exhaustion threshold", "0.50 accumulated energy loss · game default", "Higher is easier: more energy must be lost in a row before the one-time exhaustion penalty triggers and the counter resets.", false),
        new("exhaustion-penalty", "Exhaustion stamina penalty", "0.15 stamina per trigger · game default", "Higher is harder: each time the accumulated energy loss reaches the threshold, this much stamina is removed once.", true),
        new("wound-recovery-duration", "Major wound recovery time", "480 minutes base healing time · game default", "Higher is harder: major wounds take longer to heal on their own. Sleep portions and cure abilities still reduce the remaining time normally.", true),
        new("poison-stamina-penalty", "Poison stamina penalty", "25% maximum stamina while majorly poisoned · game default", "Higher is harder: major poisoning reduces maximum stamina more. Extreme values can leave a poisoned character unable to act.", true),
    ];

    public static bool Supports(GameInspectionSnapshot? snapshot) =>
        snapshot?.Installation is
        {
            Store: StoreKind.Steam,
            BuildId: SupportedSteamBuildId,
            ContentSignature: AncestorsGameProfile.SupportedContentSignature,
            ContentSignatureReadFailed: false,
        };

    public static GameplayReadinessViewModel AssessReadiness(GameInspectionSnapshot? snapshot)
    {
        if (!Supports(snapshot))
        {
            return new(
                "Exact game identity required",
                "Gameplay research is available only for Steam build 5495393 with the matching stock PAK signatures. Reload after verifying game files or changing game versions.",
                "#E04D42",
                true);
        }

        int additionalPaks = snapshot!.PakFiles.Count(pak => pak.Classification is not PakClassification.BaseGame and not PakClassification.AecOwned);
        if (additionalPaks > 0)
        {
            return new(
                "External PAKs detected",
                $"{additionalPaks} additional PAK(s) were found. AEC cannot yet inspect their asset entries, so it will not claim that a gameplay patch is conflict-free.",
                "#D6BC84",
                true);
        }

        return new(
            "Ready to build · runtime verification pending",
            "AEC can build and safely manage the exact researched assets. The generated PAK is verified before installation; player-visible behavior still needs the planned in-game verification.",
            "#D6BC84",
            false);
    }

    public static IReadOnlyList<GameplayResearchValueViewModel> CreateAdvancedValues() =>
    [
        ResearchValue(
            "Minor wound base recovery time",
            "Native default (no asset override)",
            "Minor wounds use the same healing-duration formula as major wounds, but the value is set by the game code itself and is not stored in the player asset, so there are no bytes to patch.",
            "Blocked: the named property is absent from VL01_Player_Shared_CDSWounds.uasset (miner v13)."),
        ResearchValue(
            "Minor poison stamina penalty",
            "Unknown (no asset override)",
            "The code reads the same StaminaLost member for minor poisoning as for major poisoning, but the minor value is set by the game code itself and is not stored in the player asset.",
            "Blocked: the named property is absent from VL01_Player_Shared_CDSVenomPoisoning.uasset (miner v12/v13)."),
        ResearchValue(
            "Stamina regained on consumed portion",
            "0.03 stamina",
            "No native function in the complete 32-function vitality set or the six regimen-stamina callers reads this value; only the constructor assigns it. A hidden Blueprint or reflection consumer cannot be ruled out.",
            "Blocked: no gameplay consumer found; exposing it could have no or unintended effects."),
    ];

    private static GameplayResearchValueViewModel ResearchValue(string name, string stockValue, string description, string editability) => new(
        name,
        stockValue,
        description,
        "Static code evidence and deterministic PAK representation/readback for the exact researched build.",
        editability);
}
