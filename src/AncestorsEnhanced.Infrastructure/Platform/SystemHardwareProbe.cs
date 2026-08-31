using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace AncestorsEnhanced.Infrastructure.Platform;

/// <summary>
/// Queries only local operating-system inventory. Failures become an explicit
/// incomplete snapshot so recommendations cannot silently guess hardware.
/// </summary>
public sealed class SystemHardwareProbe : IHardwareProbe
{
    private static readonly Regex MemoryMegabytes = new(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*(?:MB|MiB)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Guid DxgiFactory1InterfaceId = new("770aae78-f26f-4dba-a829-253c83d1b387");

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
                adapters = MergeDetailedGraphicsAdapters(adapters, ReadDxDiagGraphicsAdapters());
            }
            return new(
                global::System.Environment.OSVersion.VersionString,
                processor.Name,
                processor.LogicalProcessorCount,
                processor.PhysicalCoreCount,
                installedMemory,
                adapters,
                adapters.Any(adapter => adapter.IsMemoryAuthoritative)
                    ? null
                    : includeDetailedGraphics
                        ? "Windows did not report dedicated GPU memory through DXGI or the bounded DxDiag scan. No automatic graphics recommendation was made."
                        : "Windows did not report dedicated GPU memory through DXGI. You can run the detailed hardware scan for a second, bounded source.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.ComponentModel.Win32Exception)
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
        string name = "Unavailable";
        try
        {
            using RegistryKey? processor = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (processor?.GetValue("ProcessorNameString") is string processorName && !string.IsNullOrWhiteSpace(processorName))
            {
                name = processorName.Trim();
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The processor name is supplementary; the native processor count remains useful.
        }

        return new(name, global::System.Environment.ProcessorCount, null);
    }

    [SupportedOSPlatform("windows")]
    private static ulong? ReadInstalledMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0 ? status.TotalPhysical : null;
    }

    [SupportedOSPlatform("windows")]
    private static GraphicsAdapterSnapshot[] ReadGraphicsAdapters()
    {
        GraphicsAdapterSnapshot[] dxgiAdapters = ReadDxgiGraphicsAdapters();
        return dxgiAdapters.Length > 0 ? dxgiAdapters : ReadDisplayDeviceNames();
    }

    [SupportedOSPlatform("windows")]
    private static GraphicsAdapterSnapshot[] ReadDisplayDeviceNames()
    {
        var adapters = new List<GraphicsAdapterSnapshot>();
        for (uint index = 0; ; index++)
        {
            var displayDevice = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref displayDevice, 0))
            {
                break;
            }

