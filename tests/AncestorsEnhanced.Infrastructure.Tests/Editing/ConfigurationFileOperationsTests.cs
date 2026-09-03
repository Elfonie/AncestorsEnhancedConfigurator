using AncestorsEnhanced.Infrastructure.Editing;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

public sealed class ConfigurationFileOperationsTests
{
    [Fact]
    public void CompareAndReplaceWritesWhenStateMatches()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aec-cas-match-{Guid.NewGuid():N}.ini");
        try
        {
            byte[] original = [1, 2, 3];
            File.WriteAllBytes(path, original);

            ConfigurationFileOperations.CompareAndReplace(
                path,
                [4, 5, 6],
                ConfigurationFileOperations.Sha256(original),
                expectedExists: true);

            Assert.Equal([4, 5, 6], File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CompareAndReplaceAbortsWhenFileChangedSincePreview()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aec-cas-mismatch-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            // The expected hash no longer matches the on-disk content (now [9,9,9]).
            File.WriteAllBytes(path, [9, 9, 9]);

            Assert.Throws<IOException>(() => ConfigurationFileOperations.CompareAndReplace(
                path,
                [4, 5, 6],
                "DEADBEEF",
                expectedExists: true));
            // The target was left untouched.
            Assert.Equal([9, 9, 9], File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CompareAndReplaceDoesNotOverwriteUnexpectedNewFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aec-cas-new-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllBytes(path, [9, 9, 9]);

            Assert.Throws<IOException>(() => ConfigurationFileOperations.CompareAndReplace(
                path,
                [4, 5, 6],
                expectedSha256: null,
                expectedExists: false));

            Assert.Equal([9, 9, 9], File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CompareAndDeletePreservesUnexpectedContent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aec-cas-delete-{Guid.NewGuid():N}.ini");
        try
        {
            File.WriteAllBytes(path, [9, 9, 9]);

            Assert.Throws<IOException>(() => ConfigurationFileOperations.CompareAndDelete(
                path,
                ConfigurationFileOperations.Sha256([1, 2, 3])));

            Assert.Equal([9, 9, 9], File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecoveryRestoresCapturedFileWhenCrashLeftTargetMissing()
    {
        string directory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), $"aec-cas-recovery-{Guid.NewGuid():N}")).FullName;
        try
        {
            string target = Path.Combine(directory, "Engine.ini");
            string operation = Guid.NewGuid().ToString("N");
            string captured = Path.Combine(directory, $".Engine.ini.{operation}.cas");
            string temporary = Path.Combine(directory, $".Engine.ini.{operation}.new");
            File.WriteAllBytes(captured, [1, 2, 3]);
            File.WriteAllBytes(temporary, [4, 5, 6]);

            bool recovered = ConfigurationFileOperations.RecoverInterruptedTarget(target);

            Assert.True(recovered);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(target));
            Assert.False(File.Exists(captured));
            Assert.False(File.Exists(temporary));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RecoveryNeverOverwritesCurrentFileWithCapturedBytes()
    {
        string directory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), $"aec-cas-ambiguous-{Guid.NewGuid():N}")).FullName;
        try
        {
            string target = Path.Combine(directory, "Engine.ini");
            string captured = Path.Combine(directory, $".Engine.ini.{Guid.NewGuid():N}.cas");
            File.WriteAllBytes(target, [9, 9, 9]);
            File.WriteAllBytes(captured, [1, 2, 3]);

            Assert.Throws<IOException>(() =>
                ConfigurationFileOperations.RecoverInterruptedTarget(target));

            Assert.Equal([9, 9, 9], File.ReadAllBytes(target));
            Assert.Equal([1, 2, 3], File.ReadAllBytes(captured));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RecoveryAcceptsAnIdenticalJournalledTargetAndSidecar()
    {
        string directory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), $"aec-cas-identical-{Guid.NewGuid():N}")).FullName;
        try
        {
            string target = Path.Combine(directory, "Engine.ini");
            string captured = Path.Combine(directory, $".Engine.ini.{Guid.NewGuid():N}.cas");
            byte[] content = [1, 2, 3];
            File.WriteAllBytes(target, content);
            File.WriteAllBytes(captured, content);

            bool hasSidecar = ConfigurationFileOperations.ValidateInterruptedTargetRecovery(
                target,
                [ConfigurationFileOperations.Sha256(content)]);

            Assert.True(hasSidecar);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DeleteDirectorySafelyRejectsSameDirectory()
    {
        string directory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), $"aec-del-safe-{Guid.NewGuid():N}")).FullName;
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                ConfigurationFileOperations.DeleteDirectorySafely(directory, directory));
            Assert.True(Directory.Exists(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResolvePhysicalPathNormalizesOrdinaryAndNonExistentPaths()
    {
        string path = Path.Combine(Path.GetTempPath(), "ordinary-folder", "file.txt");
        string resolved = ConfigurationFileOperations.ResolvePhysicalPath(path);
        Assert.Equal(Path.GetFullPath(path), resolved);
    }

    [Fact]
    public void ResolvePhysicalPathResolvesSymlinkOrJunction()
    {
        string baseDir = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), $"aec-res-test-{Guid.NewGuid():N}")).FullName;
        try
        {
            string targetDir = Directory.CreateDirectory(Path.Combine(baseDir, "target")).FullName;
            string subDir = Directory.CreateDirectory(Path.Combine(targetDir, "sub")).FullName;
            string linkDir = Path.Combine(baseDir, "link");

            if (!TryCreateLink(linkDir, targetDir))
            {
                return; // Symlinks/junctions not supported in current test context
            }

            string resolvedLink = ConfigurationFileOperations.ResolvePhysicalPath(linkDir);
            Assert.Equal(Path.GetFullPath(targetDir), resolvedLink);

            string resolvedSub = ConfigurationFileOperations.ResolvePhysicalPath(Path.Combine(linkDir, "sub"));
            Assert.Equal(Path.GetFullPath(subDir), resolvedSub);
        }
        finally
        {
            TryDeleteDirectoryTree(baseDir);
        }
    }

    [Fact]
    public void ValidateMutationDirectoryAllowsWritingWhenParentIsSymlinkAboveTrustedRoot()
    {
        string baseDir = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), $"aec-parent-link-{Guid.NewGuid():N}")).FullName;
        try
        {
            string realRoot = Directory.CreateDirectory(Path.Combine(baseDir, "realRoot")).FullName;
            string gameDir = Directory.CreateDirectory(Path.Combine(realRoot, "game")).FullName;
            string linkRoot = Path.Combine(baseDir, "linkRoot");

            if (!TryCreateLink(linkRoot, realRoot))
            {
                return;
            }

            // A path that went through linkRoot points into the game
            string targetThroughLink = Path.Combine(linkRoot, "game", "config.ini");
            byte[] content = [10, 20, 30];

            // Writing with trustedRoot = gameDir should succeed because gameDir is canonical
            // and the parent symlink (linkRoot) is above the trusted root.
            ConfigurationFileOperations.WriteBytesAtomically(
                targetThroughLink,
                content,
                trustedRoot: gameDir);

            Assert.True(File.Exists(Path.Combine(gameDir, "config.ini")));
            Assert.Equal(content, File.ReadAllBytes(Path.Combine(gameDir, "config.ini")));
        }
        finally
        {
            TryDeleteDirectoryTree(baseDir);
        }
    }

    [Fact]
    public void ValidateMutationDirectoryRejectsEscapeFromTrustedRoot()
    {
        string baseDir = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), $"aec-escape-test-{Guid.NewGuid():N}")).FullName;
        try
        {
            string trustedRoot = Directory.CreateDirectory(Path.Combine(baseDir, "trusted")).FullName;
            string outside = Directory.CreateDirectory(Path.Combine(baseDir, "outside")).FullName;
            string targetFile = Path.Combine(outside, "escape.ini");

            Assert.Throws<InvalidOperationException>(() =>
                ConfigurationFileOperations.WriteBytesAtomically(
                    targetFile,
                    [1, 2, 3],
                    trustedRoot: trustedRoot));
        }
        finally
        {
            TryDeleteDirectoryTree(baseDir);
        }
    }

    private static bool TryCreateLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return Directory.Exists(linkPath);
        }
        catch
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                    });
                    process?.WaitForExit();
                    return process?.ExitCode == 0 && Directory.Exists(linkPath);
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }
    }

    private static void TryDeleteDirectoryTree(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            // Remove any junction/symlink children first to prevent deleting targets
            foreach (string entry in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
            {
                if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
                {
                    Directory.Delete(entry);
                }
            }

            Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }
}
