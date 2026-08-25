using System.Globalization;
using AncestorsEnhanced.Infrastructure.Platform;

namespace AncestorsEnhanced.App.ViewModels;

public sealed record HardwareDiagnosticsViewModel(
    string Cpu,
    string Memory,
    string Graphics,
    string Status,
    HardwareRecommendationViewModel Recommendation)
{
    public static HardwareDiagnosticsViewModel FromSnapshot(HardwareSnapshot snapshot)
    {
        string cores = snapshot.PhysicalCoreCount is int physical
            ? $"{physical} cores · {snapshot.LogicalProcessorCount} logical processors"
            : $"{snapshot.LogicalProcessorCount} logical processors";
        string graphics = snapshot.GraphicsAdapters.Count == 0
            ? "Not available"
            : string.Join(
                Environment.NewLine,
                snapshot.GraphicsAdapters.Select(adapter =>
                    adapter.ReportedMemoryBytes is ulong memory
                        ? $"{adapter.Name} · {FormatBytes(memory)} reported VRAM"
                        : $"{adapter.Name} · VRAM not reported"));
        return new(
            $"{snapshot.CpuName} · {cores}",
            snapshot.InstalledMemoryBytes is ulong memory ? FormatBytes(memory) : "Not reported",
            graphics,
            snapshot.UnavailableReason ?? "Read locally from the operating system. GPU memory is inventory data, not a benchmark.",
            HardwareRecommendationEngine.Recommend(snapshot));
    }

    internal static string FormatBytes(ulong bytes) =>
        $"{bytes / (1024d * 1024d * 1024d):0.#} GB";
}

public sealed record HardwareRecommendationViewModel(
    string PresetName,
    string Description,
    bool CanStagePreset);

public static class HardwareRecommendationEngine
{
    public static HardwareRecommendationViewModel Recommend(HardwareSnapshot snapshot)
    {
        if (snapshot.UnavailableReason is not null || snapshot.MaximumReportedGraphicsMemoryBytes is not ulong vram)
        {
            return new("No automatic preset", "AEC needs a locally reported graphics adapter and VRAM before it can make a conservative recommendation.", false);
        }

        const ulong GiB = 1024UL * 1024UL * 1024UL;
        ulong? ram = snapshot.InstalledMemoryBytes;
        if (vram <= 4 * GiB)
        {
            return new("Low VRAM", $"Based on {HardwareDiagnosticsViewModel.FormatBytes(vram)} reported VRAM. This is a conservative starting point, not a performance measurement.", true);
        }

        if (vram <= 6 * GiB || snapshot.LogicalProcessorCount <= 4)
        {
            return new("Performance", $"Based on {HardwareDiagnosticsViewModel.FormatBytes(vram)} reported VRAM and {snapshot.LogicalProcessorCount} logical processors. Test in-game before keeping it.", true);
        }

        if (vram >= 12 * GiB && ram >= 24 * GiB)
        {
            return new("High Quality", $"Based on {HardwareDiagnosticsViewModel.FormatBytes(vram)} reported VRAM and {HardwareDiagnosticsViewModel.FormatBytes(ram.Value)} system memory. This is not an FPS guarantee.", true);
        }

        return new("Balanced", $"Based on {HardwareDiagnosticsViewModel.FormatBytes(vram)} reported VRAM. Balanced is a cautious baseline; use the normal review flow to stage it.", true);
    }
}
