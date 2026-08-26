using System.Text.Json;
using AncestorsEnhanced.Core.Editing;

namespace AncestorsEnhanced.Infrastructure.Paks;

internal static class AecPakOwnershipMarker
{
    private const int CurrentVersion = 3;
    private const int LegacyGameplayVersion = 1;
    private const int PreviousGameplayVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static byte[] CreateGameplay(byte[] pak, GameplayDifficultySettings settings)
    {
        ArgumentNullException.ThrowIfNull(pak);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(
            new Marker(CurrentVersion, "gameplay", Sha256(pak), settings),
            JsonOptions);
    }

    public static bool TryReadExpectedSha256(string text, out string sha256)
    {
        sha256 = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (IsSha256(trimmed))
        {
            sha256 = trimmed.ToUpperInvariant();
            return true;
        }

        try
        {
            Marker? marker = JsonSerializer.Deserialize<Marker>(trimmed, JsonOptions);
            if (marker is null || !IsSupportedVersion(marker.Version) || !IsSha256(marker.PakSha256))
            {
                return false;
            }

            sha256 = marker.PakSha256.ToUpperInvariant();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryReadGameplay(
        byte[] content,
        out string pakSha256,
        out GameplayDifficultySettings settings,
        out int version)
    {
        pakSha256 = string.Empty;
        settings = GameplayDifficultySettings.GameDefault;
        version = 0;
        try
        {
            Marker? marker = JsonSerializer.Deserialize<Marker>(content, JsonOptions);
            if (marker is null || !IsSupportedVersion(marker.Version) ||
                !string.Equals(marker.Component, "gameplay", StringComparison.Ordinal) ||
                !IsSha256(marker.PakSha256) || marker.Settings is null)
            {
                return false;
            }

            marker.Settings.Validate();
            if (marker.Settings.IsGameDefault)
            {
                return false;
            }

            pakSha256 = marker.PakSha256.ToUpperInvariant();
            settings = marker.Settings;
            version = marker.Version;
            return true;
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);

    private static bool IsSupportedVersion(int version) =>
        version is LegacyGameplayVersion or PreviousGameplayVersion or CurrentVersion;

    private static string Sha256(byte[] content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));

    private sealed record Marker(
        int Version,
        string Component,
        string PakSha256,
        GameplayDifficultySettings? Settings);
}
