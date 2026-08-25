namespace AncestorsEnhanced.App.ViewModels;

/// <summary>
/// Explains why gameplay drafts remain non-writing. It deliberately reports
/// evidence gates instead of implying that a generated archive would load.
/// </summary>
public sealed record GameplayReadinessViewModel(
    string Title,
    string Description,
    string AccentColor,
    bool IsBlocked);