            string name = displayDevice.DeviceString?.Trim() ?? string.Empty;
            if (name.Length > 0)
            {
                adapters.Add(new GraphicsAdapterSnapshot(name, null, IsMemoryAuthoritative: false));
            }
        }

        return adapters
            .DistinctBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static GraphicsAdapterSnapshot[] ReadDxgiGraphicsAdapters()
    {
        IntPtr factory = IntPtr.Zero;
        try
        {
            Guid factoryInterfaceId = DxgiFactory1InterfaceId;
            if (CreateDXGIFactory1(ref factoryInterfaceId, out factory) < 0 || factory == IntPtr.Zero)
            {
                return [];
            }

            var adapters = new List<GraphicsAdapterSnapshot>();
            EnumAdapters1Delegate enumerate = GetComDelegate<EnumAdapters1Delegate>(factory, 12);
            for (uint index = 0; ; index++)
            {
                IntPtr adapter = IntPtr.Zero;
                int result = enumerate(factory, index, out adapter);
                if (unchecked((uint)result) == 0x887A0002 || result < 0 || adapter == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    GetDesc1Delegate getDescription = GetComDelegate<GetDesc1Delegate>(adapter, 10);
                    if (getDescription(adapter, out DxgiAdapterDescription description) >= 0 &&
                        (description.Flags & 0x2) == 0)
                    {
                        string name = description.Description?.Trim() ?? string.Empty;
                        if (name.Length > 0)
                        {
                            ulong? dedicatedMemory = description.DedicatedVideoMemory > 0
                                ? (ulong)description.DedicatedVideoMemory
                                : null;
                            adapters.Add(new GraphicsAdapterSnapshot(name, dedicatedMemory, dedicatedMemory is not null));
                        }
                    }
                }
                finally
                {
                    ReleaseComObject(adapter);
                }
            }

            return adapters
                .DistinctBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            return [];
        }
        finally
        {
            ReleaseComObject(factory);
        }
    }

    internal static GraphicsAdapterSnapshot[] ParseDxDiagXml(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document.SelectNodes("/DxDiag/DisplayDevices/DisplayDevice")?
            .Cast<XmlElement>()
            .Select(device =>
            {
                ulong? dedicated = ParseDxDiagMemory(device["DedicatedMemory"]?.InnerText);
                return new GraphicsAdapterSnapshot(
                    device["CardName"]?.InnerText.Trim() is { Length: > 0 } name ? name : "Unnamed graphics adapter",
                    dedicated ?? ParseDxDiagMemory(device["DisplayMemory"]?.InnerText),
                    IsMemoryAuthoritative: dedicated.HasValue);
            })
            .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    [SupportedOSPlatform("windows")]
    private static GraphicsAdapterSnapshot[] ReadDxDiagGraphicsAdapters()
    {
        string reportPath = Path.Combine(Path.GetTempPath(), $"aec-dxdiag-{Guid.NewGuid():N}.xml");
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = "dxdiag.exe",
                Arguments = $"/x \"{reportPath}\" /whql:off",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null || !process.WaitForExit(5000) || process.ExitCode != 0 || !File.Exists(reportPath))
            {
                return [];
            }

            FileInfo report = new(reportPath);
            if (report.Length is <= 0 or > 8 * 1024 * 1024)
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
            if (process is { HasExited: false })
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(500);
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                }
            }

            process?.Dispose();
            try
            {
                File.Delete(reportPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    internal static GraphicsAdapterSnapshot[] MergeDetailedGraphicsAdapters(
        IReadOnlyList<GraphicsAdapterSnapshot> ordinary,
        IReadOnlyList<GraphicsAdapterSnapshot> detailed)
    {
        var merged = new Dictionary<string, GraphicsAdapterSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (GraphicsAdapterSnapshot adapter in ordinary)
        {
            merged[adapter.Name] = adapter;
        }

        foreach (GraphicsAdapterSnapshot adapter in detailed)
        {
            if (!merged.TryGetValue(adapter.Name, out GraphicsAdapterSnapshot? existing) ||
                (!existing.IsMemoryAuthoritative && adapter.IsMemoryAuthoritative))
            {
                merged[adapter.Name] = adapter;
            }
        }

        return merged.Values.OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ulong? ParseDxDiagMemory(string? value)
    {
        Match match = MemoryMegabytes.Match(value ?? string.Empty);
        if (!match.Success || !double.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out double megabytes) || megabytes <= 0)
        {
            return null;
        }

        double bytes = megabytes * 1024d * 1024d;
        return bytes <= ulong.MaxValue ? (ulong)bytes : null;
    }

    private static ulong? ReadPositiveUInt64(string? value) =>
        ulong.TryParse(value, out ulong parsed) && parsed > 0 ? parsed : null;

    private static T GetComDelegate<T>(IntPtr instance, int slot) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(instance);
        IntPtr method = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(method);
    }

    private static void ReleaseComObject(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
        {
            _ = GetComDelegate<ReleaseDelegate>(instance, 2)(instance);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSystemId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr factory, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr adapter, out DxgiAdapterDescription description);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint deviceIndex, ref DisplayDevice displayDevice, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    private sealed record ProcessorInventory(string Name, int LogicalProcessorCount, int? PhysicalCoreCount);
}
