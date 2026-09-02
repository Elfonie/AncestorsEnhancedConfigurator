using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Editing;

namespace AncestorsEnhanced.Infrastructure.Environment;

internal sealed class LinuxHostEnvironment : IHostEnvironment
{
    private readonly string _home = System.Environment.GetFolderPath(
        System.Environment.SpecialFolder.UserProfile);

    public HostKind Host => HostKind.Linux;

    public string? LocalApplicationDataPath => null;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public IReadOnlyList<string> GetSteamRootCandidates() =>
        new[]
        {
            Path.Combine(_home, ".steam", "steam"),
            Path.Combine(_home, ".steam", "root"),
            Path.Combine(_home, ".local", "share", "Steam"),
            Path.Combine(_home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(_home, ".var", "app", "com.valvesoftware.Steam", ".steam", "steam"),
        }
        .Select(Normalize)
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<string> GetEpicManifestDirectories() => [];

    public IReadOnlyList<string> GetGogInstallCandidates() => [];
    public IReadOnlyList<string> GetHeroicConfigDirectories() =>
        new[]
        {
            Path.Combine(_home, ".config", "heroic"),
            Path.Combine(_home, ".local", "share", "heroic"),
            Path.Combine(_home, ".config", "legendary"),
            Path.Combine(_home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic"),
            Path.Combine(_home, ".var", "app", "com.heroicgameslauncher.hgl", "data", "heroic"),
        }
        .Select(Normalize)
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static string? Normalize(string path)
    {
        try
        {
            return ConfigurationFileOperations.ResolvePhysicalPath(path);
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
