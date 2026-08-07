using System.Security.Cryptography;
using AncestorsEnhanced.Core.Editing;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal static class ConfigurationFileOperations
{
    private const string BackupFolderName = "AncestorsEnhanced";
    private static readonly HashSet<string> AllowedFiles =
        new(StringComparer.OrdinalIgnoreCase) { "Engine.ini", "Game.ini", "Input.ini" };
    private static readonly HashSet<string> AllowedPakFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "AncestorsEnhanced-Vignette_P.pak",
            "pakchunk99-WindowsNoEditor_P.pak",
        };

    public static string GetConfigurationDirectory(string userDataDirectory) =>
        Path.GetFullPath(Path.Combine(userDataDirectory, "Config", "WindowsNoEditor"));

    public static string GetBackupRoot(string userDataDirectory) =>
        Path.GetFullPath(Path.Combine(userDataDirectory, BackupFolderName, "Backups"));

    public static string GetPakDirectory(string installDirectory) =>
        Path.GetFullPath(Path.Combine(installDirectory, "Ancestors", "Content", "Paks"));

    public static string GetSystemSaveDirectory(string userDataDirectory) =>
        Path.GetFullPath(Path.Combine(userDataDirectory, "SaveGames"));

    public static string GetOperationDirectory(string userDataDirectory, string operationId)
    {
        if (operationId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new InvalidOperationException("The operation identifier is invalid.");
        }

        return Path.Combine(GetBackupRoot(userDataDirectory), operationId);
    }

    public static string GetTargetPath(string configDirectory, string fileName)
    {
        ValidateFileName(fileName);
        string path = Path.GetFullPath(Path.Combine(configDirectory, fileName));
        if (!string.Equals(
                Path.GetDirectoryName(path),
                configDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The target path leaves the configuration directory.");
        }

        return path;
    }

    public static void ValidateFileName(string fileName)
    {
        if (!AllowedFiles.Contains(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{fileName} is not an allowed configuration target.");
        }
    }

    public static void ValidatePakFileName(string fileName)
    {
        if (!AllowedPakFiles.Contains(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{fileName} is not an allowed PAK target.");
        }
    }

    public static void ValidateSystemSaveFileName(string fileName)
    {
        if (!string.Equals(fileName, "System.sav", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{fileName} is not an allowed save target.");
        }
    }

    public static string GetTargetPath(
        string userDataDirectory,
        string? installDirectory,
        string fileName,
        SettingFileTarget target)
    {
        if (target == SettingFileTarget.Ini)
        {
            return GetTargetPath(GetConfigurationDirectory(userDataDirectory), fileName);
        }

        if (target == SettingFileTarget.SystemSave)
        {
            ValidateSystemSaveFileName(fileName);
            string saveDirectory = GetSystemSaveDirectory(userDataDirectory);
            string savePath = Path.GetFullPath(Path.Combine(saveDirectory, fileName));
            if (!string.Equals(Path.GetDirectoryName(savePath), saveDirectory, PathComparison))
            {
                throw new InvalidOperationException("The target path leaves the save directory.");
            }

            return savePath;
        }

        if (target != SettingFileTarget.Pak)
        {
            throw new InvalidOperationException("The target type is not supported.");
        }

        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new InvalidOperationException("The game installation directory is missing.");
        }

        ValidatePakFileName(fileName);
        string directory = GetPakDirectory(installDirectory);
        string path = Path.GetFullPath(Path.Combine(directory, fileName));
        if (!string.Equals(Path.GetDirectoryName(path), directory, PathComparison))
        {
            throw new InvalidOperationException("The target path leaves the PAK directory.");
        }

        return path;
    }

    public static void ValidateConfigurationPath(
        string userDataDirectory,
        string configurationDirectory)
    {
        string root = Path.GetFullPath(userDataDirectory);
        string current = Path.GetFullPath(configurationDirectory);
        string relative = Path.GetRelativePath(root, current);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The configuration path leaves the user-data directory.");
        }

        while (true)
        {
            if (Directory.Exists(current) &&
                File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException("A linked configuration directory will not be changed.");
            }

            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException("The configuration path is invalid.");
        }
    }

    public static void ValidateWritableTarget(string path)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} is a link and will not be changed.");
        }
    }

    public static void WriteBytesAtomically(string path, byte[] content)
    {
        ValidateWritableTarget(path);
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The target directory is missing.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: File.Exists(path));
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Compare-and-swap write: replaces <paramref name="path"/> only if its current
    /// on-disk state still matches the expected state that a plan/preview was built
    /// from. For a file that existed, <paramref name="expectedSha256"/> must equal the
    /// hash of the current bytes; for a file that did not exist, the path must still be
    /// absent. On any mismatch the write is aborted and the target is left untouched,
    /// closing the lost-update / data-TOCTOU window (F066, F067, F074).
    /// </summary>
    public static void CompareAndReplace(
        string path,
        byte[] content,
        string? expectedSha256,
        bool expectedExists)
    {
        bool currentExists = File.Exists(path);
        if (currentExists != expectedExists)
        {
            throw new IOException(
                $"The target file changed after the preview (expected {(expectedExists ? "present" : "absent")}). Refresh and try again.");
        }

        if (expectedExists)
        {
            string currentSha = Sha256(File.ReadAllBytes(path));
            if (!string.Equals(currentSha, expectedSha256, StringComparison.Ordinal))
            {
                throw new IOException(
                    "The target file changed after the preview. Refresh and try again.");
            }
        }

        WriteBytesAtomically(path, content);
    }

    /// <summary>
    /// Compare-and-swap delete: removes <paramref name="path"/> only if its current
    /// bytes still match <paramref name="expectedSha256"/>. On any mismatch the target
    /// is left untouched so a plan can never delete bytes it did not see (F066/F067).
    /// </summary>
    public static void CompareAndDelete(string path, string expectedSha256)
    {
        if (!File.Exists(path))
        {
            throw new IOException("The target file no longer exists. Refresh and try again.");
        }

        string currentSha = Sha256(File.ReadAllBytes(path));
        if (!string.Equals(currentSha, expectedSha256, StringComparison.Ordinal))
        {
            throw new IOException("The target file changed after the preview. Refresh and try again.");
        }

        File.Delete(path);
    }

    public static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content));

    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
