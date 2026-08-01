using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Paks;
using AncestorsEnhanced.Infrastructure.SystemSave;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

public sealed class SafeGameSettingsEditor : IGameSettingsEditor
{
    private const int MaximumIniSizeBytes = 4 * 1024 * 1024;
    private const int MaximumSystemSaveSizeBytes = 1024 * 1024;
    private const string SupportedBuildId = "5495393";

    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<bool> _isGameRunning;
    private readonly Func<string, bool> _isExpectedUserDataDirectory;
    private readonly object _planLock = new();
    private SettingsChangePlan? _issuedPlan;
    private string? _issuedPlanFingerprint;

    public SafeGameSettingsEditor()
        : this(() => DateTimeOffset.UtcNow, IsAncestorsRunning, IsExpectedNativeUserDataDirectory)
    {
    }

    internal SafeGameSettingsEditor(
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning)
        : this(utcNow, isGameRunning, _ => true)
    {
    }

    internal SafeGameSettingsEditor(
        Func<DateTimeOffset> utcNow,
        Func<bool> isGameRunning,
        Func<string, bool> isExpectedUserDataDirectory)
    {
        _utcNow = utcNow;
        _isGameRunning = isGameRunning;
        _isExpectedUserDataDirectory = isExpectedUserDataDirectory;
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

        Dictionary<SettingChangeRequest, SettingFileTarget> targets = requests.ToDictionary(
            request => request,
            request => EditableSettingsCatalog.Create(snapshot, request.Key, null)!.Target);

        SettingChangeRequest[] iniRequests = requests
            .Where(request => targets[request] == SettingFileTarget.Ini)
            .ToArray();
        foreach (IGrouping<string, SettingChangeRequest> fileGroup in iniRequests.GroupBy(
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

        SettingChangeRequest[] systemRequests = requests
            .Where(request => targets[request] == SettingFileTarget.SystemSave)
            .ToArray();
        if (systemRequests.Length > 0)
        {
            string systemSaveDirectory = GetSystemSaveDirectory(userDataDirectory);
            ValidateConfigurationPath(userDataDirectory, systemSaveDirectory);
            string fullPath = GetTargetPath(
                userDataDirectory,
                snapshot.Installation?.InstallDirectory,
                "System.sav",
                SettingFileTarget.SystemSave);
            ValidateWritableTarget(fullPath);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException("System.sav was not found and cannot be created safely.");
            }

            byte[] original = File.ReadAllBytes(fullPath);
            if (original.Length > MaximumSystemSaveSizeBytes)
            {
                throw new InvalidOperationException("System.sav is unexpectedly large and will not be changed.");
            }

            var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SettingChangeRequest change in systemRequests)
            {
                string before = EditableSettingsCatalog.GetCurrentSystemValue(snapshot, change.Key)
                    ?? throw new InvalidOperationException("The current System.sav graphics settings could not be read.");
                string after = change.Value!;
                if (!string.Equals(before, after, StringComparison.Ordinal))
                {
                    previews.Add(new SettingChangePreview(
                        change.DisplayName,
                        "System.sav",
                        change.Key,
                        before,
                        after));
                    changes.Add(change.Key, after);
                }
            }

            if (changes.Count > 0)
            {
                byte[] updated = AncestorsSystemSaveCodec.Apply(original, changes);
                filePlans.Add(new ConfigurationFileChangePlan(
                    "System.sav",
                    fullPath,
                    Existed: true,
                    Sha256(original),
                    original,
                    updated,
                    SettingFileTarget.SystemSave));
            }
        }

        SettingChangeRequest? vignetteRequest = requests.SingleOrDefault(request =>
            targets[request] == SettingFileTarget.Pak);
        if (vignetteRequest is not null)
        {
            ConfigurationFileChangePlan vignette = VignettePakEditor.CreatePlan(
                snapshot,
                vignetteRequest.Value);
            if (vignette.Existed || vignette.ResultExists)
            {
                string before = snapshot.Vignette?.Percent?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ?? "100";
                string after = vignetteRequest.Value ?? "100";
                if (!string.Equals(before, after, StringComparison.Ordinal))
                {
                    previews.Add(new SettingChangePreview(
                        vignetteRequest.DisplayName,
                        vignette.FileName,
                        vignetteRequest.Key,
                        before,
                        after));
                    filePlans.Add(vignette);
                }
            }
        }

        if (filePlans.Count == 0)
        {
            throw new InvalidOperationException("The selected values already match the configuration files.");
        }

        DateTimeOffset createdAt = _utcNow();
        string operationId = $"{createdAt:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..32];
        var plan = new SettingsChangePlan(
            operationId,
            createdAt,
            SupportedBuildId,
            userDataDirectory,
            previews,
            filePlans,
            snapshot.Installation!.InstallDirectory);
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

    public void DiscardPlan(SettingsChangePlan plan)
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
                        if (!string.Equals(
                                Sha256(original),
                                file.OriginalSha256,
                                StringComparison.Ordinal))
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

            return new SettingsOperationResult(true, "The last configurator change was restored from its backup.");
        }
        catch (Exception exception) when (IsExpectedWriteException(exception))
        {
            return Failure($"Nothing was restored: {exception.Message}");
        }
    }

    private void ValidateSnapshot(GameInspectionSnapshot snapshot)
    {
        if (!EditableSettingsCatalog.IsVerifiedEditingTarget(snapshot))
        {
            throw new InvalidOperationException(
                "Editing requires a supported Ancestors installation.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.UserDataDirectory))
        {
            throw new InvalidOperationException("The Ancestors user-data directory was not detected.");
        }

        if (!_isExpectedUserDataDirectory(snapshot.UserDataDirectory))
        {
            throw new InvalidOperationException("The detected user-data directory is not a supported Ancestors location.");
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

        ValidateConfigurationPath(
            plan.UserDataDirectory,
            GetConfigurationDirectory(plan.UserDataDirectory));
        if (plan.Files.Any(file => file.Target == SettingFileTarget.SystemSave))
        {
            ValidateConfigurationPath(
                plan.UserDataDirectory,
                GetSystemSaveDirectory(plan.UserDataDirectory));
        }
        if (plan.Files.Any(file => file.Target == SettingFileTarget.Pak))
        {
            if (string.IsNullOrWhiteSpace(plan.InstallDirectory))
            {
                throw new InvalidOperationException("The game installation directory is missing.");
            }

            ValidateConfigurationPath(
                plan.InstallDirectory,
                GetPakDirectory(plan.InstallDirectory));
        }
        foreach (ConfigurationFileChangePlan file in plan.Files)
        {
            string expectedPath = GetTargetPath(
                plan.UserDataDirectory,
                plan.InstallDirectory,
                file.FileName,
                file.Target);
            if (!string.Equals(expectedPath, Path.GetFullPath(file.FullPath), PathComparison))
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

    private static string Fingerprint(SettingsChangePlan plan) =>
        Sha256(JsonSerializer.SerializeToUtf8Bytes(plan));

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

            bool exists = useOriginal ? file.Existed : file.ResultExists;
            if (exists)
            {
                WriteBytesAtomically(
                    file.FullPath,
                    useOriginal ? file.OriginalContent : file.UpdatedContent);
            }
            else
            {
                File.Delete(file.FullPath);
            }
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

    private static bool IsExpectedNativeUserDataDirectory(string path)
    {
        string localApplicationData = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.LocalApplicationData);
        string fullPath = Path.GetFullPath(path);
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            string expected = Path.GetFullPath(Path.Combine(localApplicationData, "Ancestors", "Saved"));
            if (string.Equals(expected, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string normalized = fullPath.Replace('\\', '/');
        return normalized.Contains("/steamapps/compatdata/536270/pfx/drive_c/users/", StringComparison.Ordinal) &&
               normalized.EndsWith("/AppData/Local/Ancestors/Saved", StringComparison.Ordinal);
    }

    private static bool IsExpectedWriteException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or JsonException or DecoderFallbackException;

}
