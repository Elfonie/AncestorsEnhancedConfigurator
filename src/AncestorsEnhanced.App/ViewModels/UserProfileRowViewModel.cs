using CommunityToolkit.Mvvm.ComponentModel;

namespace AncestorsEnhanced.App.ViewModels;

public sealed partial class UserProfileRowViewModel : ObservableObject
{
    public UserProfileRowViewModel(
        string id,
        string name,
        string description,
        string contents,
        AncestorsEnhanced.Core.Profiles.UserProfile profile)
    {
        Id = id;
        Name = name;
        Description = description;
        Contents = contents;
        Profile = profile;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Contents { get; }
    public AncestorsEnhanced.Core.Profiles.UserProfile Profile { get; }

    [ObservableProperty]
    private bool _isPendingDeletion;
}
