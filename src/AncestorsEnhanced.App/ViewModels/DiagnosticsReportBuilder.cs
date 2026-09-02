using System.Text.RegularExpressions;

namespace AncestorsEnhanced.App.ViewModels;

public static class DiagnosticsReportBuilder
{
    private static readonly Regex WindowsUserDirectory = new(
        @"(?i)([a-z]:\\users\\)[^\\\r\n]+",
        RegexOptions.Compiled);

    private static readonly Regex LinuxHomeDirectory = new(
        @"(?m)(/home/)[^/\r\n]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WindowsAbsolutePath = new(
        @"(?<!\S)(?:[a-z]:\\|\\\\)[^\s\""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LinuxAbsolutePath = new(
        @"(?<!\S)/(?:[^\s/]+/)*[^\s]+",
        RegexOptions.Compiled);

    private static readonly Regex SteamUserDataDirectory = new(
        @"(?i)([\\/]userdata[\\/])\d+([\\/])",
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

    public static string RedactPath(string? value)
    {
        string redacted = RedactKnownUserPath(value);
        return string.Equals(redacted, value, StringComparison.Ordinal) &&
               (WindowsAbsolutePath.IsMatch(value ?? string.Empty) || LinuxAbsolutePath.IsMatch(value ?? string.Empty))
            ? "<custom path>"
            : redacted;
    }

    private static string RedactText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Not available";
        }

        string redacted = RedactKnownUserPath(value);
        return LinuxAbsolutePath.Replace(
            WindowsAbsolutePath.Replace(redacted, "<path>"),
            "<path>");
    }

    private static string RedactKnownUserPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Not available";
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string redacted = value;
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            redacted = redacted.Replace(
                userProfile,
                OperatingSystem.IsWindows() ? "%USERPROFILE%" : "%HOME%",
                StringComparison.OrdinalIgnoreCase);
        }

        redacted = SteamUserDataDirectory.Replace(redacted, "$1<steamid>$2");

        return LinuxHomeDirectory.Replace(
            WindowsUserDirectory.Replace(redacted, "$1<user>"),
            "$1<user>");
    }
}
