using static Kavosh.Core.Loc;

namespace Kavosh.Core;

// ───────────────────────────── System specs ─────────────────────────────

public sealed class CoreCluster
{
    public string Name { get; set; } = "";
    public int Cores { get; set; }

    public CoreCluster() { }
    public CoreCluster(string name, int cores) { Name = name; Cores = cores; }

    /// <summary>"P" for performance cores, "E" for efficiency cores, "LP" for low-power cores.</summary>
    public string ShortLetter
    {
        get
        {
            string lower = Name.ToLowerInvariant();
            if (lower.Contains("low")) return "LP";
            if (lower.Contains("perf") || lower.Contains("super")) return "P";
            if (lower.Contains("eff")) return "E";
            return Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
        }
    }
}

/// <summary>A physical disk as Windows lists it (\\.\PhysicalDriveN).</summary>
public sealed class StorageDeviceInfo
{
    /// <summary>Disk number as a string ("0", "1", …).</summary>
    public string Id { get; set; } = "";
    public string Model { get; set; } = "";
    public string? Vendor { get; set; }
    public string? SerialNumber { get; set; }
    public string? Firmware { get; set; }
    public ulong SizeBytes { get; set; }
    /// <summary>"Solid State" / "Rotational" / null when unknown.</summary>
    public string? Medium { get; set; }
    /// <summary>"NVMe", "SATA", "USB", "SD", "RAID", "Virtual"…</summary>
    public string? Interconnect { get; set; }
    /// <summary>"Internal" or "External".</summary>
    public string? Location { get; set; }
    public bool Removable { get; set; }

    public bool IsInternal => string.Equals(Location, "internal", StringComparison.OrdinalIgnoreCase);
    public bool IsSolidState => (Medium ?? "").Contains("solid", StringComparison.OrdinalIgnoreCase);
    public bool IsNvme => (Interconnect ?? "").Contains("nvme", StringComparison.OrdinalIgnoreCase);

    public string DisplayName
    {
        get
        {
            string model = Model.Trim();
            string vendor = (Vendor ?? "").Trim();
            if (model.Length == 0) return vendor.Length == 0 ? "Disk " + Id : vendor;
            if (vendor.Length == 0 || model.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)) return model;
            return vendor + " " + model;
        }
    }
}

public sealed class SystemSpecs
{
    /// <summary>"Lenovo ThinkPad X1 Carbon Gen 11".</summary>
    public string ModelName { get; set; } = "PC";
    /// <summary>System SKU or product number.</summary>
    public string ModelIdentifier { get; set; } = "";
    public string CpuName { get; set; } = "";
    /// <summary>"x64" or "arm64".</summary>
    public string Architecture { get; set; } = "";
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public List<CoreCluster> CoreClusters { get; set; } = new();
    public double? CpuMaxClockGHz { get; set; }
    public string GpuName { get; set; } = "";
    public int? GpuCoreCount { get; set; }
    /// <summary>Dedicated video memory. Integrated GPUs share system RAM.</summary>
    public double? GpuMemoryGB { get; set; }
    public bool HasUnifiedMemory { get; set; }
    /// <summary>"Direct3D 12 (feature level 12_1)".</summary>
    public string? GraphicsApi { get; set; }
    public List<string> OtherGpus { get; set; } = new();
    public double RamCapacityGB { get; set; }
    public string? RamType { get; set; }
    public string? RamManufacturer { get; set; }
    public int? RamSpeedMHz { get; set; }
    public int? RamModuleCount { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayResolution { get; set; }
    public int? DisplayRefreshRateHz { get; set; }
    public string OperatingSystem { get; set; } = "";
    public string? FirmwareVersion { get; set; }
    public List<StorageDeviceInfo> StorageDevices { get; set; } = new();

    /// <summary>"14C (6P+8E)" style summary of the CPU cores.</summary>
    public string ClusterShorthand
    {
        get
        {
            var parts = CoreClusters.Where(c => c.Cores > 0).Select(c => $"{c.Cores}{c.ShortLetter}").ToList();
            if (parts.Count > 1) return $"{CpuCores}C ({string.Join("+", parts)})";
            if (CpuThreads > CpuCores) return $"{CpuCores}C / {CpuThreads}T";
            return $"{CpuCores}C";
        }
    }

    public bool IsArm => Architecture.Contains("arm", StringComparison.OrdinalIgnoreCase);
}

// ───────────────────────────── Live sensors ─────────────────────────────

public enum ThermalLevel { Nominal = 0, Fair = 1, Serious = 2, Critical = 3 }

public enum MemoryPressureLevel { Normal = 0, Warning = 1, Critical = 2 }

public static class SensorTextExtensions
{
    public static string Title(this ThermalLevel level) => level switch
    {
        ThermalLevel.Nominal => T("Normal", "عادی"),
        ThermalLevel.Fair => T("Warm", "گرم"),
        ThermalLevel.Serious => T("Hot — slowed down", "داغ — کند شده"),
        _ => T("Critical — heavily slowed", "بحرانی — به‌شدت کند شده"),
    };

