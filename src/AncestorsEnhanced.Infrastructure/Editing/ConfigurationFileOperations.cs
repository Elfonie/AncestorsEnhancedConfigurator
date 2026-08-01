using System.Security.Cryptography;

namespace AncestorsEnhanced.Infrastructure.Editing;

internal static class ConfigurationFileOperations
{
    private const string BackupFolderName = "AncestorsEnhanced";
    private static readonly HashSet<string> AllowedFiles =
        new(StringComparer.OrdinalIgnoreCase) { "Engine.ini", "Game.ini" };

    public static string GetConfigurationDirectory(string userDataDirectory) =>
        Path.GetFullPath(Path.Combine(userDataDirectory, "Config", "WindowsNoEditor"));

    public static string GetBackupRoot(string userDataDirectory) =>
        Path.GetFullPath(Path.Combine(userDataDirectory, BackupFolderName, "Backups"));

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

    public static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content));
}
