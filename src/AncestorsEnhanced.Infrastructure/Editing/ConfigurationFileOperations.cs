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

    public static string GetToolChangesRoot(string userDataDirectory) =>
        Path.GetFullPath(Path.Combine(userDataDirectory, BackupFolderName, "ToolChanges"));

    public static string GetPakDirectory(string installDirectory) =>
        Path.GetFullPath(Path.Combine(installDirectory, "Ancestors", "Content", "Paks"));

    public static string GetSystemSaveDirectory(string userDataDirectory) =>
        Path.GetFullPath(Path.Combine(userDataDirectory, "SaveGames"));

    public static string GetOperationDirectory(string userDataDirectory, string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId) ||
            operationId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
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
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The target directory is missing.");
        Directory.CreateDirectory(directory);
        ValidateConfigurationPath(directory, directory);
        ValidateWritableTarget(path);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        FileAttributes? attributes = File.Exists(path) ? File.GetAttributes(path) : null;
        UnixFileMode? unixMode = File.Exists(path) && !OperatingSystem.IsWindows()
            ? File.GetUnixFileMode(path)
            : null;

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
            if (attributes is not null)
            {
                File.SetAttributes(path, attributes.Value & ~FileAttributes.ReparsePoint);
            }
            if (!OperatingSystem.IsWindows() && unixMode is not null)
            {
                File.SetUnixFileMode(path, unixMode.Value);
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
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
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The target directory is missing.");
        Directory.CreateDirectory(directory);
        _ = RecoverInterruptedTarget(path);
        ValidateWritableTarget(path);
        string operationId = Guid.NewGuid().ToString("N");
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{operationId}.new");
        string capturedPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{operationId}.cas");
        bool committed = false;
        try
        {
            WriteBytesAtomically(temporaryPath, content);
            if (!expectedExists)
            {
                File.Move(temporaryPath, path, overwrite: false);
                return;
            }

            if (!File.Exists(path))
            {
                throw new IOException("The target file no longer exists. Refresh and try again.");
            }

            FileAttributes attributes = File.GetAttributes(path);
            UnixFileMode? unixMode = !OperatingSystem.IsWindows() ? File.GetUnixFileMode(path) : null;
            File.Move(path, capturedPath, overwrite: false);
            ValidateWritableTarget(capturedPath);
            string capturedSha = Sha256(ReadStableBounded(capturedPath, 64L * 1024 * 1024));
            if (!string.Equals(capturedSha, expectedSha256, StringComparison.Ordinal))
            {
                RestoreCapturedFile(path, capturedPath);
                throw new IOException("The target file changed after the preview. Refresh and try again.");
            }

            File.SetAttributes(temporaryPath, attributes & ~FileAttributes.ReparsePoint);
            if (!OperatingSystem.IsWindows() && unixMode is not null)
            {
                File.SetUnixFileMode(temporaryPath, unixMode.Value);
            }
            try
            {
                File.Move(temporaryPath, path, overwrite: false);
                committed = true;
            }
            catch
            {
                RestoreCapturedFile(path, capturedPath);
                throw;
            }
            TryDeleteFile(capturedPath);
        }
        catch
        {
            if (!committed && File.Exists(capturedPath) && !File.Exists(path))
            {
                RestoreCapturedFile(path, capturedPath);
            }
            throw;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    /// <summary>
    /// Compare-and-swap delete: removes <paramref name="path"/> only if its current
    /// bytes still match <paramref name="expectedSha256"/>. On any mismatch the target
    /// is left untouched so a plan can never delete bytes it did not see (F066/F067).
    /// </summary>
    public static void CompareAndDelete(string path, string expectedSha256)
    {
        _ = RecoverInterruptedTarget(path);
        if (!File.Exists(path))
        {
            throw new IOException("The target file no longer exists. Refresh and try again.");
        }

        ValidateWritableTarget(path);
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The target directory is missing.");
        string capturedPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.cas");
        File.Move(path, capturedPath, overwrite: false);
        try
        {
            ValidateWritableTarget(capturedPath);
            string currentSha = Sha256(ReadStableBounded(capturedPath, 64L * 1024 * 1024));
            if (!string.Equals(currentSha, expectedSha256, StringComparison.Ordinal))
            {
                RestoreCapturedFile(path, capturedPath);
                throw new IOException("The target file changed after the preview. Refresh and try again.");
            }

            try
            {
                File.Delete(capturedPath);
            }
            catch
            {
                RestoreCapturedFile(path, capturedPath);
                throw;
            }
        }
        catch
        {
            if (File.Exists(capturedPath) && !File.Exists(path))
            {
                RestoreCapturedFile(path, capturedPath);
            }
            throw;
        }
    }

    /// <summary>
    /// Reads a file as one stable version. <paramref name="maxSizeBytes"/> (when &gt; 0)
    /// bounds the accepted size; a larger file is rejected. If the length or last-write
    /// time changes while the file is being read, the read is retried a bounded number
    /// of times and then aborted rather than returning a torn/inconsistent version (F016).
    /// </summary>
    public static byte[] ReadStableBounded(string path, long maxSizeBytes = 0)
    {
        const int maxStableAttempts = 4;
        for (int attempt = 0; attempt < maxStableAttempts; attempt++)
        {
            try
            {
                byte[] first = ReadBoundedVersion(path, maxSizeBytes);
                byte[] second = ReadBoundedVersion(path, maxSizeBytes);
                if (first.AsSpan().SequenceEqual(second))
                {
                    return second;
                }
            }
            catch (IOException) when (attempt < maxStableAttempts - 1)
            {
                Thread.Sleep(150);
                continue;
            }

            if (attempt < maxStableAttempts - 1)
            {
                Thread.Sleep(150);
            }
        }

        throw new IOException("The save file is being written and could not be read as a stable version.");
    }

    private static byte[] ReadBoundedVersion(string path, long maxSizeBytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        long length = stream.Length;
        if (maxSizeBytes > 0 && length > maxSizeBytes)
        {
            throw new IOException("The target file is unexpectedly large.");
        }
        if (length >= int.MaxValue)
        {
            throw new IOException("The target file is unexpectedly large.");
        }

        int budget = maxSizeBytes > 0
            ? maxSizeBytes >= int.MaxValue
                ? int.MaxValue
                : checked((int)maxSizeBytes + 1)
            : checked((int)length + 1);
        using var memory = new MemoryStream((int)Math.Min(length, budget));
        byte[] buffer = new byte[Math.Min(81920, Math.Max(1, budget))];
        while (memory.Length < budget)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, budget - memory.Length));
            if (read == 0)
            {
                break;
            }
            memory.Write(buffer, 0, read);
        }

        if (maxSizeBytes > 0 && memory.Length > maxSizeBytes)
        {
            throw new IOException("The target file is unexpectedly large.");
        }
        if (stream.ReadByte() >= 0 || stream.Length != length || memory.Length != length)
        {
            throw new IOException("The target file changed while it was being read.");
        }

        return memory.ToArray();
    }

    private static void RestoreCapturedFile(string path, string capturedPath)
    {
        if (!File.Exists(capturedPath))
        {
            return;
        }
        if (File.Exists(path))
        {
            throw new IOException(
                $"A concurrent file appeared at the target. The captured bytes were preserved at {capturedPath}.");
        }
        File.Move(capturedPath, path, overwrite: false);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static bool RecoverInterruptedOperations(
        string userDataDirectory,
        string? installDirectory)
    {
        var targets = new List<string>
        {
            GetTargetPath(GetConfigurationDirectory(userDataDirectory), "Engine.ini"),
            GetTargetPath(GetConfigurationDirectory(userDataDirectory), "Game.ini"),
            GetTargetPath(GetConfigurationDirectory(userDataDirectory), "Input.ini"),
            GetTargetPath(userDataDirectory, installDirectory, "System.sav", SettingFileTarget.SystemSave),
        };
        if (!string.IsNullOrWhiteSpace(installDirectory))
        {
            targets.Add(GetTargetPath(
                userDataDirectory,
                installDirectory,
                "AncestorsEnhanced-Vignette_P.pak",
                SettingFileTarget.Pak));
            targets.Add(GetTargetPath(
                userDataDirectory,
                installDirectory,
                "pakchunk99-WindowsNoEditor_P.pak",
                SettingFileTarget.Pak));
        }

        bool recovered = false;
        foreach (string target in targets)
        {
            recovered |= RecoverInterruptedTarget(target);
        }
        return recovered;
    }

    internal static bool RecoverInterruptedTarget(
        string path,
        IReadOnlyCollection<string>? expectedExistingHashes = null)
    {
        string[] captured = GetCapturedSidecars(path);
        if (captured.Length == 0)
        {
            return false;
        }
        ValidateInterruptedTargetRecovery(path, expectedExistingHashes);
        string targetName = Path.GetFileName(path);
        if (File.Exists(path))
        {
            TryDeleteFile(captured[0]);
            if (File.Exists(captured[0]))
            {
                throw new IOException($"The completed write sidecar for {targetName} could not be removed.");
            }
            TryDeleteFile(Path.ChangeExtension(captured[0], ".new"));
            return true;
        }

        string capturedPath = captured[0];
        ValidateWritableTarget(capturedPath);
        ValidateWritableTarget(path);
        File.Move(capturedPath, path, overwrite: false);
        TryDeleteFile(Path.ChangeExtension(capturedPath, ".new"));
        return true;
    }

    internal static bool ValidateInterruptedTargetRecovery(
        string path,
        IReadOnlyCollection<string>? expectedExistingHashes = null)
    {
        string[] captured = GetCapturedSidecars(path);
        if (captured.Length == 0)
        {
            return false;
        }

        string targetName = Path.GetFileName(path);
        if (captured.Length > 1)
        {
            throw new IOException($"Multiple interrupted writes were found for {targetName}. No automatic recovery was attempted.");
        }

        ValidateWritableTarget(captured[0]);
        string capturedHash = Sha256(ReadStableBounded(captured[0], 64L * 1024 * 1024));
        if (!File.Exists(path))
        {
            if (expectedExistingHashes is not null &&
                !expectedExistingHashes.Contains(capturedHash, StringComparer.Ordinal))
            {
                throw new IOException($"The interrupted write sidecar for {targetName} does not match the operation journal.");
            }
            return true;
        }

        if (expectedExistingHashes is null)
        {
            throw new IOException(
                $"An interrupted write and a current {targetName} both exist. Neither file was changed.");
        }

        string targetHash = Sha256(ReadStableBounded(path, 64L * 1024 * 1024));
        if (!expectedExistingHashes.Contains(targetHash, StringComparer.Ordinal) ||
            !expectedExistingHashes.Contains(capturedHash, StringComparer.Ordinal) ||
            string.Equals(targetHash, capturedHash, StringComparison.Ordinal))
        {
            throw new IOException(
                $"The current {targetName} and its write sidecar do not prove a completed tool operation. Neither file was changed.");
        }
        return true;
    }

    private static string[] GetCapturedSidecars(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is null || !Directory.Exists(directory))
        {
            return [];
        }

        string targetName = Path.GetFileName(path);
        string prefix = $".{targetName}.";
        return Directory.EnumerateFiles(directory, $".{targetName}.*.cas")
            .Where(candidate => IsCapturedName(Path.GetFileName(candidate), prefix))
            .ToArray();
    }

    private static bool IsCapturedName(string fileName, string prefix)
    {
        const string Suffix = ".cas";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> operationId = fileName.AsSpan(
            prefix.Length,
            fileName.Length - prefix.Length - Suffix.Length);
        return operationId.Length == 32 && operationId.ToArray().All(char.IsAsciiHexDigit);
    }

    public static void DeleteDirectorySafely(string allowedRoot, string targetDirectory)
    {
        string root = Path.GetFullPath(allowedRoot);
        string target = Path.GetFullPath(targetDirectory);
        string relative = Path.GetRelativePath(root, target);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The directory to delete is outside the allowed root.");
        }

        ValidateConfigurationPath(root, target);
        DeleteTree(target);
    }

    private static void DeleteTree(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }
        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("A linked directory will not be deleted.");
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            ValidateWritableTarget(file);
            File.Delete(file);
        }
        foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
        {
            DeleteTree(child);
        }
        Directory.Delete(directory, recursive: false);
    }

    public static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content));

    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