    public static string Title(this MemoryPressureLevel level) => level switch
    {
        MemoryPressureLevel.Normal => T("Pressure: normal", "فشار حافظه: عادی"),
        MemoryPressureLevel.Warning => T("Pressure: high", "فشار حافظه: زیاد"),
        _ => T("Pressure: critical", "فشار حافظه: بحرانی"),
    };
}

public sealed class SensorSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double CpuLoadPercent { get; set; }
    public List<double> PerCoreLoadPercent { get; set; } = new();
    public double RamUsedGB { get; set; }
    public double RamTotalGB { get; set; }
    public MemoryPressureLevel MemoryPressure { get; set; }
    public double? GpuLoadPercent { get; set; }
    public double? CpuTemperatureCelsius { get; set; }
    public double? CpuTemperatureMaxCelsius { get; set; }
    public double? GpuTemperatureCelsius { get; set; }
    /// <summary>Whole-system power, known while running on battery (the battery's discharge rate).</summary>
    public double? SystemPowerWatts { get; set; }
    /// <summary>CPU package power from the processor's own energy counters.</summary>
    public double? CpuPowerWatts { get; set; }
    public double? GpuPowerWatts { get; set; }
    public double? CpuClockGHz { get; set; }
    public List<double> FanRpm { get; set; } = new();
    public int? FanCount { get; set; }
    public ThermalLevel ThermalState { get; set; }
    public bool HasSensorAccess { get; set; }

    public double RamLoadPercent => RamTotalGB > 0 ? Math.Min(100, RamUsedGB / RamTotalGB * 100) : 0;
}

// ───────────────────────────── Test modules ─────────────────────────────

public enum TestModule { Cpu, Gpu, Memory, Battery, Display, Speakers, MicCamera, Input, Drives }

public static class TestModules
{
    /// <summary>Order of a full run.</summary>
    public static readonly TestModule[] FullRun =
    {
        TestModule.Cpu, TestModule.Gpu, TestModule.Memory, TestModule.Battery, TestModule.Display,
        TestModule.Speakers, TestModule.MicCamera, TestModule.Input, TestModule.Drives,
    };

    /// <summary>Performance-only run.</summary>
    public static readonly TestModule[] PerformanceRun = { TestModule.Cpu, TestModule.Gpu, TestModule.Memory };

    public static string Title(this TestModule m) => m switch
    {
        TestModule.Cpu => T("CPU Benchmark", "بنچمارک پردازنده"),
        TestModule.Gpu => T("GPU Render Test", "تست رندر گرافیک"),
        TestModule.Memory => T("Memory Test", "تست حافظه"),
        TestModule.Battery => T("Battery Health", "سلامت باتری"),
        TestModule.Display => T("Display Check", "بررسی نمایشگر"),
        TestModule.Speakers => T("Speaker Check", "بررسی بلندگوها"),
        TestModule.MicCamera => T("Mic & Camera Check", "بررسی میکروفون و دوربین"),
        TestModule.Input => T("Keyboard & Touchpad", "کیبورد و تاچ‌پد"),
        _ => T("Drive Health (SMART)", "سلامت درایو (SMART)"),
    };

