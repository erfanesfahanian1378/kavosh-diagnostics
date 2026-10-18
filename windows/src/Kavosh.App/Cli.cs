using System.Globalization;
using Kavosh.Core;
using Kavosh.Hardware;
using Kavosh.Hardware.Gpu;

namespace Kavosh.App;

/// <summary>
/// Command-line mode. Lets you check every reading and run the engines without the window:
///
///     Kavosh.exe --dump
///     Kavosh.exe --cpu [--quick] [--duration 60]
///     Kavosh.exe --memory [--quick]
///     Kavosh.exe --gpu [--quick] [--duration 30]
///     Kavosh.exe --ssd D:\ [--quick]
/// </summary>
internal static class Cli
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        bool quick = args.Contains("--quick");
        double? duration = Value("--duration", args) is { } d && double.TryParse(d, NumberStyles.Float, Invariant, out double seconds) ? seconds : null;
        try
        {
            return args[0] switch
            {
                "--dump" => Dump(),
                "--cpu" => RunCpu(quick, duration),
                "--memory" => RunMemory(quick),
                "--gpu" => RunGpu(quick, duration ?? 20),
                "--ssd" => RunSsd(Value("--ssd", args) ?? Path.GetTempPath(), quick),
                "--version" => Version(),
                _ => Help(),
            };
        }
        catch (Exception e)
        {
            Console.WriteLine($"failed: {e.Message}");
            return 1;
        }
    }

    private static string? Value(string flag, string[] args)
    {
        int index = Array.IndexOf(args, flag);
        if (index < 0 || index + 1 >= args.Length) return null;
        return args[index + 1].StartsWith("--", StringComparison.Ordinal) ? null : args[index + 1];
    }

    private static int Help()
    {
        Console.WriteLine("""
        Kavosh Diagnostics — command-line mode

          --dump                           print specs, live sensors, battery and drive health
          --cpu [--quick] [--duration S]   run the CPU benchmark
          --memory [--quick]               run the memory integrity test
          --gpu [--quick] [--duration S]   run the Direct3D 11 GPU benchmark
          --ssd [PATH] [--quick]           run the SSD speed test in PATH (default: temp folder)
          --version                        print the version

        With no arguments the app window opens.
        """);
        return 0;
    }

    private static int Version()
    {
        Console.WriteLine($"Kavosh Diagnostics {typeof(Cli).Assembly.GetName().Version}");
        return 0;
    }

    private static int Dump()
    {
        Console.WriteLine("== specs");
        var specs = Platform.QuickSpecs();
        Platform.AddSlowDetails(specs);
        Console.WriteLine(ResultStore.Serialize(specs));

        Console.WriteLine("\n== gpus");
        foreach (var gpu in Platform.GpuList())
            Console.WriteLine($"{gpu.Name}  vendor 0x{gpu.VendorId:X4}  dedicated {gpu.DedicatedVideoMemory / 1048576} MiB  shared {gpu.SharedSystemMemory / 1048576} MiB");

        Console.WriteLine("\n== sensors");
        using (var telemetry = Platform.CreateTelemetry())
        {
            telemetry.Sample();
            Thread.Sleep(1500);
            Console.WriteLine(ResultStore.Serialize(telemetry.Sample()));
            var status = telemetry.Status;
            Console.WriteLine($"administrator: {status.IsAdministrator}  PawnIO installed: {status.PawnIoInstalled} {status.PawnIoVersion}  sensor library: {status.SensorLibraryLoaded} {status.Problem}");
        }

        Console.WriteLine("\n== battery");
        if (Platform.ReadBattery() is { } b)
        {
            Console.WriteLine($"design {b.DesignCapacityMwh} mWh, full charge {b.FullChargeCapacityMwh} mWh, health {Fmt.Fixed(b.HealthPercent, 1)}%");
            Console.WriteLine($"cycles {b.CycleCount}, charge {b.ChargePercent}%, charging {b.IsCharging}, plugged in {b.IsPluggedIn}");
            Console.WriteLine($"rate {(b.RateMw is { } r ? r + " mW" : "—")}, voltage {(b.VoltageMv is { } v ? v + " mV" : "—")}, temperature {Fmt.Celsius(b.TemperatureCelsius)}");
            Console.WriteLine($"{b.Manufacturer} {b.DeviceName} {b.Chemistry}");
        }
        else
        {
            Console.WriteLine("none");
        }

        Console.WriteLine("\n== drives");
        Console.WriteLine(ResultStore.Serialize(Platform.ReadDriveHealth()));
        return 0;
    }

    private static int RunCpu(bool quick, double? duration)
    {
        var configuration = quick ? CpuBenchmarkConfiguration.Quick : new CpuBenchmarkConfiguration();
        if (duration is { } d) configuration.DurationSeconds = d;
        Console.WriteLine($"CPU benchmark: {configuration.Threads} threads, {configuration.MultiCoreUnits} multi-core units, {configuration.SingleCoreUnits} single-core units");
        var last = (CpuBenchmarkPhase)(-1);
        var result = new CpuBenchmarkEngine(configuration).Run(() => false, p =>
        {
            if (p.Phase == last) return;
            last = p.Phase;
            Console.WriteLine($"  stage: {p.Phase}");
        });
        Console.WriteLine(string.Format(Invariant, "  multi-core   {0,8:F1}   ({1:F1} ms for {2} units on {3} threads)", result.Score, result.MultiCoreBatchMs, result.MultiCoreUnits, result.ThreadsUsed));
        Console.WriteLine(string.Format(Invariant, "  single-core  {0,8:F1}   ({1:F1} ms for {2} units)", result.SingleCoreScore, result.SingleCoreBatchMs, result.SingleCoreUnits));
        if (result.SustainedScore is { } sustained) Console.WriteLine(string.Format(Invariant, "  sustained    {0,8:F1}", sustained));
        Console.WriteLine($"  verified: {result.ResultsVerified} (wrong results: {result.WrongResults})   throttled: {result.ThermalThrottleDetected}");
        return result.WrongResults > 0 ? 2 : 0;
    }

    private static int RunMemory(bool quick)
    {
        long size = quick ? 128L * 1024 * 1024 : MemoryTestEngine.RecommendedSize(Platform.PhysicalMemoryBytes);
        var r = new MemoryTestEngine(size).Run(() => false, _ => { });
        Console.WriteLine(string.Format(Invariant, "memory  {0} MiB tested  write {1:F0} MB/s  read {2:F0} MB/s  errors {3}  passed {4}  ({5:F2} s)",
            r.TestedMiB, r.AverageWriteThroughputMBps, r.AverageReadThroughputMBps, r.ErrorsDetected, r.Passed ? "yes" : "NO", r.DurationSeconds));
        return r.Passed ? 0 : 2;
    }

    private static int RunGpu(bool quick, double duration)
    {
        var configuration = new GpuBenchmarkConfiguration { TotalSeconds = duration };
        if (quick)
        {
            // A tiny render that only proves the shaders compile and frames complete (CI has no real GPU).
            configuration = new GpuBenchmarkConfiguration
            {
                OutputWidth = 320, OutputHeight = 180, Supersample = 1, Bands = 2, MinimumWarmUp = 1, MaximumWarmUp = 1,
                OrbitFrames = 3, MinimumScoredSeconds = 0, MaximumScoredOrbits = 1, TotalSeconds = 0, PreviewWidth = 160, PreviewHeight = 90,
            };
        }
        using var benchmark = new D3D11RaymarchBenchmark(configuration);
        Console.WriteLine($"GPU benchmark on {benchmark.AdapterName} ({benchmark.FeatureLevel}): {benchmark.InternalWidth}×{benchmark.InternalHeight} internal");
        var last = (GpuBenchmarkStage)(-1);
        var r = benchmark.Run(() => false, p =>
        {
            if (p.Stage == last) return;
            last = p.Stage;
            Console.WriteLine($"  stage: {p.Stage}");
        });
        Console.WriteLine(string.Format(Invariant, "  score        {0,8:F1}   ({1:F2} ms/frame, {2:F2} fps; slowest {3:F2} fps, fastest {4:F2} fps)",
            r.Score, r.AverageFrameTimeMs, r.AverageFps, r.MinFps, r.MaxFps));
        if (r.SustainedScore is { } sustained && r.SustainedAverageFrameTimeMs is { } ms)
            Console.WriteLine(string.Format(Invariant, "  sustained    {0,8:F1}   ({1:F2} ms/frame)", sustained, ms));
        return 0;
    }

    private static int RunSsd(string path, bool quick)
    {
        var profile = quick
            ? new StorageTestProfile { Name = "quick", Runs = 1, SecondsPerRun = 1, FileSizeBytes = 64L << 20, SettleSeconds = 0 }
            : StorageTestProfile.Standard;
        var engine = new StorageBenchmarkEngine(profile);
        Console.WriteLine($"SSD speed test in {path} ({profile.Name})");
        try
        {
            var r = engine.Run(path, () => false, _ => { });
            foreach (var m in r.Measurements)
            {
                Console.WriteLine(string.Format(Invariant, "  {0,-11} {1,-5}  {2,9:F1} MB/s  {3,9:F1} MiB/s  {4,10:F0} IOPS  {5,7:F3} ms",
                    m.Kind.Label(), m.Operation.ToString().ToLowerInvariant(), m.MbPerSecond, m.MibPerSecond, m.Iops, m.AverageLatencyMs));
            }
            return 0;
        }
        catch (StorageBenchmarkException e)
        {
            Console.WriteLine($"failed: {e.Message}");
            return 1;
        }
    }
}
