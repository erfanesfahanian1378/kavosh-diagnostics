using System.Runtime.InteropServices;
using Kavosh.Core;

namespace Kavosh.Hardware;

/// <summary>
/// Readings for non-Windows systems, used while developing and in automated tests. Linux /proc
/// gives CPU, memory and load; everything Windows-only is left empty.
/// </summary>
internal static class FallbackProbe
{
    public static SystemSpecs QuickSpecs()
    {
        var specs = new SystemSpecs
        {
            ModelName = Environment.MachineName,
            Architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64",
            OperatingSystem = RuntimeInformation.OSDescription,
            CpuThreads = Environment.ProcessorCount,
            CpuCores = Environment.ProcessorCount,
            RamCapacityGB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0,
            GpuName = "—",
        };
        try
        {
            if (File.Exists("/proc/cpuinfo"))
            {
                var lines = File.ReadAllLines("/proc/cpuinfo");
                string? model = lines.FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim();
                if (model is not null) specs.CpuName = WindowsSystemProbe.CleanCpuName(model);
                var coreIds = lines.Where(l => l.StartsWith("core id")).Select(l => l.Split(':', 2)[1].Trim()).Distinct().Count();
                var packages = lines.Where(l => l.StartsWith("physical id")).Select(l => l.Split(':', 2)[1].Trim()).Distinct().Count();
                if (coreIds > 0) specs.CpuCores = coreIds * Math.Max(1, packages);
            }
            if (MemInfo() is { } mem) specs.RamCapacityGB = mem.TotalKb / 1048576.0;
        }
        catch
        {
            // Keep the defaults.
        }
        return specs;
    }

    public static void AddSlowDetails(SystemSpecs specs)
    {
        try
        {
            string vendor = ReadText("/sys/class/dmi/id/sys_vendor");
            string product = ReadText("/sys/class/dmi/id/product_name");
            if (product.Length > 0) specs.ModelName = WindowsSystemProbe.CleanManufacturer(vendor) is { Length: > 0 } v && !product.StartsWith(v) ? $"{v} {product}" : product;
            string bios = ReadText("/sys/class/dmi/id/bios_version");
            if (bios.Length > 0) specs.FirmwareVersion = bios;
        }
        catch
        {
            // Not available in containers.
        }
    }

    public static BatteryReading? ReadBattery()
    {
        try
        {
            string dir = Directory.Exists("/sys/class/power_supply") ? Directory.GetDirectories("/sys/class/power_supply", "BAT*").FirstOrDefault() ?? "" : "";
            if (dir.Length == 0) return null;
            long Read(string name) => long.TryParse(ReadText(Path.Combine(dir, name)), out long v) ? v : 0;
            long voltage = Read("voltage_min_design");
            long design = Read("energy_full_design") / 1000;
            long full = Read("energy_full") / 1000;
            if (design == 0 && voltage > 0)
            {
                design = Read("charge_full_design") * voltage / 1_000_000_000;
                full = Read("charge_full") * voltage / 1_000_000_000;
            }
            string status = ReadText(Path.Combine(dir, "status"));
            return new BatteryReading
            {
                DesignCapacityMwh = (int)design,
                FullChargeCapacityMwh = (int)full,
                CycleCount = (int)Read("cycle_count"),
                ChargePercent = (int)Read("capacity"),
                IsCharging = status == "Charging",
                IsPluggedIn = status is "Charging" or "Full" or "Not charging",
                FullyCharged = status == "Full",
                RateMw = status == "Discharging" ? -(int)(Read("power_now") / 1000) : null,
            };
        }
        catch
        {
            return null;
        }
    }

    public static DriveHealthReport ReadDriveHealth() => new();

    internal static (long TotalKb, long AvailableKb)? MemInfo()
    {
        if (!File.Exists("/proc/meminfo")) return null;
        long total = 0, available = 0;
        foreach (string line in File.ReadLines("/proc/meminfo"))
        {
            var parts = line.Split(':', 2);
            if (parts.Length < 2) continue;
            long value = long.TryParse(parts[1].Trim().Split(' ')[0], out long v) ? v : 0;
            if (parts[0] == "MemTotal") total = value;
            else if (parts[0] == "MemAvailable") available = value;
        }
        return total > 0 ? (total, available) : null;
    }

    private static string ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : ""; }
        catch { return ""; }
    }
}

internal sealed class FallbackTelemetry : ITelemetrySource
{
    private long[][] _previous = ReadStat();

    public SensorStatus Status => new(false, false, null, false, "Hardware sensors are only read on Windows.");

    public SensorSnapshot Sample()
    {
        var snapshot = new SensorSnapshot();
        var current = ReadStat();
        if (current.Length == _previous.Length && current.Length > 1)
        {
            var perCore = new List<double>();
            for (int i = 0; i < current.Length; i++)
            {
                double total = current[i].Sum() - _previous[i].Sum();
                double idle = current[i][3] + current[i][4] - _previous[i][3] - _previous[i][4];
                double load = total > 0 ? Math.Clamp((total - idle) / total * 100, 0, 100) : 0;
                if (i == 0) snapshot.CpuLoadPercent = load;
                else perCore.Add(load);
            }
            snapshot.PerCoreLoadPercent = perCore;
        }
        _previous = current;
        if (FallbackProbe.MemInfo() is { } mem)
        {
            snapshot.RamTotalGB = mem.TotalKb / 1048576.0;
            snapshot.RamUsedGB = (mem.TotalKb - mem.AvailableKb) / 1048576.0;
        }
        return snapshot;
    }

    private static long[][] ReadStat()
    {
        try
        {
            if (!File.Exists("/proc/stat")) return Array.Empty<long[]>();
            return File.ReadLines("/proc/stat")
                .Where(l => l.StartsWith("cpu"))
                .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(v => long.TryParse(v, out long n) ? n : 0).ToArray())
                .Where(a => a.Length >= 5)
                .ToArray();
        }
        catch
        {
            return Array.Empty<long[]>();
        }
    }

    public void Dispose() { }
}
