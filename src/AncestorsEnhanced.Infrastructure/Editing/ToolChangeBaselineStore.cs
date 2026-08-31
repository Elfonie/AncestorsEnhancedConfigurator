using System.Text.Json;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal static class ToolChangeBaselineStore
{
    private const string ManifestName = "baseline.json";
    private const string FilesDirectoryName = "files";
    private const int ManifestVersion = 3;
    private const int MaximumManifestSize = 1024 * 1024;
    // Keep the reader and writer bound identical.  The old reader-only limit could
    // silently discard a valid baseline after a larger apply operation.
    private const int MaximumTrackedFiles = 64;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool CanCreateRemovalPlan(GameInspectionSnapshot snapshot)
    {
        try
        {
            _ = CreateRemovalPlan(snapshot, DateTimeOffset.UtcNow);
            return true;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return false;
        }
    }

    public static SettingsChangePlan CreateRemovalPlan(
        GameInspectionSnapshot snapshot,
        DateTimeOffset createdAtUtc)
    {
        VerifiedGameContext context = VerifiedGameContext.TryCreateFromSnapshot(snapshot)
            ?? throw new InvalidOperationException("The game context cannot be verified.");
        BaselineManifest manifest = Read(snapshot)
            ?? throw new InvalidOperationException("No tool-change baseline was found.");
        if (!string.Equals(manifest.ContextFingerprint, context.ContextFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The baseline belongs to a different game installation or save location.");
        }

        List<ConfigurationFileChangePlan> files = [];
        List<SettingChangePreview> changes = [];
        foreach (BaselineFile file in manifest.Files)
        {
            string path = GetTargetPath(
                context.UserDataDirectory,
                context.InstallDirectory,
                file.FileName,
                file.Target);
            bool exists = File.Exists(path);
            byte[] current = exists ? ReadStableBounded(path, 64L * 1024 * 1024) : [];
            if (exists != file.ToolStateExists || !string.Equals(Sha256(current), file.ToolStateSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{file.FileName} changed outside the Configurator. No tool changes were removed.");
            }

            byte[] original = file.OriginalExists ? ReadOriginal(snapshot.UserDataDirectory!, file) : [];
            if (exists == file.OriginalExists &&
                string.Equals(Sha256(current), file.OriginalSha256, StringComparison.Ordinal))
            {
                continue;
            }
            files.Add(new ConfigurationFileChangePlan(
                file.FileName,
                path,
                exists,
                Sha256(current),
                current,
                original,
                file.Target,
                file.OriginalExists));
            changes.Add(new SettingChangePreview(
                $"Remove tool changes from {file.FileName}",
                file.FileName,
                "Tool baseline",
                "Tool-managed state",
                file.OriginalExists ? "Original state" : "Remove file created by tool"));
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException("No active tool changes were found.");
        }

        return new SettingsChangePlan(
            $"remove-tool-changes-{createdAtUtc:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}",
            createdAtUtc,
            context.BuildId ?? string.Empty,
            context.UserDataDirectory,
            changes,
            files,
            context.InstallDirectory,
            context.ContextFingerprint,
            context.ContentSignature,
            IsToolChangeRemoval: true,
            Store: context.Store);
    }

    public static BaselineCapture CaptureBeforeApply(SettingsChangePlan plan)
    {
        string root = GetToolChangesRoot(plan.UserDataDirectory);
        ValidateConfigurationPath(plan.UserDataDirectory, root);
        BaselineManifest? existingManifest = Read(plan.UserDataDirectory);
        if (plan.IsToolChangeRemoval && existingManifest is null)
        {
            throw new IOException("The tool-change baseline disappeared before removal.");
        }

        BaselineManifest manifest = existingManifest ?? new BaselineManifest(
            Version: ManifestVersion,
            ContextFingerprint: plan.ContextFingerprint ?? string.Empty,
            Files: []);
        if (manifest.Version == 1)
        {
            manifest = MigrateLegacy(plan.UserDataDirectory, manifest);
        }
        if (!string.Equals(manifest.ContextFingerprint, plan.ContextFingerprint ?? string.Empty, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The existing tool-change baseline belongs to a different game context.");
        }
        if (plan.IsToolChangeRemoval)
        {
            return BaselineCapture.Empty;
        }

        List<BaselineFile> tracked = [.. manifest.Files];
        var introducedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int newFiles = plan.Files.Count(file => !tracked.Any(candidate =>
            candidate.Target == file.Target &&
            string.Equals(candidate.FileName, file.FileName, StringComparison.OrdinalIgnoreCase)));
        if (tracked.Count + newFiles > MaximumTrackedFiles)
        {
            throw new InvalidOperationException(
                $"A tool-change baseline may track at most {MaximumTrackedFiles} files.");
        }

        foreach (ConfigurationFileChangePlan file in plan.Files)
        {
            BaselineFile? existing = tracked.SingleOrDefault(candidate =>
                candidate.Target == file.Target &&
                string.Equals(candidate.FileName, file.FileName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (existing.ToolStateExists != file.Existed ||
                    !string.Equals(existing.ToolStateSha256, file.OriginalSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{file.FileName} changed outside the Configurator. Refresh and resolve that change before editing it again.");
                }
                if (existing.OriginalExists)
                {
                    _ = ReadOriginal(plan.UserDataDirectory, existing);
                }
                continue;
            }

            string backupName = file.Existed ? GetBackupName(file.Target, file.FileName) : string.Empty;
            if (file.Existed)
            {
                Directory.CreateDirectory(Path.Combine(root, FilesDirectoryName));
                WriteBytesAtomically(Path.Combine(root, FilesDirectoryName, backupName), file.OriginalContent);
            }
            tracked.Add(new BaselineFile(
                file.FileName,
                file.Target,
                file.Existed,
                file.OriginalSha256,
                backupName,
                file.Existed,
                file.OriginalSha256,
                IsProvisional: true));
            introducedKeys.Add(KeyFor(file.Target, file.FileName));
        }

        Write(plan.UserDataDirectory, manifest with { Version = ManifestVersion, Files = tracked });
        return new BaselineCapture(introducedKeys);
    }

    public static void MarkApplied(SettingsChangePlan plan)
    {
        BaselineManifest manifest = Read(plan.UserDataDirectory)
            ?? throw new IOException("The tool-change baseline disappeared during the update.");
        List<BaselineFile> files = [.. manifest.Files];
        foreach (ConfigurationFileChangePlan change in plan.Files)
        {
            int index = files.FindIndex(file => file.Target == change.Target &&
                string.Equals(file.FileName, change.FileName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new IOException($"The baseline is missing {change.FileName}.");
            }
            BaselineFile file = files[index];
            files[index] = file with
            {
                ToolStateExists = change.ResultExists,
                ToolStateSha256 = Sha256(change.UpdatedContent),
                IsProvisional = false,
            };
        }
        Write(plan.UserDataDirectory, manifest with { Version = ManifestVersion, Files = files });
    }

    public static void RollbackApplied(SettingsChangePlan plan, BaselineCapture? capture = null)
    {
        BaselineManifest? manifest = Read(plan.UserDataDirectory);
        if (manifest is null)
        {
            return;
        }

        List<BaselineFile> files = [.. manifest.Files];
        foreach (ConfigurationFileChangePlan change in plan.Files)
        {
            int index = files.FindIndex(file => file.Target == change.Target &&
                string.Equals(file.FileName, change.FileName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            if (capture?.IntroducedKeys.Contains(KeyFor(change.Target, change.FileName)) == true ||
                files[index].IsProvisional)
            {
                files.RemoveAll(file => file.Target == change.Target &&
                    string.Equals(file.FileName, change.FileName, StringComparison.OrdinalIgnoreCase));
                continue;
            }

            files[index] = files[index] with
            {
                ToolStateExists = change.Existed,
                ToolStateSha256 = change.OriginalSha256,
            };
        }

        if (files.All(IsAtOriginalState))
        {
            Delete(plan.UserDataDirectory);
        }
        else
        {
            Write(plan.UserDataDirectory, manifest with { Files = files });
        }
    }

    public static void RollbackInterrupted(
        VerifiedGameContext context,
        OperationManifest operation)
    {
        BaselineManifest? manifest = Read(context.UserDataDirectory);
        if (manifest is null)
        {
            return;
        }
        if (!string.Equals(manifest.ContextFingerprint, context.ContextFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The tool-change baseline belongs to a different game context.");
        }

        List<BaselineFile> files = [.. manifest.Files];
        foreach (ManifestFile interrupted in operation.Files)
        {
            int index = files.FindIndex(file => file.Target == interrupted.Target &&
                string.Equals(file.FileName, interrupted.FileName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            BaselineFile file = files[index];
            bool capturedState = file.ToolStateExists == interrupted.Existed &&
                string.Equals(file.ToolStateSha256, interrupted.OriginalSha256, StringComparison.Ordinal);
            bool appliedState = file.ToolStateExists == interrupted.ResultExists &&
                string.Equals(file.ToolStateSha256, interrupted.ResultSha256, StringComparison.Ordinal);
            if (!capturedState && !appliedState)
            {
                throw new InvalidOperationException(
                    $"The baseline state for {interrupted.FileName} does not match the interrupted operation.");
            }
            files[index] = file with
            {
                ToolStateExists = interrupted.Existed,
                ToolStateSha256 = interrupted.OriginalSha256,
            };
        }

        // Capture happens before target mutation. An interrupted operation may
        // therefore have durable records for files AEC never successfully owned.
        // Recovery restored the target state above, so remove those provisional
        // records instead of carrying false ownership into later sessions.
        files.RemoveAll(file => file.IsProvisional && operation.Files.Any(interrupted =>
            interrupted.Target == file.Target &&
            string.Equals(interrupted.FileName, file.FileName, StringComparison.OrdinalIgnoreCase)));

        if (files.All(IsAtOriginalState))
        {
            Delete(context.UserDataDirectory);
        }
        else
        {
            Write(context.UserDataDirectory, manifest with { Files = files });
        }
    }

    public static void MarkReverted(VerifiedGameContext context, OperationManifest operation)
    {
        BaselineManifest? manifest = Read(context.UserDataDirectory);
        if (manifest is null)
        {
            return;
        }

        if (!string.Equals(manifest.ContextFingerprint, context.ContextFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The tool-change baseline belongs to a different game context.");
        }

        List<BaselineFile> files = [.. manifest.Files];
        foreach (ManifestFile reverted in operation.Files)
        {
            int index = files.FindIndex(file => file.Target == reverted.Target &&
                string.Equals(file.FileName, reverted.FileName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            BaselineFile file = files[index];
            bool stillApplied = file.ToolStateExists == reverted.ResultExists &&
                string.Equals(file.ToolStateSha256, reverted.ResultSha256, StringComparison.Ordinal);
            bool alreadyReverted = file.ToolStateExists == reverted.Existed &&
                string.Equals(file.ToolStateSha256, reverted.OriginalSha256, StringComparison.Ordinal);
            if (!stillApplied && !alreadyReverted)
            {
                throw new InvalidOperationException(
                    $"The baseline state for {reverted.FileName} no longer matches the operation being undone.");
            }

            if (alreadyReverted)
            {
                continue;
            }

            files[index] = file with
            {
                ToolStateExists = reverted.Existed,
                ToolStateSha256 = reverted.OriginalSha256,
            };
        }

        if (files.All(IsAtOriginalState))
        {
            Delete(context.UserDataDirectory);
            return;
        }

        Write(context.UserDataDirectory, manifest with { Files = files });
    }

    private static BaselineManifest? Read(GameInspectionSnapshot snapshot)
    {
        string? userData = snapshot.UserDataDirectory;
        return userData is null ? null : Read(userData);
    }

    private static BaselineManifest? Read(string userDataDirectory)
    {
        string root = GetToolChangesRoot(userDataDirectory);
        string path = Path.Combine(root, ManifestName);
        if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            return null;
        }
        BaselineManifest? manifest = JsonSerializer.Deserialize<BaselineManifest>(
            ReadStableBounded(path, MaximumManifestSize), JsonOptions);
        return manifest is not null && IsValid(manifest) ? manifest : null;
    }

    private static byte[] ReadOriginal(string userDataDirectory, BaselineFile file)
    {
        string filesRoot = Path.GetFullPath(Path.Combine(GetToolChangesRoot(userDataDirectory), FilesDirectoryName));
        string backupName = file.BackupName.Length == 10 &&
            file.BackupName.EndsWith(".before", StringComparison.Ordinal) &&
            file.BackupName.AsSpan(0, 3).ToArray().All(char.IsAsciiDigit)
                ? file.BackupName
                : GetBackupName(file.Target, file.FileName);
        string path = Path.GetFullPath(Path.Combine(filesRoot, backupName));
        if (!string.Equals(Path.GetDirectoryName(path), filesRoot, PathComparison))
        {
            throw new IOException($"The baseline backup path for {file.FileName} is invalid.");
        }
        if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException($"The baseline backup for {file.FileName} is missing.");
        }
        byte[] original = ReadStableBounded(path, 64L * 1024 * 1024);
        if (!string.Equals(Sha256(original), file.OriginalSha256, StringComparison.Ordinal))
        {
            throw new IOException($"The baseline backup for {file.FileName} failed validation.");
        }
        return original;
    }

    private static void Write(string userDataDirectory, BaselineManifest manifest)
    {
        string root = GetToolChangesRoot(userDataDirectory);
        Directory.CreateDirectory(root);
        WriteBytesAtomically(
            Path.Combine(root, ManifestName),
            JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
    }

    private static void Delete(string userDataDirectory)
    {
        string root = GetToolChangesRoot(userDataDirectory);
        ValidateConfigurationPath(userDataDirectory, root);
        if (Directory.Exists(root) && !File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
        {
            DeleteDirectorySafely(userDataDirectory, root);
        }
    }

    private static bool IsAtOriginalState(BaselineFile file) =>
        file.ToolStateExists == file.OriginalExists &&
        string.Equals(file.ToolStateSha256, file.OriginalSha256, StringComparison.Ordinal);

    private static BaselineManifest MigrateLegacy(string userDataDirectory, BaselineManifest manifest)
    {
        string filesRoot = Path.Combine(GetToolChangesRoot(userDataDirectory), FilesDirectoryName);
        List<BaselineFile> files = [];
        foreach (BaselineFile file in manifest.Files)
        {
            if (!file.OriginalExists)
            {
                files.Add(file with { BackupName = string.Empty });
                continue;
            }

            byte[] original = ReadOriginal(userDataDirectory, file);
            string backupName = GetBackupName(file.Target, file.FileName);
            WriteBytesAtomically(Path.Combine(filesRoot, backupName), original);
            files.Add(file with { BackupName = backupName });
        }

        BaselineManifest migrated = manifest with { Version = ManifestVersion, Files = files };
        Write(userDataDirectory, migrated);
        return migrated;
    }

    private static string GetBackupName(SettingFileTarget target, string fileName)
    {
        ValidateTargetFileName(target, fileName);
        return $"{(int)target}-{fileName}.before";
    }

    private static string KeyFor(SettingFileTarget target, string fileName) =>
        $"{(int)target}:{fileName}";

    private static bool IsValid(BaselineManifest manifest)
    {
        if (manifest.Version is not (1 or 2 or ManifestVersion) ||
            string.IsNullOrWhiteSpace(manifest.ContextFingerprint) ||
            manifest.ContextFingerprint.Length > 256 ||
            manifest.Files.Count is < 1 or > MaximumTrackedFiles)
        {
            return false;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BaselineFile file in manifest.Files)
        {
            try
            {
                ValidateTargetFileName(file.Target, file.FileName);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (!keys.Add($"{(int)file.Target}:{file.FileName}") ||
                !IsSha256(file.OriginalSha256) ||
                !IsSha256(file.ToolStateSha256) ||
                (file.OriginalExists && manifest.Version == ManifestVersion && !string.Equals(
                    file.BackupName, GetBackupName(file.Target, file.FileName), StringComparison.Ordinal)) ||
                (file.OriginalExists && manifest.Version == 1 && !IsLegacyBackupName(file.BackupName)) ||
                (!file.OriginalExists && !string.IsNullOrEmpty(file.BackupName)))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateTargetFileName(SettingFileTarget target, string fileName)
    {
        switch (target)
        {
            case SettingFileTarget.Ini:
                ValidateFileName(fileName);
                break;
            case SettingFileTarget.Pak:
                ValidatePakFileName(fileName);
                break;
            case SettingFileTarget.SystemSave:
                ValidateSystemSaveFileName(fileName);
                break;
            default:
                throw new InvalidOperationException("The baseline target is invalid.");
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));

    private static bool IsLegacyBackupName(string value) =>
        value.Length == 10 &&
        value.EndsWith(".before", StringComparison.Ordinal) &&
        value.AsSpan(0, 3).ToArray().All(char.IsAsciiDigit);

    private static bool IsExpected(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or JsonException;

    private sealed record BaselineManifest(int Version, string ContextFingerprint, List<BaselineFile> Files);

    private sealed record BaselineFile(
        string FileName,
        SettingFileTarget Target,
        bool OriginalExists,
        string OriginalSha256,
        string BackupName,
        bool ToolStateExists,
        string ToolStateSha256,
        bool IsProvisional = false);

    internal sealed record BaselineCapture(IReadOnlySet<string> IntroducedKeys)
    {
        public static BaselineCapture Empty { get; } = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }
}
