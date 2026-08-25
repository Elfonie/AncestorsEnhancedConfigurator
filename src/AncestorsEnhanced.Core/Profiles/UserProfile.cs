using System.Text.Json;
using System.Text.Json.Serialization;
using AncestorsEnhanced.Core.Editing;

namespace AncestorsEnhanced.Core.Profiles;

public sealed record UserProfile(
    int SchemaVersion,
    string Name,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    string CreatedWithVersion,
    IReadOnlyList<ProfileSetting> Graphics,
    IReadOnlyList<ProfileSetting> Display,
    IReadOnlyList<ProfileSetting> Gameplay)
{
    public const string Format = "ancestors-enhanced-profile";
    public const int CurrentSchemaVersion = 1;
}

public sealed record ProfileSetting(string Key, string Value);

public sealed record StoredUserProfile(string Id, UserProfile Profile);

public static class UserProfileCodec
{
    public const int MaximumFileSize = 64 * 1024;
    private const int MaximumNameLength = 80;
    private const int MaximumDescriptionLength = 400;
    private const int MaximumVersionLength = 32;
    private const int MaximumSettingsPerSection = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static byte[] Serialize(UserProfile profile)
    {
        Validate(profile);
        return JsonSerializer.SerializeToUtf8Bytes(
            new UserProfileDocument(
                UserProfile.Format,
                profile.SchemaVersion,
                profile.Name,
                profile.Description,
                profile.CreatedAtUtc,
                profile.CreatedWithVersion,
                profile.Graphics,
                profile.Display,
                profile.Gameplay),
            JsonOptions);
    }

    public static UserProfile Deserialize(ReadOnlySpan<byte> content)
    {
        if (content.Length is < 2 or > MaximumFileSize)
        {
            throw new InvalidDataException("The profile file has an invalid size.");
        }

        UserProfileDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<UserProfileDocument>(content, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The profile file is not valid JSON.", exception);
        }

        if (document is null || !string.Equals(document.Format, UserProfile.Format, StringComparison.Ordinal))
        {
            throw new InvalidDataException("This is not an Ancestors Enhanced profile.");
        }

        var profile = new UserProfile(
            document.SchemaVersion,
            document.Name ?? string.Empty,
            document.Description,
            document.CreatedAtUtc,
            document.CreatedWithVersion ?? string.Empty,
            document.Graphics ?? [],
            document.Display ?? [],
            document.Gameplay ?? []);
        Validate(profile);
        return profile;
    }

    public static void Validate(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion != UserProfile.CurrentSchemaVersion)
        {
            throw new InvalidDataException("This profile uses an unsupported format version.");
        }
        ValidateText(profile.Name, "profile name", MaximumNameLength, required: true);
        ValidateText(profile.Description, "profile description", MaximumDescriptionLength, required: false);
        ValidateText(profile.CreatedWithVersion, "creator version", MaximumVersionLength, required: true);
        if (profile.CreatedAtUtc == default)
        {
            throw new InvalidDataException("The profile creation time is invalid.");
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ValidateSection(profile.Graphics, "graphics", keys);
        ValidateSection(profile.Display, "display", keys);
        ValidateSection(profile.Gameplay, "gameplay", keys);
        ValidateUnsupportedSection(profile.Display, "display");
        ValidateUnsupportedSection(profile.Gameplay, "gameplay");
        if (keys.Count == 0)
        {
            throw new InvalidDataException("The profile does not contain any settings.");
        }
    }

    private static void ValidateUnsupportedSection(IReadOnlyList<ProfileSetting> settings, string section)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count != 0)
        {
            throw new InvalidDataException($"The {section} section is not supported by this version of AEC.");
        }
    }

    private static void ValidateSection(
        IReadOnlyList<ProfileSetting> settings,
        string section,
        HashSet<string> keys)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count > MaximumSettingsPerSection)
        {
            throw new InvalidDataException($"The {section} section has too many settings.");
        }

        foreach (ProfileSetting setting in settings)
        {
            if (setting is null || !EditableSettingsCatalog.IsDefined(setting.Key))
            {
                throw new InvalidDataException($"The {section} section contains an unsupported setting.");
            }
            if (string.IsNullOrWhiteSpace(setting.Value) || setting.Value.Length > 128 ||
                setting.Value.Any(char.IsControl))
            {
                throw new InvalidDataException($"The {section} section contains an invalid value.");
            }
            if (!keys.Add(setting.Key))
            {
                throw new InvalidDataException("The profile contains the same setting more than once.");
            }
        }
    }

    private static void ValidateText(string? value, string label, int maximumLength, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                throw new InvalidDataException($"The {label} is required.");
            }
            return;
        }
        if (value.Length > maximumLength || value.Any(character =>
                char.IsControl(character) && character is not '\r' and not '\n'))
        {
            throw new InvalidDataException($"The {label} is invalid.");
        }
    }

    private sealed record UserProfileDocument(
        string Format,
        int SchemaVersion,
        string? Name,
        string? Description,
        DateTimeOffset CreatedAtUtc,
        string? CreatedWithVersion,
        IReadOnlyList<ProfileSetting>? Graphics,
        IReadOnlyList<ProfileSetting>? Display,
        IReadOnlyList<ProfileSetting>? Gameplay);
}
