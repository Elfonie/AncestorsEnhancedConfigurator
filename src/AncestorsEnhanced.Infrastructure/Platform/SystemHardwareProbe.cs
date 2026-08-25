using System.Management;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Xml;

namespace AncestorsEnhanced.Infrastructure.Platform;

/// <summary>
/// Queries only local operating-system inventory. Failures become an explicit
/// incomplete snapshot so recommendations cannot silently guess hardware.
/// </summary>
public sealed class SystemHardwareProbe : IHardwareProbe
{
    private static readonly Regex MemoryMegabytes = new(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*(?:MB|MiB)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public HardwareSnapshot Inspect(bool includeDetailedGraphics = false) => OperatingSystem.IsWindows()
        ? InspectWindows(includeDetailedGraphics)
        : OperatingSystem.IsLinux()
            ? InspectLinux()
            : new(
                global::System.Environment.OSVersion.VersionString,
                "Not available on this platform",
                global::System.Environment.ProcessorCount,
                null,
                null,
                [],
                "Hardware inventory is currently implemented for Windows and Linux only.");

    private static HardwareSnapshot InspectLinux()
    {
        try
        {
            return new(
                global::System.Environment.OSVersion.VersionString,
                ReadLinuxCpuName(),
                global::System.Environment.ProcessorCount,
                null,
                ReadLinuxMemory(),
                ReadLinuxGraphicsAdapters(),
                "Read locally from /proc and /sys. Integrated graphics may not report dedicated VRAM.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(
                global::System.Environment.OSVersion.VersionString,
                "Unavailable",
                global::System.Environment.ProcessorCount,
                null,
                null,
                [],
                $"Linux hardware inventory could not be read ({exception.GetType().Name}).");
        }
    }

    private static string ReadLinuxCpuName()
    {
        const string cpuInfo = "/proc/cpuinfo";
        if (!File.Exists(cpuInfo))
        {
            return "Unavailable";
        }

        return File.ReadLines(cpuInfo)
            .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && (parts[0] == "model name" || parts[0] == "Hardware"))
            .Select(parts => parts[1])
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "Unavailable";
    }

    private static ulong? ReadLinuxMemory()
    {
        const string memInfo = "/proc/meminfo";
        if (!File.Exists(memInfo))
        {
            return null;
        }

        string? line = File.ReadLines(memInfo).FirstOrDefault(value => value.StartsWith("MemTotal:", StringComparison.Ordinal));
        Match match = Regex.Match(line ?? string.Empty, @"\d+");
        return match.Success && ulong.TryParse(match.Value, out ulong kibibytes) && kibibytes > 0
            ? kibibytes * 1024
            : null;
    }