    public static string ShortTitle(this TestModule m) => m switch
    {
        TestModule.Cpu => "CPU",
        TestModule.Gpu => "GPU",
        TestModule.Memory => "RAM",
        TestModule.Battery => T("Battery", "باتری"),
        TestModule.Display => T("Display", "نمایشگر"),
        TestModule.Speakers => T("Speakers", "بلندگو"),
        TestModule.MicCamera => T("Mic & Camera", "میکروفون و دوربین"),
        TestModule.Input => T("Input", "ورودی"),
        _ => T("Drives", "درایوها"),
    };

    public static string Subtitle(this TestModule m) => m switch
    {
        TestModule.Cpu => T("Fixed multi-core and single-core workloads, then a sustained load",
                            "بار کاری ثابت چندهسته‌ای و تک‌هسته‌ای، سپس بار پایدار"),
        TestModule.Gpu => T("Off-screen Direct3D 11 raymarch at 2560×1440 with supersampling",
                            "رندر رِی‌مارچ Direct3D 11 خارج از صفحه در ۲۵۶۰×۱۴۴۰ با سوپرسمپلینگ"),
        TestModule.Memory => T("Pattern write/verify integrity check + throughput",
                               "نوشتن و بررسی الگو برای سلامت حافظه + سرعت"),
        TestModule.Battery => T("Design vs. full-charge capacity + discharge rate under load",
                                "ظرفیت طراحی در برابر ظرفیت شارژ کامل + نرخ تخلیه زیر بار"),
        TestModule.Display => T("Fullscreen color sweep, dead-pixel scan, refresh-rate check, and a defect checklist",
                                "رنگ‌های تمام‌صفحه، پیکسل سوخته، نرخ تازه‌سازی و فهرست ایرادها"),
        TestModule.Speakers => T("Left / right channel tone playback", "پخش صدای کانال چپ / راست"),
        TestModule.MicCamera => T("Live webcam preview + microphone level/frequency",
                                  "پیش‌نمایش زنده دوربین + سطح و فرکانس میکروفون"),
        TestModule.Input => T("Type a prompt, press every key, then try the touchpad",
                              "یک جمله تایپ کنید، همه کلیدها را بزنید، سپس تاچ‌پد را امتحان کنید"),
        _ => T("Read from each drive's own SMART / NVMe health log", "خواندن گزارش سلامت SMART / NVMe هر درایو"),
    };

    /// <summary>Runs without user interaction.</summary>
    public static bool IsAutomatic(this TestModule m) =>
        m is TestModule.Cpu or TestModule.Gpu or TestModule.Memory or TestModule.Battery or TestModule.Drives;
}

// ───────────────────────────── CPU ─────────────────────────────

public sealed class CpuBenchmarkResult
{
    public double Score { get; set; }
    public double SingleCoreScore { get; set; }
    public double? SustainedScore { get; set; }
    public List<double> SustainedSamples { get; set; } = new();
    public double MultiCoreBatchMs { get; set; }
    public double SingleCoreBatchMs { get; set; }
    public int MultiCoreUnits { get; set; }
    public int SingleCoreUnits { get; set; }
    public int ThreadsUsed { get; set; }
    public double DurationSeconds { get; set; }
    public int WrongResults { get; set; }
    public bool ThermalThrottleDetected { get; set; }
    public double? PeakTemperatureCelsius { get; set; }
    public double? AveragePowerWatts { get; set; }
    public int ScoringVersion { get; set; }

    public bool ResultsVerified => WrongResults == 0;
}

// ───────────────────────────── GPU ─────────────────────────────

public sealed class GpuBenchmarkResult
{
    public string AdapterName { get; set; } = "";
    public double Score { get; set; }
    public double? SustainedScore { get; set; }
    public double AverageFrameTimeMs { get; set; }
    public double? SustainedAverageFrameTimeMs { get; set; }
    public double AverageFps { get; set; }
    public double MinFps { get; set; }
    public double MaxFps { get; set; }
    public double OnePercentLowFps { get; set; }
    public int OutputWidth { get; set; }
    public int OutputHeight { get; set; }
    public int RenderWidth { get; set; }
    public int RenderHeight { get; set; }
    public int SamplesPerPixel { get; set; }
    public int TotalFramesRendered { get; set; }
    public double DurationSeconds { get; set; }
    public double? PeakTemperatureCelsius { get; set; }
    public int ScoringVersion { get; set; }

