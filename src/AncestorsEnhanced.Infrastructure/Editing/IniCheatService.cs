using System.Text;
using AncestorsEnhanced.Core.Editing;

namespace AncestorsEnhanced.Infrastructure.Editing;

/// <summary>
/// Applies lightweight INI-based tweaks (free camera, developer console) to the
/// user configuration. These are not savegame changes and never touch the save files.
/// The free-camera toggle owns exactly one ConsoleKeys=F10 entry: existing console
/// keys (Tilde, Backslash, custom keybinds) are always preserved.
/// </summary>
public sealed class IniCheatService
{
    private const string InputFileName = "Input.ini";
    private const string InputSettingsSection = "/Script/Engine.InputSettings";
    private const string OwnedKey = "F10";
    private const string OwnershipFileName = "AncestorsEnhanced_FreeCamera.json";

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
        byte[] readBytes = fileExisted ? File.ReadAllBytes(path) : [];
        EncodedTextFile file = fileExisted
            ? EncodedTextFile.Decode(readBytes)
            : new EncodedTextFile(string.Empty, new UTF8Encoding(false), []);

        bool owned = LoadOwnership();
        string updated;
        if (enabled)
        {
            updated = EnableFreeCamera(file.Text, ref owned);
        }
        else
        {
            updated = owned ? DisableFreeCamera(file.Text) : file.Text;
            owned = false;
        }

        if (string.Equals(updated, file.Text, StringComparison.Ordinal))
        {
            return;
        }

        // Never fabricate an otherwise empty Input.ini just to honour a "disabled" toggle.
        if (!fileExisted && string.IsNullOrWhiteSpace(updated))
        {
            return;
        }

