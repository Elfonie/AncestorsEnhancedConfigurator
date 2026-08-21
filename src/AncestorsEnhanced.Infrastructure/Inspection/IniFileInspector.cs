using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.FileSystem;
using AncestorsEnhanced.Infrastructure.Parsing;
using AncestorsEnhanced.Infrastructure.Editing;

namespace AncestorsEnhanced.Infrastructure.Inspection;

internal sealed class IniFileInspector(IReadOnlyFileSystem fileSystem)
{
    public ConfigurationFileSnapshot[] Read(
        string? userDataDirectory,
        List<InspectionNotice> notices)
    {
        if (userDataDirectory is null)
        {
            return [];
        }

        string directory = Path.Combine(userDataDirectory, "Config", "WindowsNoEditor");
        if (!fileSystem.DirectoryExists(directory))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Warning,
                "config.directory-not-found",
                "The Ancestors configuration directory was not found."));
            return [];
        }

        try
        {
            return [.. fileSystem.EnumerateFiles(directory, "*.ini").Select(ReadFile)];
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            notices.Add(new InspectionNotice(
                InspectionSeverity.Error,
                "config.directory-unreadable",
                $"The configuration directory could not be read: {exception.Message}"));
            return [];
        }
    }

    private ConfigurationFileSnapshot ReadFile(ReadOnlyFileMetadata metadata)
    {
        if (metadata.SizeBytes > InspectionLimits.TextFile)
        {
            return Snapshot(metadata, [], "File is unexpectedly large and was not read.");
        }

        try
        {
            return Snapshot(
                metadata,
                IniSnapshotParser.Parse(
                    EncodedTextFile.Decode(ConfigurationFileOperations.ReadStableBounded(
                        metadata.FullPath, InspectionLimits.TextFile)).Text),
                null);
        }
        catch (Exception exception) when (InspectionErrors.IsExpected(exception))
        {
            return Snapshot(metadata, [], exception.Message);
        }
    }

    private static ConfigurationFileSnapshot Snapshot(
        ReadOnlyFileMetadata metadata,
        IReadOnlyList<IniSettingSnapshot> settings,
        string? error) =>
        new(
            metadata.Name,
            metadata.FullPath,
            true,
            metadata.SizeBytes,
            metadata.LastWriteTimeUtc,
            settings,
            error);
}
