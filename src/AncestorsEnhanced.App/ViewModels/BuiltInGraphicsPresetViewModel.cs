using AncestorsEnhanced.Core.Profiles;

namespace AncestorsEnhanced.App.ViewModels;

public sealed record BuiltInGraphicsPresetViewModel(
    string Name,
    string Summary,
    UserProfile Profile)
{
    public string DisplayName => Name.EndsWith(" Tweak", StringComparison.Ordinal)
        ? Name[..^" Tweak".Length]
        : Name;
}
