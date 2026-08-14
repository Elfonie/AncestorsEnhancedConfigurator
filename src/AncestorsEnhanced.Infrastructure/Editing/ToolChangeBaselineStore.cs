using System.Text.Json;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal static class ToolChangeBaselineStore
{
    private const string ManifestName = "baseline.json";
    private const string FilesDirectoryName = "files";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool CanCreateRemovalPlan(GameInspectionSnapshot snapshot)
    {
        try
        {
            return Read(snapshot) is not null;
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
            byte[] current = exists ? File.ReadAllBytes(path) : [];
            if (exists != file.ToolStateExists || !string.Equals(Sha256(current), file.ToolStateSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{file.FileName} changed outside the Configurator. No tool changes were removed.");
            }

            byte[] original = file.OriginalExists ? ReadOriginal(snapshot.UserDataDirectory!, file) : [];
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

        return new SettingsChangePlan(
            $"remove-tool-changes-{createdAtUtc:yyyyMMddHHmmssfff}",
            createdAtUtc,
            context.BuildId ?? string.Empty,
            context.UserDataDirectory,
            changes,
            files,
            context.InstallDirectory,
            context.ContextFingerprint,
            context.ContentSignature,
            IsToolChangeRemoval: true);
    }

    public static void CaptureBeforeApply(SettingsChangePlan plan)
    {
        if (plan.IsToolChangeRemoval)
        {
            return;
        }

        string root = GetToolChangesRoot(plan.UserDataDirectory);
        ValidateConfigurationPath(plan.UserDataDirectory, root);
        BaselineManifest manifest = Read(plan.UserDataDirectory) ?? new BaselineManifest(
            Version: 1,
            ContextFingerprint: plan.ContextFingerprint ?? string.Empty,
            Files: []);
        if (!string.Equals(manifest.ContextFingerprint, plan.ContextFingerprint ?? string.Empty, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The existing tool-change baseline belongs to a different game context.");
        }

        List<BaselineFile> tracked = [.. manifest.Files];
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
                continue;
            }

            string backupName = file.Existed ? $"{tracked.Count:D3}.before" : string.Empty;
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
                file.OriginalSha256));
        }

        Write(plan.UserDataDirectory, manifest with { Files = tracked });
    }

    public static void MarkApplied(SettingsChangePlan plan)
    {
        if (plan.IsToolChangeRemoval)
        {
            Delete(plan.UserDataDirectory);
            return;
        }

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
            };
        }
        Write(plan.UserDataDirectory, manifest with { Files = files });
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
            if (file.ToolStateExists != reverted.ResultExists ||
                !string.Equals(file.ToolStateSha256, reverted.ResultSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The baseline state for {reverted.FileName} no longer matches the operation being undone.");
            }

            files[index] = file with
            {
                ToolStateExists = reverted.Existed,
                ToolStateSha256 = reverted.OriginalSha256,
            };
        }

        if (files.All(file =>
                file.ToolStateExists == file.OriginalExists &&
                string.Equals(file.ToolStateSha256, file.OriginalSha256, StringComparison.Ordinal)))
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
        BaselineManifest? manifest = JsonSerializer.Deserialize<BaselineManifest>(File.ReadAllText(path), JsonOptions);
        return manifest is { Version: 1 } && manifest.Files.Count > 0 ? manifest : null;
    }

    private static byte[] ReadOriginal(string userDataDirectory, BaselineFile file)
    {
        string path = Path.Combine(GetToolChangesRoot(userDataDirectory), FilesDirectoryName, file.BackupName);
        if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException($"The baseline backup for {file.FileName} is missing.");
        }
        byte[] original = File.ReadAllBytes(path);
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
            Directory.Delete(root, recursive: true);
        }
    }

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
        string ToolStateSha256);
}
