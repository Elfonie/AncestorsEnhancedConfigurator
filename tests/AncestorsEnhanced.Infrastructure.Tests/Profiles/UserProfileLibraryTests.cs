using AncestorsEnhanced.Core.Profiles;
using AncestorsEnhanced.Infrastructure.Profiles;

namespace AncestorsEnhanced.Infrastructure.Tests.Profiles;

public sealed class UserProfileLibraryTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"aec-profile-tests-{Guid.NewGuid():N}");

    [Fact]
    public void SaveCreatesOpaqueLibraryEntryAndListReadsIt()
    {
        var library = new UserProfileLibrary(_temporaryDirectory);
        UserProfile profile = Profile("Clean high");

        StoredUserProfile saved = library.Save(profile);
        StoredUserProfile listed = Assert.Single(library.List());

        Assert.True(Guid.TryParseExact(saved.Id, "N", out _));
        Assert.Equal(saved.Id, listed.Id);
        Assert.Equal(saved.Profile.Name, listed.Profile.Name);
        Assert.Equal(saved.Profile.Graphics, listed.Profile.Graphics);
        Assert.True(File.Exists(Path.Combine(_temporaryDirectory, saved.Id + ".aecprofile")));
    }

    [Fact]
    public void ListSkipsMalformedExternalFiles()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        File.WriteAllText(Path.Combine(_temporaryDirectory, $"{Guid.NewGuid():N}.aecprofile"), "not json");
        var library = new UserProfileLibrary(_temporaryDirectory);

        Assert.Empty(library.List());
    }

    [Fact]
    public void ReadExternalRejectsWrongExtensionWithoutReadingIt()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string path = Path.Combine(_temporaryDirectory, "profile.json");
        File.WriteAllText(path, "{}");
        var library = new UserProfileLibrary(_temporaryDirectory);

        Assert.Throws<InvalidDataException>(() => library.ReadExternal(path));
    }

    [Fact]
    public void ReadExternalReturnsValidatedProfileWithoutAddingItToLibrary()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string path = Path.Combine(_temporaryDirectory, "downloaded.aecprofile");
        File.WriteAllBytes(path, UserProfileCodec.Serialize(Profile("Downloaded")));
        var library = new UserProfileLibrary(Path.Combine(_temporaryDirectory, "library"));

        UserProfile imported = library.ReadExternal(path);

        Assert.Equal("Downloaded", imported.Name);
        Assert.Empty(library.List());
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static UserProfile Profile(string name) =>
        new(
            UserProfile.CurrentSchemaVersion,
            name,
            null,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            "1.0.0",
            [new ProfileSetting("r.MotionBlurQuality", "0")],
            [],
            []);
}
