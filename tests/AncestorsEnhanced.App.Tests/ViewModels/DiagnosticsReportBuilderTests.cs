using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Infrastructure.Platform;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class DiagnosticsReportBuilderTests
{
    [Fact]
    public void BuildRedactsWindowsUserPathsIncludingInspectionNotes()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string userName = Path.GetFileName(profile.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string report = DiagnosticsReportBuilder.Build(
            "AEC",
            "1.0.0",
            "Ready",
            "Steam build 5495393",
            Path.Combine(profile, "Games", "Ancestors"),
            Path.Combine(profile, "AppData", "Local", "Ancestors"),
            Path.Combine(profile, "AppData", "Local", "Ancestors", "System.sav"),
            "Read successfully",
            "Windows · X64 · 8 logical processors",
            HardwareDiagnosticsViewModel.FromSnapshot(new HardwareSnapshot(
                "Windows",
                "Test CPU",
                8,
                4,
                16UL * 1024 * 1024 * 1024,
                [new GraphicsAdapterSnapshot("Test GPU", 8UL * 1024 * 1024 * 1024, true)])),
            [new ConfigurationFileRowViewModel("Engine.ini", "1 KB", "Read successfully")],
            [new PakFileRowViewModel("mod.pak", "2 KB", "Unclassified package")],
            [new NoticeRowViewModel("Warning", $"Read {Path.Combine(profile, "AppData", "Local", "Ancestors")}")]);

        Assert.DoesNotContain(userName, report, StringComparison.OrdinalIgnoreCase);
        string expectedInstallation = OperatingSystem.IsWindows()
            ? @"%USERPROFILE%\Games\Ancestors"
            : "%HOME%/Games/Ancestors";
        Assert.Contains(expectedInstallation, report, StringComparison.Ordinal);
        Assert.Contains("GPU and VRAM: Test GPU", report, StringComparison.Ordinal);
        Assert.Contains("Hardware recommendation: Balanced Setup", report, StringComparison.Ordinal);
        Assert.Contains("mod.pak", report, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactPathRedactsAnotherWindowsUserDirectory()
    {
        Assert.Equal(
            @"D:\Users\<user>\Ancestors",
            DiagnosticsReportBuilder.RedactPath(@"D:\Users\AnotherUser\Ancestors"));
    }

    [Fact]
    public void RedactPathRedactsLinuxHomeDirectory()
    {
        Assert.Equal(
            "/home/<user>/.steam/steamapps/common/Ancestors",
            DiagnosticsReportBuilder.RedactPath("/home/another-user/.steam/steamapps/common/Ancestors"));
    }

    [Fact]
    public void BuildRedactsCustomAbsolutePathsFromFieldsAndInspectionNotes()
    {
        string report = DiagnosticsReportBuilder.Build(
            "AEC", "1.0.0", "Ready", "Detected",
            @"D:\PrivateDiskName\Games\Ancestors",
            "/mnt/private-volume/SteamLibrary/Ancestors",
            @"D:\PrivateDiskName\Games\Ancestors\System.sav",
            "Read successfully", "Windows", TestHardware(), [], [],
            [new NoticeRowViewModel("Warning", "Found D:\\PrivateDiskName\\Games\\Ancestors and /mnt/private-volume/SteamLibrary/Ancestors")]);

        Assert.DoesNotContain("PrivateDiskName", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-volume", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Installation path: <custom path>", report, StringComparison.Ordinal);
        Assert.Contains("<path>", report, StringComparison.Ordinal);
    }

    private static HardwareDiagnosticsViewModel TestHardware() => HardwareDiagnosticsViewModel.FromSnapshot(new HardwareSnapshot(
        "Windows", "Test CPU", 8, 4, 16UL * 1024 * 1024 * 1024,
        [new GraphicsAdapterSnapshot("Test GPU", 8UL * 1024 * 1024 * 1024, true)]));
}
