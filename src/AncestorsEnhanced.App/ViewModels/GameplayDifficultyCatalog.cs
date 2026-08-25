namespace AncestorsEnhanced.App.ViewModels;

/// <summary>
/// Build-bound presentation data. This is deliberately separate from PAK writing;
/// no entry becomes editable in-game until its load and conflict gates are verified.
/// </summary>
public static class GameplayDifficultyCatalog
{
    public const string SupportedSteamBuildId = "5495393";

    public static IReadOnlyList<GameplayDifficultyPresetViewModel> CreatePresets() =>
    [
        new("Game default", "100% across every available control", "No gameplay patch is created. This is the reference point for all future percentage changes.", 100),
        new("Explorer (planned)", "Lower survival pressure", "Will reduce the food, water, sleep and fall-damage categories together after the PAK load path is verified.", 70),
        new("Survival (planned)", "Higher survival pressure", "Will raise the same simple categories together. It will not silently alter combat, QTEs or animal damage.", 130),
        new("Custom (planned)", "Choose each simple category yourself", "Each available category will use 10% steps relative to the game default.", 100),
    ];

    public static IReadOnlyList<GameplayDifficultyControlViewModel> CreateSimpleControls() =>
    [
        new("Food need", "24 portions per day · game default", "Higher is harder: the named Food NeededPerDay value defines a larger food requirement."),
        new("Water need", "30 portions per day · game default", "Higher is harder: the named Liquid NeededPerDay value defines a larger liquid requirement."),
        new("Sleep need", "16 portions per day · game default", "Higher is harder: the named Sleep NeededPerDay value defines a larger sleep requirement."),
        new("Energy recovery", "1.0 energy per second · game default", "Higher is easier while energy regeneration is active. Normal stamina and health limits still apply."),
        new("Fall damage", "Minor 2.5% · Major 5% · game default", "Higher is harder: minor and major falls use separate, named damage values and will remain a paired Simple control."),
    ];

    public static IReadOnlyList<GameplayResearchValueViewModel> CreateAdvancedValues() =>
    [
        new("Energy recovery delay", "1.5 seconds", "The delay before resting enables energy regeneration. This is not a regeneration rate."),
        new("Cumulative energy-loss threshold", "0.50 energy", "Recorded energy loss at or beyond this threshold triggers one stamina penalty, then the accumulator resets."),
        new("Cumulative energy-loss stamina penalty", "0.15 stamina", "The penalty is one absolute stamina subtraction per threshold crossing; excess loss is not carried over."),
        new("Major wound base recovery time", "480 minutes", "The game multiplies this by one minus the applicable wound-duration ability modifiers, clamped to 0–1."),
        new("Minor wound stamina penalty", "0.15 maximum stamina", "While wounded, this is a maximum-stamina modifier, not an immediate current-stamina drain."),
        new("Major wound stamina penalty", "0.30 maximum stamina", "While wounded, this is a maximum-stamina modifier, not an immediate current-stamina drain."),
        new("Major poison stamina penalty", "0.25 maximum stamina", "While majorly poisoned, this is a maximum-stamina modifier. The minor-poison override is not known."),
    ];
}