    /// <summary>Builds a result from measured frame times. The reference frame time scores 1,000.</summary>
    public static GpuBenchmarkResult Make(IReadOnlyList<double> frameTimesMs, double? sustainedMeanMs, int totalFrames,
        double seconds, string adapterName, int outputWidth, int outputHeight, int renderWidth, int renderHeight,
        int samplesPerPixel, double referenceFrameMs, int scoringVersion)
    {
        var valid = frameTimesMs.Where(t => double.IsFinite(t) && t > 0).ToList();
        double mean = valid.Count == 0 ? 0 : valid.Average();
        var sortedFps = valid.Select(t => 1000 / t).OrderBy(f => f).ToList();
        int lowCount = Math.Max(1, (int)Math.Ceiling(sortedFps.Count * 0.01));
        double onePercentLow = sortedFps.Count == 0 ? 0 : sortedFps.Take(lowCount).Sum() / Math.Min(lowCount, sortedFps.Count);
        double score = mean > 0 ? 1000 * referenceFrameMs / mean : 0;
        double? sustainedScore = sustainedMeanMs is > 0 ? 1000 * referenceFrameMs / sustainedMeanMs.Value : null;
        return new GpuBenchmarkResult
        {
            AdapterName = adapterName,
            Score = score,
            SustainedScore = sustainedScore,
            AverageFrameTimeMs = mean,
            SustainedAverageFrameTimeMs = sustainedMeanMs,
            AverageFps = mean > 0 ? 1000 / mean : 0,
            MinFps = sortedFps.Count > 0 ? sortedFps[0] : 0,
            MaxFps = sortedFps.Count > 0 ? sortedFps[^1] : 0,
            OnePercentLowFps = onePercentLow,
            OutputWidth = outputWidth,
            OutputHeight = outputHeight,
            RenderWidth = renderWidth,
            RenderHeight = renderHeight,
            SamplesPerPixel = samplesPerPixel,
            TotalFramesRendered = totalFrames,
            DurationSeconds = seconds,
            ScoringVersion = scoringVersion,
        };
    }
}

// ───────────────────────────── Memory ─────────────────────────────

public sealed class MemoryTestResult
{
    public long TestedBytes { get; set; }
    public int Passes { get; set; }
    public double AverageWriteThroughputMBps { get; set; }
    public double AverageReadThroughputMBps { get; set; }
    public long ErrorsDetected { get; set; }
    public double DurationSeconds { get; set; }

    public bool Passed => ErrorsDetected == 0;
    public long TestedMiB => TestedBytes / (1024 * 1024);
}

// ───────────────────────────── Battery ─────────────────────────────

public enum BatteryIssueKind { HighCycleCount, HealthBelow90, HealthCritical, NotCharging }

public sealed record BatteryIssue(BatteryIssueKind Kind, double Value = 0)
{
    public string Text => Kind switch
    {
        BatteryIssueKind.HighCycleCount => T($"Cycle count is high ({(int)Value} cycles, rated for about 1,000)",
                                             $"تعداد سیکل شارژ زیاد است ({(int)Value} سیکل، عمر اسمی حدود ۱۰۰۰)"),
        BatteryIssueKind.HealthBelow90 => T($"Battery health is below 90% ({Fmt.Fixed(Value, 1)}%)",
                                            $"سلامت باتری زیر ۹۰٪ است ({Fmt.Fixed(Value, 1)}٪)"),
        BatteryIssueKind.HealthCritical => T($"Battery health is critically low ({Fmt.Fixed(Value, 1)}%) — service recommended",
                                             $"سلامت باتری به‌شدت پایین است ({Fmt.Fixed(Value, 1)}٪) — سرویس توصیه می‌شود"),
        _ => T("Plugged in but not charging properly", "به برق وصل است اما درست شارژ نمی‌شود"),
    };
}

public sealed class BatteryHealthResult
{
    public bool BatteryPresent { get; set; }
    /// <summary>Windows reports capacities in mWh; mAh values are derived when the voltage is known.</summary>
    public int DesignCapacityMwh { get; set; }
    public int FullChargeCapacityMwh { get; set; }
    public double HealthPercent { get; set; }
    public int CycleCount { get; set; }
    public int CurrentChargePercent { get; set; }
    public bool IsCharging { get; set; }
    public bool IsPluggedIn { get; set; }
    public double? DischargeRateWatts { get; set; }
    public double? TemperatureCelsius { get; set; }
    public string? Manufacturer { get; set; }
    public string? DeviceName { get; set; }
    public string? Chemistry { get; set; }
    public string? Condition { get; set; }
    public List<BatteryIssue> Issues { get; set; } = new();

