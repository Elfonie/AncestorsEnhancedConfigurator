using AncestorsEnhanced.Core.Profiles;

namespace AncestorsEnhanced.Core.Tests.Profiles;

public sealed class UserProfileCodecTests
{
    [Fact]
    public void SerializeAndDeserializeRoundTripsPortableGraphicsProfile()
    {
        UserProfile profile = Profile(
            graphics:
            [
                new ProfileSetting("r.MotionBlurQuality", "0"),
                new ProfileSetting("mod.VignettePercent", "50"),
            ]);

        UserProfile restored = UserProfileCodec.Deserialize(UserProfileCodec.Serialize(profile));

        Assert.Equal(profile.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(profile.Name, restored.Name);
        Assert.Equal(profile.Description, restored.Description);
        Assert.Equal(profile.CreatedAtUtc, restored.CreatedAtUtc);
        Assert.Equal(profile.CreatedWithVersion, restored.CreatedWithVersion);
        Assert.Equal(profile.Graphics, restored.Graphics);
        Assert.Equal(profile.Display, restored.Display);
        Assert.Equal(profile.Gameplay, restored.Gameplay);
    }

    [Fact]
    public void DeserializeRejectsUnknownJsonMembers()
    {
        byte[] content = """
            {"format":"ancestors-enhanced-profile","schemaVersion":1,"name":"Test","createdAtUtc":"2026-08-24T10:00:00+00:00","createdWithVersion":"1.0.0","graphics":[{"key":"r.MotionBlurQuality","value":"0"}],"display":[],"gameplay":[],"unexpected":true}
            """u8.ToArray();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => UserProfileCodec.Deserialize(content));

        Assert.Contains("valid JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsDuplicateSettingsAcrossSections()
    {
        UserProfile profile = new(
            UserProfile.CurrentSchemaVersion,
            "Duplicate",
            null,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            "1.0.0",
            [new ProfileSetting("r.MotionBlurQuality", "0")],
            [new ProfileSetting("r.MotionBlurQuality", "1")],
            []);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => UserProfileCodec.Serialize(profile));

        Assert.Contains("more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsUnknownSetting()
    {
        UserProfile profile = Profile(graphics: [new ProfileSetting("r.NotARealSetting", "1")]);

        Assert.Throws<InvalidDataException>(() => UserProfileCodec.Serialize(profile));
    }

    [Fact]
    public void SerializeAndDeserializeAllowsDisplaySettings()
    {
        UserProfile profile = new(
            UserProfile.CurrentSchemaVersion,
            "Display setup",
            null,
            DateTimeOffset.UnixEpoch,
            "1.0.0",
            [],
            [new ProfileSetting("r.ViewDistanceScale", "1.2")],
            []);

        UserProfile restored = UserProfileCodec.Deserialize(UserProfileCodec.Serialize(profile));

        Assert.Equal(profile.Display, restored.Display);
    }

    [Fact]
    public void SerializeAndDeserializeAllowsMultilineDescriptions()
    {
        UserProfile profile = Profile(graphics: [new ProfileSetting("r.MotionBlurQuality", "0")]) with
        {
            Description = "First line\nSecond line",
        };

        UserProfile restored = UserProfileCodec.Deserialize(UserProfileCodec.Serialize(profile));

        Assert.Equal(profile.Description, restored.Description);
    }

    private static UserProfile Profile(IReadOnlyList<ProfileSetting> graphics) =>
        new(
            UserProfile.CurrentSchemaVersion,
            "Clean 1440p High",
            "High visuals with reduced vignette",
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            "1.0.0",
            graphics,
            [],
            []);
}
