using System.Text;
using System.Text.Json;
using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal static class SettingsBackupStore
{
    private const string ManifestFileName = "operation.json";
    private const int MaxRetainedOperations = 50;
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

        EnforceRetention(GetBackupRoot(plan.UserDataDirectory));
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

    public static StoredSettingsOperation? FindLast(VerifiedGameContext context)
    {
        string backupRoot = GetBackupRoot(context.UserDataDirectory);
        if (!Directory.Exists(backupRoot))
        {
            return null;
        }

        foreach (string directory in Directory.EnumerateDirectories(backupRoot)
                     .OrderByDescending(path => path, StringComparer.Ordinal))
        {
            try
            {
                // A reparse point is a security violation: this candidate is skipped
                // (fail-safe) but does not block older valid candidates (F128).
                if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (!File.Exists(Path.Combine(directory, AppliedMarkerName)) ||
                    File.Exists(Path.Combine(directory, RevertedMarkerName)) ||
                    Directory.EnumerateFiles(directory).Any(name => IsReparsePointFile(Path.Combine(directory, name))))
                {
                    continue;
                }

                OperationManifest? manifest = ReadManifest(directory);
                if (manifest is null || !IsCandidateUsable(manifest, context))
                {
                    continue;
                }

                bool validFiles = manifest.Files.Count > 0 && manifest.Files.All(file =>
                {
                    _ = GetTargetPath(
                        manifest.UserDataDirectory,
                        manifest.InstallDirectory,
                        file.FileName,
                        file.Target);
                    string? expectedBackup = file.Existed ? $"{file.FileName}.before" : null;
                    string backupPath = Path.Combine(directory, expectedBackup ?? string.Empty);
                    // F127: a candidate is only eligible when its backup content still matches
                    // the recorded OriginalSha256. A tampered backup is not detected mid-restore;
                    // it makes this candidate ineligible and an older valid one is considered.
                    bool backupOk = !file.Existed ||
                        (IsNormalFile(backupPath) &&
                         string.Equals(Sha256(File.ReadAllBytes(backupPath)), file.OriginalSha256, StringComparison.Ordinal));
                    return string.Equals(file.BackupFileName, expectedBackup, PathComparison) &&
                        backupOk;
                });
                if (!validFiles)
                {
                    continue;
                }

                // A valid but externally-modified newest candidate must not stop the search:
                // keep looking for an older, still-unchanged operation (F019).
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
                if (!unchanged)
                {
                    continue;
                }

                return new StoredSettingsOperation(directory, manifest);
            }
            catch (Exception exception) when (IsExpectedStoreException(exception))
            {
                // F128: a broken or unreadable candidate must never prevent older valid
                // candidates from being checked.
                continue;
            }
        }

        return null;
    }

    private static bool IsCandidateUsable(OperationManifest manifest, VerifiedGameContext context)
    {
        if (manifest.Version != 2)
        {
            return false;
        }

        bool pathMatches = PathEquals(manifest.UserDataDirectory, context.UserDataDirectory) &&
            PathEquals(manifest.InstallDirectory, context.InstallDirectory);

        // Newer manifests carry the context fingerprint: it must match exactly (Paket 3).
        if (!string.IsNullOrEmpty(manifest.ContextFingerprint))
        {
            return pathMatches && string.Equals(manifest.ContextFingerprint, context.ContextFingerprint, StringComparison.Ordinal);
        }

        // Legacy manifest without a fingerprint: fail-safe, accept only when the identity
        // and the exact paths unambiguously match the current context.
        return pathMatches && GameIdentity.IsSupported(manifest.BuildId, manifest.ContentSignature, contentSignatureReadFailed: false);
    }

    private static bool PathEquals(string? first, string? second)
        => PathEqualsForPlatform(first, second, OperatingSystem.IsWindows());

    internal static bool PathEqualsForPlatform(string? first, string? second, bool isWindows)
    {
        if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
        {
            return false;
        }

        // F126: path comparison is platform-specific (OrdinalIgnoreCase on Windows,
        // Ordinal on Linux), never a blanket OrdinalIgnoreCase.
        return string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsReparsePointFile(string path)
    {
        try
        {
            return File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (IsExpectedStoreException(exception))
        {
            return true;
        }
    }

    private static bool IsExpectedStoreException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or
            System.Text.Json.JsonException or ArgumentException;

    private static void EnforceRetention(string backupRoot)
    {
        List<string> operations = Directory
            .EnumerateDirectories(backupRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        int overflow = operations.Count - MaxRetainedOperations;
        for (int index = 0; index < overflow; index++)
        {
            try
            {
                // Never prune directories that are currently applied but not reverted;
                // the list is ordered oldest first and older applied operations are
                // still needed for an Undo, so only remove entries that are either
                // already reverted or were never applied.
                string candidate = operations[index];
                if (File.Exists(Path.Combine(candidate, RevertedMarkerName)) ||
                    !File.Exists(Path.Combine(candidate, AppliedMarkerName)))
                {
                    Directory.Delete(candidate, recursive: true);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
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
                file.ResultExists)).ToArray(),
            plan.ContentSignature,
            plan.ContextFingerprint);



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
    IReadOnlyList<ManifestFile> Files,
    string? ContentSignature = null,
    string? ContextFingerprint = null);

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
