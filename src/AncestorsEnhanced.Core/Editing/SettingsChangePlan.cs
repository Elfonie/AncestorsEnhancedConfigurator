namespace AncestorsEnhanced.Core.Editing;

public sealed record SettingsChangePlan(
    string OperationId,
    DateTimeOffset CreatedAtUtc,
    string BuildId,
    string UserDataDirectory,
    IReadOnlyList<SettingChangePreview> Changes,
    IReadOnlyList<ConfigurationFileChangePlan> Files,
    string? InstallDirectory = null);
