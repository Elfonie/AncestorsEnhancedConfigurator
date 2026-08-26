namespace AncestorsEnhanced.Core.Editing;

public enum GameplayDifficultyStateKind
{
    GameDefault,
    Active,
    Unverified,
}

public sealed record GameplayDifficultyState(
    GameplayDifficultyStateKind Kind,
    GameplayDifficultySettings Settings,
    string Description)
{
    public static GameplayDifficultyState GameDefault { get; } = new(
        GameplayDifficultyStateKind.GameDefault,
        GameplayDifficultySettings.GameDefault,
        "Game default · no AEC gameplay PAK installed");
}
