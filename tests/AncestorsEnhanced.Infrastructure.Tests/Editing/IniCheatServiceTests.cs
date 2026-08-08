using AncestorsEnhanced.Infrastructure.Editing;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

public sealed class IniCheatServiceTests
{
    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "aec-inicheat-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnablingFreeCameraWritesDebugCameraBindingToInputIniAndBacksUp()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.PlayerInput]\nJump=True\n");

        var service = new IniCheatService(dir);
        service.SetFreeCamera(true);

        string content = File.ReadAllText(inputPath);
        Assert.Contains("+DebugExecBindings=(Key=F10,Command=\"ToggleDebugCamera\")", content, StringComparison.Ordinal);
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
    public void TogglingOffRemovesOnlyToolOwnedDebugCameraBinding()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.PlayerInput]\nDebugExecBindings=(Key=F11,Command=\"stat fps\")\n");

        var service = new IniCheatService(dir);
        service.SetFreeCamera(true);
        Assert.Contains("ToggleDebugCamera", File.ReadAllText(inputPath), StringComparison.Ordinal);
        service.SetFreeCamera(false);

        string content = File.ReadAllText(inputPath);
        Assert.DoesNotContain("ToggleDebugCamera", content, StringComparison.Ordinal);
        Assert.Contains("Key=F11", content, StringComparison.Ordinal);
        Assert.Contains("[/Script/Engine.PlayerInput]", content, StringComparison.Ordinal);

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("DebugExecBindings=(Key=F11,Command=\"stat fps\")")]
    [InlineData("DebugExecBindings=(Key=F12,Command=\"stat unit\")")]
    public void ExistingDebugBindingsArePreserved(string initial)
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.PlayerInput]\n" + initial + "\n");

        var service = new IniCheatService(dir);
        service.SetFreeCamera(true);

        string content = File.ReadAllText(inputPath);
        Assert.Contains(initial, content, StringComparison.Ordinal);
        Assert.Contains("ToggleDebugCamera", content, StringComparison.Ordinal);

        service.SetFreeCamera(false);
        content = File.ReadAllText(inputPath);
        Assert.Contains(initial, content, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleDebugCamera", content, StringComparison.Ordinal);

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PreExistingF10IsNeverRemoved()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.PlayerInput]\n+DebugExecBindings=(Key=F10,Command=\"OtherCamera\")\n");

        var service = new IniCheatService(dir);
        // User already had F10: enabling then disabling must keep it.
        service.SetFreeCamera(true);
        service.SetFreeCamera(false);

        string content = File.ReadAllText(inputPath);
        Assert.Contains("OtherCamera", content, StringComparison.Ordinal);

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OwnershipPersistsAcrossServiceInstances()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, "[/Script/Engine.PlayerInput]\nDebugExecBindings=(Key=F11,Command=\"stat fps\")\n");

        var first = new IniCheatService(dir);
        first.SetFreeCamera(true);
        Assert.True(first.IsFreeCameraEnabled());

        // New service instance: no state in memory, must read ownership from disk.
        var second = new IniCheatService(dir);
        Assert.True(second.IsFreeCameraEnabled());

        second.SetFreeCamera(false);
        Assert.False(second.IsFreeCameraEnabled());

        string content = File.ReadAllText(inputPath);
        Assert.Contains("Key=F11", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleDebugCamera", content, StringComparison.Ordinal);

        if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
    }

    [Fact]
    public void EnablingFreeCameraMigratesThePreviousConsoleKeyMarker()
    {
        string dir = NewTempDir();
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(
            inputPath,
            "[/Script/Engine.InputSettings]\n; AncestorsEnhanced:FreeCamera:F10\nConsoleKeys=F10\n");

        new IniCheatService(dir).SetFreeCamera(true);

        string content = File.ReadAllText(inputPath);
        Assert.Contains("ToggleDebugCamera", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsoleKeys=F10", content, StringComparison.Ordinal);

        Directory.Delete(dir, true);
    }
}

