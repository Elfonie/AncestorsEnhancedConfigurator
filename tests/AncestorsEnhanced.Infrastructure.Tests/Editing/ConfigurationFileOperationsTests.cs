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
}