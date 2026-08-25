namespace AncestorsEnhanced.Core.Profiles;

public interface IUserProfileLibrary
{
    IReadOnlyList<StoredUserProfile> List();

    UserProfile Read(string id);

    StoredUserProfile Save(UserProfile profile);

    UserProfile ReadExternal(string path);
}
