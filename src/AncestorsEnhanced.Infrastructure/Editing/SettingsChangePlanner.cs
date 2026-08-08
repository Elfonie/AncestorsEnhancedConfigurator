using System.Text;
using AncestorsEnhanced.Core;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using AncestorsEnhanced.Infrastructure.Paks;
using AncestorsEnhanced.Infrastructure.SystemSave;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal sealed class SettingsChangePlanner(
    Func<DateTimeOffset> utcNow,
    Func<string, bool> isExpectedUserDataDirectory)
{
    private const int MaximumIniSizeBytes = 4 * 1024 * 1024;
    private const int MaximumSystemSaveSizeBytes = 1024 * 1024;

    private readonly Func<DateTimeOffset> _utcNow = utcNow;
    private readonly Func<string, bool> _isExpectedUserDataDirectory = isExpectedUserDataDirectory;

    public SettingsChangePlan Create(
        GameInspectionSnapshot snapshot,
        IReadOnlyList<SettingChangeRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(requests);

        GameEditingGuard.ValidateSnapshot(snapshot, _isExpectedUserDataDirectory);
        if (requests.Count == 0)
        {
            throw new InvalidOperationException("There are no pending changes.");
        }

        SettingChangeRequest[] expandedRequests = ExpandCompositeRequests(requests);
        foreach (SettingChangeRequest request in expandedRequests)
        {
            if (!EditableSettingsCatalog.TryValidate(snapshot, request, out string? error))
            {
                throw new InvalidOperationException(error);
            }
        }

        bool hasDuplicates = expandedRequests
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
        var targets = expandedRequests.ToDictionary(
            request => request,
            request => EditableSettingsCatalog.Create(snapshot, request.Key, null)!.Target);

        PlanIniChanges(expandedRequests, targets, configDirectory, filePlans, previews);
        PlanSystemSaveChanges(snapshot, expandedRequests, targets, userDataDirectory, filePlans, previews);
        PlanVignetteChange(snapshot, expandedRequests, targets, filePlans, previews);

        if (filePlans.Count == 0)
        {
            throw new InvalidOperationException("The selected values already match the configuration files.");
        }

        DateTimeOffset createdAt = _utcNow();
        string operationId = $"{createdAt:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..32];
        GameInstallationSnapshot installation = snapshot.Installation!;
        return new SettingsChangePlan(
            operationId,
            createdAt,
            // Record the identity that was actually recognised, never a hard-coded
            // supported claim (F064).
            installation.BuildId ?? string.Empty,
            userDataDirectory,
            previews,
            filePlans,
            installation.InstallDirectory,
            installation.ContentSignature);
    }

    private static SettingChangeRequest[] ExpandCompositeRequests(
        IReadOnlyList<SettingChangeRequest> requests)
    {
        List<SettingChangeRequest> expanded = [.. requests];
        SettingChangeRequest? startupMovies = requests.SingleOrDefault(request =>
            string.Equals(request.FileName, "Game.ini", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.Key, "!StartupMovies", StringComparison.OrdinalIgnoreCase));
        if (startupMovies is not null)
        {
            expanded.Add(startupMovies with
            {
                Key = "bWaitForMoviesToComplete",
                Value = startupMovies.Value is null ? null : "False",
            });
        }

        return [.. expanded];
    }

    private static void PlanIniChanges(
        IReadOnlyList<SettingChangeRequest> requests,
        Dictionary<SettingChangeRequest, SettingFileTarget> targets,
        string configDirectory,
        List<ConfigurationFileChangePlan> filePlans,
        List<SettingChangePreview> previews)
    {
        foreach (IGrouping<string, SettingChangeRequest> fileGroup in requests
                     .Where(request => targets[request] == SettingFileTarget.Ini)
                     .GroupBy(request => request.FileName, StringComparer.OrdinalIgnoreCase))
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
            SettingChangeRequest[] changes = [.. fileGroup];
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

            byte[] updated = textFile.Encode(IniDocumentEditor.Apply(textFile.Text, changes));
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
    }

    private static void PlanSystemSaveChanges(
        GameInspectionSnapshot snapshot,
        IReadOnlyList<SettingChangeRequest> requests,
        Dictionary<SettingChangeRequest, SettingFileTarget> targets,
        string userDataDirectory,
        List<ConfigurationFileChangePlan> filePlans,
        List<SettingChangePreview> previews)
    {
        SettingChangeRequest[] systemRequests = [.. requests.Where(request => targets[request] == SettingFileTarget.SystemSave)];
        if (systemRequests.Length == 0)
        {
            return;
        }

        ValidateConfigurationPath(userDataDirectory, GetSystemSaveDirectory(userDataDirectory));
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

        if (changes.Count == 0)
        {
            return;
        }

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

    private static void PlanVignetteChange(
        GameInspectionSnapshot snapshot,
        IReadOnlyList<SettingChangeRequest> requests,
        Dictionary<SettingChangeRequest, SettingFileTarget> targets,
        List<ConfigurationFileChangePlan> filePlans,
        List<SettingChangePreview> previews)
    {
        SettingChangeRequest? request = requests.SingleOrDefault(candidate =>
            targets[candidate] == SettingFileTarget.Pak);
        if (request is null)
        {
            return;
        }

        ConfigurationFileChangePlan vignette = VignettePakEditor.CreatePlan(snapshot, request.Value);
        if (!vignette.Existed && !vignette.ResultExists)
        {
            return;
        }

        string before = snapshot.Vignette?.Percent?.ToString(
            System.Globalization.CultureInfo.InvariantCulture) ?? "100";
        string after = request.Value ?? "100";
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            previews.Add(new SettingChangePreview(
                request.DisplayName,
                vignette.FileName,
                request.Key,
                before,
                after));
            filePlans.Add(vignette);
        }
    }
}
