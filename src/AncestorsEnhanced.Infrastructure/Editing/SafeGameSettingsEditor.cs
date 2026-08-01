using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

public sealed class SafeGameSettingsEditor : IGameSettingsEditor
{
    private const int MaximumIniSizeBytes = 4 * 1024 * 1024;
    private const string SupportedBuildId = "5495393";

    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<bool> _isGameRunning;

    public SafeGameSettingsEditor()
        : this(() => DateTimeOffset.UtcNow, IsAncestorsRunning)
    {
    }

    internal SafeGameSettingsEditor(
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning)
    {
        _utcNow = utcNow;
        _isGameRunning = isGameRunning;
    }

    public SettingsChangePlan CreatePlan(
        GameInspectionSnapshot snapshot,
        IReadOnlyList<SettingChangeRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(requests);

        ValidateSnapshot(snapshot);
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("There are no pending changes.");
        }

        foreach (SettingChangeRequest request in requests)
        {
            if (!EditableSettingsCatalog.TryValidate(snapshot, request, out string? error))
            {
                throw new InvalidOperationException(error);
            }
        }

        bool hasDuplicates = requests
            .GroupBy(
                request => $"{request.FileName}\0{request.Section}\0{request.Key}",
                StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);
        if (hasDuplicates)
        {
            throw new InvalidOperationException("The same setting cannot appear twice in one operation.");
        }

        string userDataDirectory = Path.GetFullPath(snapshot.UserDataDirectory!);
        string configDirectory = GetConfigurationDirectory(userDataDirectory);
        ValidateConfigurationPath(userDataDirectory, configDirectory);

        List<ConfigurationFileChangePlan> filePlans = [];
        List<SettingChangePreview> previews = [];

        foreach (IGrouping<string, SettingChangeRequest> fileGroup in requests.GroupBy(
                     request => request.FileName,
                     StringComparer.OrdinalIgnoreCase))
        {
            string fileName = fileGroup.Key;
            ValidateFileName(fileName);
            string fullPath = GetTargetPath(configDirectory, fileName);
            ValidateWritableTarget(fullPath);

            bool existed = File.Exists(fullPath);
            byte[] original = existed ? File.ReadAllBytes(fullPath) : [];
            if (original.Length > MaximumIniSizeBytes)
            {
                throw new InvalidOperationException($"{fileName} is unexpectedly large and will not be changed.");
            }

            EncodedTextFile textFile = original.Length == 0
                ? new EncodedTextFile(string.Empty, new UTF8Encoding(false, true), [])
                : EncodedTextFile.Decode(original);
            SettingChangeRequest[] changes = fileGroup.ToArray();

            foreach (SettingChangeRequest change in changes)
            {
                string? before = IniDocumentEditor.FindLastValue(
                    textFile.Text,
                    change.Section,
                    change.Key);
                if (!string.Equals(before, change.Value, StringComparison.Ordinal))
                {
                    previews.Add(new SettingChangePreview(
                        change.DisplayName,
                        fileName,
                        change.Key,
                        before,
                        change.Value));
                }
            }

            string updatedText = IniDocumentEditor.Apply(textFile.Text, changes);
            byte[] updated = textFile.Encode(updatedText);
            if (!original.AsSpan().SequenceEqual(updated))
            {
                filePlans.Add(new ConfigurationFileChangePlan(
                    fileName,
                    fullPath,
                    existed,
                    Sha256(original),
                    original,
                    updated));
            }
        }

        if (filePlans.Count == 0)
        {
            throw new InvalidOperationException("The selected values already match the configuration files.");
        }

