using AncestorsEnhanced.Infrastructure.Editing;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

/// <summary>F012/F075 - free-camera ownership lives in Input.ini via a unique marker line.</summary>
public sealed class F012FreeCameraOwnershipTests
{
    private const string Section = "[/Script/Engine.PlayerInput]";
    private const string Marker = "; AncestorsEnhanced:FreeCamera:F10";
    private const string ToolEntry = "+DebugExecBindings=(Key=F10,Command=\"ToggleDebugCamera\")";

    [Fact]
    public void UserOwnedF10IsNeverClaimedOrDuplicated()
    {
        (string userData, string input) = MakeInput("+DebugExecBindings=(Key=F10,Command=\"OtherCamera\")\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);

        string content = File.ReadAllText(input);
        Assert.DoesNotContain(Marker, content, StringComparison.Ordinal);
        Assert.Equal(1, CountBindings(content));
        Assert.False(service.IsFreeCameraEnabled(), "a user F10 without our marker is not owned");

        service.SetFreeCamera(false);
        content = File.ReadAllText(input);
        Assert.Equal(1, CountBindings(content));
        Assert.DoesNotContain(Marker, content, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralUserF10EntriesAreLeftUntouched()
    {
        (string userData, string input) = MakeInput("+DebugExecBindings=(Key=F10,Command=\"OtherCamera\")\n+DebugExecBindings=(Key=F10,Command=\"OtherCamera2\")\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);

        string content = File.ReadAllText(input);
        Assert.DoesNotContain(Marker, content, StringComparison.Ordinal);
        Assert.Equal(2, CountBindings(content));
    }

    [Fact]
    public void ToolAddsMarkerAndEntryTogetherInOneWrite()
    {
        (string userData, string input) = MakeInput("+DebugExecBindings=(Key=F11,Command=\"stat fps\")\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);

        string content = File.ReadAllText(input);
        Assert.Contains(Marker + "\n" + ToolEntry, content, StringComparison.Ordinal);
        Assert.True(service.IsFreeCameraEnabled());
        Assert.Contains("Key=F11", content, StringComparison.Ordinal);
    }

    [Fact]
    public void UserF10AddedAboveToolEntrySurvivesDisable()
    {
        (string userData, string input) = MakeInput("+DebugExecBindings=(Key=F11,Command=\"stat fps\")\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);
        Assert.True(service.IsFreeCameraEnabled());

        // The user later adds their own F10 above the tool-owned block.
        File.WriteAllText(input, Section + "\n+DebugExecBindings=(Key=F10,Command=\"OtherCamera\")\n" + Marker + "\n" + ToolEntry + "\n");

        service.SetFreeCamera(false);

        string content = File.ReadAllText(input);
        Assert.DoesNotContain(Marker, content, StringComparison.Ordinal);
        // Only the tool entry was removed; the user's own F10 above it survives.
        Assert.Equal(1, CountBindings(content));
    }

    [Fact]
    public void EditedToolLineIsNeverRemoved()
    {
        (string userData, string input) = MakeInput("+DebugExecBindings=(Key=F11,Command=\"stat fps\")\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);

        // The user edits the tool line so it no longer matches the exact owned entry.
        File.WriteAllText(input, Section + "\n" + Marker + "\n+DebugExecBindings=(Key=F11,Command=\"ToggleDebugCamera\")\n");

        service.SetFreeCamera(false);

        string content = File.ReadAllText(input);
        Assert.Contains(Marker, content, StringComparison.Ordinal);
        Assert.Contains("Key=F11", content, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkerWithoutToolLineIsNotRemoved()
    {
        (string userData, string input) = MakeInput("+DebugExecBindings=(Key=F11,Command=\"stat fps\")\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);

        File.WriteAllText(input, Section + "\n" + Marker + "\n+DebugExecBindings=(Key=F11,Command=\"stat fps\")\n");

        service.SetFreeCamera(false);

        string content = File.ReadAllText(input);
        Assert.Contains(Marker, content, StringComparison.Ordinal);
        Assert.Contains("Key=F11", content, StringComparison.Ordinal);
    }

    private static (string UserData, string InputPath) MakeInput(string body)
    {
        string dir = Path.Combine(Path.GetTempPath(), "aec-f012-" + Guid.NewGuid().ToString("N"));
        string inputPath = Path.Combine(dir, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, Section + "\n" + body);
        return (dir, inputPath);
    }

    private static int CountBindings(string text) => CountOccurrences(text, "DebugExecBindings=");

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
