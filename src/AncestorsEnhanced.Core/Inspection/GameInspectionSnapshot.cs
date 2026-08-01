namespace AncestorsEnhanced.Core.Inspection;

public sealed record GameInspectionSnapshot(
    DateTimeOffset InspectedAtUtc,
    GameInstallationSnapshot? Installation,
    string? UserDataDirectory,
    IReadOnlyList<ConfigurationFileSnapshot> ConfigurationFiles,
    BinarySettingsFileSnapshot? BinarySettingsFile,
    IReadOnlyList<PakFileSnapshot> PakFiles,
    IReadOnlyList<InspectionNotice> Notices,
    VignetteModSnapshot? Vignette = null)
{
    public bool IsGameDetected => Installation is not null;

    public bool HasErrors => Notices.Any(notice => notice.Severity == InspectionSeverity.Error);
}