        MutationCoordinator.Run(() =>
        {
            if (fileExisted)
            {
                BackupInputIni(path);
            }

            // CAS immediately before the write: the live file must still match the
            // bytes that were read at the start of this operation, so a free-camera
            // toggle can never overwrite changes made by the game or another tool in
            // between (F074).
            ConfigurationFileOperations.CompareAndReplace(
                path,
                file.Encode(updated),
                fileExisted ? ConfigurationFileOperations.Sha256(readBytes) : null,
                fileExisted);
            SaveOwnership(owned);
        });
    }

    /// <summary>
    /// True when this tool previously added ConsoleKeys=F10 and the owned entry
    /// still exists in Input.ini. Never guessed from the INI alone.
    /// </summary>
    public bool IsFreeCameraEnabled()
    {
        if (!LoadOwnership())
        {
            return false;
        }

        string path = GetTargetPath(
            GetConfigurationDirectory(_userDataDirectory),
            InputFileName);
        if (!File.Exists(path))
        {
            return false;
        }

        EncodedTextFile file = EncodedTextFile.Decode(File.ReadAllBytes(path));
        return ContainsExactKey(file.Text, OwnedKey);
    }

    private static string EnableFreeCamera(string text, ref bool owned)
    {
        // Keep every existing console key; only add F10 when no exact F10 entry exists.
        if (ContainsExactKey(text, OwnedKey))
        {
            // F10 already present (owned by the user or previously by us): adding
            // another entry would be a duplicate, so nothing changes.
            owned = false;
            return text;
        }

        string updated = AddConsoleKeyEntry(text, OwnedKey);
        owned = !string.Equals(updated, text, StringComparison.Ordinal);
        return updated;
    }

    /// <summary>
    /// Inserts a fresh "ConsoleKeys=&lt;key&gt;" line inside the Input settings section,
    /// leaving every existing ConsoleKeys line untouched. Comments, blank lines and
    /// indentation outside the inserted line are preserved and the encoding/newline
    /// style of the file is kept.
    /// </summary>
    private static string AddConsoleKeyEntry(string text, string key)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hasFinalNewline = text.EndsWith('\n') || text.EndsWith('\r');
        string[] lines = NormalizeLines(text);
        bool hasTrailingEmpty = hasFinalNewline && lines.Length > 0 && lines[^1].Length == 0;
        if (hasTrailingEmpty)
        {
            lines = lines[..^1];
        }

        string insertion = $"ConsoleKeys={key}";
        int sectionStart = -1;
        int sectionEnd = -1;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string section = line[1..^1].Trim();
                if (string.Equals(section, InputSettingsSection, StringComparison.OrdinalIgnoreCase))
                {
                    sectionStart = index;
                    sectionEnd = -1;
                    continue;
                }

                if (sectionStart >= 0 && sectionEnd < 0)
                {
                    sectionEnd = index;
                    break;
                }
            }
        }

        if (sectionEnd < 0 && sectionStart >= 0)
        {
            sectionEnd = lines.Length;
        }

        List<string> result = [.. lines];
        if (sectionStart >= 0)
        {
            result.Insert(sectionEnd, insertion);
        }
        else
        {
            if (result.Count > 0 && result[^1].Length != 0)
            {
                result.Add(string.Empty);
            }

            result.Add($"[{InputSettingsSection}]");
            result.Add(insertion);
        }

        string joined = string.Join(newline, result);
        return hasFinalNewline ? joined + newline : joined;
    }

    private static string DisableFreeCamera(string text) =>
        RemoveExactKey(text, OwnedKey);

    private static bool ContainsExactKey(string text, string key)
    {
        string currentSection = string.Empty;
        foreach (string sourceLine in NormalizeLines(text))
        {
            string line = sourceLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            if (string.Equals(currentSection, InputSettingsSection, StringComparison.OrdinalIgnoreCase) &&
                TryReadKeyValue(line, out string? parsedKey, out string? parsedValue) &&
                string.Equals(parsedKey, "ConsoleKeys", StringComparison.OrdinalIgnoreCase) &&
                ContainsToken(parsedValue, key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes only entries whose value exactly matches the owned key. Values that
    /// combine several keys (e.g. ConsoleKeys=Tilde,F10) keep every other token.
    /// Comments, blank lines, indentation and unaffected entries are preserved.
    /// </summary>
    private static string RemoveExactKey(string text, string key)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hasFinalNewline = text.EndsWith('\n') || text.EndsWith('\r');
        string[] lines = NormalizeLines(text);
        string currentSection = string.Empty;
        var kept = new List<string>(lines.Length);
        bool removedAny = false;

        foreach (string sourceLine in lines)
        {
            string line = sourceLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                kept.Add(sourceLine);
                continue;
            }

            string? ownedValue = null;
            bool isConsoleKeyEntry = false;
            if (string.Equals(currentSection, InputSettingsSection, StringComparison.OrdinalIgnoreCase) &&
                TryReadKeyValue(line, out string? parsedKey, out string? parsedValue) &&
                string.Equals(parsedKey, "ConsoleKeys", StringComparison.OrdinalIgnoreCase))
            {
                isConsoleKeyEntry = true;
                ownedValue = parsedValue;
            }

            if (!isConsoleKeyEntry || !ContainsToken(ownedValue, key))
            {
                kept.Add(sourceLine);
                continue;
            }

            string[] tokens = ownedValue!.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => token.Length > 0 && !string.Equals(token, key, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (tokens.Length == 0)
            {
                removedAny = true;
                continue;
            }

            string indentation = sourceLine[..(sourceLine.Length - sourceLine.TrimStart().Length)];
            kept.Add($"{indentation}ConsoleKeys={string.Join(',', tokens)}");
            removedAny = true;
        }

        if (!removedAny)
        {
            return text;
        }

        string result = string.Join(newline, kept);
        return hasFinalNewline && kept.Count > 0 ? result + newline : result;
    }

    private static string[] NormalizeLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static bool ContainsToken(string? value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Any(part => string.Equals(part, token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryReadKeyValue(string line, out string key, out string value)
    {
        if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        int separator = line.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = line[..separator].Trim();
        value = line[(separator + 1)..].Trim();
        return key.Length > 0;
    }

    private bool LoadOwnership()
    {
        string path = OwnershipPath();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            OwnershipState? state = System.Text.Json.JsonSerializer.Deserialize<OwnershipState>(
                File.ReadAllBytes(path));
            return state?.FreeCameraF10Owned == true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void SaveOwnership(bool owned)
    {
        string path = OwnershipPath();
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        WriteBytesAtomically(
            path,
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new OwnershipState { FreeCameraF10Owned = owned }));
    }

    private string OwnershipPath() =>
        Path.Combine(_userDataDirectory, OwnershipFileName);

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

    private sealed class OwnershipState
    {
        public bool FreeCameraF10Owned { get; set; }
    }
}
