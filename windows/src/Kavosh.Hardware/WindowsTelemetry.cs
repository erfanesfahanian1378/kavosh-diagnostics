using System.Runtime.InteropServices;
using Kavosh.Core;

namespace Kavosh.Hardware;

/// <summary>
/// Takes one reading of everything the live dashboard shows. Call <see cref="Sample"/> about once
/// a second from one background thread; CPU load is measured between consecutive calls.
/// </summary>
internal sealed unsafe class WindowsTelemetry : ITelemetrySource
{
    private Native.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] _previous;
    private readonly PdhGpuCounter? _gpuCounter;
    private LhmSensors? _lhm;
    private string? _lhmProblem;
    private readonly Task _lhmStart;

    public WindowsTelemetry()
    {
        _previous = ReadProcessorTimes();
        try { _gpuCounter = new PdhGpuCounter(); } catch { _gpuCounter = null; }
        _lhmStart = Task.Run(StartLhm);
    }

    private void StartLhm()
    {
        try
        {
            var lhm = new LhmSensors();
            lhm.Open();
            _lhm = lhm;
        }
        catch (Exception e)
        {
            _lhmProblem = e.Message;
        }
    }

    public SensorStatus Status
    {
        get
        {
            bool admin = Elevation.IsAdministrator;
            bool pawn = PawnIo.IsInstalled;
            return new SensorStatus(admin, pawn, PawnIo.Version, _lhm is not null, _lhmProblem);
        }
    }

    public SensorSnapshot Sample()
    {
        var snapshot = new SensorSnapshot { Timestamp = DateTime.UtcNow };

        // CPU load per logical processor.
        var current = ReadProcessorTimes();
        if (current.Length == _previous.Length && current.Length > 0)
        {
            double busyAll = 0, totalAll = 0;
            var perCore = new List<double>(current.Length);
            for (int i = 0; i < current.Length; i++)
            {
                double idle = current[i].IdleTime - _previous[i].IdleTime;
                double total = current[i].KernelTime - _previous[i].KernelTime + current[i].UserTime - _previous[i].UserTime;
                double busy = Math.Max(0, total - idle);
                perCore.Add(total > 0 ? Math.Clamp(busy / total * 100, 0, 100) : 0);
                busyAll += busy;
                totalAll += total;
            }
            snapshot.CpuLoadPercent = totalAll > 0 ? Math.Clamp(busyAll / totalAll * 100, 0, 100) : 0;
            snapshot.PerCoreLoadPercent = perCore;
        }
        _previous = current;

        // Memory.
        var memory = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref memory))
        {
            snapshot.RamTotalGB = memory.ullTotalPhys / 1073741824.0;
            snapshot.RamUsedGB = (memory.ullTotalPhys - memory.ullAvailPhys) / 1073741824.0;
            snapshot.MemoryPressure = memory.dwMemoryLoad >= 95 ? MemoryPressureLevel.Critical
                : memory.dwMemoryLoad >= 85 ? MemoryPressureLevel.Warning : MemoryPressureLevel.Normal;
        }

        // Sensors.
        LhmReading? lhm = null;
        try { lhm = _lhm?.Read(); } catch { lhm = null; }
        if (lhm is not null)
        {
            snapshot.CpuTemperatureCelsius = lhm.CpuTemperature;
            snapshot.CpuTemperatureMaxCelsius = lhm.CpuTemperatureMax;
            snapshot.CpuPowerWatts = lhm.CpuPower;
            snapshot.CpuClockGHz = lhm.CpuClockGHz;
            snapshot.GpuTemperatureCelsius = lhm.GpuTemperature;
            snapshot.GpuPowerWatts = lhm.GpuPower;
            snapshot.GpuLoadPercent = lhm.GpuLoad;
            snapshot.FanRpm = lhm.FanRpm;
            snapshot.FanCount = lhm.FanRpm.Count > 0 ? lhm.FanRpm.Count : null;
            snapshot.HasSensorAccess = lhm.HasCpuTemperature;
        }
        snapshot.GpuLoadPercent ??= _gpuCounter?.Read();

        // On battery, the battery's discharge rate is the whole computer's power draw.
        var battery = WindowsBattery.Read();
        if (battery?.DischargeWatts is { } watts && !battery.IsPluggedIn) snapshot.SystemPowerWatts = watts;

        snapshot.ThermalState = ThermalFromTemperature(snapshot.CpuTemperatureMaxCelsius ?? snapshot.CpuTemperatureCelsius);
        return snapshot;
    }

    internal static ThermalLevel ThermalFromTemperature(double? celsius) => celsius switch
    {
        null => ThermalLevel.Nominal,
        < 85 => ThermalLevel.Nominal,
        < 94 => ThermalLevel.Fair,
        < 99 => ThermalLevel.Serious,
        _ => ThermalLevel.Critical,
    };

    private static Native.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] ReadProcessorTimes()
    {
        int count = Environment.ProcessorCount;
        var result = new Native.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[count];
        fixed (Native.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION* p = result)
        {
            uint size = (uint)(sizeof(Native.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION) * count);
            int status = Native.NtQuerySystemInformation(Native.SystemProcessorPerformanceInformation, p, size, out uint returned);
            if (status != 0) return Array.Empty<Native.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
            int filled = (int)(returned / sizeof(Native.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));
            if (filled < count) Array.Resize(ref result, filled);
        }
        return result;
    }

    public void Dispose()
    {
        try { _lhmStart.Wait(TimeSpan.FromSeconds(3)); } catch { /* ignore */ }
        _lhm?.Dispose();
        _gpuCounter?.Dispose();
    }
}

