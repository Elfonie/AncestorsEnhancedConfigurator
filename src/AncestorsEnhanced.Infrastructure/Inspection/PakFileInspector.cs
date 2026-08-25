using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.FileSystem;
using System.Security.Cryptography;


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
                    Classify(file, fileSystem)))];
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

    private static PakClassification Classify(ReadOnlyFileMetadata file, IReadOnlyFileSystem fileSystem)
    {
        string name = file.Name;
        if (string.Equals(name, "Ancestors-WindowsNoEditor.pak", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "VL01E01.pak", StringComparison.OrdinalIgnoreCase))
        {
            return PakClassification.BaseGame;
        }

        // AEC ownership is proven by a sidecar hash written when AEC creates the
        // package. A matching filename alone is never enough, since users and
        // other mods can place identically named files in the Paks directory.
        string marker = file.FullPath + ".aec-owned.sha256";
        if (fileSystem.FileExists(marker))
        {
            try
            {
                string expected = fileSystem.ReadAllText(marker).Trim();
                using Stream pak = fileSystem.OpenRead(file.FullPath);
                string actual = Convert.ToHexString(SHA256.HashData(pak));
                if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    return PakClassification.AecOwned;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
            {
                // Unverifiable ownership remains PatchStyle below.
            }
        }

        return name.EndsWith("_P.pak", StringComparison.OrdinalIgnoreCase)
            ? PakClassification.PatchStyle
            : PakClassification.Unclassified;
    }
}
