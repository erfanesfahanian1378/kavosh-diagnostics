using System.Diagnostics;
using Kavosh.Core;
using Xunit;

namespace Kavosh.Core.Tests;

public class CpuWorkloadTests
{
    [Fact]
    public void IntegerChecksumsMatchSharedTable()
    {
        using var workspace = new CpuWorkspace();
        foreach (int unit in new[] { 0, 1, 37, 128, 255, 256 + 3 })
            Assert.Equal(CpuWorkload.ExpectedIntegerChecksum(unit), CpuWorkload.IntegerChecksum(unit, workspace));
    }

    [Fact]
    public void EveryTableEntryMatches()
    {
        // The whole table, in parallel: proves the C# port does exactly the macOS app's work.
        var bad = new System.Collections.Concurrent.ConcurrentBag<int>();
        Parallel.For(0, CpuWorkload.DistinctUnits, () => new CpuWorkspace(), (unit, _, ws) =>
        {
            if (CpuWorkload.IntegerChecksum(unit, ws) != CpuWorkload.ExpectedIntegerChecksum(unit)) bad.Add(unit);
            return ws;
        }, ws => ws.Dispose());
        Assert.Empty(bad);
    }

    [Fact]
    public void PrimeCountMatchesKnownValue()
    {
        using var workspace = new CpuWorkspace();
        ulong lo = CpuWorkload.PrimeWindowStart;
        Assert.Equal(347_566UL, CpuWorkload.CountPrimes(lo, lo + CpuWorkload.PrimeWindowSize, workspace));
    }

    [Fact]
    public void SmallPrimeWindow()
    {
        using var workspace = new CpuWorkspace();
        Assert.Equal(21UL, CpuWorkload.CountPrimes(100, 200, workspace));
    }

    [Fact]
    public void UnitVerifies()
    {
        using var workspace = new CpuWorkspace();
        ulong reference = CpuWorkload.Mandelbrot();
        Assert.True(CpuWorkload.RunUnit(5, workspace, reference));
        Assert.False(CpuWorkload.RunUnit(5, workspace, reference + 1));
    }

    [Fact]
    public void QuickEngineRun()
    {
        var config = CpuBenchmarkConfiguration.Quick;
        config.MultiCoreUnits = 16;
        config.SingleCoreUnits = 2;
        var phases = new HashSet<CpuBenchmarkPhase>();
        var result = new CpuBenchmarkEngine(config).Run(() => false, p => phases.Add(p.Phase));
        Assert.True(result.Score > 0);
        Assert.True(result.SingleCoreScore > 0);
        Assert.Equal(0, result.WrongResults);
        Assert.Null(result.SustainedScore);
        Assert.Contains(CpuBenchmarkPhase.MultiCore, phases);
        Assert.Contains(CpuBenchmarkPhase.SingleCore, phases);
        Assert.Contains(CpuBenchmarkPhase.Finished, phases);
    }

    [Fact]
    public void EngineCancels()
    {
        var engine = new CpuBenchmarkEngine(new CpuBenchmarkConfiguration { MultiCoreUnits = 10_000, SingleCoreUnits = 10, DurationSeconds = 0 });
        var clock = Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => engine.Run(() => clock.Elapsed.TotalSeconds > 0.3, _ => { }));
        Assert.True(clock.Elapsed.TotalSeconds < 5);
    }

    [Fact]
    public void ScoreFormulas()
    {
        double refMs = CpuBenchmarkEngine.ReferenceMultiCoreMs(960);
        Assert.Equal(1000, CpuBenchmarkEngine.MultiCoreScore(960, refMs), 3);
        Assert.Equal(2000, CpuBenchmarkEngine.MultiCoreScore(960, refMs / 2), 3);
        Assert.Equal(100, CpuBenchmarkEngine.SingleCoreScore(10, CpuBenchmarkEngine.ReferenceSingleCoreMs * 10), 3);
    }

    [Fact]
    public void LoadGeneratorStartsAndStops()
    {
        var load = new CpuLoadGenerator();
        load.Start(2);
        Assert.True(load.IsRunning);
        Thread.Sleep(200);
        load.Stop();
        Assert.False(load.IsRunning);
    }
}

