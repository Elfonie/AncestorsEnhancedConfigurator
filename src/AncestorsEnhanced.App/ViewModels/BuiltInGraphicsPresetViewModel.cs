using AncestorsEnhanced.Core.Profiles;

namespace AncestorsEnhanced.App.ViewModels;

public sealed record BuiltInGraphicsPresetViewModel(
    string Name,
    string Summary,
    UserProfile Profile)
{
    public bool IsHardwareSetup => Name is "Performance Setup" or "Balanced Setup" or "High Quality Setup" or "Ultra Setup" or "Low VRAM Setup";

    public string ActionLabel => IsHardwareSetup ? "Use setup" : "Add to review";

    public string Category => Name switch
    {
        "Clear Image" => "Image Style",
        "Cinematic Tweak" => "Image Style",
        _ => "Hardware"
    };

    public string DisplayName => Name.EndsWith(" Tweak", StringComparison.Ordinal)
        ? Name[..^" Tweak".Length]
        : Name;
}
