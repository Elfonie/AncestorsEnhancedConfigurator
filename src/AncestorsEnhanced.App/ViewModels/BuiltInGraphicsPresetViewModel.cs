using AncestorsEnhanced.Core.Profiles;

namespace AncestorsEnhanced.App.ViewModels;

public sealed record BuiltInGraphicsPresetViewModel(
    string Name,
    string Summary,
    UserProfile Profile)
{
    public string Category => Name switch
    {
        "Clear Image" => "Image Style",
        "Performance Tweak" or "Balanced Tweak" or "High Quality Tweak" => "Quality",
        _ => "Hardware"
    };

    public string DisplayName => Name.EndsWith(" Tweak", StringComparison.Ordinal)
        ? Name[..^" Tweak".Length]
        : Name;
}
