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
    private const string AbortedMarkerName = "aborted";
    private const string RevertPendingMarkerName = "revert-pending";
    private const int MaximumManifestSize = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Prepare(SettingsChangePlan plan)
    {
        ValidateConfigurationPath(
            plan.UserDataDirectory,
            GetBackupRoot(plan.UserDataDirectory));
        string directory = GetOperationDirectory(plan.UserDataDirectory, plan.OperationId);
        if (Directory.Exists(directory) || File.Exists(directory))
        {
            throw new IOException("The settings operation identifier already exists.");
        }
        Directory.CreateDirectory(directory);

        foreach (ConfigurationFileChangePlan file in plan.Files.Where(file => file.Existed))
        {
            string backupPath = Path.Combine(directory, $"{file.FileName}.before");
            WriteBytesAtomically(
                backupPath,
                file.OriginalContent);
            if (!string.Equals(
                    Sha256(ReadStableBounded(backupPath, 64L * 1024 * 1024)),
                    file.OriginalSha256,
                    StringComparison.Ordinal))
            {
                throw new IOException($"Validation failed after backing up {file.FileName}.");
            }
        }

        // The manifest is the recovery commit record.  Do not publish it until every
        // required before-image has been written and re-read successfully; a crash
        // during preparation then leaves only an inert, markerless directory.
        WriteManifest(directory, CreateManifest(plan));

        return directory;
    }

    public static string GetManifestPath(string directory) =>
        Path.Combine(directory, ManifestFileName);

    public static void MarkApplied(string directory, DateTimeOffset createdAtUtc)
    {
        WriteBytesAtomically(
            Path.Combine(directory, AppliedMarkerName),
            Encoding.UTF8.GetBytes(createdAtUtc.ToString("O")));

        string backupRoot = Path.GetDirectoryName(directory)
            ?? throw new InvalidOperationException("The settings backup root is missing.");
        try
        {
            EnforceRetention(backupRoot, directory);
        }
        catch (Exception exception) when (IsExpectedStoreException(exception))
        {
            // The operation is already committed. Retention is best-effort and must
            // not turn a successful settings write into a reported failure.
        }
    }

    public static void MarkReverted(string directory, DateTimeOffset revertedAtUtc) =>
        WriteBytesAtomically(
            Path.Combine(directory, RevertedMarkerName),
            Encoding.UTF8.GetBytes(revertedAtUtc.ToString("O")));

    public static void MarkAborted(string directory, DateTimeOffset abortedAtUtc) =>
        WriteBytesAtomically(
            Path.Combine(directory, AbortedMarkerName),
            Encoding.UTF8.GetBytes(abortedAtUtc.ToString("O")));

    public static void MarkRevertPending(string directory, DateTimeOffset startedAtUtc) =>
        WriteBytesAtomically(
            Path.Combine(directory, RevertPendingMarkerName),
            Encoding.UTF8.GetBytes(startedAtUtc.ToString("O")));

    public static void ClearRevertPending(string directory)
    {
        string path = Path.Combine(directory, RevertPendingMarkerName);
        File.Delete(path);
        if (File.Exists(path))
        {
            throw new IOException("The interrupted undo marker could not be removed.");
        }
    }

    public static bool RecoverInterrupted(VerifiedGameContext context)
    {
        string backupRoot = GetBackupRoot(context.UserDataDirectory);
        if (!Directory.Exists(backupRoot))
        {
            return false;
        }

        bool recovered = false;
        foreach (OperationDirectory operation in EnumerateOperations(backupRoot, newestFirst: true))
        {
            string directory = operation.Directory;
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            OperationManifest manifest = operation.Manifest;
            if (!IsCandidateUsable(manifest, context))
            {
                continue;
            }

            bool applied = File.Exists(Path.Combine(directory, AppliedMarkerName));
            bool reverted = File.Exists(Path.Combine(directory, RevertedMarkerName));
            bool aborted = File.Exists(Path.Combine(directory, AbortedMarkerName));
            bool revertPending = File.Exists(Path.Combine(directory, RevertPendingMarkerName));
            if (aborted || (reverted && !revertPending))
            {
                continue;
            }

            List<RecoveryFile> files;
            try
            {
                files = ValidateRecoveryFiles(context, directory, manifest);
            }
            catch (IOException) when (IsIncompletePreparation(directory, manifest))
            {
                // Older versions wrote the manifest before its backups.  Treat that
                // precise crash residue as uncommitted and continue checking newer
                // healthy journals.  Other invalid journals still fail closed.
                continue;
            }
            if (applied && !reverted && !revertPending)
            {
                // The applied marker is the commit record. The target may have been
                // changed by the game, the user, or a newer operation afterwards.
                // Only a still-present CAS sidecar belongs to crash recovery here.
                foreach (RecoveryFile file in files.Where(file => file.HasSidecar))
                {
                    recovered |= RecoverInterruptedTarget(file.TargetPath, file.ExpectedExistingHashes);
                }
                break;
            }

            ManifestFile? preexistingForeign = files
                .Where(file => !file.HasSidecar)
                .FirstOrDefault(file =>
                    ReadTargetState(file.TargetPath, file.Manifest) == RecoveryTargetState.Foreign)
                ?.Manifest;
            if (preexistingForeign is not null)
            {
                throw ManualRecoveryRequired(directory, preexistingForeign.FileName);
            }

            foreach (RecoveryFile file in files.Where(file => file.HasSidecar))
            {
                recovered |= RecoverInterruptedTarget(file.TargetPath, file.ExpectedExistingHashes);
            }

            files = files.Select(file => file with
            {
                State = ReadTargetState(file.TargetPath, file.Manifest),
            }).ToList();
            ManifestFile? foreign = files.FirstOrDefault(file => file.State == RecoveryTargetState.Foreign)?.Manifest;
            if (foreign is not null)
            {
                throw ManualRecoveryRequired(directory, foreign.FileName);
            }

            bool allOriginal = files.All(file => file.State == RecoveryTargetState.Original);
            if (reverted)
            {
                if (!allOriginal)
                {
                    throw new IOException(
                        $"The completed undo journal does not match its target files. Inspect {GetManifestPath(directory)} manually.");
                }
                ToolChangeBaselineStore.MarkReverted(context, manifest);
                ClearRevertPending(directory);
                recovered = true;
                continue;
            }

            RestoreOriginalStates(directory, files);
            if (applied)
            {
                MarkReverted(directory, DateTimeOffset.UtcNow);
                ToolChangeBaselineStore.MarkReverted(context, manifest);
                if (revertPending)
                {
                    ClearRevertPending(directory);
                }
            }
            else
            {
                ToolChangeBaselineStore.RollbackInterrupted(context, manifest);
                MarkAborted(directory, DateTimeOffset.UtcNow);
            }
            recovered = true;
        }

        return recovered;
    }

    private static IOException ManualRecoveryRequired(string directory, string fileName) =>
        new(
            $"Interrupted settings recovery stopped because {fileName} matches neither the original nor the tool result. " +
            $"No foreign game file was overwritten. Inspect {GetManifestPath(directory)} manually.");

    public static StoredSettingsOperation? FindLast(VerifiedGameContext context)
    {
        string backupRoot = GetBackupRoot(context.UserDataDirectory);
        if (!Directory.Exists(backupRoot))
        {
            return null;
        }

        foreach (OperationDirectory operation in EnumerateOperations(backupRoot, newestFirst: true))
        {
            string directory = operation.Directory;
            try
            {
                // A reparse point is a security violation: this candidate is skipped
                // but does not block older valid candidates.
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

                OperationManifest manifest = operation.Manifest;
                if (!IsCandidateUsable(manifest, context))
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
                    // A candidate is only eligible when its backup content still matches
                    // the recorded OriginalSha256. A tampered backup is not detected mid-restore;
                    // it makes this candidate ineligible and an older valid one is considered.
                    bool backupOk = !file.Existed ||
                        (IsNormalFile(backupPath) &&
                         string.Equals(Sha256(ReadStableBounded(backupPath, 64L * 1024 * 1024)), file.OriginalSha256, StringComparison.Ordinal));
                    return string.Equals(file.BackupFileName, expectedBackup, PathComparison) &&
                        backupOk;
                });
                if (!validFiles)
                {
                    continue;
                }

                // A valid but externally-modified newest candidate must not stop the search:
                // keep looking for an older, still-unchanged operation.
                bool unchanged = manifest.Files.All(file =>
                {
                    string path = GetTargetPath(
                        manifest.UserDataDirectory,
                        manifest.InstallDirectory,
                        file.FileName,
                        file.Target);
                    return file.ResultExists
                        ? File.Exists(path) && string.Equals(
                            Sha256(ReadStableBounded(path, 64L * 1024 * 1024)),
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
                // A broken or unreadable candidate must never prevent older valid
                // candidates from being checked.
                continue;
            }
        }

        return null;
    }

    private static List<RecoveryFile> ValidateRecoveryFiles(
        VerifiedGameContext context,
        string operationDirectory,
        OperationManifest manifest)
    {
        if (manifest.Files.Count == 0)
        {
            throw new IOException($"The interrupted operation has no files: {GetManifestPath(operationDirectory)}");
        }

        var files = new List<RecoveryFile>(manifest.Files.Count);
        foreach (ManifestFile file in manifest.Files)
        {
            string targetPath = GetTargetPath(
                context.UserDataDirectory,
                context.InstallDirectory,
                file.FileName,
                file.Target);
            string? expectedBackupName = file.Existed ? $"{file.FileName}.before" : null;
            if (!string.Equals(file.BackupFileName, expectedBackupName, PathComparison))
            {
                throw new IOException($"The recovery backup name for {file.FileName} is invalid.");
            }

            if (file.Existed)
            {
                string backupPath = Path.Combine(operationDirectory, expectedBackupName!);
                if (!IsNormalFile(backupPath) ||
                    !string.Equals(
                        Sha256(ReadStableBounded(backupPath, 64L * 1024 * 1024)),
                        file.OriginalSha256,
                        StringComparison.Ordinal))
                {
                    throw new IOException($"The recovery backup for {file.FileName} is missing or invalid.");
                }
            }

            var expectedExistingHashes = new List<string>(2);
            if (file.Existed)
            {
                expectedExistingHashes.Add(file.OriginalSha256);
            }
            if (file.ResultExists)
            {
                expectedExistingHashes.Add(file.ResultSha256);
            }
            bool hasSidecar = ValidateInterruptedTargetRecovery(targetPath, expectedExistingHashes);
            string trustedRoot = file.Target == SettingFileTarget.Pak
                ? context.InstallDirectory!
                : context.UserDataDirectory;
            files.Add(new RecoveryFile(
                file,
                targetPath,
                expectedExistingHashes,
                hasSidecar,
                RecoveryTargetState.Foreign,
                trustedRoot));
        }
        return files;
    }

    private static bool IsIncompletePreparation(string operationDirectory, OperationManifest manifest)
    {
        if (File.Exists(Path.Combine(operationDirectory, AppliedMarkerName)) ||
            File.Exists(Path.Combine(operationDirectory, RevertedMarkerName)) ||
            File.Exists(Path.Combine(operationDirectory, AbortedMarkerName)))
        {
            return false;
        }

        return manifest.Files.Where(file => file.Existed).Any(file =>
            !IsNormalFile(Path.Combine(operationDirectory, $"{file.FileName}.before")));
    }

    private static RecoveryTargetState ReadTargetState(string targetPath, ManifestFile file)
    {
        if (Directory.Exists(targetPath))
        {
            return RecoveryTargetState.Foreign;
        }

        bool exists = File.Exists(targetPath);
        string? hash = exists
            ? Sha256(ReadStableBounded(targetPath, 64L * 1024 * 1024))
            : null;
        bool original = exists == file.Existed &&
            (!exists || string.Equals(hash, file.OriginalSha256, StringComparison.Ordinal));
        if (original)
        {
            return RecoveryTargetState.Original;
        }

        bool result = exists == file.ResultExists &&
            (!exists || string.Equals(hash, file.ResultSha256, StringComparison.Ordinal));
        return result ? RecoveryTargetState.Result : RecoveryTargetState.Foreign;
    }

    private static void RestoreOriginalStates(
        string operationDirectory,
        IReadOnlyList<RecoveryFile> files)
    {
        foreach (RecoveryFile recovery in files.Where(file => file.State == RecoveryTargetState.Result))
        {
            ManifestFile file = recovery.Manifest;
            if (file.Existed)
            {
                byte[] original = ReadStableBounded(
                    Path.Combine(operationDirectory, file.BackupFileName!),
                    64L * 1024 * 1024);
                CompareAndReplace(
                    recovery.TargetPath,
                    original,
                    file.ResultExists ? file.ResultSha256 : null,
                    file.ResultExists,
                    recovery.TrustedRoot);
            }
            else if (file.ResultExists)
            {
                CompareAndDelete(recovery.TargetPath, file.ResultSha256, recovery.TrustedRoot);
            }

            if (ReadTargetState(recovery.TargetPath, file) != RecoveryTargetState.Original)
            {
                throw new IOException($"Recovery validation failed for {file.FileName}.");
            }
        }
    }

    private static bool IsCandidateUsable(OperationManifest manifest, VerifiedGameContext context)
    {
        if (manifest.Version != 2)
        {
            return false;
        }

        bool pathMatches = PathEquals(manifest.UserDataDirectory, context.UserDataDirectory) &&
            PathEquals(manifest.InstallDirectory, context.InstallDirectory);

        // Newer manifests carry the context fingerprint, which must match exactly.
        if (!string.IsNullOrEmpty(manifest.ContextFingerprint))
        {
            return pathMatches && string.Equals(manifest.ContextFingerprint, context.ContextFingerprint, StringComparison.Ordinal);
        }

        // Legacy manifest without a fingerprint: fail-safe, accept only when the identity
        // and the exact paths unambiguously match the current context.
        return pathMatches && GameIdentity.IsSupported(
            StoreKind.Steam,
            manifest.BuildId,
            manifest.ContentSignature,
            contentSignatureReadFailed: false);
    }

    private static bool PathEquals(string? first, string? second)
        => PathEqualsForPlatform(first, second, OperatingSystem.IsWindows());

    internal static bool PathEqualsForPlatform(string? first, string? second, bool isWindows)
    {
        if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
        {
            return false;
        }

        // Path comparison is platform-specific (OrdinalIgnoreCase on Windows,
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

    private static void EnforceRetention(string backupRoot, string protectedDirectory)
    {
        DeleteMarkerlessPreparationResidue(backupRoot, protectedDirectory);
        List<OperationDirectory> operations = EnumerateOperations(backupRoot, newestFirst: false);
        int remaining = operations.Count;
        foreach (OperationDirectory operation in operations)
        {
            if (remaining <= MaxRetainedOperations)
            {
                break;
            }
            string candidate = operation.Directory;
            if (PathEquals(candidate, protectedDirectory))
            {
                continue;
            }
            try
            {
                bool pendingUndo = File.Exists(Path.Combine(candidate, RevertPendingMarkerName));
                bool committedOrTerminal =
                    File.Exists(Path.Combine(candidate, AppliedMarkerName)) ||
                    File.Exists(Path.Combine(candidate, RevertedMarkerName)) ||
                    File.Exists(Path.Combine(candidate, AbortedMarkerName));
                if (!pendingUndo && committedOrTerminal)
                {
                    DeleteDirectorySafely(backupRoot, candidate);
                    remaining--;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void DeleteMarkerlessPreparationResidue(string backupRoot, string protectedDirectory)
    {
        foreach (string candidate in Directory.EnumerateDirectories(backupRoot))
        {
            try
            {
                if (PathEquals(candidate, protectedDirectory) ||
                    File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint) ||
                    IsNormalFile(Path.Combine(candidate, ManifestFileName)))
                {
                    continue;
                }

                // A directory without a manifest has never become a recovery
                // operation.  It can only be a preparation crash residue.
                DeleteDirectorySafely(backupRoot, candidate);
            }
            catch (Exception exception) when (IsExpectedStoreException(exception))
            {
                // Retention remains best effort and never follows reparse points.
            }
        }
    }

    private static List<OperationDirectory> EnumerateOperations(string backupRoot, bool newestFirst)
    {
        var operations = new List<OperationDirectory>();
        foreach (string directory in Directory.EnumerateDirectories(backupRoot))
        {
            try
            {
                if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }
                OperationManifest? manifest = ReadManifest(directory);
                if (manifest is not null)
                {
                    operations.Add(new OperationDirectory(directory, manifest));
                }
            }
            catch (Exception exception) when (IsExpectedStoreException(exception))
            {
            }
        }

        IOrderedEnumerable<OperationDirectory> ordered = newestFirst
            ? operations.OrderByDescending(item => item.Manifest.CreatedAtUtc)
                .ThenByDescending(item => item.Manifest.OperationId, StringComparer.Ordinal)
            : operations.OrderBy(item => item.Manifest.CreatedAtUtc)
                .ThenBy(item => item.Manifest.OperationId, StringComparer.Ordinal);
        return ordered.ToList();
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
            ? JsonSerializer.Deserialize<OperationManifest>(ReadStableBounded(path, MaximumManifestSize))
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

internal sealed record OperationDirectory(
    string Directory,
    OperationManifest Manifest);

internal enum RecoveryTargetState
{
    Original,
    Result,
    Foreign,
}

internal sealed record RecoveryFile(
    ManifestFile Manifest,
    string TargetPath,
    IReadOnlyCollection<string> ExpectedExistingHashes,
    bool HasSidecar,
    RecoveryTargetState State,
    string? TrustedRoot = null);
