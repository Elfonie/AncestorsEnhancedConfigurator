using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.FileSystem;
using AncestorsEnhanced.Infrastructure.SystemSave;

namespace AncestorsEnhanced.Infrastructure.Inspection;

internal sealed class SystemSaveInspector(IReadOnlyFileSystem fileSystem)
{
    public BinarySettingsFileSnapshot? Read(string? userDataDirectory)
    {
        if (userDataDirectory is null)
        {
            return null;
        }

        string path = Path.Combine(userDataDirectory, "SaveGames", "System.sav");
        if (!fileSystem.FileExists(path))
        {
            return new BinarySettingsFileSnapshot(
                "System.sav",
                path,
                false,
                null,
                null,
                "Not found");
        }

        try
        {
            ReadOnlyFileMetadata metadata = fileSystem.GetFileMetadata(path);
            if (metadata.SizeBytes > InspectionLimits.SystemSave)
            {
                return Snapshot(
                    metadata,
                    "File is unexpectedly large and was not decoded",
                    null);
            }

            SystemGraphicsSettingsSnapshot graphics = AncestorsSystemSaveCodec.Read(
                fileSystem.ReadAllBytes(path));
            return Snapshot(metadata, "Decoded and verified", graphics);
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            return new BinarySettingsFileSnapshot(
                "System.sav",
                path,
                true,
                null,
                null,
                $"Could not decode: {exception.Message}");
        }
    }

    private static BinarySettingsFileSnapshot Snapshot(
        ReadOnlyFileMetadata metadata,
        string status,
        SystemGraphicsSettingsSnapshot? graphics) =>
        new(
            metadata.Name,
            metadata.FullPath,
            true,
            metadata.SizeBytes,
            metadata.LastWriteTimeUtc,
            status,
            graphics);
}
