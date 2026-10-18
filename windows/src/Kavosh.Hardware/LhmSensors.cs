using LibreHardwareMonitor.Hardware;

namespace Kavosh.Hardware;

/// <summary>What LibreHardwareMonitor found on one update.</summary>
internal sealed class LhmReading
{
    public double? CpuTemperature;
    public double? CpuTemperatureMax;
    public double? CpuPower;
    public double? CpuClockGHz;
    public double? GpuTemperature;
    public double? GpuLoad;
    public double? GpuPower;
    public List<double> FanRpm = new();
    public bool HasCpuTemperature => CpuTemperature is not null;
}

/// <summary>
/// Wraps LibreHardwareMonitor. All LibreHardwareMonitor types stay inside this class so the
/// library is only loaded on Windows. CPU temperatures and power need administrator rights and
/// the PawnIO driver; GPU sensors work without them.
/// </summary>
internal sealed class LhmSensors : IDisposable
{
    private readonly Computer _computer;
    private readonly object _lock = new();
    private bool _open;

    public LhmSensors()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
        };
    }

    /// <summary>Opens the library (can take a few seconds while it probes the hardware).</summary>
    public void Open()
    {
        lock (_lock)
        {
            if (_open) return;
            _computer.Open();
            _open = true;
        }
    }

    public static bool PawnIoInstalled
    {
        get
        {
            try { return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; }
            catch { return false; }
        }
    }

    public static string? PawnIoVersion
    {
        get
        {
            try { return LibreHardwareMonitor.PawnIo.PawnIo.Version?.ToString(); }
            catch { return null; }
        }
    }

    public LhmReading Read()
    {
        var reading = new LhmReading();
        lock (_lock)
        {
            if (!_open) return reading;
            double? discreteTemp = null, discreteLoad = null, discretePower = null;
            double? integratedTemp = null, integratedLoad = null, integratedPower = null;
            foreach (IHardware hardware in _computer.Hardware)
            {
                Update(hardware);
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        ReadCpu(hardware, reading);
                        break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                        discreteTemp ??= Pick(hardware, SensorType.Temperature, "GPU Core", "GPU Hot Spot");
                        discreteLoad ??= Pick(hardware, SensorType.Load, "GPU Core", "D3D 3D");
                        discretePower ??= Pick(hardware, SensorType.Power, "GPU Package", "GPU Power", "GPU Core");
                        break;
                    case HardwareType.GpuIntel:
                        integratedTemp ??= Pick(hardware, SensorType.Temperature, "GPU Core", "GPU Package");
                        integratedLoad ??= Pick(hardware, SensorType.Load, "D3D 3D", "GPU Core");
                        integratedPower ??= Pick(hardware, SensorType.Power, "GPU Power", "GPU Package");
                        break;
                }
                foreach (var sensor in AllSensors(hardware))
                {
                    if (sensor.SensorType == SensorType.Fan && sensor.Value is { } rpm && rpm is >= 0 and < 20000)
                        reading.FanRpm.Add(rpm);
                }
            }
            // The busier GPU is the interesting one (laptops switch between them).
            if (discreteLoad is not null && (integratedLoad is null || discreteLoad >= integratedLoad))
            {
                reading.GpuLoad = discreteLoad;
                reading.GpuTemperature = discreteTemp ?? integratedTemp;
                reading.GpuPower = discretePower;
            }
            else
            {
                reading.GpuLoad = integratedLoad ?? discreteLoad;
                reading.GpuTemperature = integratedTemp ?? discreteTemp;
                reading.GpuPower = integratedPower ?? discretePower;
            }
        }
        return reading;
    }

    private static void ReadCpu(IHardware cpu, LhmReading reading)
    {
        // Intel: "CPU Package"; AMD: "Core (Tctl/Tdie)" or "Package".
        reading.CpuTemperature ??= Pick(cpu, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Package", "Core (Tctl)", "Core Max", "CCDs Max (Tdie)");
        var coreTemps = AllSensors(cpu)
            .Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0 and < 125 && !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase))
            .Select(s => (double)s.Value!.Value).ToList();
        if (coreTemps.Count > 0)
        {
            reading.CpuTemperatureMax = Math.Max(reading.CpuTemperatureMax ?? 0, coreTemps.Max());
            reading.CpuTemperature ??= coreTemps.Max();
        }
        reading.CpuPower ??= Pick(cpu, SensorType.Power, "CPU Package", "Package");
        var clocks = AllSensors(cpu)
            .Where(s => s.SensorType == SensorType.Clock && s.Name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase) && s.Value is > 100)
            .Select(s => (double)s.Value!.Value).ToList();
        if (clocks.Count > 0) reading.CpuClockGHz = clocks.Max() / 1000;
    }

    private static void Update(IHardware hardware)
    {
        try
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Update();
        }
        catch
        {
            // One misbehaving sensor chip shouldn't stop the others.
        }
    }

    private static IEnumerable<ISensor> AllSensors(IHardware hardware) =>
        hardware.Sensors.Concat(hardware.SubHardware.SelectMany(s => s.Sensors));

    private static double? Pick(IHardware hardware, SensorType type, params string[] names)
    {
        var sensors = AllSensors(hardware).Where(s => s.SensorType == type && s.Value is { } v && float.IsFinite(v)).ToList();
        foreach (string name in names)
        {
            var match = sensors.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match?.Value is { } value)
            {
                if (type == SensorType.Temperature && value is <= 0 or >= 130) continue;
                return value;
            }
        }
        return null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_open) return;
            try { _computer.Close(); } catch { /* shutting down */ }
            _open = false;
        }
    }
}
