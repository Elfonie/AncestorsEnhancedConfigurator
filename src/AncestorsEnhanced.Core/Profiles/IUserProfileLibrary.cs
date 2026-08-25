namespace AncestorsEnhanced.Core.Profiles;

public interface IUserProfileLibrary
{
    int UnreadableProfileCount { get; }

    IReadOnlyList<StoredUserProfile> List();

    UserProfile Read(string id);

    StoredUserProfile Save(UserProfile profile);

    void Delete(string id);

    UserProfile ReadExternal(string path);
}
