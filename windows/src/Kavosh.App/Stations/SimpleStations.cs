using Avalonia.Controls;
using Avalonia.Media;
using Kavosh.App.Ui;
using Kavosh.App.Views;
using Kavosh.Core;
using Kavosh.Hardware;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Stations;

// ───────────────────────────── Memory ─────────────────────────────

public sealed class MemoryStation : Station
{
    private volatile bool _cancelled;
    private readonly TextBlock _phase = Kv.Text(T("Allocating memory…", "در حال گرفتن حافظه…"), 18, FontWeight.Bold);
    private readonly RetroProgressBar _bar = new(14, Palette.PurpleColor);
    private readonly Slot _tiles = new();

    public MemoryStation(TestSuiteRunner runner) : base(runner)
    {
        Content = Kv.Card(Kv.Column(14, Kv.SectionLabel(T("Pattern write / verify", "نوشتن / بررسی الگو"), Icon.Memory), _phase, _bar, _tiles));
        ShowTiles(null);
    }

    protected override void Start()
    {
        bool pressure = (Runner.Telemetry.Latest?.MemoryPressure ?? MemoryPressureLevel.Normal) != MemoryPressureLevel.Normal;
        long size = MemoryTestEngine.RecommendedSize(Platform.PhysicalMemoryBytes, pressure);
        var engine = new MemoryTestEngine(size);
        RunInBackground(() =>
        {
            try
            {
                var result = engine.Run(() => _cancelled, p => OnUi(() => Show(p)));
                OnUi(() => Finish(result));
            }
            catch (MemoryAllocationException e)
            {
                OnUi(() => Content = StationError.Make(T("The memory test couldn't run", "تست حافظه اجرا نشد"), e.Message,
                    () => Runner.Fail(TestModule.Memory, e.Message)));
            }
            catch
            {
                // Cancelled.
            }
        }, "Kavosh memory test");
    }

    protected override void Stop() => _cancelled = true;

    private void Show(MemoryTestProgress p)
    {
        string hex = p.Pattern.ToString("X16");
        _phase.Text = p.Phase switch
        {
            MemoryTestPhase.Writing => T($"Writing pass {p.Pass}/{p.Passes} (0x{hex})", $"نوشتن دور {p.Pass}/{p.Passes} (0x{hex})"),
            MemoryTestPhase.Verifying => T($"Verifying pass {p.Pass}/{p.Passes} (0x{hex})", $"بررسی دور {p.Pass}/{p.Passes} (0x{hex})"),
            MemoryTestPhase.Finished => T("Done", "تمام شد"),
            _ => T("Allocating memory…", "در حال گرفتن حافظه…"),
        };
        _bar.Value = p.Fraction;
        ShowTiles(p);
    }

    private void ShowTiles(MemoryTestProgress? p)
    {
        long errors = p?.Errors ?? 0;
        _tiles.Content = Kv.Columns(14,
            Kv.StatTile(T("Tested", "حجم تست"), p is { } v ? $"{v.TestedBytes / 1_048_576} MiB" : "—"),
            Kv.StatTile(T("Write throughput", "سرعت نوشتن"), p?.WriteMBps is { } w ? Fmt.MBps(w) : "—"),
            Kv.StatTile(T("Read throughput", "سرعت خواندن"), p?.ReadMBps is { } r ? Fmt.MBps(r) : "—"),
            Kv.StatTile(T("Errors detected", "خطاهای پیدا‌شده"), errors.ToString(), color: errors > 0 ? Palette.Red : Palette.Green));
    }

    private void Finish(MemoryTestResult result)
    {
        Content = Kv.Column(18, ResultCard(result),
            new ContinueBar(Runner.IsLastStation, AutoAdvance, () => Runner.Complete(TestModule.Memory, r => r.Memory = result)));
    }

    public static Control ResultCard(MemoryTestResult result)
    {
        return Kv.Card(Kv.Column(14,
            Kv.SectionLabel(T("Memory test results", "نتایج تست حافظه"), Icon.Memory),
            result.Passed
                ? Kv.Label(T("No issues found.", "مشکلی پیدا نشد."), Icon.Seal, Palette.Green, 16)
                : Kv.Label(T("Memory errors detected", "خطای حافظه پیدا شد"), Icon.Cross, Palette.Red, 16),
            Kv.Columns(14,
                Kv.StatTile(T("Tested", "حجم تست"), $"{result.TestedMiB} MiB", T($"{result.Passes} patterns", $"{result.Passes} الگو")),
                Kv.StatTile(T("Write throughput", "سرعت نوشتن"), Fmt.MBps(result.AverageWriteThroughputMBps)),
                Kv.StatTile(T("Read throughput", "سرعت خواندن"), Fmt.MBps(result.AverageReadThroughputMBps)),
                Kv.StatTile(T("Errors detected", "خطاهای پیدا‌شده"), result.ErrorsDetected.ToString(), color: result.Passed ? Palette.Green : Palette.Red))));
    }
}