/// <summary>GPU 3D engine utilisation from the Windows "GPU Engine" performance counters (what Task Manager shows).</summary>
internal sealed unsafe class PdhGpuCounter : IDisposable
{
    private readonly IntPtr _query;
    private readonly IntPtr _counter;

    public PdhGpuCounter()
    {
        if (Native.PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) throw new InvalidOperationException("PDH unavailable");
        if (Native.PdhAddEnglishCounterW(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _counter) != 0)
        {
            Native.PdhCloseQuery(_query);
            throw new InvalidOperationException("GPU counters unavailable");
        }
        Native.PdhCollectQueryData(_query);
    }

    public double? Read()
    {
        if (Native.PdhCollectQueryData(_query) != 0) return null;
        uint size = 0;
        uint status = Native.PdhGetFormattedCounterArrayW(_counter, Native.PDH_FMT_DOUBLE | Native.PDH_FMT_NOCAP100, ref size, out uint count, null);
        if (status != Native.PDH_MORE_DATA || size == 0) return count == 0 ? 0 : null;
        var buffer = new byte[size];
        fixed (byte* p = buffer)
        {
            if (Native.PdhGetFormattedCounterArrayW(_counter, Native.PDH_FMT_DOUBLE | Native.PDH_FMT_NOCAP100, ref size, out count, p) != 0) return null;
            // PDH_FMT_COUNTERVALUE_ITEM_W: { LPWSTR name; { DWORD status; double value; } }
            int itemSize = IntPtr.Size + 16;
            var perAdapter = new Dictionary<string, double>();
            for (int i = 0; i < count; i++)
            {
                byte* item = p + i * itemSize;
                string name = Marshal.PtrToStringUni(*(IntPtr*)item) ?? "";
                uint cStatus = *(uint*)(item + IntPtr.Size);
                double value = *(double*)(item + IntPtr.Size + 8);
                if (cStatus > 1 || !double.IsFinite(value)) continue;
                // Instance names look like "pid_1234_luid_0x00000000_0x0000C2A1_phys_0_eng_0_engtype_3D".
                int luid = name.IndexOf("luid_", StringComparison.Ordinal);
                int phys = name.IndexOf("_phys", StringComparison.Ordinal);
                string adapter = luid >= 0 && phys > luid ? name[luid..phys] : "gpu";
                perAdapter[adapter] = perAdapter.GetValueOrDefault(adapter) + value;
            }
            return perAdapter.Count == 0 ? 0 : Math.Clamp(perAdapter.Values.Max(), 0, 100);
        }
    }

    public void Dispose() => Native.PdhCloseQuery(_query);
}
