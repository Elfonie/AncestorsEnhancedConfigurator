using AncestorsEnhanced.Core.Profiles;

namespace AncestorsEnhanced.App.ViewModels;

public sealed record ImportedProfileViewModel(UserProfile Profile, string Source)
{
    public string Name => Profile.Name;

    public string Description => Profile.Description ?? "No description";

    public string Contents => string.Join(
        " · ",
        new[]
        {
            Profile.Graphics.Count > 0 ? "Graphics" : null,
            Profile.Display.Count > 0 ? "Display" : null,
            Profile.Gameplay.Count > 0 ? "Gameplay" : null,
        }.OfType<string>());

    public int SettingCount => Profile.Graphics.Count + Profile.Display.Count + Profile.Gameplay.Count;
}