    public bool NeedsService => BatteryPresent && HealthPercent > 0 && HealthPercent < 80;

    /// <summary>Works out the issue list from the readings.</summary>
    public static List<BatteryIssue> EvaluateIssues(double healthPercent, int cycleCount, bool isPluggedIn, bool isCharging,
        int chargePercent, bool fullyCharged)
    {
        var issues = new List<BatteryIssue>();
        if (healthPercent > 0 && healthPercent < 80) issues.Add(new BatteryIssue(BatteryIssueKind.HealthCritical, healthPercent));
        else if (healthPercent > 0 && healthPercent < 90) issues.Add(new BatteryIssue(BatteryIssueKind.HealthBelow90, healthPercent));
        if (cycleCount >= 1000) issues.Add(new BatteryIssue(BatteryIssueKind.HighCycleCount, cycleCount));
        // Many laptops stop charging at 80% to protect the battery, so only flag a clearly low battery.
        if (isPluggedIn && !isCharging && !fullyCharged && chargePercent < 75) issues.Add(new BatteryIssue(BatteryIssueKind.NotCharging));
        return issues;
    }
}

// ───────────────────────────── Display ─────────────────────────────

public enum DisplayIssue { DeadPixels, Ghosting, Brightness, BurnIn, LightBleed, RefreshRate }

public static class DisplayIssues
{
    public static readonly DisplayIssue[] All = Enum.GetValues<DisplayIssue>();

    public static string Text(this DisplayIssue issue) => issue switch
    {
        DisplayIssue.DeadPixels => T("Small black or stuck-color dots on the color/white screens (dead or stuck pixels)",
                                     "نقطه‌های سیاه یا رنگی ثابت روی صفحه‌های رنگی/سفید (پیکسل سوخته یا گیرکرده)"),
        DisplayIssue.Ghosting => T("Ghosting or motion blur", "سایه‌افتادن (Ghosting) یا تاری حرکت"),
        DisplayIssue.Brightness => T("Brightness looked too low or uneven", "روشنایی کم یا ناهموار بود"),
        DisplayIssue.BurnIn => T("Burn-in (a faint image staying on screen)", "سوختگی صفحه (تصویر کم‌رنگی که روی صفحه می‌ماند)"),
        DisplayIssue.LightBleed => T("Light bleeding in from the edges on the black screen", "نشت نور از لبه‌ها روی صفحه سیاه"),
        _ => T("Refresh rate looked wrong or stuttered on the motion test", "نرخ تازه‌سازی در تست حرکت درست نبود یا لرزش داشت"),
    };

    public static string ShortText(this DisplayIssue issue) => issue switch
    {
        DisplayIssue.DeadPixels => T("Dead or stuck pixels", "پیکسل سوخته یا گیرکرده"),
        DisplayIssue.Ghosting => T("Ghosting / motion blur", "سایه‌افتادن / تاری حرکت"),
        DisplayIssue.Brightness => T("Brightness / backlight issue", "مشکل روشنایی / نور پس‌زمینه"),
        DisplayIssue.BurnIn => T("Screen burn-in", "سوختگی صفحه"),
        DisplayIssue.LightBleed => T("Screen bleed", "نشت نور"),
        _ => T("Refresh rate issue", "مشکل نرخ تازه‌سازی"),
    };

    /// <summary>Points taken off the display score for this issue.</summary>
    public static double Deduction(this DisplayIssue issue) => issue switch
    {
        DisplayIssue.DeadPixels => 25,
        DisplayIssue.Ghosting => 10,
        DisplayIssue.Brightness => 20,
        DisplayIssue.BurnIn => 20,
        DisplayIssue.LightBleed => 15,
        _ => 15,
    };
}

public sealed class DisplayTestResult
{
    public List<DisplayIssue> Issues { get; set; } = new();
    public double? MeasuredRefreshRateHz { get; set; }
}

// ───────────────────────────── Speakers / mic / camera ─────────────────────────────

