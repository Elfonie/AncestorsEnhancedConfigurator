using System.Text.RegularExpressions;

namespace AncestorsEnhanced.App.ViewModels;

public static class DiagnosticsReportBuilder
{
    private static readonly Regex WindowsUserDirectory = new(
        @"(?i)([a-z]:\\users\\)[^\\\r\n]+",
        RegexOptions.Compiled);

    public static string Build(
        string productName,
        string version,
        string detectionStatus,
        string installationDetails,
        string installationPath,
        string userDataPath,
        string binarySettingsPath,
        string binarySettingsStatus,
        string systemDetails,
        HardwareDiagnosticsViewModel hardware,
        IReadOnlyList<ConfigurationFileRowViewModel> configurationFiles,
        IReadOnlyList<PakFileRowViewModel> pakFiles,
        IReadOnlyList<NoticeRowViewModel> notices)
    {
        var lines = new List<string>
        {
            productName,
            $"Version: {version}",
            $"Detection: {detectionStatus}",
            $"Installation: {installationDetails}",
            $"Installation path: {RedactPath(installationPath)}",
            $"User data path: {RedactPath(userDataPath)}",
            $"System.sav path: {RedactPath(binarySettingsPath)}",
            $"System.sav status: {binarySettingsStatus}",
            $"System: {systemDetails}",
            $"CPU: {hardware.Cpu}",
            $"Memory: {hardware.Memory}",
            $"GPU and VRAM: {hardware.Graphics}",
            $"Hardware status: {hardware.Status}",
            $"Hardware recommendation: {hardware.Recommendation.PresetName} — {hardware.Recommendation.Description}",
            $"Configuration files ({configurationFiles.Count}):",
        };

        lines.AddRange(configurationFiles.Select(file => $"- {file.Name}: {file.Details} ({file.Status})"));
        lines.Add($"PAK packages ({pakFiles.Count}):");
        lines.AddRange(pakFiles.Select(file => $"- {file.Name}: {file.Details} ({file.Classification})"));
        lines.Add($"Inspection notes ({notices.Count}):");
        lines.AddRange(notices.Select(notice => $"- {notice.Severity}: {RedactText(notice.Message)}"));
        return string.Join(Environment.NewLine, lines);
    }

    public static string RedactPath(string? value) => RedactText(value);

    private static string RedactText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Not available";
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string redacted = value;
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            redacted = redacted.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        return WindowsUserDirectory.Replace(redacted, "$1<user>");
    }
}
