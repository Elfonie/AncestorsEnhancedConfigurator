using System.Text.Json;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Environment;
using AncestorsEnhanced.Infrastructure.FileSystem;

namespace AncestorsEnhanced.Infrastructure.Inspection;

/// <summary>
/// Locates Ancestors installs managed by the Heroic Games Launcher, which installs
/// both Epic and GOG titles on Windows and Linux. Reads Heroic's per-game config
/// JSON files and the Legendary (Epic) installed.json for install paths.
/// </summary>
internal sealed class HeroicInstallationLocator(
    IReadOnlyFileSystem fileSystem,
    IHostEnvironment environment,
    GameInstallationFactory factory)
{
    public IReadOnlyList<GameInstallationSnapshot> Find(List<InspectionNotice> notices)
    {
        List<GameInstallationSnapshot> found = [];
        foreach (string heroicRoot in environment.GetHeroicConfigDirectories()
                     .Where(fileSystem.DirectoryExists))
        {
            found.AddRange(ReadGameConfigs(heroicRoot, notices));
            found.AddRange(ReadLegendaryInstalled(heroicRoot, notices));
        }

        return found;
    }

    private List<GameInstallationSnapshot> ReadGameConfigs(
        string heroicRoot,
        List<InspectionNotice> notices)
    {
        string configDirectory = Path.Combine(heroicRoot, "games_config");
        if (!fileSystem.DirectoryExists(configDirectory))
        {
            return [];
        }

        List<GameInstallationSnapshot> found = [];
        foreach (ReadOnlyFileMetadata file in fileSystem.EnumerateFiles(configDirectory, "*.json"))
        {
            try
            {
                if (file.SizeBytes > InspectionLimits.TextFile)
                {
                    continue;
                }

                using var json = JsonDocument.Parse(fileSystem.ReadAllText(file.FullPath));
                JsonElement root = json.RootElement;
                string? title = ReadString(root, "title");
                string? install = ReadString(root, "install_path");
                if (string.IsNullOrWhiteSpace(install) ||
                    title?.Contains("Ancestors", StringComparison.OrdinalIgnoreCase) != true)
                {
                    continue;
                }

                GameInstallationSnapshot? snapshot = CreateForStore(install, null);
                if (snapshot is not null)
                {
                    found.Add(snapshot);
                }
            }
            catch (Exception exception) when (InspectionErrors.IsExpected(exception))
            {
                notices.Add(new InspectionNotice(
                    InspectionSeverity.Information,
                    "heroic.config-unreadable",
                    $"A Heroic game config could not be read: {exception.Message}"));
            }
        }

        return found;
    }

    private List<GameInstallationSnapshot> ReadLegendaryInstalled(
        string heroicRoot,
        List<InspectionNotice> notices)
    {
        string legendaryPath = Path.Combine(heroicRoot, "legendary", "installed.json");
        if (!fileSystem.FileExists(legendaryPath))
        {
            return [];
        }

        try
        {
            if (fileSystem.GetFileMetadata(legendaryPath).SizeBytes > InspectionLimits.TextFile)
            {
                return [];
            }

            using var json = JsonDocument.Parse(fileSystem.ReadAllText(legendaryPath));
            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<GameInstallationSnapshot> found = [];
            foreach (JsonElement entry in json.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? title = ReadString(entry, "title");
                string? install = ReadString(entry, "install_path");
                if (string.IsNullOrWhiteSpace(install) ||
                    title?.Contains("Ancestors", StringComparison.OrdinalIgnoreCase) != true)
                {
                    continue;
                }

                GameInstallationSnapshot? snapshot = CreateForStore(
                    install,
                    ReadString(entry, "version"));
                if (snapshot is not null)
                {
                    found.Add(snapshot);
                }
            }

            return found;
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Information,
                "heroic.legendary-unreadable",
                $"The Legendary installed.json could not be read: {exception.Message}"));
            return [];
        }
    }

    private GameInstallationSnapshot? CreateForStore(string install, string? buildId) =>
        environment.Host == HostKind.Linux
            ? factory.CreateLinux(StoreKind.Unknown, install, buildId, CompatibilityLayerKind.Proton)
            : factory.CreateWindows(StoreKind.Unknown, install, buildId);

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}