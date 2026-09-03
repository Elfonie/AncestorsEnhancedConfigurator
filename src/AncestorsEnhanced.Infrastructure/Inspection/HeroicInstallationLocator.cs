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
                string? install = ReadFirstString(root, "install_path", "installPath");
                if (string.IsNullOrWhiteSpace(install) ||
                    (title?.Contains("Ancestors", StringComparison.OrdinalIgnoreCase) != true &&
                     !file.Name.Contains("Ancestors", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                GameInstallationSnapshot? snapshot = CreateForStore(
                    install,
                    null,
                    ReadFirstString(root, "winePrefix", "wine_prefix", "prefix"));
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
        string? legendaryPath = new[]
            {
                Path.Combine(heroicRoot, "legendary", "installed.json"),
                Path.Combine(heroicRoot, "legendaryConfig", "legendary", "installed.json"),
                Path.Combine(heroicRoot, "installed.json"),
            }
            .FirstOrDefault(fileSystem.FileExists);
        if (legendaryPath is null)
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
            IEnumerable<JsonElement> entries = json.RootElement.ValueKind switch
            {
                JsonValueKind.Array => json.RootElement.EnumerateArray(),
                JsonValueKind.Object => json.RootElement.EnumerateObject().Select(property => property.Value),
                _ => [],
            };
            if (!entries.Any())
            {
                return [];
            }

            List<GameInstallationSnapshot> found = [];
            foreach (JsonElement entry in entries)
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? title = ReadString(entry, "title");
                string? install = ReadFirstString(entry, "install_path", "installPath");
                if (string.IsNullOrWhiteSpace(install) ||
                    title?.Contains("Ancestors", StringComparison.OrdinalIgnoreCase) != true)
                {
                    continue;
                }

                GameInstallationSnapshot? snapshot = CreateForStore(
                    install,
                    ReadString(entry, "version"),
                    ReadFirstString(entry, "winePrefix", "wine_prefix", "prefix"));
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

    private GameInstallationSnapshot? CreateForStore(string install, string? buildId, string? prefix = null) =>
        environment.Host == HostKind.Linux
            ? factory.CreateLinux(StoreKind.Heroic, install, buildId, CompatibilityLayerKind.Proton, prefix)
            : factory.CreateWindows(StoreKind.Heroic, install, buildId);

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? ReadFirstString(JsonElement element, params string[] names) =>
        names.Select(name => ReadString(element, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
