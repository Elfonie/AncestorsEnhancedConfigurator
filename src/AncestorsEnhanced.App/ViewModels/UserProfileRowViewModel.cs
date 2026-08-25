namespace AncestorsEnhanced.App.ViewModels;

public sealed record UserProfileRowViewModel(
    string Id,
    string Name,
    string Description,
    string Contents,
    AncestorsEnhanced.Core.Profiles.UserProfile Profile);
