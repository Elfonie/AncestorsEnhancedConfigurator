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
            string backupPath = Path.Combine(directory, $"{file.FileName}.before");
            WriteBytesAtomically(
                backupPath,
                file.OriginalContent);
            if (!string.Equals(
                    Sha256(File.ReadAllBytes(backupPath)),
                    file.OriginalSha256,
                    StringComparison.Ordinal))
            {
                throw new IOException($"Validation failed after backing up {file.FileName}.");
            }
        }

        return directory;
    }

    public static string GetManifestPath(string directory) =>
        Path.Combine(directory, ManifestFileName);

    public static void MarkApplied(string directory, DateTimeOffset createdAtUtc) =>
        WriteBytesAtomically(
            Path.Combine(directory, AppliedMarkerName),
            Encoding.UTF8.GetBytes(createdAtUtc.ToString("O")));

    public static void MarkReverted(string directory, DateTimeOffset revertedAtUtc) =>
        WriteBytesAtomically(
            Path.Combine(directory, RevertedMarkerName),
            Encoding.UTF8.GetBytes(revertedAtUtc.ToString("O")));

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
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }

            if (!File.Exists(Path.Combine(directory, AppliedMarkerName)) ||
                File.Exists(Path.Combine(directory, RevertedMarkerName)))
            {
                continue;
            }

            OperationManifest? manifest = ReadManifest(directory);
            if (manifest is null ||
                manifest.Version != 2 ||
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
                _ = GetTargetPath(
                    manifest.UserDataDirectory,
                    manifest.InstallDirectory,
                    file.FileName,
                    file.Target);
                string? expectedBackup = file.Existed ? $"{file.FileName}.before" : null;
                return string.Equals(
                    file.BackupFileName,
                    expectedBackup,
                    StringComparison.OrdinalIgnoreCase) &&
                    (!file.Existed || IsNormalFile(Path.Combine(directory, expectedBackup!)));
            });
            if (!validFiles)
            {
                return null;
            }

            bool unchanged = manifest.Files.All(file =>
            {
                string path = GetTargetPath(
                    manifest.UserDataDirectory,
                    manifest.InstallDirectory,
                    file.FileName,
                    file.Target);
                return file.ResultExists
                    ? File.Exists(path) && string.Equals(
                        Sha256(File.ReadAllBytes(path)),
                        file.ResultSha256,
                        StringComparison.Ordinal)
                    : !File.Exists(path);
            });

            return unchanged ? new StoredSettingsOperation(directory, manifest) : null;
        }

        return null;
    }

    private static OperationManifest CreateManifest(SettingsChangePlan plan) =>
        new(
            Version: 2,
            plan.OperationId,
            plan.CreatedAtUtc,
            plan.BuildId,
            plan.UserDataDirectory,
            plan.InstallDirectory,
            plan.Changes,
            plan.Files.Select(file => new ManifestFile(
                file.FileName,
                file.Existed,
                file.OriginalSha256,
                Sha256(file.UpdatedContent),
                file.Existed ? $"{file.FileName}.before" : null,
                file.Target,
                file.ResultExists)).ToArray());

    private static OperationManifest? ReadManifest(string directory)
    {
        string path = Path.Combine(directory, ManifestFileName);
        OperationManifest? manifest = IsNormalFile(path)
            ? JsonSerializer.Deserialize<OperationManifest>(File.ReadAllText(path, Encoding.UTF8))
            : null;
        return manifest?.Version == 1
            ? manifest with
            {
                Version = 2,
                Files = manifest.Files.Select(file => file with
                {
                    Target = SettingFileTarget.Ini,
                    ResultExists = true,
                }).ToArray(),
            }
            : manifest;
    }

    private static bool IsNormalFile(string path) =>
        File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

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
    string? InstallDirectory,
    IReadOnlyList<SettingChangePreview> Changes,
    IReadOnlyList<ManifestFile> Files);

internal sealed record ManifestFile(
    string FileName,
    bool Existed,
    string OriginalSha256,
    string ResultSha256,
    string? BackupFileName,
    SettingFileTarget Target,
    bool ResultExists);

internal sealed record StoredSettingsOperation(
    string Directory,
    OperationManifest Manifest);
