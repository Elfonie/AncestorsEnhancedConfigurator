using System.Text;
using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Core.Inspection;
using static AncestorsEnhanced.Infrastructure.Editing.ConfigurationFileOperations;

namespace AncestorsEnhanced.Infrastructure.Editing;

/// <summary>
/// Applies lightweight INI-based tweaks (free camera, developer console) to the user
/// configuration. These are not savegame changes and never touch the save files.
/// The free-camera toggle owns exactly one <c>ConsoleKeys=F10</c> entry, and that
/// ownership is proven by the input file itself: the tool writes a unique marker line
/// directly above the entry it added, so the INI, not a side JSON file, records which
/// entry belongs to the tool (F012/F075). Existing user console keys are preserved.
/// </summary>
public sealed class IniCheatService
{
    private const string InputFileName = "Input.ini";
    private const string InputSettingsSection = "/Script/Engine.InputSettings";
    private const string OwnedKey = "F10";
    private const string OwnedEntryLine = "ConsoleKeys=F10";
    private const string OwnershipMarker = "; AncestorsEnhanced:FreeCamera:F10";
    // Legacy side-file ownership is never used to authorise a change; it is only
    // cleaned up so it cannot cause confusion (F012/F075).
    private const string LegacyOwnershipFileName = "AncestorsEnhanced_FreeCamera.json";

    private readonly string _userDataDirectory;
    private readonly Func<bool>? _revalidate;

    /// <summary>Binds to a verified game context; the user-data path comes from the context (F078).</summary>
    public IniCheatService(VerifiedGameContext context, GameContextVerifier verifier)
        : this(context.UserDataDirectory, () => verifier.Verify(context))
    {
    }

    public IniCheatService(string userDataDirectory, Func<bool>? revalidate = null)
    {
        ArgumentNullException.ThrowIfNull(userDataDirectory);
        _userDataDirectory = userDataDirectory;
        _revalidate = revalidate;
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

        string updated = enabled
            ? EnableFreeCamera(file.Text)
            : DisableFreeCamera(file.Text);

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
            // Revalidate inside the global mutation lock, immediately before the write (F063-1c).
            if (_revalidate is not null && !_revalidate())
            {
                throw new InvalidOperationException("The game context changed; the free camera cannot be toggled safely. Refresh and try again.");
            }
            if (fileExisted)
            {
                BackupInputIni(path);
            }

            // CAS immediately before the write: the live file must still match the
            // bytes that were read at the start of this operation, so a free-camera
            // toggle can never overwrite changes made by the game or another tool in
            // between (F074).
            CompareAndReplace(
                path,
                file.Encode(updated),
                fileExisted ? Sha256(readBytes) : null,
                fileExisted);
            RemoveLegacyOwnershipFile();

            // F147: read the file back and confirm the desired semantic state
            // actually landed. A CAS-accepted write that does not produce the
            // expected ownership state must surface as an error, not pass silently.
            if (File.Exists(path))
            {
                string readback = EncodedTextFile.Decode(File.ReadAllBytes(path)).Text;
                if (HasOwnedPair(readback) != enabled)
                {
                    throw new IOException("The free-camera write did not verify; the Input.ini state is unexpected.");
                }
            }
        });
    }

    /// <summary>
    /// True only when the tool-owned marker + ConsoleKeys entry pair is present in
    /// Input.ini. Never guessed from the INI alone (the pair must be intact).
    /// </summary>
    public bool IsFreeCameraEnabled()
    {
        string path = GetTargetPath(
            GetConfigurationDirectory(_userDataDirectory),
            InputFileName);
        if (!File.Exists(path))
        {
            return false;
        }

        EncodedTextFile file = EncodedTextFile.Decode(File.ReadAllBytes(path));
        return HasOwnedPair(file.Text);
    }

    private static string EnableFreeCamera(string text)
    {
        if (HasOwnedPair(text))
        {
            return text;
        }

        // A foreign (user) F10 entry without our marker must not be claimed and no
        // extra entry may be added (F012).
        if (HasAnyF10(text))
        {
            return text;
        }

        return InsertOwnedPair(text);
    }

    private static string DisableFreeCamera(string text) =>
        RemoveOwnedPair(text);

    /// <summary>
    /// True when the marker line is immediately followed by the exact tool entry
    /// <c>ConsoleKeys=F10</c> inside the Input settings section.
    /// </summary>
    private static bool HasOwnedPair(string text)
    {
        string[] lines = NormalizeLines(text);
        string currentSection = string.Empty;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            if (!string.Equals(currentSection, InputSettingsSection, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(line, OwnershipMarker, StringComparison.Ordinal) &&
                index + 1 < lines.Length &&
                string.Equals(lines[index + 1].Trim(), OwnedEntryLine, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnyF10(string text)
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
                ContainsToken(parsedValue, OwnedKey))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Inserts the marker + owned entry as one pair inside the Input settings section.</summary>
    private static string InsertOwnedPair(string text)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hasFinalNewline = text.EndsWith('\n') || text.EndsWith('\r');
        string[] lines = NormalizeLines(text);
        bool hasTrailingEmpty = hasFinalNewline && lines.Length > 0 && lines[^1].Length == 0;
        if (hasTrailingEmpty)
        {
            lines = lines[..^1];
        }

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
        string[] pair = [OwnershipMarker, OwnedEntryLine];
        if (sectionStart >= 0)
        {
            result.InsertRange(sectionEnd, pair);
        }
        else
        {
            if (result.Count > 0 && result[^1].Length != 0)
            {
                result.Add(string.Empty);
            }

            result.Add($"[{InputSettingsSection}]");
            result.AddRange(pair);
        }

        string joined = string.Join(newline, result);
        return hasFinalNewline ? joined + newline : joined;
    }

    /// <summary>
    /// Removes the intact paid pair (marker immediately followed by the exact tool
    /// entry). If the marker is missing, damaged or the adjacent entry has been
    /// changed, nothing is removed — ownership is lost rather than deleting user data.
    /// </summary>
    private static string RemoveOwnedPair(string text)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool hasFinalNewline = text.EndsWith('\n') || text.EndsWith('\r');
        string[] lines = NormalizeLines(text);
        string currentSection = string.Empty;
        var kept = new List<string>(lines.Length);
        bool removed = false;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                kept.Add(lines[index]);
                continue;
            }

            if (!removed &&
                string.Equals(currentSection, InputSettingsSection, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(line, OwnershipMarker, StringComparison.Ordinal) &&
                index + 1 < lines.Length &&
                string.Equals(lines[index + 1].Trim(), OwnedEntryLine, StringComparison.Ordinal))
            {
                // Skip marker + the exact owned entry.
                removed = true;
                index++;
                continue;
            }

            kept.Add(lines[index]);
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

    private void RemoveLegacyOwnershipFile()
    {
        string path = Path.Combine(_userDataDirectory, LegacyOwnershipFileName);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
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
