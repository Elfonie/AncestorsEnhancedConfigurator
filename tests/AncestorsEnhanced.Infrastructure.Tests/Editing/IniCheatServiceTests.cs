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
    public void TogglingOffRemovesExistingConsoleKeys()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.InputSettings]\nConsoleKeys=F10\n");

        var service = new IniCheatService(dir);
        service.SetFreeCamera(false);

        string content = File.ReadAllText(inputPath);
        Assert.DoesNotContain("ConsoleKeys", content, StringComparison.Ordinal);
        Assert.Contains("[/Script/Engine.InputSettings]", content, StringComparison.Ordinal);

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }
}

