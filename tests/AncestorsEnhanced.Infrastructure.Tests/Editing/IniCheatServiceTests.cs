using AncestorsEnhanced.Infrastructure.Editing;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

public sealed class IniCheatServiceTests
{
    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "aec-inicheat-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnablingFreeCameraWritesConsoleKeysToInputIniAndBacksUp()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.InputSettings]\nJump=True\n");

        var service = new IniCheatService(dir);
        service.SetFreeCamera(true);

        string content = File.ReadAllText(inputPath);
        Assert.Contains("ConsoleKeys=F10", content, StringComparison.Ordinal);
        Assert.Contains("Jump=True", content, StringComparison.Ordinal);

        string backupRoot = Path.Combine(dir, "AncestorsEnhanced", "Backups");
        Assert.True(
            Directory.Exists(backupRoot) &&
            Directory.EnumerateFiles(backupRoot, "Input.ini.*.before").Any(),
            "expected an Input.ini backup under the backups folder");

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DisablingFreeCameraOnMissingFileDoesNotCreateEmptydIni()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");

        var service = new IniCheatService(dir);
        service.SetFreeCamera(false);

        Assert.False(File.Exists(inputPath), "should not fabricate an empty Input.ini");

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TogglingOffRemovesOnlyToolOwnedConsoleKeys()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.InputSettings]\nConsoleKeys=Tilde\n");

        var service = new IniCheatService(dir);
        service.SetFreeCamera(true);
        Assert.Contains("ConsoleKeys=F10", File.ReadAllText(inputPath), StringComparison.Ordinal);
        service.SetFreeCamera(false);

        string content = File.ReadAllText(inputPath);
        Assert.DoesNotContain("ConsoleKeys=F10", content, StringComparison.Ordinal);
        Assert.Contains("ConsoleKeys=Tilde", content, StringComparison.Ordinal);
        Assert.Contains("[/Script/Engine.InputSettings]", content, StringComparison.Ordinal);

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("ConsoleKeys=Tilde")]
    [InlineData("ConsoleKeys=Tilde\nConsoleKeys=F10")]
    [InlineData("ConsoleKeys=Tilde\nConsoleKeys=Backslash")]
    public void ExistingConsoleKeysArePreserved(string initial)
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.InputSettings]\n" + initial + "\n");

        var service = new IniCheatService(dir);
        service.SetFreeCamera(true);

        string content = File.ReadAllText(inputPath);
        Assert.Contains("ConsoleKeys=Tilde", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsoleKeys=F10\nConsoleKeys=Tilde", content, StringComparison.Ordinal);
        if (initial.Contains("Backslash"))
        {
            Assert.Contains("ConsoleKeys=Backslash", content, StringComparison.Ordinal);
        }

        service.SetFreeCamera(false);
        content = File.ReadAllText(inputPath);
        Assert.Contains("ConsoleKeys=Tilde", content, StringComparison.Ordinal);
        if (initial.Contains("Backslash"))
        {
            Assert.Contains("ConsoleKeys=Backslash", content, StringComparison.Ordinal);
        }

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PreExistingF10IsNeverRemoved()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.InputSettings]\nConsoleKeys=F10\n");

        var service = new IniCheatService(dir);
        // User already had F10: enabling then disabling must keep it.
        service.SetFreeCamera(true);
        service.SetFreeCamera(false);

        string content = File.ReadAllText(inputPath);
        Assert.Contains("ConsoleKeys=F10", content, StringComparison.Ordinal);

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OwnershipPersistsAcrossServiceInstances()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.InputSettings]\nConsoleKeys=Tilde\n");

        var first = new IniCheatService(dir);
        first.SetFreeCamera(true);
        Assert.True(first.IsFreeCameraEnabled());

        // New service instance: no state in memory, must read ownership from disk.
        var second = new IniCheatService(dir);
        Assert.True(second.IsFreeCameraEnabled());

        second.SetFreeCamera(false);
        Assert.False(second.IsFreeCameraEnabled());

        string content = File.ReadAllText(inputPath);
        Assert.Contains("ConsoleKeys=Tilde", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsoleKeys=F10", content, StringComparison.Ordinal);

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }
}

