namespace AncestorsEnhanced.Infrastructure.Platform;

/// <summary>
/// Read-only local hardware facts. GPU memory is reported by the operating
/// system and is deliberately not treated as a benchmark result.
/// </summary>
public sealed record HardwareSnapshot(
    string OperatingSystem,
    string CpuName,
    int LogicalProcessorCount,
    int? PhysicalCoreCount,
    ulong? InstalledMemoryBytes,
    IReadOnlyList<GraphicsAdapterSnapshot> GraphicsAdapters,
    string? UnavailableReason = null)
{
    public bool HasGraphicsMemory => GraphicsAdapters.Any(adapter => adapter.ReportedMemoryBytes is not null);

    public ulong? MaximumReportedGraphicsMemoryBytes => GraphicsAdapters
        .Where(adapter => adapter.ReportedMemoryBytes is not null)
        .Select(adapter => adapter.ReportedMemoryBytes!.Value)
        .DefaultIfEmpty()
        .Max() is ulong value && value > 0 ? value : null;
}

public sealed record GraphicsAdapterSnapshot(string Name, ulong? ReportedMemoryBytes);

public interface IHardwareProbe
{
    HardwareSnapshot Inspect();
}

public sealed class EmptyHardwareProbe : IHardwareProbe
{
    public static EmptyHardwareProbe Instance { get; } = new();

    private EmptyHardwareProbe()
    {
    }

    public HardwareSnapshot Inspect() => new(
        global::System.Environment.OSVersion.VersionString,
        "Not queried",
        global::System.Environment.ProcessorCount,
        null,
        null,
        [],
        "Hardware detection was not configured.");
}
