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
    Func<string, bool> isExpectedUserDataDirectory)
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow;
    private readonly Func<bool> _isGameRunning = isGameRunning;
    private readonly Func<string, bool> _isExpectedUserDataDirectory = isExpectedUserDataDirectory;
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
            List<ConfigurationFileChangePlan> applied = [];
            try
            {
                foreach (ConfigurationFileChangePlan file in plan.Files)
                {
                    if (file.ResultExists)
                    {
                        WriteBytesAtomically(file.FullPath, file.UpdatedContent);
                    }
                    else
                    {
                        File.Delete(file.FullPath);
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
            }
            catch
            {
                RestoreFiles(applied);
                throw;
            }

            return new SettingsOperationResult(
                true,
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

    public bool CanRevertLast(GameInspectionSnapshot snapshot)
    {
        try
        {
            GameEditingGuard.ValidateSnapshot(snapshot, _isExpectedUserDataDirectory);
            return SettingsBackupStore.FindLast(
                snapshot,
                AncestorsGameProfile.SupportedBuildId) is not null;
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
            StoredSettingsOperation? operation = SettingsBackupStore.FindLast(
                snapshot,
                AncestorsGameProfile.SupportedBuildId);
            if (operation is null)
            {
                return Failure("There is no unchanged configurator operation that can be restored safely.");
            }

            List<(ManifestFile File, byte[] Current)> restored = [];
            try
            {
                foreach (ManifestFile file in operation.Manifest.Files)
                {
                    string targetPath = GetTargetPath(
                        operation.Manifest.UserDataDirectory,
                        operation.Manifest.InstallDirectory,
                        file.FileName,
                        file.Target);
                    byte[] current = File.Exists(targetPath) ? File.ReadAllBytes(targetPath) : [];
                    restored.Add((file, current));
                    if (file.Existed)
                    {
                        byte[] original = File.ReadAllBytes(
                            Path.Combine(operation.Directory, file.BackupFileName!));
                        if (!string.Equals(Sha256(original), file.OriginalSha256, StringComparison.Ordinal))
                        {
                            throw new IOException($"The backup for {file.FileName} failed validation.");
                        }

                        WriteBytesAtomically(targetPath, original);
                        if (!string.Equals(
                                Sha256(File.ReadAllBytes(targetPath)),
                                file.OriginalSha256,
                                StringComparison.Ordinal))
                        {
                            throw new IOException($"Validation failed after restoring {file.FileName}.");
                        }
                    }
                    else
                    {
                        File.Delete(targetPath);
                        if (File.Exists(targetPath))
                        {
                            throw new IOException($"Validation failed after removing {file.FileName}.");
                        }
                    }
                }
            }
            catch
            {
                RestoreCurrentFiles(operation, restored);
                throw;
            }

            try
            {
                SettingsBackupStore.MarkReverted(operation.Directory, _utcNow());
            }
            catch (Exception exception) when (IsExpectedWriteException(exception))
            {
                return new SettingsOperationResult(
                    true,
                    $"The configuration was restored, but its history marker could not be written: {exception.Message}");
            }

            return new SettingsOperationResult(
                true,
                "The last configurator change was restored from its backup.");
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

    private static bool MatchesCurrentFile(ConfigurationFileChangePlan file)
    {
        if (File.Exists(file.FullPath) != file.Existed)
        {
            return false;
        }

        byte[] current = file.Existed ? File.ReadAllBytes(file.FullPath) : [];
        return string.Equals(Sha256(current), file.OriginalSha256, StringComparison.Ordinal);
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

    private static SettingsOperationResult Failure(string message) => new(false, message);

    private static bool IsExpectedWriteException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or JsonException or DecoderFallbackException;
}
