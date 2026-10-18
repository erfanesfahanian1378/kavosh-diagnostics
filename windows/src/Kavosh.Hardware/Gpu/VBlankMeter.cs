using System.Diagnostics;
using Vortice.DXGI;

namespace Kavosh.Hardware.Gpu;

/// <summary>
/// Measures a monitor's real refresh rate by waiting for its vertical blanks (the moment the
/// panel starts drawing a new frame) and timing the gaps — the number the panel is actually
/// running at, not a value read from settings.
/// </summary>
public sealed class VBlankMeter : IDisposable
{
    private readonly int _x, _y;
    private volatile bool _stop;
    private Thread? _thread;

    /// <summary>Raised on a background thread about twice a second with the measured rate in Hz.</summary>
    public event Action<double>? Measured;

    /// <param name="x">A point (desktop pixels) on the monitor to measure.</param>
    /// <param name="y">A point (desktop pixels) on the monitor to measure.</param>
    public VBlankMeter(int x, int y)
    {
        _x = x;
        _y = y;
    }

    /// <summary>Starts measuring. Returns false when the monitor can't be found (then nothing is measured).</summary>
    public bool Start()
    {
        if (!OperatingSystem.IsWindows() || _thread is not null) return false;
        IDXGIOutput? output = FindOutput();
        if (output is null) return false;
        _thread = new Thread(() => Loop(output)) { IsBackground = true, Name = "Kavosh vblank", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        return true;
    }

    private IDXGIOutput? FindOutput()
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            IDXGIOutput? fallback = null;
            for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Success && adapter is not null; a++)
            {
                using (adapter)
                {
                    for (uint o = 0; adapter.EnumOutputs(o, out IDXGIOutput? output).Success && output is not null; o++)
                    {
                        var rect = output.Description.DesktopCoordinates;
                        if (_x >= rect.Left && _x < rect.Right && _y >= rect.Top && _y < rect.Bottom)
                        {
                            fallback?.Dispose();
                            return output;
                        }
                        if (fallback is null) fallback = output;
                        else output.Dispose();
                    }
                }
            }
            return fallback;
        }
        catch
        {
            return null;
        }
    }

    private void Loop(IDXGIOutput output)
    {
        using (output)
        {
            var intervals = new Queue<double>();
            long last = 0, lastReport = Stopwatch.GetTimestamp();
            while (!_stop)
            {
                try { output.WaitForVBlank(); }
                catch { break; }
                long now = Stopwatch.GetTimestamp();
                if (last != 0)
                {
                    double dt = (double)(now - last) / Stopwatch.Frequency;
                    if (dt is > 0.001 and < 0.25)
                    {
                        intervals.Enqueue(dt);
                        if (intervals.Count > 240) intervals.Dequeue();
                    }
                }
                last = now;
                if (intervals.Count >= 20 && Stopwatch.GetElapsedTime(lastReport, now).TotalSeconds > 0.5)
                {
                    lastReport = now;
                    // The median ignores the odd missed blank.
                    var sorted = intervals.OrderBy(v => v).ToArray();
                    double median = sorted[sorted.Length / 2];
                    if (median > 0) Measured?.Invoke(1 / median);
                }
            }
        }
    }

    public void Dispose() => _stop = true;
}