public class MemoryTestTests
{
    [Fact]
    public void SmallRunHasNoErrors()
    {
        var engine = new MemoryTestEngine(16 * 1024 * 1024, threads: 2);
        var result = engine.Run(() => false, _ => { });
        Assert.Equal(0, result.ErrorsDetected);
        Assert.True(result.Passed);
        Assert.Equal(MemoryTestEngine.DefaultPatterns.Length, result.Passes);
        Assert.True(result.AverageReadThroughputMBps > 0);
        Assert.True(result.AverageWriteThroughputMBps > 0);
    }

    [Fact]
    public void RecommendedSize()
    {
        const ulong gib = 1UL << 30;
        Assert.Equal((long)gib, MemoryTestEngine.RecommendedSize(8 * gib));
        Assert.Equal(2 * (long)gib, MemoryTestEngine.RecommendedSize(64 * gib));
        Assert.Equal(256L * 1024 * 1024, MemoryTestEngine.RecommendedSize(1 * gib));
    }

    [Fact]
    public void PatternValues()
    {
        Assert.Equal("AAAAAAAAAAAAAAAA", MemoryPattern.Constant(0xAAAAAAAAAAAAAAAAUL).Label);
        Assert.NotEqual(MemoryPattern.Address.ValueAt(1), MemoryPattern.Address.ValueAt(2));
        Assert.Equal(SplitMix64.Mix(7UL ^ 3UL), MemoryPattern.Random(7).ValueAt(3));
    }
}

