using System.Text.Json;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Environment;
using AncestorsEnhanced.Infrastructure.FileSystem;

namespace AncestorsEnhanced.Infrastructure.Inspection;

internal sealed class EpicInstallationLocator(
    IReadOnlyFileSystem fileSystem,
    IHostEnvironment environment,
    GameInstallationFactory factory)
{
    public IReadOnlyList<GameInstallationSnapshot> Find(List<InspectionNotice> notices)
    {
        List<GameInstallationSnapshot> found = [];
        foreach (string directory in environment.GetEpicManifestDirectories()
                     .Where(fileSystem.DirectoryExists))
        {
            foreach (ReadOnlyFileMetadata file in fileSystem.EnumerateFiles(directory, "*.item"))
            {
                try
                {
                    if (file.SizeBytes > InspectionLimits.TextFile)
                    {
                        continue;
                    }

                    using var json = JsonDocument.Parse(fileSystem.ReadAllText(file.FullPath));
                    JsonElement root = json.RootElement;
                    string? name = ReadString(root, "DisplayName");
                    string? install = ReadString(root, "InstallLocation");
                    if (string.IsNullOrWhiteSpace(install) ||
                        name?.Contains("Ancestors", StringComparison.OrdinalIgnoreCase) != true)
                    {
                        continue;
                    }

                    GameInstallationSnapshot? snapshot = environment.Host == HostKind.Linux
                        ? factory.CreateLinux(StoreKind.EpicGames, install, ReadString(root, "BuildVersion"), CompatibilityLayerKind.Proton)
                        : factory.CreateWindows(StoreKind.EpicGames, install, ReadString(root, "BuildVersion"));
                    if (snapshot is not null)
                    {
                        found.Add(snapshot);
                    }
                }
                catch (Exception exception) when (InspectionErrors.IsExpected(exception))
                {
                    notices.Add(new InspectionNotice(
                        InspectionSeverity.Information,
                        "epic.manifest-unreadable",
                        $"An Epic manifest could not be read: {exception.Message}"));
                }
            }
        }

        return found;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
