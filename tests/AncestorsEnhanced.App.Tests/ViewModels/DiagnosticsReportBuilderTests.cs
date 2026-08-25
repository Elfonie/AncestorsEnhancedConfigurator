using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Infrastructure.Platform;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class DiagnosticsReportBuilderTests
{
    [Fact]
    public void BuildRedactsWindowsUserPathsIncludingInspectionNotes()
    {
        string report = DiagnosticsReportBuilder.Build(
            "AEC",
            "1.0.0",
            "Ready",
            "Steam build 5495393",
            @"C:\Users\Firefly\Games\Ancestors",
            @"C:\Users\Firefly\AppData\Local\Ancestors",
            @"C:\Users\Firefly\AppData\Local\Ancestors\System.sav",
            "Read successfully",
            "Windows · X64 · 8 logical processors",
            HardwareDiagnosticsViewModel.FromSnapshot(new HardwareSnapshot(
                "Windows",
                "Test CPU",
                8,
                4,
                16UL * 1024 * 1024 * 1024,
                [new GraphicsAdapterSnapshot("Test GPU", 8UL * 1024 * 1024 * 1024)])),
            [new ConfigurationFileRowViewModel("Engine.ini", "1 KB", "Read successfully")],
            [new PakFileRowViewModel("mod.pak", "2 KB", "Unclassified package")],
            [new NoticeRowViewModel("Warning", @"Read C:\Users\Firefly\AppData\Local\Ancestors")]);

        Assert.DoesNotContain("Firefly", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%USERPROFILE%\Games\Ancestors", report, StringComparison.Ordinal);
        Assert.Contains("GPU and VRAM: Test GPU", report, StringComparison.Ordinal);
        Assert.Contains("Hardware recommendation: Balanced", report, StringComparison.Ordinal);
        Assert.Contains("mod.pak", report, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactPathRedactsAnotherWindowsUserDirectory()
    {
        Assert.Equal(
            @"D:\Users\<user>\Ancestors",
            DiagnosticsReportBuilder.RedactPath(@"D:\Users\AnotherUser\Ancestors"));
    }
}
