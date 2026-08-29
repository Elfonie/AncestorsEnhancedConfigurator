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
                {
                    if (adapter.ReportedMemoryBytes is not ulong memory)
                    {
                        return $"{adapter.Name} · VRAM not reported";
                    }

                    return adapter.IsMemoryAuthoritative
                        ? $"{adapter.Name} · {FormatBytes(memory)} reported VRAM"
                        : $"{adapter.Name} · {FormatBytes(memory)} legacy inventory value (not used for recommendations)";
                }));
        return new(
            $"{snapshot.CpuName} · {cores}",
            snapshot.InstalledMemoryBytes is ulong memory ? FormatBytes(memory) : "Not reported",
            graphics,
            snapshot.UnavailableReason ?? "Read locally from the operating system. Only detailed GPU memory sources are used for recommendations.",
            HardwareRecommendationEngine.Recommend(snapshot));
    }

    internal static string FormatBytes(ulong bytes) =>
        $"{bytes / (1024d * 1024d * 1024d):0.#} GiB";
}

public sealed record HardwareRecommendationViewModel(
    string PresetName,
    string Description,
    bool CanStagePreset);

public static class HardwareRecommendationEngine
{
    public static HardwareRecommendationViewModel Recommend(HardwareSnapshot snapshot)
    {
        GraphicsAdapterSnapshot[] authoritativeAdapters = snapshot.GraphicsAdapters
            .Where(adapter => adapter.IsMemoryAuthoritative && adapter.ReportedMemoryBytes is > 0)
            .Where(adapter => !IsBasicDisplayAdapter(adapter.Name))
            .ToArray();
        if (snapshot.UnavailableReason is not null || authoritativeAdapters.Length == 0)
        {
            return new("No automatic preset", "AEC needs a locally reported graphics adapter and VRAM before it can make a conservative recommendation.", false);
        }
        if (authoritativeAdapters.Length > 1)
        {
            return new(
                "No automatic preset",
                "More than one graphics adapter reported dedicated VRAM. AEC cannot safely tell which one Ancestors uses, so it will not choose a preset for you.",
                false);
        }

        GraphicsAdapterSnapshot primaryAdapter = authoritativeAdapters[0];
        if (primaryAdapter.ReportedMemoryBytes is not ulong vram)
        {
            return new("No automatic preset", "AEC needs a locally reported graphics adapter and VRAM before it can make a conservative recommendation.", false);
        }

        const ulong GiB = 1024UL * 1024UL * 1024UL;
        ulong? ram = snapshot.InstalledMemoryBytes;
        string hardware = $"{primaryAdapter.Name} · {HardwareDiagnosticsViewModel.FormatBytes(vram)} dedicated VRAM · {snapshot.LogicalProcessorCount} logical processors" +
            (ram is ulong memory ? $" · {HardwareDiagnosticsViewModel.FormatBytes(memory)} system memory" : string.Empty);
        if (vram <= 4 * GiB)
        {
            return new("Low VRAM Setup", $"Based on {hardware}. This complete quality baseline protects graphics memory; it is not a performance measurement.", true);
        }

        if (vram <= 6 * GiB || snapshot.LogicalProcessorCount <= 4 || ram is < 16 * GiB)
        {
            return new("Performance Setup", $"Based on {hardware}. This complete quality baseline favors stable frame times; test in-game before keeping it.", true);
        }

        if (vram >= 15 * GiB && ram >= 32 * GiB && snapshot.LogicalProcessorCount >= 12)
        {
            return new("Ultra Setup", $"Based on {hardware}. This complete quality baseline raises world, fog and shadow detail; it is not an FPS guarantee.", true);
        }

        // Windows reports bytes while graphics cards are commonly sold in decimal GB.
        // Eleven GiB includes the normal reported size of a 12 GB card without
        // treating an 8 GB class adapter as high-end.
        if (vram >= 11 * GiB && ram >= 24 * GiB && snapshot.LogicalProcessorCount >= 8)
        {
            return new("High Quality Setup", $"Based on {hardware}. This complete quality baseline adds world and reflection detail; it is not an FPS guarantee.", true);
        }

        return new("Balanced Setup", $"Based on {hardware}. This complete quality baseline covers all six Ancestors quality categories.", true);
    }

    private static bool IsBasicDisplayAdapter(string adapterName) =>
        adapterName.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase);
}