public class StorageBenchmarkTests
{
    [Fact]
    public void TinyRunProducesAllMeasurements()
    {
        string folder = Path.Combine(Path.GetTempPath(), "kavosh-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var profile = new StorageTestProfile { Name = "test", Runs = 1, SecondsPerRun = 0.3, FileSizeBytes = 16L << 20, SettleSeconds = 0 };
            var result = new StorageBenchmarkEngine(profile).Run(folder, () => false, _ => { });
            Assert.Equal(6, result.Measurements.Count);
            foreach (var m in result.Measurements)
            {
                Assert.True(m.MbPerSecond > 0, $"{m.Kind} {m.Operation}");
                Assert.True(m.Iops > 0);
            }
            Assert.Empty(Directory.GetFileSystemEntries(folder));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void MissingFolder()
    {
        var problem = new StorageBenchmarkEngine(StorageTestProfile.Quick).CheckTarget(Path.Combine(Path.GetTempPath(), "definitely", "not", "here"));
        Assert.NotNull(problem);
        Assert.Equal(StorageFailureKind.NotFound, problem!.Kind);
    }

    [Fact]
    public void CancelStopsAndCleansUp()
    {
        string folder = Path.Combine(Path.GetTempPath(), "kavosh-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var profile = new StorageTestProfile { Name = "test", Runs = 3, SecondsPerRun = 2, FileSizeBytes = 16L << 20, SettleSeconds = 0 };
            var clock = Stopwatch.StartNew();
            var error = Assert.Throws<StorageBenchmarkException>(() =>
                new StorageBenchmarkEngine(profile).Run(folder, () => clock.Elapsed.TotalSeconds > 0.5, _ => { }));
            Assert.Equal(StorageFailureKind.Cancelled, error.Kind);
            Assert.Empty(Directory.GetFileSystemEntries(folder));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}

public class AudioAnalysisTests
{
    private static float[] Sine(double frequency, double sampleRate = 48_000, int count = 4096, float amplitude = 0.5f) =>
        Enumerable.Range(0, count).Select(i => amplitude * (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate)).ToArray();

    [Theory]
    [InlineData(110.0)]
    [InlineData(220.0)]
    [InlineData(440.0)]
    [InlineData(880.0)]
    public void PitchOfSine(double f)
    {
        double? pitch = AudioAnalysis.EstimatePitch(Sine(f), 48_000);
        Assert.NotNull(pitch);
        Assert.InRange(pitch!.Value, f * 0.98, f * 1.02);
    }

    [Fact]
    public void SilenceHasNoPitch() => Assert.Null(AudioAnalysis.EstimatePitch(new float[4096], 48_000));

    [Fact]
    public void Levels()
    {
        Assert.InRange(AudioAnalysis.RmsDecibels(Sine(440, amplitude: 1)), -3.11, -2.91);
        Assert.Equal(1, AudioAnalysis.MeterLevel(0));
        Assert.Equal(0, AudioAnalysis.MeterLevel(-60));
        Assert.Equal("A4", AudioAnalysis.NoteName(440));
        Assert.Equal("C4", AudioAnalysis.NoteName(261.63));
    }
}

public class DriveHealthTests
{
    private static byte[] MakeLog(byte warning = 0, byte spare = 100, byte used = 3, byte mediaErrors = 0)
    {
        var b = new byte[512];
        b[0] = warning;
        b[1] = 0x3A; b[2] = 0x01;                 // 314 K = ~41 °C
        b[3] = spare;
        b[4] = 10;
        b[5] = used;
        b[48] = 0x40; b[49] = 0x42; b[50] = 0x0F; // 1,000,000 units written = 512 GB
        b[128] = 0xE8; b[129] = 0x03;             // 1,000 power-on hours
        b[160] = mediaErrors;
        return b;
    }

    private static readonly StorageDeviceInfo Device = new()
    {
        Id = "0", Model = "Samsung SSD 980 PRO 1TB", SizeBytes = 1_000_204_886_016, Medium = "Solid State",
        Interconnect = "NVMe", Location = "Internal",
    };

    [Fact]
    public void Parsing()
    {
        var log = NVMeSmartLog.Parse(MakeLog());
        Assert.NotNull(log);
        Assert.Equal(3, log!.PercentageUsed);
        Assert.Equal(1000, log.PowerOnHours);
        Assert.Equal(0.512, log.TerabytesWritten, 4);
        Assert.Equal(40.85, log.TemperatureCelsius!.Value, 2);
        Assert.Null(NVMeSmartLog.Parse(new byte[512]));
    }

    [Fact]
    public void HealthyDrive()
    {
        var info = DriveHealthEvaluator.Evaluate(Device, NVMeSmartLog.Parse(MakeLog())!.ToReadings(), "Healthy");
        Assert.Equal(DriveHealthStatus.Good, info.Status);
        Assert.Equal(100, info.Score);
        Assert.Empty(info.Issues);
        Assert.Equal(97, info.LifeLeftPercent);
    }

    [Fact]
    public void FailingDrive()
    {
        var info = DriveHealthEvaluator.Evaluate(Device, NVMeSmartLog.Parse(MakeLog(warning: 0x05, mediaErrors: 2))!.ToReadings(), "Healthy");
        Assert.Equal(DriveHealthStatus.Failing, info.Status);
        Assert.Contains(new DriveHealthIssue(DriveHealthIssueKind.SpareBelowThreshold), info.Issues);
        Assert.Contains(new DriveHealthIssue(DriveHealthIssueKind.ReliabilityDegraded), info.Issues);
        Assert.Contains(new DriveHealthIssue(DriveHealthIssueKind.MediaErrors, 2), info.Issues);
    }

    [Fact]
    public void WindowsSaysUnhealthy()
    {
        var info = DriveHealthEvaluator.Evaluate(Device, null, "Unhealthy");
        Assert.Equal(DriveHealthStatus.Failing, info.Status);
        Assert.Equal(0, info.Score);
    }

    [Fact]
    public void NoData()
    {
        var info = DriveHealthEvaluator.Evaluate(Device, null, null);
        Assert.Equal(DriveHealthStatus.Unknown, info.Status);
        Assert.Null(info.Score);
    }

    private static byte[] AtaTable(params (byte Id, byte Current, ulong Raw)[] attributes)
    {
        var b = new byte[362];
        b[0] = 0x10;
        for (int i = 0; i < attributes.Length; i++)
        {
            int o = 2 + i * 12;
            b[o] = attributes[i].Id;
            b[o + 3] = attributes[i].Current;
            b[o + 4] = attributes[i].Current;
            for (int k = 0; k < 6; k++) b[o + 5 + k] = (byte)(attributes[i].Raw >> (8 * k));
        }
        return b;
    }

    [Fact]
    public void AtaAttributes()
    {
        var data = AtaTable((5, 100, 12), (9, 99, 15_000), (12, 99, 2_100), (194, 70, 0x0016_0012_0023), (177, 92, 120), (197, 100, 0));
        var ata = AtaSmartData.Parse(data);
        Assert.NotNull(ata);
        var r = ata!.ToReadings();
        Assert.Equal(12, r.ReallocatedSectors);
        Assert.Equal(15_000, r.PowerOnHours);
        Assert.Equal(2_100, r.PowerCycles);
        Assert.Equal(35, r.TemperatureCelsius);
        Assert.Equal(8, r.PercentageUsed);
        Assert.Equal(0, r.PendingSectors);
        Assert.False(r.PredictsFailure);

        var device = new StorageDeviceInfo { Id = "1", Model = "CT500MX500SSD1", SizeBytes = 500_107_862_016, Interconnect = "SATA", Location = "Internal" };
        var info = DriveHealthEvaluator.Evaluate(device, r, "Healthy");
        Assert.Equal(DriveHealthStatus.Warning, info.Status);
        Assert.Contains(new DriveHealthIssue(DriveHealthIssueKind.ReallocatedSectors, 12), info.Issues);
    }

    [Fact]
    public void AtaThresholdTripped()
    {
        var data = AtaTable((5, 5, 3000));
        var thresholds = new byte[362];
        thresholds[2] = 5;
        thresholds[3] = 10;
        var r = AtaSmartData.Parse(data, thresholds)!.ToReadings();
        Assert.True(r.PredictsFailure);
        var info = DriveHealthEvaluator.Evaluate(Device, r, "Healthy");
        Assert.Equal(DriveHealthStatus.Failing, info.Status);
    }

    [Fact]
    public void ReadingsMerge()
    {
        var primary = new DriveReadings { Source = "NVMe health log", PercentageUsed = 4 };
        primary.FillFrom(new DriveReadings { Source = "x", PercentageUsed = 9, TemperatureCelsius = 40 });
        Assert.Equal(4, primary.PercentageUsed);
        Assert.Equal(40, primary.TemperatureCelsius);
        Assert.Equal("NVMe health log", primary.Source);
    }

    [Fact]
    public void UniqueAndPhysicalDrives()
    {
        var usb = new StorageDeviceInfo { Id = "2", Model = "Portable SSD T7", Vendor = "Samsung", SizeBytes = 1_000_000_000_000, Interconnect = "USB", Location = "External" };
        var vhd = new StorageDeviceInfo { Id = "3", Model = "Msft Virtual Disk", SizeBytes = 1, Interconnect = "File Backed Virtual" };
        var physical = new[] { Device, usb, vhd }.Where(DriveHealthEvaluator.IsPhysical).ToList();
        var unique = DriveHealthEvaluator.UniquePhysicalDrives(physical);
        Assert.Equal(new[] { "0", "2" }, unique.Select(d => d.Id));
        Assert.Equal("Samsung Portable SSD T7", usb.DisplayName);
        Assert.Equal("Samsung SSD 980 PRO 1TB", Device.DisplayName);
    }
}

public class ScoringTests
{
    internal static FinalReport PerfectReport()
    {
        var report = new FinalReport(TestModules.FullRun)
        {
            Keyboard = new KeyboardTestResult { SentenceCorrect = true, TotalKeys = 70, PressedKeys = 70, TrackpadMoved = true, TrackpadClicked = true, TrackpadScrolled = true },
            Display = new DisplayTestResult { MeasuredRefreshRateHz = 120 },
            Audio = new AudioTestResult { LeftHeard = true, RightHeard = true },
            MicCamera = new MicCameraTestResult { CameraPresent = true, CameraWorks = true, MicrophonePresent = true, MicrophoneWorks = true },
            Battery = new BatteryHealthResult { BatteryPresent = true, DesignCapacityMwh = 57000, FullChargeCapacityMwh = 56000, HealthPercent = 98.3, CycleCount = 40, CurrentChargePercent = 80 },
            Memory = new MemoryTestResult { TestedBytes = 1L << 30, Passes = 6, AverageWriteThroughputMBps = 50_000, AverageReadThroughputMBps = 60_000, DurationSeconds = 3 },
            Drives = new DriveHealthReport { Drives = { new DriveHealthInfo { Id = "0", Model = "SSD", SizeBytes = 1, Status = DriveHealthStatus.Good, Score = 100 } } },
        };
        return report;
    }

    [Fact]
    public void Perfect()
    {
        var e = BenchmarkScoring.Evaluate(PerfectReport());
        Assert.Equal(DeviceGrade.APlus, e.Grade);
        Assert.Equal(100, e.Score!.Value, 2);
        Assert.Equal(0, e.TotalIssues);
    }

    [Fact]
    public void WornBatteryLowersGrade()
    {
        var report = PerfectReport();
        report.Battery!.HealthPercent = 75;
        var e = BenchmarkScoring.Evaluate(report);
        Assert.Equal(1, e.BatteryPenaltySteps);
        Assert.NotNull(e.Grade);
        Assert.NotEqual(e.GradeFromScore, e.Grade);
    }

    [Fact]
    public void MemoryErrorsCapGrade()
    {
        var report = PerfectReport();
        report.Memory!.ErrorsDetected = 3;
        var e = BenchmarkScoring.Evaluate(report);
        Assert.Equal(DeviceGrade.D, e.Grade);
        Assert.Contains(HealthComponentKind.Memory, e.CappedBy);
    }

    [Fact]
    public void BrokenKeyboardPullsScoreDown()
    {
        var report = PerfectReport();
        report.Keyboard!.MissingKeys = new List<string> { "A", "S", "D", "F", "G", "H", "J", "K" };
        report.Keyboard.TrackpadClicked = false;
        Assert.True(BenchmarkScoring.Evaluate(report).Score < 92);
    }

    [Fact]
    public void NoHardwareChecks()
    {
        var report = new FinalReport(TestModules.PerformanceRun);
        Assert.Null(BenchmarkScoring.Evaluate(report).Grade);
    }

    [Fact]
    public void GradeHelpers()
    {
        Assert.Equal(DeviceGrade.B, DeviceGrade.APlus.Lowered(2));
        Assert.Equal(DeviceGrade.F, DeviceGrade.F.Lowered(3));
        Assert.Equal(DeviceGrade.D, DeviceGrade.A.CappedAt(DeviceGrade.D));
        Assert.Equal(DeviceGrade.F, DeviceGrade.F.CappedAt(DeviceGrade.D));
        Assert.Equal(DeviceGrade.APlus, DeviceGrades.FromScore(97));
        Assert.Equal(DeviceGrade.F, DeviceGrades.FromScore(59.9));
        Assert.Equal("A+", DeviceGrade.APlus.Letter());
    }

    [Fact]
    public void BatteryIssues()
    {
        var issues = BatteryHealthResult.EvaluateIssues(78, 1200, true, false, 40, false);
        Assert.Contains(new BatteryIssue(BatteryIssueKind.HealthCritical, 78), issues);
        Assert.Contains(new BatteryIssue(BatteryIssueKind.HighCycleCount, 1200), issues);
        Assert.Contains(new BatteryIssue(BatteryIssueKind.NotCharging), issues);
        // A charge limit holding at 80% is fine.
        Assert.Empty(BatteryHealthResult.EvaluateIssues(99, 10, true, false, 80, false));
    }

    [Fact]
    public void GpuResultMath()
    {
        var r = GpuBenchmarkResult.Make(new double[] { 10, 10, 20, 10 }, 20, 40, 1, "Test", 2560, 1440, 5120, 2880, 4, 12.5, 1);
        Assert.Equal(12.5, r.AverageFrameTimeMs, 3);
        Assert.Equal(1000, r.Score, 3);
        Assert.Equal(625, r.SustainedScore!.Value, 3);
        Assert.Equal(50, r.MinFps, 3);
        Assert.Equal(100, r.MaxFps, 3);
        Assert.Equal(50, r.OnePercentLowFps, 3);
    }
}

public class MiscTests
{
    [Fact]
    public void Csv()
    {
        Assert.Equal("plain", CsvExport.Escape("plain"));
        Assert.Equal("\"a,b\"", CsvExport.Escape("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", CsvExport.Escape("say \"hi\""));
        var report = ScoringTests.PerfectReport();
        string row = CsvExport.Row(report, BenchmarkScoring.Evaluate(report), null);
        int fields = 1;
        bool quoted = false;
        foreach (char c in row)
        {
            if (c == '"') quoted = !quoted;
            if (c == ',' && !quoted) fields++;
        }
        Assert.Equal(CsvExport.Header.Length, fields);
    }

    [Fact]
    public void KeyChatter()
    {
        var tracker = new KeyPressTracker();
        tracker.KeyDown("A", 1.000); tracker.KeyUp("A");
        tracker.KeyDown("A", 1.010); tracker.KeyUp("A");   // 10 ms later: chatter
        tracker.KeyDown("S", 2.000); tracker.KeyUp("S");
        tracker.KeyDown("S", 2.300); tracker.KeyUp("S");   // normal double-tap
        tracker.KeyDown("D", 3.000);
        tracker.KeyDown("D", 3.005);                        // held: Windows auto-repeat, ignored
        tracker.KeyUp("D");
        Assert.Equal(new[] { "A" }, tracker.DoublePressed);
        Assert.True(tracker.IsPressed("S"));
        Assert.Equal(1, tracker.Counts["D"]);
        Assert.Equal("D", tracker.LastKeyLabel);
        var result = tracker.Result(true, true, true, false);
        Assert.Equal(new[] { "A" }, result.DoublePressedKeys);
        Assert.Equal(2, result.TrackpadChecksPassed);
        Assert.NotEmpty(result.MissingKeys);
    }

    [Fact]
    public void KeyboardLayout()
    {
        var codes = PcKeyboardLayout.AllKeys.Select(k => k.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.Equal("space", PcKeyboardLayout.Key("Space")?.Label);
        Assert.Equal("left shift", PcKeyboardLayout.Describe(PcKeyboardLayout.Key("ShiftLeft")!));
        Assert.True(PcKeyboardLayout.RequiredKeys.Count() > 60);
    }

    [Fact]
    public void TypingPromptChecks()
    {
        Assert.True(TypingPrompt.Matches("  the quick brown fox jumps over the lazy dog 1234567890 "));
        Assert.False(TypingPrompt.Matches("the quick brown fox"));
        Assert.Equal(new HashSet<int> { 2 }, TypingPrompt.MismatchedIndices("Tha"));
    }

    [Fact]
    public void Formatting()
    {
        Assert.Equal("1,234.5", Fmt.Number(1234.5, 1));
        Assert.Equal("512 GB", Fmt.StorageSize(512_110_190_592));
        Assert.Equal("2.00 TB", Fmt.StorageSize(2_000_398_934_016));
        Assert.Equal("1:05", Fmt.Duration(65));
        Assert.Equal("1:01:01", Fmt.Duration(3661));
        Assert.Equal("16 GB", Fmt.MemoryGB(16));
        Assert.Equal("15.8 GB", Fmt.MemoryGB(15.8));
    }

    [Fact]
    public void Localization()
    {
        lock (typeof(Loc))
        {
            Loc.Language = AppLanguage.Fa;
            Assert.Equal("سلام", Loc.T("Hello", "سلام"));
            Loc.Language = AppLanguage.En;
            Assert.Equal("Hello", Loc.T("Hello", "سلام"));
        }
    }

    [Fact]
    public void SettingsParseTolerant()
    {
        var s = AppSettings.Parse("{\"language\":\"fa\",\"washerRotate\":false,\"washerPattern\":\"bogus\"}");
        Assert.Equal(AppLanguage.Fa, s.Language);
        Assert.False(s.WasherRotate);
        Assert.Equal(60, s.PerformanceTestSeconds);
        Assert.Equal(ScreenWasherPattern.Gradient, s.WasherPattern);
        Assert.Equal(AppLanguage.En, AppSettings.Parse("not json").Language);
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var s = new AppSettings { Language = AppLanguage.Fa, WasherDuration = ScreenWasherDuration.Deep, PerformanceTestSeconds = 180 };
        var back = AppSettings.Parse(ResultStore.Serialize(s));
        Assert.Equal(AppLanguage.Fa, back.Language);
        Assert.Equal(ScreenWasherDuration.Deep, back.WasherDuration);
        Assert.Equal(180, back.PerformanceTestSeconds);
    }

    [Fact]
    public void ReportRoundTrip()
    {
        var report = ScoringTests.PerfectReport();
        report.Battery!.Issues = new List<BatteryIssue> { new(BatteryIssueKind.HighCycleCount, 1100), new(BatteryIssueKind.NotCharging) };
        report.Drives!.Drives[0].Issues = new List<DriveHealthIssue> { new(DriveHealthIssueKind.MediaErrors, 4) };
        report.Specs = new SystemSpecs { CpuName = "Intel Core i7-1360P", CoreClusters = { new CoreCluster("Performance", 4), new CoreCluster("Efficient", 8) }, CpuCores = 12, CpuThreads = 16 };
        string json = ResultStore.Serialize(report);
        var back = System.Text.Json.JsonSerializer.Deserialize<FinalReport>(json, ResultStore.Options);
        Assert.NotNull(back);
        Assert.Equal(json, ResultStore.Serialize(back));
        Assert.Equal(report.Battery.Issues, back!.Battery!.Issues);
        Assert.Equal("12C (4P+8E)", back.Specs!.ClusterShorthand);
    }

    [Fact]
    public void HistoryCsvAppends()
    {
        string folder = Path.Combine(Path.GetTempPath(), "kavosh-paths-" + Guid.NewGuid().ToString("N"));
        AppPaths.OverrideDirectory = folder;
        try
        {
            var report = ScoringTests.PerfectReport();
            var e = BenchmarkScoring.Evaluate(report);
            ResultStore.AppendToHistory(report, e, null);
            ResultStore.AppendToHistory(report, e, null);
            var lines = File.ReadAllLines(AppPaths.HistoryCsv);
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("Recorded (UTC)", lines[0]);
        }
        finally
        {
            AppPaths.OverrideDirectory = null;
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void SplitMixFill()
    {
        var a = new byte[13];
        var rng = new SplitMix64(42);
        rng.Fill(a);
        var check = new SplitMix64(42);
        ulong first = check.Next();
        Assert.Equal((byte)first, a[0]);
        Assert.Equal((byte)(first >> 56), a[7]);
    }
}
