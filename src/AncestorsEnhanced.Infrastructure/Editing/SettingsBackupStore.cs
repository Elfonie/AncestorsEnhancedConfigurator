using System.Text;
using System.Text.Json;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal static class SettingsBackupStore
{
    private const string ManifestFileName = "operation.json";
    private const string AppliedMarkerName = "applied";
    private const string RevertedMarkerName = "reverted";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Prepare(SettingsChangePlan plan)
    {
        ValidateConfigurationPath(
            plan.UserDataDirectory,
            GetBackupRoot(plan.UserDataDirectory));
        string directory = GetOperationDirectory(plan.UserDataDirectory, plan.OperationId);
        Directory.CreateDirectory(directory);
        WriteManifest(directory, CreateManifest(plan));

        foreach (ConfigurationFileChangePlan file in plan.Files.Where(file => file.Existed))
        {
            WriteBytesAtomically(
                Path.Combine(directory, $"{file.FileName}.before"),
                file.OriginalContent);
        }

        return directory;
    }

    public static string GetManifestPath(string directory) =>
        Path.Combine(directory, ManifestFileName);

    public static void MarkApplied(string directory, DateTimeOffset createdAtUtc) =>
        File.WriteAllText(
            Path.Combine(directory, AppliedMarkerName),
            createdAtUtc.ToString("O"),
            Encoding.UTF8);

    public static void MarkReverted(string directory, DateTimeOffset revertedAtUtc) =>
        File.WriteAllText(
            Path.Combine(directory, RevertedMarkerName),
            revertedAtUtc.ToString("O"),
            Encoding.UTF8);

    public static StoredSettingsOperation? FindLast(
        GameInspectionSnapshot snapshot,
        string supportedBuildId)
    {
        string backupRoot = GetBackupRoot(snapshot.UserDataDirectory!);
        if (!Directory.Exists(backupRoot))
        {
            return null;
        }

        foreach (string directory in Directory.EnumerateDirectories(backupRoot)
                     .OrderByDescending(path => path, StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(directory, AppliedMarkerName)) ||
                File.Exists(Path.Combine(directory, RevertedMarkerName)))
            {
                continue;
            }

            OperationManifest? manifest = ReadManifest(directory);
            if (manifest is null ||
                manifest.Version != 1 ||
                !string.Equals(manifest.BuildId, supportedBuildId, StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetFullPath(manifest.UserDataDirectory),
                    Path.GetFullPath(snapshot.UserDataDirectory!),
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            bool validFiles = manifest.Files.Count > 0 && manifest.Files.All(file =>
            {
                ValidateFileName(file.FileName);
                string? expectedBackup = file.Existed ? $"{file.FileName}.before" : null;
                return string.Equals(
                    file.BackupFileName,
                    expectedBackup,
                    StringComparison.OrdinalIgnoreCase) &&
                    (!file.Existed || File.Exists(Path.Combine(directory, expectedBackup!)));
            });
            if (!validFiles)
            {
                return null;
            }

            string configDirectory = GetConfigurationDirectory(manifest.UserDataDirectory);
            bool unchanged = manifest.Files.All(file =>
            {
                string path = GetTargetPath(configDirectory, file.FileName);
                return File.Exists(path) && string.Equals(
                    Sha256(File.ReadAllBytes(path)),
                    file.ResultSha256,
                    StringComparison.Ordinal);
            });

            return unchanged ? new StoredSettingsOperation(directory, manifest) : null;
        }

        return null;
    }

    private static OperationManifest CreateManifest(SettingsChangePlan plan) =>
        new(
            Version: 1,
            plan.OperationId,
            plan.CreatedAtUtc,
            plan.BuildId,
            plan.UserDataDirectory,
            plan.Changes,
            plan.Files.Select(file => new ManifestFile(
                file.FileName,
                file.Existed,
                file.OriginalSha256,
                Sha256(file.UpdatedContent),
                file.Existed ? $"{file.FileName}.before" : null)).ToArray());

    private static OperationManifest? ReadManifest(string directory)
    {
        string path = Path.Combine(directory, ManifestFileName);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<OperationManifest>(File.ReadAllText(path, Encoding.UTF8))
            : null;
    }

    private static void WriteManifest(string directory, OperationManifest manifest)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        WriteBytesAtomically(Path.Combine(directory, ManifestFileName), bytes);
    }
}

internal sealed record OperationManifest(
    int Version,
    string OperationId,
    DateTimeOffset CreatedAtUtc,
    string BuildId,
    string UserDataDirectory,
    IReadOnlyList<SettingChangePreview> Changes,
    IReadOnlyList<ManifestFile> Files);

internal sealed record ManifestFile(
    string FileName,
    bool Existed,
    string OriginalSha256,
    string ResultSha256,
    string? BackupFileName);

internal sealed record StoredSettingsOperation(
    string Directory,
    OperationManifest Manifest);
