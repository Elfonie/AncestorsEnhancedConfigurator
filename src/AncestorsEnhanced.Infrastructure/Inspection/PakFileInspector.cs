using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.FileSystem;
using AncestorsEnhanced.Infrastructure.Paks;
using System.Security.Cryptography;


namespace AncestorsEnhanced.Infrastructure.Inspection;

internal sealed class PakFileInspector(IReadOnlyFileSystem fileSystem)
{
    public PakFileSnapshot[] Read(
        GameInstallationSnapshot? installation,
        List<InspectionNotice> notices,
        VignetteModSnapshot? vignette = null)
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
                    Classify(file, fileSystem, vignette)))];
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

    private static PakClassification Classify(
        ReadOnlyFileMetadata file,
        IReadOnlyFileSystem fileSystem,
        VignetteModSnapshot? vignette)
    {
        string name = file.Name;
        if (string.Equals(name, "Ancestors-WindowsNoEditor.pak", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "VL01E01.pak", StringComparison.OrdinalIgnoreCase))
        {
            return PakClassification.BaseGame;
        }

        if (string.Equals(name, VignettePakEditor.OwnPatchName, StringComparison.OrdinalIgnoreCase))
        {
            if (vignette is { IsEditable: true, ActivePatchPath: not null } &&
                string.Equals(
                    Path.GetFullPath(vignette.ActivePatchPath),
                    Path.GetFullPath(file.FullPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                // VignettePakEditor has verified the stock asset and reconstructed the
                // complete deterministic package. This is content proof, not a name check.
                return PakClassification.AecOwned;
            }

            // A file named AncestorsEnhanced-Vignette_P.pak that cannot be cryptographically
            // verified via VignettePakEditor content reconstruction is an unverified / conflicting patch.
            // AEC vignette packages never use sidecar markers.
            return PakClassification.PatchStyle;
        }

        // Gameplay PAK ownership is proven by a structured AEC JSON marker written
        // when AEC creates the package. A matching filename alone or a bare hash
        // is never enough, since users and other mods can place identically named
        // files or spoofed sidecars in the Paks directory.
        if (string.Equals(name, GameplayPakBuilder.OwnPatchName, StringComparison.OrdinalIgnoreCase))
        {
            string marker = file.FullPath + ".aec-owned.sha256";
            if (fileSystem.FileExists(marker))
            {
                try
                {
                    string markerText = fileSystem.ReadAllText(marker);
                    if (AecPakOwnershipMarker.TryReadExpectedSha256(markerText, out string expected))
                    {
                        using Stream pak = fileSystem.OpenRead(file.FullPath);
                        string actual = Convert.ToHexString(SHA256.HashData(pak));
                        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                        {
                            return PakClassification.AecOwned;
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
                {
                    // Unverifiable ownership remains PatchStyle below.
                }
            }

            return PakClassification.PatchStyle;
        }

        return name.EndsWith("_P.pak", StringComparison.OrdinalIgnoreCase)
            ? PakClassification.PatchStyle
            : PakClassification.Unclassified;
    }
}