public sealed class AudioTestResult
{
    public bool LeftHeard { get; set; }
    public bool RightHeard { get; set; }
    public string? ReportedIssue { get; set; }
}

public sealed class MicCameraTestResult
{
    public bool CameraPresent { get; set; }
    public bool CameraWorks { get; set; }
    public bool MicrophonePresent { get; set; }
    public bool MicrophoneWorks { get; set; }
    public double? PeakLevelDb { get; set; }
    public string? ReportedIssue { get; set; }
}

// ───────────────────────────── Keyboard & touchpad ─────────────────────────────

public sealed class KeyboardTestResult
{
    public bool Bypassed { get; set; }
    public bool SentenceCorrect { get; set; }
    public int TotalKeys { get; set; }
    public int PressedKeys { get; set; }
    public List<string> MissingKeys { get; set; } = new();
    public List<string> DoublePressedKeys { get; set; } = new();
    public bool TrackpadMoved { get; set; }
    public bool TrackpadClicked { get; set; }
    public bool TrackpadScrolled { get; set; }

    public int TrackpadChecksPassed => (TrackpadMoved ? 1 : 0) + (TrackpadClicked ? 1 : 0) + (TrackpadScrolled ? 1 : 0);

    public int IssueCount => Bypassed ? 0
        : MissingKeys.Count + DoublePressedKeys.Count + (SentenceCorrect ? 0 : 1) + (3 - TrackpadChecksPassed);
}

// ───────────────────────────── Drive health ─────────────────────────────

public enum DriveHealthStatus { Good, Warning, Failing, Unknown }

public enum DriveHealthIssueKind
{
    SpareBelowThreshold,
    TemperatureWarning,
    ReliabilityDegraded,
    ReadOnly,
    BackupPowerFailed,
    EnduranceUsedUp,
    EnduranceAlmostUsedUp,
    MediaErrors,
    ReallocatedSectors,
    PendingSectors,
    PredictsFailure,
    OsFailing,
    OsWarning,
}

public sealed record DriveHealthIssue(DriveHealthIssueKind Kind, long Count = 0)
{
    public string Text => Kind switch
    {
        DriveHealthIssueKind.SpareBelowThreshold => T("Spare blocks have fallen below the manufacturer's safe threshold",
                                                      "بلوک‌های یدکی از حد امن سازنده پایین‌تر رفته‌اند"),
        DriveHealthIssueKind.TemperatureWarning => T("The drive reports an over-temperature warning", "درایو هشدار دمای بالا گزارش می‌دهد"),
        DriveHealthIssueKind.ReliabilityDegraded => T("The drive reports its reliability is degraded", "درایو کاهش قابلیت اطمینان را گزارش می‌دهد"),
        DriveHealthIssueKind.ReadOnly => T("The drive has switched itself to read-only", "درایو خودش را فقط‌خواندنی کرده است"),
        DriveHealthIssueKind.BackupPowerFailed => T("The drive's power-loss protection has failed", "محافظت درایو در برابر قطع برق از کار افتاده است"),
        DriveHealthIssueKind.EnduranceUsedUp => T("Rated write endurance used up (100% or more)", "عمر نوشتن اسمی تمام شده است (۱۰۰٪ یا بیشتر)"),
        DriveHealthIssueKind.EnduranceAlmostUsedUp => T("Rated write endurance almost used up (90% or more)", "عمر نوشتن اسمی تقریباً تمام شده است (۹۰٪ یا بیشتر)"),
        DriveHealthIssueKind.MediaErrors => T($"Unrecovered data errors have been recorded ({Count})", $"خطاهای داده غیرقابل بازیابی ثبت شده است ({Count})"),
        DriveHealthIssueKind.ReallocatedSectors => T($"Bad sectors have been replaced with spares ({Count})", $"سکتورهای خراب با یدکی جایگزین شده‌اند ({Count})"),
        DriveHealthIssueKind.PendingSectors => T($"Unstable sectors are waiting to be remapped ({Count})", $"سکتورهای ناپایدار در انتظار جایگزینی هستند ({Count})"),
        DriveHealthIssueKind.PredictsFailure => T("The drive's own SMART check predicts it will fail soon", "بررسی SMART خود درایو خرابی نزدیک را پیش‌بینی می‌کند"),
        DriveHealthIssueKind.OsFailing => T("Windows reports this drive as unhealthy", "ویندوز این درایو را ناسالم گزارش می‌کند"),
        _ => T("Windows reports a warning for this drive", "ویندوز برای این درایو هشدار می‌دهد"),
    };

