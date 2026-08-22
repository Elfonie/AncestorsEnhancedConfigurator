using System.Runtime.Versioning;
using System.Security;
using AncestorsEnhanced.Core.Inspection;
using Microsoft.Win32;

namespace AncestorsEnhanced.Infrastructure.Environment;

[SupportedOSPlatform("windows")]
internal sealed class WindowsHostEnvironment : IHostEnvironment
{
    public HostKind Host => HostKind.Windows;

    public string? LocalApplicationDataPath =>
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public IReadOnlyList<string> GetSteamRootCandidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        List<string> candidates = [];
        AddRegistryValue(candidates, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddRegistryValue(candidates, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        AddRegistryValue(candidates, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        string programFilesX86 = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "Steam"));
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePathOrNull)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> GetEpicManifestDirectories()
    {
        string common = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.CommonApplicationData);
        return string.IsNullOrWhiteSpace(common)
            ? []
            : [Path.Combine(common, "Epic", "EpicGamesLauncher", "Data", "Manifests")];
    }

    public IReadOnlyList<string> GetGogInstallCandidates()
    {
        List<string> candidates = [];
        AddGogGames(candidates, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\Games");
        AddGogGames(candidates, Registry.LocalMachine, @"SOFTWARE\GOG.com\Games");
        AddGogGames(candidates, Registry.CurrentUser, @"Software\GOG.com\Games");

        string programFiles = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.ProgramFilesX86);
        foreach (string root in new[] { programFiles, programFilesX86 })
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                candidates.Add(Path.Combine(root, "GOG Galaxy", "Games", "Ancestors The Humankind Odyssey"));
            }
        }

        return candidates.Select(NormalizePathOrNull).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> GetHeroicConfigDirectories()
    {
        List<string> candidates = [];
        foreach (System.Environment.SpecialFolder folder in new[]
                 {
                     System.Environment.SpecialFolder.ApplicationData,
                     System.Environment.SpecialFolder.LocalApplicationData,
                 })
        {
            string? basePath = System.Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(basePath))
            {
                candidates.Add(Path.Combine(basePath, "heroic"));
            }
        }

        return candidates.Select(NormalizePathOrNull).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    [SupportedOSPlatform("windows")]
    private static void AddGogGames(
        List<string> candidates,
        RegistryKey root,
        string gamesKeyName)
    {
        try
        {
            using RegistryKey? games = root.OpenSubKey(gamesKeyName, writable: false);
            foreach (string name in games?.GetSubKeyNames() ?? [])
            {
                using RegistryKey? game = games?.OpenSubKey(name, writable: false);
                string? gameName = game?.GetValue("gameName") as string;
                string? path = game?.GetValue("path") as string;
                if (gameName?.Contains("Ancestors", StringComparison.OrdinalIgnoreCase) == true &&
                    !string.IsNullOrWhiteSpace(path))
                {
                    candidates.Add(path);
                }
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or SecurityException or IOException)
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryValue(
        List<string> candidates,
        RegistryKey root,
        string subKeyName,
        string valueName)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(subKeyName, writable: false);
            if (key?.GetValue(valueName) is string path && !string.IsNullOrWhiteSpace(path))
            {
                candidates.Add(path.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or SecurityException or IOException)
        {
        }
    }

    private static string? NormalizePathOrNull(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
