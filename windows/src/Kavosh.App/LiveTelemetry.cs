using Avalonia.Threading;
using Kavosh.Core;
using Kavosh.Hardware;

namespace Kavosh.App;

/// <summary>Peak and average readings collected while a test runs.</summary>
public sealed class TelemetryRecording
{
    public double? PeakCpuTemperature { get; private set; }
    public double? PeakGpuTemperature { get; private set; }
    private double _powerSum;
    private int _powerSamples;
    public ThermalLevel WorstThermal { get; private set; } = ThermalLevel.Nominal;
    public int Samples { get; private set; }

    public double? AveragePower => _powerSamples > 0 ? _powerSum / _powerSamples : null;

    public void Add(SensorSnapshot s)
    {
        Samples++;
        if ((s.CpuTemperatureMaxCelsius ?? s.CpuTemperatureCelsius) is { } t) PeakCpuTemperature = Math.Max(PeakCpuTemperature ?? t, t);
        if (s.GpuTemperatureCelsius is { } g) PeakGpuTemperature = Math.Max(PeakGpuTemperature ?? g, g);
        if ((s.SystemPowerWatts ?? s.CpuPowerWatts) is { } p)
        {
            _powerSum += p;
            _powerSamples++;
        }
        if (s.ThermalState > WorstThermal) WorstThermal = s.ThermalState;
    }
}

/// <summary>Samples the sensors once a second on a background thread and keeps the last 90 seconds.</summary>
public sealed class LiveTelemetry : IDisposable
{
    public const int Capacity = 90;

    public SensorSnapshot? Latest { get; private set; }
    public SensorStatus? Status { get; private set; }
    public List<double> CpuHistory { get; } = new();
    public List<double> RamHistory { get; } = new();
    public List<double> GpuHistory { get; } = new();
    public List<double> CpuTemperatureHistory { get; } = new();
    public List<double> PowerHistory { get; } = new();

    /// <summary>Raised on the UI thread after every sample.</summary>
    public event Action? Updated;

    private readonly Dictionary<Guid, TelemetryRecording> _recordings = new();
    private Thread? _thread;
    private volatile bool _stop;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Kavosh telemetry", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private void Loop()
    {
        ITelemetrySource? source = null;
        try
        {
            source = Platform.CreateTelemetry();
            Thread.Sleep(300);
            while (!_stop)
            {
                var started = DateTime.UtcNow;
                SensorSnapshot snapshot;
                SensorStatus status;
                try
                {
                    snapshot = source.Sample();
                    status = source.Status;
                }
                catch
                {
                    snapshot = new SensorSnapshot();
                    status = new SensorStatus(false, false, null, false, null);
                }
                Dispatcher.UIThread.Post(() => Publish(snapshot, status));
                var wait = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - started);
                if (wait > TimeSpan.Zero) Thread.Sleep(wait);
            }
        }
        finally
        {
            source?.Dispose();
        }
    }

    public Guid BeginRecording()
    {
        var id = Guid.NewGuid();
        var recording = new TelemetryRecording();
        if (Latest is not null) recording.Add(Latest);
        _recordings[id] = recording;
        return id;
    }

    public TelemetryRecording EndRecording(Guid id) => _recordings.Remove(id, out var r) ? r : new TelemetryRecording();

    public TelemetryRecording? Peek(Guid id) => _recordings.GetValueOrDefault(id);

    internal void Publish(SensorSnapshot snapshot, SensorStatus status)
    {
        Latest = snapshot;
        Status = status;
        Append(CpuHistory, snapshot.CpuLoadPercent);
        Append(RamHistory, snapshot.RamLoadPercent);
        Append(GpuHistory, snapshot.GpuLoadPercent ?? 0);
        if (snapshot.CpuTemperatureCelsius is { } t) Append(CpuTemperatureHistory, t);
        if ((snapshot.SystemPowerWatts ?? snapshot.CpuPowerWatts) is { } p) Append(PowerHistory, p);
        foreach (var recording in _recordings.Values) recording.Add(snapshot);
        Updated?.Invoke();
    }

    private static void Append(List<double> list, double value)
    {
        list.Add(value);
        if (list.Count > Capacity) list.RemoveRange(0, list.Count - Capacity);
    }

    public void Dispose() => _stop = true;
}