// ───────────────────────────── Battery ─────────────────────────────

public sealed class BatteryStation : Station
{
    public const double LoadSeconds = 20;
    private volatile bool _cancelled;
    private readonly Slot _info = new();
    private readonly RetroProgressBar _bar = new(14, Palette.GreenColor);
    private readonly Slot _live = new();

    public BatteryStation(TestSuiteRunner runner) : base(runner)
    {
        Content = Kv.Card(Kv.Busy(T("Reading the battery…", "در حال خواندن باتری…")));
    }

    protected override void Start()
    {
        RunInBackground(() =>
        {
            var first = Platform.ReadBattery();
            if (first is null || first.DesignCapacityMwh <= 0)
            {
                OnUi(ShowNoBattery);
                return;
            }
            OnUi(() => ShowSampling(first));
            var load = new CpuLoadGenerator();
            load.Start();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var samples = new List<double>();
            var latest = first;
            try
            {
                while (clock.Elapsed.TotalSeconds < LoadSeconds && !_cancelled)
                {
                    Thread.Sleep(1000);
                    if (Platform.ReadBattery() is { } r)
                    {
                        latest = r;
                        if (r.DischargeWatts is { } watts && !r.IsPluggedIn) samples.Add(watts);
                    }
                    double elapsed = clock.Elapsed.TotalSeconds;
                    var current = latest;
                    double? last = samples.Count > 0 ? samples[^1] : null;
                    OnUi(() => UpdateSampling(current, elapsed, last));
                }
            }
            finally
            {
                load.Stop();
            }
            if (_cancelled) return;
            // Ignore the first couple of seconds while the load ramps up.
            var settled = samples.Count > 4 ? samples.Skip(2).ToList() : samples;
            double? average = settled.Count > 0 ? settled.Average() : null;
            var result = BatteryReading.Result(latest, average);
            OnUi(() => Finish(result));
        }, "Kavosh battery test");
    }

    protected override void Stop() => _cancelled = true;

    private void ShowNoBattery()
    {
        Content = Kv.Column(18,
            Kv.Card(Kv.Column(8, Kv.SectionLabel(T("Battery", "باتری"), Icon.Battery),
                Kv.Text(T("No battery detected on this system. This PC has no battery, so nothing is scored for it.",
                          "باتری‌ای روی این سیستم پیدا نشد. این رایانه باتری ندارد، پس امتیازی برای آن حساب نمی‌شود."), 14))),
            new ContinueBar(Runner.IsLastStation, Runner.IsSingleTest ? null : 3,
                () => Runner.Complete(TestModule.Battery, r => r.Battery = new BatteryHealthResult { BatteryPresent = false })));
    }

    private void ShowSampling(BatteryReading reading)
    {
        _info.Content = InfoCard(reading);
        Content = Kv.Column(18, _info, Kv.Card(Kv.Column(12,
            Kv.SectionLabel(T("Discharge (under load)", "تخلیه (زیر بار)"), Icon.Bolt),
            Kv.Text(T("Sampling battery + running a short load…", "در حال نمونه‌برداری از باتری و اجرای یک بار کوتاه…"), 14, FontWeight.SemiBold),
            _bar, _live)));
        UpdateSampling(reading, 0, null);
    }

    private void UpdateSampling(BatteryReading reading, double elapsed, double? lastWatts)
    {
        _info.Content = InfoCard(reading);
        _bar.Value = elapsed / LoadSeconds;
        if (reading.IsPluggedIn)
        {
            _live.Content = Kv.Text(T("The PC is plugged in, so the discharge rate can't be measured. Unplug it for this step to measure it.",
                                      "رایانه به برق وصل است، پس نرخ تخلیه اندازه‌گیری نمی‌شود. برای اندازه‌گیری، در این مرحله آن را از برق جدا کنید."),
                12, FontWeight.Medium, Palette.Muted);
        }
        else
        {
            _live.Content = lastWatts is { } w ? Kv.Hardware($"{Fmt.Fixed(w, 1)} W", 28, FontWeight.ExtraBold, Palette.Green) : null;
        }
    }

    private static Control InfoCard(BatteryReading r)
    {
        string state = r.IsCharging ? T("charging", "در حال شارژ")
            : r.IsPluggedIn ? T("plugged in, not charging", "به برق وصل، بدون شارژ") : T("on battery", "روی باتری");
        return Kv.Card(Kv.Column(12,
            Kv.SectionLabel(T("General info", "اطلاعات کلی"), Icon.Battery),
            Kv.Columns(14,
                Kv.StatTile(T("Health", "سلامت"), Fmt.Percent(r.HealthPercent, 1), CapacityText(r.FullChargeCapacityMwh, r.DesignCapacityMwh),
                    new SolidColorBrush(Palette.ScoreColor(r.HealthPercent))),
                Kv.StatTile(T("Cycles", "سیکل‌ها"), r.CycleCount.ToString()),
                Kv.StatTile(T("Charge", "شارژ"), $"{r.ChargePercent}%", state),
                Kv.StatTile(T("Temperature", "دما"), Fmt.Celsius(r.TemperatureCelsius)))));
    }

