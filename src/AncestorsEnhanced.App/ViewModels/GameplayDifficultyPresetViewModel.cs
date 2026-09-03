namespace AncestorsEnhanced.App.ViewModels;

public sealed record GameplayDifficultyPresetViewModel(
    string Name,
    string Summary,
    string Description,
    int MultiplierPercent);