    public double Deduction => Kind switch
    {
        DriveHealthIssueKind.SpareBelowThreshold => 40,
        DriveHealthIssueKind.TemperatureWarning => 10,
        DriveHealthIssueKind.ReliabilityDegraded => 50,
        DriveHealthIssueKind.ReadOnly => 60,
        DriveHealthIssueKind.BackupPowerFailed => 20,
        DriveHealthIssueKind.EnduranceUsedUp => 40,
        DriveHealthIssueKind.EnduranceAlmostUsedUp => 20,
        DriveHealthIssueKind.MediaErrors => 30,
        DriveHealthIssueKind.ReallocatedSectors => Count >= 100 ? 40 : 20,
        DriveHealthIssueKind.PendingSectors => 30,
        DriveHealthIssueKind.PredictsFailure => 100,
        DriveHealthIssueKind.OsFailing => 100,
        _ => 30,
    };
}

public sealed class DriveHealthInfo
{
    public string Id { get; set; } = "";
    public string Model { get; set; } = "";
    public ulong SizeBytes { get; set; }
    public string? BusType { get; set; }
    public string? MediaType { get; set; }
    public bool IsInternal { get; set; }
    public DriveHealthStatus Status { get; set; } = DriveHealthStatus.Unknown;
    /// <summary>What Windows itself says ("Healthy", "Warning", "Unhealthy").</summary>
    public string? OsHealthStatus { get; set; }
    /// <summary>Where the numbers came from ("NVMe health log", "ATA SMART", "Windows reliability counters").</summary>
    public string? Source { get; set; }
    public int? PercentageUsed { get; set; }
    public int? AvailableSparePercent { get; set; }
    public int? AvailableSpareThresholdPercent { get; set; }
    public double? DataWrittenTB { get; set; }
    public double? DataReadTB { get; set; }
    public long? PowerOnHours { get; set; }
    public long? PowerCycles { get; set; }
    public long? UnsafeShutdowns { get; set; }
    public long? MediaErrors { get; set; }
    public long? ReallocatedSectors { get; set; }
    public long? PendingSectors { get; set; }
    public double? TemperatureCelsius { get; set; }
    public int? CriticalWarning { get; set; }
    public List<DriveHealthIssue> Issues { get; set; } = new();
    public double? Score { get; set; }

    public int? LifeLeftPercent => PercentageUsed is { } used ? Math.Max(0, 100 - used) : null;
    public bool HasHealthData => Score is not null;
}

public static class DriveHealthStatusExtensions
{
    public static string Title(this DriveHealthStatus status) => status switch
    {
        DriveHealthStatus.Good => T("Good", "خوب"),
        DriveHealthStatus.Warning => T("Warning", "هشدار"),
        DriveHealthStatus.Failing => T("Failing", "در حال خرابی"),
        _ => T("No data", "بدون داده"),
    };
}

public sealed class DriveHealthReport
{
    public List<DriveHealthInfo> Drives { get; set; } = new();
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;

    /// <summary>The worst drive decides.</summary>
    public double? WorstScore
    {
        get
        {
            var scores = Drives.Where(d => d.Score is not null).Select(d => d.Score!.Value).ToList();
            return scores.Count == 0 ? null : scores.Min();
        }
    }
}

// ───────────────────────────── SSD speed ─────────────────────────────

public enum StorageTestKind { Seq1mQ8T1, Rnd4kQ32T1, Rnd4kQ1T1 }

public enum StorageOperation { Read, Write }

public static class StorageTestKinds
{
    public static readonly StorageTestKind[] All = { StorageTestKind.Seq1mQ8T1, StorageTestKind.Rnd4kQ32T1, StorageTestKind.Rnd4kQ1T1 };
    public static readonly StorageOperation[] Operations = { StorageOperation.Read, StorageOperation.Write };

    public static string Label(this StorageTestKind kind) => kind switch
    {
        StorageTestKind.Seq1mQ8T1 => "SEQ1M Q8T1",
        StorageTestKind.Rnd4kQ32T1 => "RND4K Q32T1",
        _ => "RND4K Q1T1",
    };

