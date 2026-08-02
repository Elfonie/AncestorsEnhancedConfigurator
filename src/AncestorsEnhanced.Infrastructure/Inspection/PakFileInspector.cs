using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.FileSystem;

namespace AncestorsEnhanced.Infrastructure.Inspection;

internal sealed class PakFileInspector(IReadOnlyFileSystem fileSystem)
{
    public PakFileSnapshot[] Read(
        GameInstallationSnapshot? installation,
        List<InspectionNotice> notices)
    {
        if (installation is null)
        {
            return [];
        }

        string directory = Path.Combine(
            installation.InstallDirectory,
            "Ancestors",
            "Content",
            "Paks");
        if (!fileSystem.DirectoryExists(directory))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Error,
                "paks.directory-not-found",
                "The expected Ancestors Paks directory is missing."));
            return [];
        }

        try
        {
            return [.. fileSystem.EnumerateFiles(directory, "*.pak")
                .Select(file => new PakFileSnapshot(
                    file.Name,
                    file.FullPath,
                    file.SizeBytes,
                    file.LastWriteTimeUtc,
                    Classify(file.Name)))];
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Error,
                "paks.directory-unreadable",
                $"The Paks directory could not be read: {exception.Message}"));
            return [];
        }
    }

    private static PakClassification Classify(string name)
    {
        if (string.Equals(name, "Ancestors-WindowsNoEditor.pak", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "VL01E01.pak", StringComparison.OrdinalIgnoreCase))
        {
            return PakClassification.BaseGame;
        }

        return name.EndsWith("_P.pak", StringComparison.OrdinalIgnoreCase)
            ? PakClassification.PatchStyle
            : PakClassification.Unclassified;
    }
}