        DateTimeOffset createdAt = _utcNow();
        string operationId = $"{createdAt:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..32];
        return new SettingsChangePlan(
            operationId,
            createdAt,
            SupportedBuildId,
            userDataDirectory,
            previews,
            filePlans);
    }

    public SettingsOperationResult Apply(SettingsChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (_isGameRunning())
        {
            return Failure("Close Ancestors before applying configuration changes.");
        }

        try
        {
            ValidatePlan(plan);
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
                    WriteBytesAtomically(file.FullPath, file.UpdatedContent);
                    if (!string.Equals(
                            Sha256(File.ReadAllBytes(file.FullPath)),
                            Sha256(file.UpdatedContent),
                            StringComparison.Ordinal))
                    {
                        throw new IOException($"Validation failed after writing {file.FileName}.");
                    }

                    applied.Add(file);
                }

                SettingsBackupStore.MarkApplied(operationDirectory, plan.CreatedAtUtc);
            }
            catch
            {
                RestoreFiles(applied, useOriginal: true);
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

    public bool CanRevertLast(GameInspectionSnapshot snapshot)
    {
        try
        {
            ValidateSnapshot(snapshot);
            return SettingsBackupStore.FindLast(snapshot, SupportedBuildId) is not null;
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
            ValidateSnapshot(snapshot);
            StoredSettingsOperation? operation = SettingsBackupStore.FindLast(
                snapshot,
                SupportedBuildId);
            if (operation is null)
            {
                return Failure("There is no unchanged 0.3 operation that can be restored safely.");
            }

            List<(ManifestFile File, byte[] Current)> restored = [];
            try
            {
                foreach (ManifestFile file in operation.Manifest.Files)
                {
                    string targetPath = GetTargetPath(
                        GetConfigurationDirectory(operation.Manifest.UserDataDirectory),
                        file.FileName);
                    byte[] current = File.ReadAllBytes(targetPath);
                    if (file.Existed)
                    {
                        byte[] original = File.ReadAllBytes(
                            Path.Combine(operation.Directory, file.BackupFileName!));
                        WriteBytesAtomically(targetPath, original);
                    }
                    else
                    {
                        File.Delete(targetPath);
                    }

                    restored.Add((file, current));
                }
            }
            catch
            {
                foreach ((ManifestFile file, byte[] current) in restored)
                {
                    string targetPath = GetTargetPath(
                        GetConfigurationDirectory(operation.Manifest.UserDataDirectory),
                        file.FileName);
                    WriteBytesAtomically(targetPath, current);
                }

                throw;
            }

            SettingsBackupStore.MarkReverted(operation.Directory, _utcNow());

            return new SettingsOperationResult(true, "The last configurator change was restored from its backup.");
        }
        catch (Exception exception) when (IsExpectedWriteException(exception))
        {
            return Failure($"Nothing was restored: {exception.Message}");
        }
    }

    private static void ValidateSnapshot(GameInspectionSnapshot snapshot)
    {
        if (!string.Equals(snapshot.Installation?.BuildId, SupportedBuildId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Editing is only enabled for verified build 5495393.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.UserDataDirectory))
        {
            throw new InvalidOperationException("The Ancestors user-data directory was not detected.");
        }
    }

    private static void ValidatePlan(SettingsChangePlan plan)
    {
        if (!string.Equals(plan.BuildId, SupportedBuildId, StringComparison.Ordinal) ||
            plan.Files.Count == 0 ||
            string.IsNullOrWhiteSpace(plan.UserDataDirectory))
        {
            throw new InvalidOperationException("The change plan is not valid for this release.");
        }

        string configDirectory = GetConfigurationDirectory(plan.UserDataDirectory);
        ValidateConfigurationPath(plan.UserDataDirectory, configDirectory);
        foreach (ConfigurationFileChangePlan file in plan.Files)
        {
            ValidateFileName(file.FileName);
            string expectedPath = GetTargetPath(configDirectory, file.FileName);
            if (!string.Equals(expectedPath, Path.GetFullPath(file.FullPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The change plan contains an unexpected target path.");
            }

            ValidateWritableTarget(expectedPath);
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

    private static void RestoreFiles(
        IEnumerable<ConfigurationFileChangePlan> files,
        bool useOriginal)
    {
        foreach (ConfigurationFileChangePlan file in files.Reverse())
        {
            if (!file.Existed)
            {
                File.Delete(file.FullPath);
                continue;
            }

            WriteBytesAtomically(
                file.FullPath,
                useOriginal ? file.OriginalContent : file.UpdatedContent);
        }
    }

    private static SettingsOperationResult Failure(string message) => new(false, message);

    private static bool IsAncestorsRunning()
    {
        try
        {
            return Process.GetProcessesByName("Ancestors-Win64-Shipping").Length > 0 ||
                   Process.GetProcessesByName("Ancestors").Length > 0;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool IsExpectedWriteException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or JsonException or DecoderFallbackException;

}
