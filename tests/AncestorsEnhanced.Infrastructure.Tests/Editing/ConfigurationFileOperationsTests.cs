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
}
