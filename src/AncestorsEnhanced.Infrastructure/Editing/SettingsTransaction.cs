using System;
using System.Text;
using System.Text.Json;
using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal sealed class SettingsTransaction(
    Func<DateTimeOffset> utcNow,
    Func<bool> isGameRunning,
    Func<string, bool> isExpectedUserDataDirectory,
    Func<SettingsChangePlan, bool>? revalidateContext = null,
    Func<GameInspectionSnapshot, bool>? revalidateSnapshot = null)
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow;
    private readonly Func<bool> _isGameRunning = isGameRunning;
    private readonly Func<string, bool> _isExpectedUserDataDirectory = isExpectedUserDataDirectory;
    private readonly Func<SettingsChangePlan, bool> _revalidateContext = revalidateContext ?? (_ => true);
    private readonly Func<GameInspectionSnapshot, bool> _revalidateSnapshot = revalidateSnapshot ?? (_ => true);
    private readonly Lock _planLock = new();
    private SettingsChangePlan? _issuedPlan;
    private string? _issuedPlanFingerprint;

    public SettingsChangePlan Issue(SettingsChangePlan plan)
    {
        lock (_planLock)
        {
            _issuedPlan = plan;
            _issuedPlanFingerprint = Fingerprint(plan);
        }

        return plan;
    }

    public SettingsOperationResult Apply(SettingsChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_planLock)
        {
            if (!ReferenceEquals(_issuedPlan, plan))
            {
                return Failure("This change plan was not created by this editor or has already been used.");
            }

            _issuedPlan = null;
            string? expectedFingerprint = _issuedPlanFingerprint;
            _issuedPlanFingerprint = null;
            if (!string.Equals(expectedFingerprint, Fingerprint(plan), StringComparison.Ordinal))
            {
                return Failure("The reviewed change plan was modified and will not be applied.");
            }
        }

        if (_isGameRunning())
        {
            return Failure("Close Ancestors before applying configuration changes.");
        }

        try
        {
            GameEditingGuard.ValidatePlan(plan);
            foreach (ConfigurationFileChangePlan file in plan.Files)
            {
                if (!MatchesCurrentFile(file))
                {
                    return Failure($"{file.FileName} changed after the preview. Refresh and try again.");
                }
            }


            string operationDirectory = SettingsBackupStore.Prepare(plan);
            SettingsOperationResult? appliedResult = MutationCoordinator.Run(() =>
            {
                // Revalidate inside the global mutation lock, immediately before the writes (F063-1c).
                if (!_revalidateContext(plan))
                {
                    return Failure("The game context changed since this change was previewed. Refresh and try again.");
                }
                List<ConfigurationFileChangePlan> applied = [];
                try
                {
                    ToolChangeBaselineStore.CaptureBeforeApply(plan);
                    foreach (ConfigurationFileChangePlan file in plan.Files)
                    {
                        if (file.ResultExists)
                        {
                            // CAS immediately before the write: the current file must
                            // still match the bytes the plan was built from, closing
                            // the multi-file TOCTOU between the up-front check and
                            // each individual write.
                            CompareAndReplace(
                                file.FullPath,
                                file.UpdatedContent,
                                file.OriginalSha256,
                                file.Existed);
                        }
                        else
                        {
                            CompareAndDelete(file.FullPath, file.OriginalSha256);
                        }

                        applied.Add(file);
                        bool valid = file.ResultExists
                            ? File.Exists(file.FullPath) && string.Equals(
                                Sha256(File.ReadAllBytes(file.FullPath)),
                                Sha256(file.UpdatedContent),
                                StringComparison.Ordinal)
                            : !File.Exists(file.FullPath);
                        if (!valid)
                        {
                            throw new IOException($"Validation failed after writing {file.FileName}.");
                        }
                    }

                    SettingsBackupStore.MarkApplied(operationDirectory, plan.CreatedAtUtc);
                    ToolChangeBaselineStore.MarkApplied(plan);
                }
                catch
                {
                    List<string> rollbackFailures = RestoreFilesBestEffort(applied);
                    if (rollbackFailures.Count > 0)
                    {
                        return SettingsOperationResult.PartialRollbackRequired(
                            "Some files could not be restored automatically. Restore them manually from the backup folder:\n" +
                            string.Join(System.Environment.NewLine, rollbackFailures),
                            SettingsBackupStore.GetManifestPath(operationDirectory));
                    }

                    throw;
                }

                return null;
            });
            if (appliedResult is not null)
            {
                return appliedResult;
            }

            return SettingsOperationResult.Applied(
                $"Applied {plan.Changes.Count} change{(plan.Changes.Count == 1 ? string.Empty : "s")}. A backup was created.",
                SettingsBackupStore.GetManifestPath(operationDirectory));
        }
        catch (Exception exception) when (IsExpectedWriteException(exception))
        {
            return Failure($"No changes were kept: {exception.Message}");
        }
    }

    public void Discard(SettingsChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_planLock)
        {
            if (ReferenceEquals(_issuedPlan, plan))
            {
                _issuedPlan = null;
                _issuedPlanFingerprint = null;
            }
        }
    }

    public bool CanRemoveToolChanges(GameInspectionSnapshot snapshot)
    {
        try
        {
            GameEditingGuard.ValidateSnapshot(snapshot, _isExpectedUserDataDirectory);
            return _revalidateSnapshot(snapshot) && ToolChangeBaselineStore.CanCreateRemovalPlan(snapshot);
        }
        catch (Exception exception) when (IsExpectedWriteException(exception))
        {
            return false;
        }
    }

    public SettingsChangePlan IssueToolChangeRemoval(GameInspectionSnapshot snapshot)
    {
        GameEditingGuard.ValidateSnapshot(snapshot, _isExpectedUserDataDirectory);
        if (!_revalidateSnapshot(snapshot))
        {
            throw new InvalidOperationException("The game context changed. Refresh and try again.");
        }

        return Issue(ToolChangeBaselineStore.CreateRemovalPlan(snapshot, _utcNow()));
    }

    public bool CanRevertLast(GameInspectionSnapshot snapshot)
    {
        try
        {
            GameEditingGuard.ValidateSnapshot(snapshot, _isExpectedUserDataDirectory);
            if (!_revalidateSnapshot(snapshot))
            {
                return false;
            }
            VerifiedGameContext? context = VerifiedGameContext.TryCreateFromSnapshot(snapshot);
            return context is not null && SettingsBackupStore.FindLast(context) is not null;
        }
        catch (Exception exception) when (IsExpectedWriteException(exception))
        {
            return false;
        }
    }

    public SettingsOperationResult RevertLast(GameInspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_isGameRunning())
        {
            return Failure("Close Ancestors before restoring a backup.");
        }

        try
        {
            GameEditingGuard.ValidateSnapshot(snapshot, _isExpectedUserDataDirectory);
            VerifiedGameContext? verifiable = VerifiedGameContext.TryCreateFromSnapshot(snapshot);
            StoredSettingsOperation? operation = verifiable is null ? null : SettingsBackupStore.FindLast(verifiable);
            if (operation is null)
            {
                return Failure("There is no unchanged configurator operation that can be restored safely.");
            }

            // The restore target is always reconstructed from the *current* snapshot,
            // never from the persisted absolute paths in the manifest (F013).
            string userDataDirectory = snapshot.UserDataDirectory ?? operation.Manifest.UserDataDirectory;
            string? installDirectory = snapshot.Installation?.InstallDirectory ?? operation.Manifest.InstallDirectory;
            // The whole restore mutation runs inside the single global mutation gate so it
            // can never race another configurator write (F001).
            return MutationCoordinator.Run(() =>
            {
                // Revalidate inside the global mutation lock, immediately before the restore (F063-1c).
                if (!_revalidateSnapshot(snapshot))
                {
                    return Failure("The game context changed; the backup cannot be restored safely. Refresh and try again.");
                }
            List<(ManifestFile File, byte[] Current)> restored = [];
            try
            {
                foreach (ManifestFile file in operation.Manifest.Files)
                {
                    string targetPath = GetTargetPath(
                        userDataDirectory,
                        installDirectory,
                        file.FileName,
                        file.Target);
                    byte[] current = File.Exists(targetPath) ? File.ReadAllBytes(targetPath) : [];
                    restored.Add((file, current));
                    // CAS immediately before the restore: the live file must still match
                    // the state this tool produced when it applied the change (the
                    // Result state). If anyone modified it since, abort without
                    // overwriting those new changes (F067/F127).
                    byte[]? original = file.Existed ? ReadOriginal(operation, file) : null;
                    if (file.ResultExists)
                    {
                        if (file.Existed)
                        {
                            CompareAndReplace(
                                targetPath,
                                original!,
                                file.ResultSha256,
                                expectedExists: true);
                        }
                        else
                        {
                            CompareAndDelete(targetPath, file.ResultSha256);
                        }
                    }
                    else
                    {
                        if (file.Existed)
                        {
                            // The live file is absent (Result deleted it), but the
                            // original existed: write the original back.
                            CompareAndReplace(
                                targetPath,
                                original!,
                                expectedSha256: null,
                                expectedExists: false);
                        }
                    }

                    if (file.Existed &&
                        !string.Equals(
                            Sha256(File.ReadAllBytes(targetPath)),
                            file.OriginalSha256,
                            StringComparison.Ordinal))
                    {
                        throw new IOException($"Validation failed after restoring {file.FileName}.");
                    }
                }
            }
            catch
            {
                List<string> restoreFailures = RestoreCurrentFilesBestEffort(userDataDirectory, installDirectory, restored);
                if (restoreFailures.Count > 0)
                {
                    return SettingsOperationResult.PartialRollbackRequired(
                        "Not all files could be restored automatically. Restore them manually from the backup folder:\n" +
                        string.Join(System.Environment.NewLine, restoreFailures) + "\nBackup folder: " + operation.Directory,
                        operation.Directory);
                }

                throw;
            }

            try
            {
                SettingsBackupStore.MarkReverted(operation.Directory, _utcNow());
            }
            catch (Exception exception) when (IsExpectedWriteException(exception))
            {
                return SettingsOperationResult.RolledBack(
                    "The configuration was restored, but its history marker could not be written: " + exception.Message);
            }

            return SettingsOperationResult.RolledBack(
                "The last configurator change was restored from its backup.");
            });
        }
        catch (Exception exception) when (IsExpectedWriteException(exception))
        {
            return Failure($"Nothing was restored: {exception.Message}");
        }
    }

    private static void RestoreCurrentFiles(
        StoredSettingsOperation operation,
        IEnumerable<(ManifestFile File, byte[] Current)> restored)
    {
        foreach ((ManifestFile file, byte[] current) in restored)
        {
            string targetPath = GetTargetPath(
                operation.Manifest.UserDataDirectory,
                operation.Manifest.InstallDirectory,
                file.FileName,
                file.Target);
            if (file.ResultExists)
            {
                WriteBytesAtomically(targetPath, current);
            }
            else
            {
                File.Delete(targetPath);
            }
        }
    }

    private static List<string> RestoreCurrentFilesBestEffort(
        string userDataDirectory,
        string? installDirectory,
        IEnumerable<(ManifestFile File, byte[] Current)> restored)
    {
        var failures = new List<string>();
        foreach ((ManifestFile file, byte[] current) in restored)
        {
            try
            {
                string targetPath = GetTargetPath(
                    userDataDirectory,
                    installDirectory,
                    file.FileName,
                    file.Target);

                string? currentHash = File.Exists(targetPath)
                    ? Sha256(File.ReadAllBytes(targetPath))
                    : null;

                // If the file is already back in its Result state (the state before this
                // undo ran), there is nothing to fold back. Only the Original state that
                // this undo wrote may be restored; a foreign concurrent change is never
                // overwritten (F067).
                bool inResult = file.ResultExists
                    ? currentHash is not null && string.Equals(currentHash, file.ResultSha256, StringComparison.Ordinal)
                    : currentHash is null;
                if (inResult)
                {
                    continue;
                }

                bool inOriginal = file.Existed
                    ? currentHash is not null && string.Equals(currentHash, file.OriginalSha256, StringComparison.Ordinal)
                    : currentHash is null;
                if (!inOriginal)
                {
                    failures.Add(file.FileName);
                    continue;
                }

                if (file.ResultExists)
                {
                    CompareAndReplace(
                        targetPath,
                        current,
                        file.Existed ? file.OriginalSha256 : null,
                        file.Existed);
                }
                else if (file.Existed)
                {
                    CompareAndDelete(targetPath, file.OriginalSha256);
                }
            }
            catch (Exception exception) when (IsExpectedWriteException(exception))
            {
                failures.Add(file.FileName);
            }
        }

        return failures;
    }

    private static bool MatchesCurrentFile(ConfigurationFileChangePlan file)
    {
        if (File.Exists(file.FullPath) != file.Existed)
        {
            return false;
        }

        byte[] current = file.Existed ? File.ReadAllBytes(file.FullPath) : [];
        return string.Equals(Sha256(current), file.OriginalSha256, StringComparison.Ordinal);
    }

    private static byte[] ReadOriginal(StoredSettingsOperation operation, ManifestFile file)
    {
        if (file.BackupFileName is null)
        {
            throw new IOException($"The backup for {file.FileName} is missing.");
        }

        byte[] original = File.ReadAllBytes(
            Path.Combine(operation.Directory, file.BackupFileName));
        if (!string.Equals(Sha256(original), file.OriginalSha256, StringComparison.Ordinal))
        {
            throw new IOException($"The backup for {file.FileName} failed validation.");
        }

        return original;
    }

    private static string Fingerprint(SettingsChangePlan plan) =>
        Sha256(JsonSerializer.SerializeToUtf8Bytes(plan));

    private static void RestoreFiles(IEnumerable<ConfigurationFileChangePlan> files)
    {
        foreach (ConfigurationFileChangePlan file in files.Reverse())
        {
            if (file.Existed)
            {
                WriteBytesAtomically(file.FullPath, file.OriginalContent);
            }
            else
            {
                File.Delete(file.FullPath);
            }
        }
    }

    /// <summary>
    /// Restores every touched file best-effort. Returns the names of files that could
    /// not be restored (empty list means the rollback was complete).
    /// </summary>
    /// <summary>
    /// Restores every already-applied file back to its original state, but only when the
    /// current file still matches the result state this apply wrote. A foreign concurrent
    /// change is never overwritten; such files are reported as failures (F066).
    /// </summary>
    private static List<string> RestoreFilesBestEffort(IEnumerable<ConfigurationFileChangePlan> files)
    {
        var failures = new List<string>();
        foreach (ConfigurationFileChangePlan file in files.Reverse())
        {
            try
            {
                string resultHash = Sha256(file.UpdatedContent);
                if (file.Existed && file.ResultExists)
                {
                    // Replace: the file existed before and after; fold our own write back
                    // only if it still matches the result state we wrote (F066).
                    CompareAndReplace(file.FullPath, file.OriginalContent, resultHash, expectedExists: true);
                }
                else if (!file.Existed && file.ResultExists)
                {
                    // Create: the file did not exist before; remove the file we created.
                    CompareAndDelete(file.FullPath, resultHash);
                }
                else if (file.Existed && !file.ResultExists)
                {
                    // Delete: the file existed before and was removed by this apply.
                    // Recreate the original only if the target is still absent, so a
                    // foreign re-created file is never overwritten (F066).
                    CompareAndReplace(file.FullPath, file.OriginalContent, expectedSha256: null, expectedExists: false);
                }
                }
            catch (Exception exception) when (IsExpectedWriteException(exception))
            {
                failures.Add(file.FileName);
            }
        }

        return failures;
    }

    private static SettingsOperationResult Failure(string message) => new(false, message);

    private static bool IsExpectedWriteException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or JsonException or DecoderFallbackException;
}
