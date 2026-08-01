using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace AncestorsEnhanced.Infrastructure.Environment;

internal sealed class WindowsHostEnvironment : IHostEnvironment
{
    public bool IsWindows => OperatingSystem.IsWindows();

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
        catch (UnauthorizedAccessException)
        {
            // A locked registry key is treated like a missing discovery source.
        }
        catch (SecurityException)
        {
            // The configurator remains usable without registry access.
        }
        catch (IOException)
        {
            // A transient registry read failure must not crash inspection.
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
