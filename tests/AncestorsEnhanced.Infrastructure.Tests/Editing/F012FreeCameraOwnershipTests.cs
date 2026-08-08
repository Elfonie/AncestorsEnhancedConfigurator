using System.Linq;
using AncestorsEnhanced.Infrastructure.Editing;
using Xunit;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

/// <summary>F012/F075 - free-camera ownership lives in Input.ini via a unique marker line.</summary>
public sealed class F012FreeCameraOwnershipTests
{
    private const string Section = "[/Script/Engine.InputSettings]";
    private const string Marker = "; AncestorsEnhanced:FreeCamera:F10";
    private const string ToolEntry = "ConsoleKeys=F10";

    [Fact]
    public void UserOwnedF10IsNeverClaimedOrDuplicated()
    {
        (string userData, string input) = Setup("ConsoleKeys=F10\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);

        string content = File.ReadAllText(input);
        Assert.DoesNotContain(Marker, content, StringComparison.Ordinal);
        Assert.False(service.IsFreeCameraEnabled(), "a user F10 without our marker is not owned");

        service.SetFreeCamera(false);
        content = File.ReadAllText(input);
        Assert.Equal(1, CountOccurrences(content, ToolEntry));
        Assert.DoesNotContain(Marker, content, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralUserF10EntriesAreLeftUntouched()
    {
        (string userData, string input) = Setup("ConsoleKeys=F10\nConsoleKeys=F10,F11\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);

        string content = File.ReadAllText(input);
        Assert.DoesNotContain(Marker, content, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(content, "ConsoleKeys=F10"));
    }

    [Fact]
    public void ToolAddsMarkerAndEntryTogetherInOneWrite()
    {
        (string userData, string input) = Setup("Jump=True\nConsoleKeys=Tilde\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);

        string content = File.ReadAllText(input);
        Assert.Contains(Marker + "\n" + ToolEntry, content, StringComparison.Ordinal);
        Assert.True(service.IsFreeCameraEnabled());
        Assert.Contains("ConsoleKeys=Tilde", content, StringComparison.Ordinal);
        Assert.Contains("Jump=True", content, StringComparison.Ordinal);
    }

    [Fact]
    public void UserF10AddedAboveToolEntrySurvivesDisable()
    {
        (string userData, string input) = Setup("ConsoleKeys=Tilde\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);
        Assert.True(service.IsFreeCameraEnabled());

        // The user later adds their own F10 above the tool-owned block.
        string withUserEntry = Section + "\nConsoleKeys=F10\n" + Marker + "\n" + ToolEntry + "\n";
        File.WriteAllText(input, withUserEntry);

        service.SetFreeCamera(false);

        string content = File.ReadAllText(input);
        Assert.DoesNotContain(Marker, content, StringComparison.Ordinal);
        // Only the tool entry was removed; the user's own F10 above it survives.
        Assert.Equal(1, CountOccurrences(content, ToolEntry));
    }

    [Fact]
    public void EditedToolLineIsNeverRemoved()
    {
        (string userData, string input) = Setup("ConsoleKeys=Tilde\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);

        string edited = Section + "\n" + Marker + "\nConsoleKeys=F11\n";
        File.WriteAllText(input, edited);

        service.SetFreeCamera(false);

        string content = File.ReadAllText(input);
        Assert.Contains(Marker, content, StringComparison.Ordinal);
        Assert.Contains("ConsoleKeys=F11", content, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkerWithoutToolLineIsNotRemoved()
    {
        (string userData, string input) = Setup("ConsoleKeys=Tilde\n");

        var service = new IniCheatService(userData);
        service.SetFreeCamera(true);

        File.WriteAllText(input, Section + "\n" + Marker + "\nConsoleKeys=Tilde\n");

        service.SetFreeCamera(false);

        string content = File.ReadAllText(input);
        Assert.Contains(Marker, content, StringComparison.Ordinal);
        Assert.Contains("ConsoleKeys=Tilde", content, StringComparison.Ordinal);
    }

    private static (string UserData, string InputPath) Setup(string body)
    {
        string userData = Path.Combine(Path.GetTempPath(), "aec-f012-" + Guid.NewGuid().ToString("N"));
        string inputPath = Path.Combine(userData, "Config", "WindowsNoEditor", "Input.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        File.WriteAllText(inputPath, Section + "\n" + body);
        return (userData, inputPath);
    }

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
