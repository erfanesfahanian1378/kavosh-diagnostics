using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Kavosh.App.Views;
using Kavosh.Core;
using Kavosh.Hardware;

namespace Kavosh.App;

/// <summary>
/// Renders the main screens off-screen to PNG files, with sample data. Used to check the layout
/// without real hardware: <c>Kavosh --screenshots out-folder [dark] [fa]</c>.
/// </summary>
internal static class Screenshots
{
    public static int Run(string[] args)
    {
        string folder = args.Length > 0 ? args[0] : "screenshots";
        Directory.CreateDirectory(folder);
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .With(new FontManagerOptions { DefaultFamilyName = "avares://Kavosh/Assets/Fonts#Nunito" })
            .SetupWithoutStarting();

        string? only = args.Skip(1).FirstOrDefault(a => a.StartsWith("only=", StringComparison.Ordinal))?[5..];
        foreach (bool dark in new[] { false, true })
        {
            if (args.Contains("light") && dark) continue;
            if (args.Contains("dark") && !dark) continue;
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            foreach (var language in new[] { AppLanguage.En, AppLanguage.Fa })
            {
                if (args.Contains("fa") && language == AppLanguage.En) continue;
                if (args.Contains("en") && language == AppLanguage.Fa) continue;
                string suffix = $"{(dark ? "dark" : "light")}-{language.ToString().ToLowerInvariant()}";
                foreach (var (name, setup) in Pages())
                {
                    if (only is not null && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;
                    Render(Path.Combine(folder, $"{name}-{suffix}.png"), language, setup);
                }
            }
        }
        Console.WriteLine($"screenshots written to {Path.GetFullPath(folder)}");
        return 0;
    }

    private static IEnumerable<(string Name, Action<AppModel, MainWindow> Setup)> Pages()
    {
        yield return ("dashboard", (_, _) => { });
        yield return ("cpu-running", (model, _) => model.StartRun(new[] { TestModule.Cpu }));
        yield return ("results", (model, _) => model.ShowReport(model.LastReport!));
        yield return ("storage", (model, _) => model.Show(Screen.Storage));
        yield return ("washer", (model, _) => model.Show(Screen.WasherSetup));
        foreach (var module in new[] { TestModule.Gpu, TestModule.Memory, TestModule.Battery, TestModule.Display, TestModule.Speakers,
                     TestModule.MicCamera, TestModule.Input, TestModule.Drives })
        {
            var m = module;
            yield return ($"station-{m.ToString().ToLowerInvariant()}", (model, _) => model.StartRun(TestModules.FullRun.SkipWhile(x => x != m)));
        }
    }

    private static void Render(string path, AppLanguage language, Action<AppModel, MainWindow> setup)
    {
        var model = new AppModel(startServices: false);
        model.UpdateSettings(s => s.Language = language);
        Loc.Language = language;
        model.SetSampleData(SampleSpecs(), SampleDrives(), SampleReport(), SampleStorage());
        var window = new MainWindow(model) { Width = 1440, Height = 900 };
        window.Show();
        // Feed the live panels a little history.
        var rng = new Random(7);
        for (int i = 0; i < 60; i++)
        {
            double cpu = 25 + 20 * Math.Sin(i / 6.0) + rng.NextDouble() * 10;
            model.Telemetry.Publish(new SensorSnapshot
            {
                CpuLoadPercent = cpu,
                PerCoreLoadPercent = Enumerable.Range(0, 16).Select(c => Math.Clamp(cpu + rng.NextDouble() * 40 - 20, 2, 100)).ToList(),
                RamUsedGB = 9.4 + rng.NextDouble(),
                RamTotalGB = 15.7,
                GpuLoadPercent = 12 + rng.NextDouble() * 8,
                CpuTemperatureCelsius = 58 + rng.NextDouble() * 4,
                CpuTemperatureMaxCelsius = 66,
                GpuTemperatureCelsius = 49,
                CpuPowerWatts = 14.2,
                FanRpm = new List<double> { 2650 },
                FanCount = 1,
                HasSensorAccess = true,
            }, new SensorStatus(true, true, "2.0.1", true, null));
        }
        setup(model, window);
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(30);
        }
        var frame = window.CaptureRenderedFrame();
        frame?.Save(path);
        // Stop any station that started work.
        if (model.Runner is not null)
        {
            (window.Content as IDisposable)?.Dispose();
        }
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    internal static SystemSpecs SampleSpecs() => new()
    {
        ModelName = "Lenovo ThinkPad X1 Carbon Gen 11",
        ModelIdentifier = "21HM000LUS",
        CpuName = "13th Gen Intel Core i7-1365U",
        Architecture = "x64",
        CpuCores = 10,
        CpuThreads = 12,
        CoreClusters = { new CoreCluster("Performance", 2), new CoreCluster("Efficient", 8) },
        CpuMaxClockGHz = 1.8,
        GpuName = "Intel Iris Xe Graphics",
        HasUnifiedMemory = true,
        GraphicsApi = "Direct3D feature level 12_1",
        RamCapacityGB = 16,
        RamType = "LPDDR5",
        RamManufacturer = "Samsung",
        RamSpeedMHz = 6400,
        RamModuleCount = 2,
        DisplayName = "14\" Built-in display",
        DisplayResolution = "1920 × 1200",
        DisplayRefreshRateHz = 60,
        OperatingSystem = "Windows 11 Pro 23H2 (build 22631.4317)",
        FirmwareVersion = "N3XET53W (1.28)",
        StorageDevices =
        {
            new StorageDeviceInfo { Id = "0", Model = "Samsung MZVL4512HBLU-00BL7", SizeBytes = 512_110_190_592, Interconnect = "NVMe", Location = "Internal", Medium = "Solid State" },
        },
    };

    internal static DriveHealthReport SampleDrives()
    {
        var log = new byte[512];
        log[1] = 0x3A; log[2] = 0x01; log[3] = 100; log[4] = 10; log[5] = 2;
        log[48] = 0x40; log[49] = 0x42; log[50] = 0x2F;
        log[128] = 0x10; log[129] = 0x0E;
        var device = SampleSpecs().StorageDevices[0];
        return new DriveHealthReport { Drives = { DriveHealthEvaluator.Evaluate(device, NVMeSmartLog.Parse(log)!.ToReadings(), "Healthy") } };
    }

    internal static FinalReport SampleReport()
    {
        var report = new FinalReport(TestModules.FullRun, SampleSpecs())
        {
            FinishedAt = DateTime.UtcNow.AddHours(-3),
            Cpu = new CpuBenchmarkResult
            {
                Score = 742, SingleCoreScore = 131, SustainedScore = 655, SustainedSamples = { 720, 700, 680, 660, 655, 650 },
                MultiCoreBatchMs = 5480, SingleCoreBatchMs = 672, MultiCoreUnits = 960, SingleCoreUnits = 40, ThreadsUsed = 12,
                DurationSeconds = 60, PeakTemperatureCelsius = 94, AveragePowerWatts = 21.5, ScoringVersion = 1,
            },
            Gpu = GpuBenchmarkResult.Make(Enumerable.Range(0, 48).Select(i => 610.0 + (i % 7) * 9).ToList(), 640, 70, 60,
                "Intel Iris Xe Graphics", 2560, 1440, 5120, 2880, 4, 260, 1),
            Memory = new MemoryTestResult { TestedBytes = 2L << 30, Passes = 6, AverageWriteThroughputMBps = 31_200, AverageReadThroughputMBps = 38_900, DurationSeconds = 4.2 },
            Battery = BatteryReading.Result(new BatteryReading
            {
                DesignCapacityMwh = 57_000, FullChargeCapacityMwh = 51_870, CycleCount = 212, ChargePercent = 86, IsPluggedIn = true,
                IsCharging = true, Manufacturer = "SMP", DeviceName = "5B10W51867", Chemistry = "Lithium polymer",
            }, 18.4),
            Display = new DisplayTestResult { MeasuredRefreshRateHz = 60.0 },
            Audio = new AudioTestResult { LeftHeard = true, RightHeard = true },
            MicCamera = new MicCameraTestResult { CameraPresent = true, CameraWorks = true, MicrophonePresent = true, MicrophoneWorks = true, PeakLevelDb = -14 },
            Keyboard = new KeyboardTestResult { SentenceCorrect = true, TotalKeys = 66, PressedKeys = 65, MissingKeys = { "F11" }, TrackpadMoved = true, TrackpadClicked = true, TrackpadScrolled = true },
            Drives = SampleDrives(),
        };
        report.Completed.AddRange(TestModules.FullRun);
        return report;
    }

    internal static StorageBenchmarkResult SampleStorage() => new()
    {
        TargetPath = @"C:\Users\Public",
        VolumeName = "Windows (C:)",
        ProfileName = "standard",
        FileSizeBytes = 1L << 30,
        RunsPerTest = 5,
        Measurements =
        {
            new StorageTestMeasurement { Kind = StorageTestKind.Seq1mQ8T1, Operation = StorageOperation.Read, MbPerSecond = 6812.4, Iops = 6497, AverageLatencyMs = 1.23 },
            new StorageTestMeasurement { Kind = StorageTestKind.Seq1mQ8T1, Operation = StorageOperation.Write, MbPerSecond = 4925.7, Iops = 4697, AverageLatencyMs = 1.70 },
            new StorageTestMeasurement { Kind = StorageTestKind.Rnd4kQ32T1, Operation = StorageOperation.Read, MbPerSecond = 702.3, Iops = 171_460, AverageLatencyMs = 0.186 },
            new StorageTestMeasurement { Kind = StorageTestKind.Rnd4kQ32T1, Operation = StorageOperation.Write, MbPerSecond = 588.1, Iops = 143_580, AverageLatencyMs = 0.222 },
            new StorageTestMeasurement { Kind = StorageTestKind.Rnd4kQ1T1, Operation = StorageOperation.Read, MbPerSecond = 81.6, Iops = 19_920, AverageLatencyMs = 0.050 },
            new StorageTestMeasurement { Kind = StorageTestKind.Rnd4kQ1T1, Operation = StorageOperation.Write, MbPerSecond = 243.9, Iops = 59_540, AverageLatencyMs = 0.016 },
        },
    };
}
