using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Kavosh.Core;
using Microsoft.Win32;

namespace Kavosh.Hardware;

/// <summary>Reads what this PC is: model, CPU, GPU, memory, display, Windows version and drives.</summary>
internal static partial class WindowsSystemProbe
{
    /// <summary>Fast part (registry and kernel calls only). Safe to call on the UI thread.</summary>
    public static SystemSpecs QuickSpecs()
    {
        var specs = new SystemSpecs
        {
            Architecture = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => "x64",
            },
            OperatingSystem = WindowsVersion(),
        };

        using (var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
        {
            specs.CpuName = CleanCpuName(cpuKey?.GetValue("ProcessorNameString") as string ?? "");
            if (cpuKey?.GetValue("~MHz") is int mhz && mhz > 0) specs.CpuMaxClockGHz = mhz / 1000.0;
        }

        var (cores, threads, clusters) = CpuTopology();
        specs.CpuCores = cores > 0 ? cores : Environment.ProcessorCount;
        specs.CpuThreads = threads > 0 ? threads : Environment.ProcessorCount;
        specs.CoreClusters = clusters;

        var memory = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref memory)) specs.RamCapacityGB = memory.ullTotalPhys / 1073741824.0;

        var gpus = GpuAdapters.List();
        if (gpus.Count > 0)
        {
            var main = gpus[0];
            specs.GpuName = CleanGpuName(main.Name);
            specs.HasUnifiedMemory = main.IsIntegrated;
            specs.GpuMemoryGB = main.IsIntegrated ? null : main.DedicatedVideoMemory / 1073741824.0;
            specs.OtherGpus = gpus.Skip(1).Select(g => CleanGpuName(g.Name)).ToList();
        }

        ReadDisplayMode(specs);
        return specs;
    }

    /// <summary>Slow part (WMI). Call from a background thread.</summary>
    public static void AddSlowDetails(SystemSpecs specs)
    {
        // Model.
        var system = Wmi.Query("SELECT Manufacturer, Model, SystemSKUNumber, SystemFamily, TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
        var product = Wmi.Query("SELECT Name, Version, IdentifyingNumber FROM Win32_ComputerSystemProduct").FirstOrDefault();
        var board = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        string maker = CleanManufacturer(system?.Str("Manufacturer") ?? "");
        string model = system?.Str("Model") ?? "";
        string? version = product?.Str("Version");
        string? family = system?.Str("SystemFamily");
        bool genericSystem = IsPlaceholder(maker) || IsPlaceholder(model);
        if (genericSystem && board is not null)
        {
            // Self-built desktops: the motherboard is the best name there is.
            maker = CleanManufacturer(board.Str("Manufacturer") ?? "");
            model = board.Str("Product") ?? "";
        }
        if (maker == "Lenovo" && !string.IsNullOrEmpty(version) && !IsPlaceholder(version) && !version.Equals("Lenovo", StringComparison.OrdinalIgnoreCase))
        {
            // Lenovo stores the machine type in Model and the marketing name in Version.
            specs.ModelIdentifier = model;
            model = version;
        }
        else
        {
            specs.ModelIdentifier = system?.Str("SystemSKUNumber") is { } sku && !IsPlaceholder(sku) ? sku : family ?? "";
        }
        string name = model.StartsWith(maker, StringComparison.OrdinalIgnoreCase) || maker.Length == 0 ? model : $"{maker} {model}";
        if (!string.IsNullOrWhiteSpace(name)) specs.ModelName = name.Trim();

        // CPU (WMI has the core count for every package, and the rated clock).
        var processors = Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
        if (processors.Count > 0)
        {
            if (string.IsNullOrEmpty(specs.CpuName)) specs.CpuName = CleanCpuName(processors[0].Str("Name") ?? "");
            long cores = processors.Sum(p => p.Long("NumberOfCores") ?? 0);
            long threads = processors.Sum(p => p.Long("NumberOfLogicalProcessors") ?? 0);
            if (cores > 0) specs.CpuCores = (int)cores;
            if (threads > 0) specs.CpuThreads = (int)threads;
            if (processors[0].Long("MaxClockSpeed") is { } mhz && mhz > 0) specs.CpuMaxClockGHz = mhz / 1000.0;
        }

        // Memory modules.
        var modules = Wmi.Query("SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer, SMBIOSMemoryType, MemoryType FROM Win32_PhysicalMemory");
        if (modules.Count > 0)
        {
            specs.RamModuleCount = modules.Count;
            ulong total = (ulong)modules.Sum(m => (decimal)(m.ULong("Capacity") ?? 0));
            if (total > 0) specs.RamCapacityGB = total / 1073741824.0;
            long speed = modules.Max(m => m.Long("ConfiguredClockSpeed") is long c && c > 0 ? c : m.Long("Speed") ?? 0);
            if (speed > 0) specs.RamSpeedMHz = (int)speed;
            long type = modules[0].Long("SMBIOSMemoryType") ?? 0;
            specs.RamType = MemoryTypeName(type) ?? MemoryTypeName(LegacyMemoryType(modules[0].Long("MemoryType") ?? 0));
            var makers = modules.Select(m => CleanMemoryMaker(m.Str("Manufacturer"))).Where(m => m is not null).Distinct().ToList();
            if (makers.Count > 0) specs.RamManufacturer = string.Join(" / ", makers);
        }

        // Firmware.
        if (Wmi.Query("SELECT SMBIOSBIOSVersion FROM Win32_BIOS").FirstOrDefault()?.Str("SMBIOSBIOSVersion") is { } bios)
            specs.FirmwareVersion = bios;

        // Display name and size.
        ReadMonitorName(specs);

        // Drives.
        specs.StorageDevices = WindowsDrives.AllDevices();
    }

    // ── CPU topology ──

    /// <summary>Physical cores, logical processors and P/E core groups from the Windows scheduler's own data.</summary>
    internal static unsafe (int Cores, int Threads, List<CoreCluster> Clusters) CpuTopology()
    {
        uint length = 0;
        Native.GetLogicalProcessorInformationEx(Native.RelationProcessorCore, null, ref length);
        if (length == 0) return (0, 0, new List<CoreCluster>());
        var managed = new byte[length];
        var byClass = new SortedDictionary<int, int>();
        int cores = 0, threads = 0;
        fixed (byte* p = managed)
        {
            if (!Native.GetLogicalProcessorInformationEx(Native.RelationProcessorCore, p, ref length)) return (0, 0, new List<CoreCluster>());
            uint offset = 0;
            while (offset < length)
            {
                byte* entry = p + offset;
                int relationship = *(int*)entry;
                uint size = *(uint*)(entry + 4);
                if (size == 0) break;
                if (relationship == Native.RelationProcessorCore)
                {
                    byte efficiencyClass = entry[9];
                    ushort groupCount = *(ushort*)(entry + 8 + 22);
                    int logical = 0;
                    for (int g = 0; g < groupCount; g++)
                    {
                        ulong mask = *(ulong*)(entry + 8 + 24 + g * 16);
                        logical += BitOperations.PopCount(mask);
                    }
                    cores++;
                    threads += Math.Max(1, logical);
                    byClass[efficiencyClass] = byClass.GetValueOrDefault(efficiencyClass) + 1;
                }
                offset += size;
            }
        }
        var clusters = new List<CoreCluster>();
        if (byClass.Count > 1)
        {
            // Higher efficiency class = faster core.
            var classes = byClass.Keys.Reverse().ToList();
            for (int i = 0; i < classes.Count; i++)
            {
                string name = i == 0 ? "Performance" : i == 1 ? "Efficient" : "Low Power Efficient";
                clusters.Add(new CoreCluster(name, byClass[classes[i]]));
            }
        }
        return (cores, threads, clusters);
    }

    // ── Display ──

    private static void ReadDisplayMode(SystemSpecs specs)
    {
        try
        {
            var mode = new Native.DEVMODEW { dmDeviceName = "", dmFormName = "" };
            mode.dmSize = (ushort)Marshal.SizeOf<Native.DEVMODEW>();
            if (Native.EnumDisplaySettingsW(null, Native.ENUM_CURRENT_SETTINGS, ref mode) && mode.dmPelsWidth > 0)
            {
                specs.DisplayResolution = $"{mode.dmPelsWidth} × {mode.dmPelsHeight}";
                if (mode.dmDisplayFrequency > 1) specs.DisplayRefreshRateHz = (int)mode.dmDisplayFrequency;
            }
        }
        catch
        {
            // No display (service session).
        }
    }

    private static void ReadMonitorName(SystemSpecs specs)
    {
        var ids = Wmi.Query("SELECT InstanceName, UserFriendlyName, ManufacturerName FROM WmiMonitorID", Wmi.RootWmi);
        var connections = Wmi.Query("SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams", Wmi.RootWmi);
        var sizes = Wmi.Query("SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams", Wmi.RootWmi);
        if (ids.Count == 0) return;
        // Prefer the built-in panel (VideoOutputTechnology 0x80000000 = internal).
        string? internalInstance = connections.FirstOrDefault(c => (c.Long("VideoOutputTechnology") ?? 0) == 0x80000000L)?.Str("InstanceName");
        var chosen = ids.FirstOrDefault(i => i.Str("InstanceName") == internalInstance) ?? ids[0];
        string? instance = chosen.Str("InstanceName");
        bool builtIn = instance is not null && instance == internalInstance;
        string? friendly = chosen.CodeUnits("UserFriendlyName");
        string? inches = null;
        var size = sizes.FirstOrDefault(s => s.Str("InstanceName") == instance);
        if (size is not null && size.Long("MaxHorizontalImageSize") is { } w && size.Long("MaxVerticalImageSize") is { } h && w > 0 && h > 0)
        {
            double diagonal = Math.Sqrt(w * w + h * h) / 2.54;
            if (diagonal is > 7 and < 100) inches = Fmt.Fixed(diagonal, 1).Replace(".0", "") + "\"";
        }
        string label = builtIn ? "Built-in display" : friendly ?? "External display";
        if (builtIn && friendly is not null) label += $" ({friendly})";
        specs.DisplayName = inches is null ? label : $"{inches} {label}";
    }

    // ── Windows version ──

    internal static string WindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = key?.GetValue("ProductName") as string ?? "Windows";
            string display = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string ?? "";
            string build = key?.GetValue("CurrentBuildNumber") as string ?? "";
            int ubr = key?.GetValue("UBR") is int u ? u : 0;
            // Windows 11 still says "Windows 10" in ProductName.
            if (int.TryParse(build, out int b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
            string text = product;
            if (display.Length > 0) text += " " + display;
            if (build.Length > 0) text += ubr > 0 ? $" (build {build}.{ubr})" : $" (build {build})";
            return text;
        }
        catch
        {
            return RuntimeInformation.OSDescription;
        }
    }

    // ── Name clean-up ──

    internal static string CleanCpuName(string name)
    {
        string text = name.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace("CPU @", "@");
        text = SpacesRegex().Replace(text, " ").Trim();
        // "13th Gen Intel Core i7-1360P" → keep; drop trailing frequency for AMD-style "... 8-Core Processor".
        text = text.Replace(" Processor", "");
        return text;
    }

    internal static string CleanGpuName(string name) =>
        SpacesRegex().Replace(name.Replace("(R)", "").Replace("(TM)", ""), " ").Trim();

    internal static string CleanManufacturer(string maker)
    {
        string m = maker.Trim();
        string lower = m.ToLowerInvariant();
        if (lower.StartsWith("lenovo")) return "Lenovo";
        if (lower.StartsWith("dell")) return "Dell";
        if (lower is "hp" or "hewlett-packard" || lower.StartsWith("hp ")) return "HP";
        if (lower.StartsWith("asus")) return "ASUS";
        if (lower.StartsWith("micro-star") || lower == "msi") return "MSI";
        if (lower.StartsWith("acer")) return "Acer";
        if (lower.StartsWith("microsoft")) return "Microsoft";
        if (lower.StartsWith("gigabyte")) return "Gigabyte";
        if (lower.StartsWith("samsung")) return "Samsung";
        if (lower.StartsWith("razer")) return "Razer";
        if (lower.StartsWith("huawei")) return "Huawei";
        if (lower.StartsWith("xiaomi") || lower.StartsWith("timi")) return "Xiaomi";
        if (lower.StartsWith("framework")) return "Framework";
        if (lower.StartsWith("lg electronics")) return "LG";
        if (lower.StartsWith("toshiba") || lower.StartsWith("dynabook")) return "Dynabook";
        if (lower.StartsWith("fujitsu")) return "Fujitsu";
        if (lower.StartsWith("medion")) return "Medion";
        return m;
    }

    private static bool IsPlaceholder(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        string lower = text.Trim().ToLowerInvariant();
        return lower is "system manufacturer" or "system product name" or "to be filled by o.e.m." or "default string"
            or "not applicable" or "none" or "o.e.m." or "oem" or "system version" or "undefined" or "invalid";
    }

    internal static string? MemoryTypeName(long smbiosType) => smbiosType switch
    {
        18 => "DDR",
        19 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => null,
    };

    private static long LegacyMemoryType(long memoryType) => memoryType switch
    {
        20 => 18,
        21 => 19,
        24 => 24,
        26 => 26,
        _ => 0,
    };

    internal static string? CleanMemoryMaker(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string text = raw.Trim();
        string upper = text.ToUpperInvariant();
        if (upper.Contains("SAMSUNG") || upper.StartsWith("80CE") || upper.StartsWith("00CE")) return "Samsung";
        if (upper.Contains("HYNIX") || upper.StartsWith("80AD") || upper.StartsWith("00AD")) return "SK hynix";
        if (upper.Contains("MICRON") || upper.StartsWith("802C") || upper.StartsWith("002C")) return "Micron";
        if (upper.Contains("CRUCIAL") || upper.StartsWith("859B")) return "Crucial";
        if (upper.Contains("KINGSTON") || upper.StartsWith("0198") || upper.StartsWith("8198")) return "Kingston";
        if (upper.Contains("CORSAIR") || upper.StartsWith("029E")) return "Corsair";
        if (upper.Contains("G.SKILL") || upper.StartsWith("04CD")) return "G.Skill";
        if (upper.Contains("ADATA") || upper.Contains("A-DATA") || upper.StartsWith("04CB")) return "ADATA";
        if (upper.Contains("NANYA") || upper.StartsWith("830B")) return "Nanya";
        if (upper.Contains("CXMT") || upper.StartsWith("8A91")) return "CXMT";
        if (upper is "UNKNOWN" or "UNDEFINED" || upper.Trim('0', ' ').Length == 0) return null;
        return text;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