    private static GraphicsAdapterSnapshot[] ReadLinuxGraphicsAdapters()
    {
        const string drmDirectory = "/sys/class/drm";
        if (!Directory.Exists(drmDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(drmDirectory, "card*")
            .Where(path => Regex.IsMatch(Path.GetFileName(path), @"^card\d+$", RegexOptions.CultureInvariant))
            .Select(ReadLinuxGraphicsAdapter)
            .OfType<GraphicsAdapterSnapshot>()
            .DistinctBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static GraphicsAdapterSnapshot? ReadLinuxGraphicsAdapter(string cardPath)
    {
        string devicePath = Path.Combine(cardPath, "device");
        string? vendor = ReadTrimmedFile(Path.Combine(devicePath, "vendor"));
        string? device = ReadTrimmedFile(Path.Combine(devicePath, "device"));
        if (vendor is null || device is null)
        {
            return null;
        }

        ulong? vram = ReadPositiveUInt64(ReadTrimmedFile(Path.Combine(devicePath, "mem_info_vram_total")));
        return new($"Linux GPU {vendor}/{device}", vram, IsMemoryAuthoritative: vram is not null);
    }

    private static string? ReadTrimmedFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static HardwareSnapshot InspectWindows(bool includeDetailedGraphics)
    {
        try
        {
            ProcessorInventory processor = ReadProcessor();
            ulong? installedMemory = ReadInstalledMemory();
            GraphicsAdapterSnapshot[] adapters = ReadGraphicsAdapters();
            if (includeDetailedGraphics)
            {
                GraphicsAdapterSnapshot[] dxDiagAdapters = TryReadDxDiagAdapters();
                if (dxDiagAdapters.Any(adapter => adapter.ReportedMemoryBytes is not null))
                {
                    adapters = dxDiagAdapters;
                }
            }
            return new(
                global::System.Environment.OSVersion.VersionString,
                processor.Name,
                processor.LogicalProcessorCount,
                processor.PhysicalCoreCount,
                installedMemory,
                adapters);
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new(
                global::System.Environment.OSVersion.VersionString,
                "Unavailable",
                global::System.Environment.ProcessorCount,
                null,
                null,
                [],
                $"Windows hardware inventory could not be read ({exception.GetType().Name}).");
        }
    }

    [SupportedOSPlatform("windows")]
    private static ProcessorInventory ReadProcessor()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
        using ManagementObjectCollection objects = searcher.Get();
        ManagementObject? processor = objects.Cast<ManagementObject>().FirstOrDefault();
        if (processor is null)
        {
            return new("Unavailable", global::System.Environment.ProcessorCount, null);
        }

        string name = ReadString(processor["Name"]) ?? "Unavailable";
        int logical = ReadPositiveInt(processor["NumberOfLogicalProcessors"]) ?? global::System.Environment.ProcessorCount;
        return new(name, logical, ReadPositiveInt(processor["NumberOfCores"]));
    }

    [SupportedOSPlatform("windows")]
    private static ulong? ReadInstalledMemory()
    {
        using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
        using ManagementObjectCollection objects = searcher.Get();
        return objects.Cast<ManagementObject>()
            .Select(item => ReadPositiveUInt64(item["TotalPhysicalMemory"]))
            .FirstOrDefault(value => value is not null);
    }

    [SupportedOSPlatform("windows")]
    private static GraphicsAdapterSnapshot[] ReadGraphicsAdapters()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
        using ManagementObjectCollection objects = searcher.Get();
        return objects.Cast<ManagementObject>()
            .Select(item => new GraphicsAdapterSnapshot(
                ReadString(item["Name"]) ?? "Unnamed graphics adapter",
                ReadPositiveUInt64(item["AdapterRAM"]),
                IsMemoryAuthoritative: false))
            .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static GraphicsAdapterSnapshot[] TryReadDxDiagAdapters()
    {
        string reportPath = Path.Combine(Path.GetTempPath(), $"aec-dxdiag-{Guid.NewGuid():N}.xml");
        try
        {
            var startInfo = new ProcessStartInfo(
                Path.Combine(global::System.Environment.SystemDirectory, "dxdiag.exe"),
                $"/x \"{reportPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using Process? process = Process.Start(startInfo);
            if (process is null || !process.WaitForExit(15_000) || process.ExitCode != 0 || !File.Exists(reportPath))
            {
                return [];
            }

            return ParseDxDiagXml(File.ReadAllText(reportPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or XmlException)
        {
            return [];
        }
        finally
        {
            try
            {
                File.Delete(reportPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The randomized report is diagnostic-only. Leaving it behind is
                // safer than turning an otherwise read-only inspection into a failure.
            }
        }
    }

    internal static GraphicsAdapterSnapshot[] ParseDxDiagXml(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document.SelectNodes("/DxDiag/DisplayDevices/DisplayDevice")?
            .Cast<XmlElement>()
            .Select(device => new GraphicsAdapterSnapshot(
                device["CardName"]?.InnerText.Trim() is { Length: > 0 } name ? name : "Unnamed graphics adapter",
                ParseDxDiagMemory(device["DedicatedMemory"]?.InnerText) ?? ParseDxDiagMemory(device["DisplayMemory"]?.InnerText),
                IsMemoryAuthoritative: true))
            .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private static ulong? ParseDxDiagMemory(string? value)
    {
        Match match = MemoryMegabytes.Match(value ?? string.Empty);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out double megabytes) || megabytes <= 0)
        {
            return null;
        }

        double bytes = megabytes * 1024d * 1024d;
        return bytes <= ulong.MaxValue ? (ulong)bytes : null;
    }

    private static string? ReadString(object? value) => value?.ToString()?.Trim() is { Length: > 0 } text ? text : null;

    private static int? ReadPositiveInt(object? value) =>
        int.TryParse(value?.ToString(), out int parsed) && parsed > 0 ? parsed : null;

    private static ulong? ReadPositiveUInt64(object? value) =>
        ulong.TryParse(value?.ToString(), out ulong parsed) && parsed > 0 ? parsed : null;

    private sealed record ProcessorInventory(string Name, int LogicalProcessorCount, int? PhysicalCoreCount);
}