    public static int BlockSize(this StorageTestKind kind) => kind == StorageTestKind.Seq1mQ8T1 ? 1024 * 1024 : 4096;

    public static int QueueDepth(this StorageTestKind kind) => kind switch
    {
        StorageTestKind.Seq1mQ8T1 => 8,
        StorageTestKind.Rnd4kQ32T1 => 32,
        _ => 1,
    };

    public static bool IsSequential(this StorageTestKind kind) => kind == StorageTestKind.Seq1mQ8T1;

    public static string Title(this StorageTestKind kind) => kind switch
    {
        StorageTestKind.Seq1mQ8T1 => T("Sequential", "ترتیبی"),
        StorageTestKind.Rnd4kQ32T1 => T("Random", "تصادفی"),
        _ => T("Random, one at a time", "تصادفی، یکی‌یکی"),
    };

    public static string Title(this StorageOperation op) => op == StorageOperation.Read ? T("Read", "خواندن") : T("Write", "نوشتن");
}

public sealed class StorageTestMeasurement
{
    public StorageTestKind Kind { get; set; }
    public StorageOperation Operation { get; set; }
    public double MbPerSecond { get; set; }
    public double Iops { get; set; }
    public double AverageLatencyMs { get; set; }

    /// <summary>Binary megabytes (1 MiB = 1,048,576 bytes).</summary>
    public double MibPerSecond => MbPerSecond * 1_000_000 / 1_048_576;
}

public sealed class StorageBenchmarkResult
{
    public string TargetPath { get; set; } = "";
    public string? VolumeName { get; set; }
    public string ProfileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public int RunsPerTest { get; set; }
    public List<StorageTestMeasurement> Measurements { get; set; } = new();
    public DateTime Date { get; set; } = DateTime.UtcNow;

    public StorageTestMeasurement? Measurement(StorageTestKind kind, StorageOperation op) =>
        Measurements.FirstOrDefault(m => m.Kind == kind && m.Operation == op);
}

// ───────────────────────────── A complete run ─────────────────────────────

public sealed class ModuleFailure
{
    public TestModule Module { get; set; }
    public string Message { get; set; } = "";

    public ModuleFailure() { }
    public ModuleFailure(TestModule module, string message) { Module = module; Message = message; }
}

public sealed class FinalReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public List<TestModule> Plan { get; set; } = new();
    public List<TestModule> Completed { get; set; } = new();
    public List<TestModule> Skipped { get; set; } = new();
    public List<ModuleFailure> Failures { get; set; } = new();
    public SystemSpecs? Specs { get; set; }
    public CpuBenchmarkResult? Cpu { get; set; }
    public GpuBenchmarkResult? Gpu { get; set; }
    public MemoryTestResult? Memory { get; set; }
    public BatteryHealthResult? Battery { get; set; }
    public DisplayTestResult? Display { get; set; }
    public AudioTestResult? Audio { get; set; }
    public MicCameraTestResult? MicCamera { get; set; }
    public KeyboardTestResult? Keyboard { get; set; }
    public DriveHealthReport? Drives { get; set; }
    public bool HasNoBuiltInInputs { get; set; }

    public FinalReport() { }

    public FinalReport(IEnumerable<TestModule> plan, SystemSpecs? specs = null, bool hasNoBuiltInInputs = false)
    {
        Plan = plan.ToList();
        Specs = specs;
        HasNoBuiltInInputs = hasNoBuiltInInputs;
    }

    public List<TestModule> Remaining =>
        Plan.Where(m => !Completed.Contains(m) && !Skipped.Contains(m) && Failures.All(f => f.Module != m)).ToList();

    public bool IsFinished => Remaining.Count == 0;

    public ModuleFailure? FailureFor(TestModule module) => Failures.FirstOrDefault(f => f.Module == module);

    /// <summary>Hardware checks only (no CPU/GPU speed).</summary>
    public bool RanAnyHardwareCheck =>
        new[] { TestModule.Battery, TestModule.Display, TestModule.Speakers, TestModule.MicCamera, TestModule.Input,
                TestModule.Drives, TestModule.Memory }.Any(Completed.Contains);
}
