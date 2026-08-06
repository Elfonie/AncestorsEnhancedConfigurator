namespace AncestorsEnhanced.Core.Editing;

/// <summary>The final disposition of a settings operation.</summary>
public enum SettingsOperationStatus
{
    Applied,
    Failed,
    // The last operation was rolled back completely; nothing of it remains.
    RolledBack,
    // The write succeeded but the rollback only partially restored the files; the
    // user must restore some files manually from the backup folder.
    PartialRollbackRequired,
}

public sealed record SettingsOperationResult(
    bool Succeeded,
    string Message,
    string? ManifestPath = null,
    SettingsOperationStatus Status = SettingsOperationStatus.Failed)
{
    public static SettingsOperationResult Applied(string message, string? manifestPath) =>
        new(true, message, manifestPath, SettingsOperationStatus.Applied);

    public static SettingsOperationResult RolledBack(string message) =>
        new(true, message, null, SettingsOperationStatus.RolledBack);

    public static SettingsOperationResult PartialRollbackRequired(string message, string? manifestPath) =>
        new(true, message, manifestPath, SettingsOperationStatus.PartialRollbackRequired);

    public static SettingsOperationResult Failed(string message) =>
        new(false, message, null, SettingsOperationStatus.Failed);
}
