using AncestorsEnhanced.Core.Editing;

namespace AncestorsEnhanced.Infrastructure.Editing;

/// <summary>
/// Applies lightweight INI-based tweaks (free camera, developer console) to the
/// user configuration. These are not savegame changes and never touch the save files.
/// </summary>
public sealed class IniCheatService
{
    private const string InputFileName = "Input.ini";
    private const string InputSettingsSection = "/Script/Engine.InputSettings";

    private readonly string _userDataDirectory;

    public IniCheatService(string userDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(userDataDirectory);
        _userDataDirectory = userDataDirectory;
    }

    /// <summary>Enables or disables the UE4 debug free camera bound to F10.</summary>
    public void SetFreeCamera(bool enabled)
    {
        string path = GetTargetPath(
            GetConfigurationDirectory(_userDataDirectory),
            InputFileName);

        bool fileExisted = File.Exists(path);
        EncodedTextFile file = fileExisted
            ? EncodedTextFile.Decode(File.ReadAllBytes(path))
            : new EncodedTextFile(string.Empty, new System.Text.UTF8Encoding(false), []);

        var change = new SettingChangeRequest(
            "Free camera",
            InputFileName,
            InputSettingsSection,
            "ConsoleKeys",
            enabled ? "F10" : null);
        string updated = IniDocumentEditor.Apply(file.Text, [change]);

        if (string.Equals(updated, file.Text, StringComparison.Ordinal))
        {
            return;
        }

        // Never fabricate an otherwise empty Input.ini just to honour a "disabled" toggle.
        if (!fileExisted && string.IsNullOrWhiteSpace(updated))
        {
            return;
        }

        if (fileExisted)
        {
            BackupInputIni(path);
        }

        WriteBytesAtomically(path, file.Encode(updated));
    }

    private void BackupInputIni(string path)
    {
        string backupDirectory = GetBackupRoot(_userDataDirectory);
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(
            backupDirectory,
            $"{InputFileName}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.before");
        WriteBytesAtomically(backupPath, File.ReadAllBytes(path));
    }

    private static string GetBackupRoot(string userDataDirectory) =>
        ConfigurationFileOperations.GetBackupRoot(userDataDirectory);

    private static string GetConfigurationDirectory(string userDataDirectory) =>
        ConfigurationFileOperations.GetConfigurationDirectory(userDataDirectory);

    private static string GetTargetPath(string configurationDirectory, string fileName) =>
        ConfigurationFileOperations.GetTargetPath(configurationDirectory, fileName);

    private static void WriteBytesAtomically(string path, byte[] content) =>
        ConfigurationFileOperations.WriteBytesAtomically(path, content);
}
