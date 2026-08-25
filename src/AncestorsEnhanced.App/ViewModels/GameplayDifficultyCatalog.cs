using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Inspection;

namespace AncestorsEnhanced.App.ViewModels;

/// <summary>
/// Build-bound presentation data. This is deliberately separate from PAK writing;
/// no entry becomes editable in-game until its load and conflict gates are verified.
/// </summary>
public static class GameplayDifficultyCatalog
{
    public const string SupportedSteamBuildId = AncestorsGameProfile.SupportedSteamBuildId;

    public static IReadOnlyList<GameplayDifficultyPresetViewModel> CreatePresets() =>
    [
        new("Game default", "100% across every available control", "No gameplay patch is created. This is the reference point for all future percentage changes.", 100),
        new("Explorer (planned)", "Lower survival pressure", "Will reduce the food, water, sleep and fall-damage categories together after the PAK load path is verified.", 70),
        new("Survival (planned)", "Higher survival pressure", "Will raise the same simple categories together. It will not silently alter combat, QTEs or animal damage.", 130),
        new("Hardcore (planned)", "Maximum tested simple pressure", "Will use 150% for each simple survival and fall-damage category. It remains a draft until runtime verification exists.", 150),
        new("Custom (planned)", "Choose each simple category yourself", "Each available category will use 10% steps relative to the game default.", 100),
    ];

    public static IReadOnlyList<GameplayDifficultyControlViewModel> CreateSimpleControls() =>
    [
        new("Food need", "24 portions per day · game default", "Higher is harder: the named Food NeededPerDay value defines a larger food requirement.", true),
        new("Water need", "30 portions per day · game default", "Higher is harder: the named Liquid NeededPerDay value defines a larger liquid requirement.", true),
        new("Sleep need", "16 portions per day · game default", "Higher is harder: the named Sleep NeededPerDay value defines a larger sleep requirement.", true),
        new("Fall damage", "Minor 2.5% · Major 5% · game default", "Higher is harder: minor and major falls use separate, named damage values and will remain a paired Simple control.", true),
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

        int additionalPaks = snapshot!.PakFiles.Count(pak => pak.Classification != PakClassification.BaseGame);
        if (additionalPaks > 0)
        {
            return new(
                "External PAKs detected",
                $"{additionalPaks} additional PAK(s) were found. AEC cannot yet inspect their asset entries, so it will not claim that a gameplay patch is conflict-free.",
                "#D6BC84",
                true);
        }

        return new(
            "Runtime loading check still required",
            "AEC can recognize the researched stock build, but no in-game test has confirmed PAK priority or observed gameplay behavior. Drafts remain read-only.",
            "#D6BC84",
            true);
    }

    public static IReadOnlyList<GameplayResearchValueViewModel> CreateAdvancedValues() =>
    [
        ResearchValue("Energy recovery delay", "1.5 seconds", "The delay before resting enables energy regeneration. This is not a regeneration rate."),
        ResearchValue("Cumulative energy-loss threshold", "0.50 energy", "Recorded energy loss at or beyond this threshold triggers one stamina penalty, then the accumulator resets."),
        ResearchValue("Cumulative energy-loss stamina penalty", "0.15 stamina", "The penalty is one absolute stamina subtraction per threshold crossing; excess loss is not carried over."),
        ResearchValue("Major wound base recovery time", "480 minutes", "The game multiplies this by one minus the applicable wound-duration ability modifiers, clamped to 0–1."),
        ResearchValue("Minor wound stamina penalty", "0.15 maximum stamina", "While wounded, this is a maximum-stamina modifier, not an immediate current-stamina drain."),
        ResearchValue("Major wound stamina penalty", "0.30 maximum stamina", "While wounded, this is a maximum-stamina modifier, not an immediate current-stamina drain."),
        ResearchValue("Major poison stamina penalty", "0.25 maximum stamina", "While majorly poisoned, this is a maximum-stamina modifier. The minor-poison override is not known."),
    ];

    private static GameplayResearchValueViewModel ResearchValue(string name, string stockValue, string description) => new(
        name,
        stockValue,
        description,
        "Static code evidence and deterministic PAK representation/readback for the exact researched build.",
        "Not editable: game PAK priority and in-game behavior are still unverified.");
}