    private static string CapacityText(int full, int design) =>
        T($"{Fmt.Number(full)} mWh of {Fmt.Number(design)} mWh design", $"{Fmt.Number(full)} از {Fmt.Number(design)} میلی‌وات‌ساعت طراحی");

    private void Finish(BatteryHealthResult result)
    {
        Content = Kv.Column(18, ResultCard(result),
            new ContinueBar(Runner.IsLastStation, Runner.IsSingleTest ? null : 5, () => Runner.Complete(TestModule.Battery, r => r.Battery = result)));
    }

    public static Control ResultCard(BatteryHealthResult result)
    {
        var heading = Kv.Spread(Kv.SectionLabel(T("Battery score", "امتیاز باتری"), Icon.Battery),
            result.NeedsService ? Kv.Badge(T("Needs service", "نیاز به سرویس"), Palette.RedColor, Brushes.White, Icon.Wrench) : null);
        var stack = Kv.Column(14, heading,
            Kv.Columns(14,
                Kv.StatTile(T("Health", "سلامت"), Fmt.Percent(result.HealthPercent, 1), CapacityText(result.FullChargeCapacityMwh, result.DesignCapacityMwh),
                    new SolidColorBrush(Palette.ScoreColor(result.HealthPercent))),
                Kv.StatTile(T("Cycles", "سیکل‌ها"), result.CycleCount.ToString(),
                    result.CycleCount == 0 ? T("0 can also mean the laptop doesn't report it", "عدد ۰ ممکن است یعنی لپ‌تاپ آن را گزارش نمی‌دهد") : null),
                Kv.StatTile(T("Discharge (under load)", "تخلیه (زیر بار)"), result.DischargeRateWatts is { } d ? $"{Fmt.Fixed(d, 1)} W" : "—",
                    result.DischargeRateWatts is null ? T("Not measured (plugged in)", "اندازه‌گیری نشد (به برق وصل)") : null),
                Kv.StatTile(T("Chemistry", "نوع باتری"), result.Chemistry ?? "—", result.Manufacturer)),
            Kv.Text(result.IsCharging
                ? T($"Currently {result.CurrentChargePercent}% charged and charging. Charged/discharged roughly {result.CycleCount} cycles so far.",
                    $"اکنون {result.CurrentChargePercent}٪ شارژ دارد و در حال شارژ است. تاکنون حدود {result.CycleCount} سیکل شارژ/تخلیه شده است.")
                : T($"Currently {result.CurrentChargePercent}% charged, not charging. Charged/discharged roughly {result.CycleCount} cycles so far.",
                    $"اکنون {result.CurrentChargePercent}٪ شارژ دارد و شارژ نمی‌شود. تاکنون حدود {result.CycleCount} سیکل شارژ/تخلیه شده است."), 13),
            Kv.Text(T("A cycle counts as one full charge worth of use, spread across however many partial charges it took — most laptop batteries are rated for roughly 500 to 1,000 cycles.",
                      "هر سیکل برابر یک شارژ کامل مصرف است، حتی اگر در چند شارژ جزئی پخش شده باشد — بیشتر باتری‌های لپ‌تاپ برای حدود ۵۰۰ تا ۱۰۰۰ سیکل طراحی شده‌اند."),
                11, FontWeight.Medium, Palette.Muted));
        foreach (var issue in result.Issues) stack.Children.Add(Kv.Label(issue.Text, Icon.Warning, Palette.Orange));
        return Kv.Card(stack);
    }
}

// ───────────────────────────── Drives ─────────────────────────────

public sealed class DriveStation : Station
{
    public DriveStation(TestSuiteRunner runner) : base(runner)
    {
        Content = Kv.Card(Kv.Column(12, Kv.SectionLabel(T("SSD / Drive health (SMART)", "سلامت SSD / درایو (SMART)"), Icon.Drive),
            Kv.Busy(T("Reading drive health (SMART)…", "در حال خواندن سلامت درایو (SMART)…"))));
    }

    protected override void Start()
    {
        RunInBackground(() =>
        {
            var report = Platform.ReadDriveHealth();
            OnUi(() => Show(report));
        }, "Kavosh drive health");
    }

    private void Show(DriveHealthReport report)
    {
        var stack = Kv.Column(12, Kv.SectionLabel(T("SSD / Drive health (SMART)", "سلامت SSD / درایو (SMART)"), Icon.Drive));
        if (report.Drives.Count == 0) stack.Children.Add(Kv.Text(T("No drives found.", "درایوی پیدا نشد."), 14));
        foreach (var drive in report.Drives) stack.Children.Add(DashboardView.DriveRow(drive));
        Content = Kv.Column(18, Kv.Card(stack),
            new ContinueBar(Runner.IsLastStation, AutoAdvance, () => Runner.Complete(TestModule.Drives, r => r.Drives = report)));
    }
}
