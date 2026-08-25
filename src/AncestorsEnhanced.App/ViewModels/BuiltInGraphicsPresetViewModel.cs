using AncestorsEnhanced.Core.Profiles;

namespace AncestorsEnhanced.App.ViewModels;

public sealed record BuiltInGraphicsPresetViewModel(
    string Name,
    string Summary,
    UserProfile Profile);
