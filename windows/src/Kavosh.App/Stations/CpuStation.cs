using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kavosh.App.Ui;
using Kavosh.App.Views;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Stations;

public sealed class CpuStation : Station
{
    private volatile bool _cancelled;
    private Guid? _recording;

    // Live view.
    private readonly Slot _stageLabelHost = new();
    private readonly TextBlock _stageDetail = Kv.Text("", 13, FontWeight.Medium, Palette.Muted);
    private readonly TextBlock _liveScore = Kv.Hardware("…", 54, FontWeight.Black, Palette.Accent, wrap: false);
    private readonly RetroProgressBar _phaseBar = new(14, Palette.AccentColor);
    private readonly RetroProgressBar _overallBar = new(8, Palette.YellowColor);
    private readonly TextBlock _elapsed = Kv.Hardware("0:00", 12, FontWeight.Bold, Palette.Muted, wrap: false);
    private readonly Slot _tiles = new();
    private readonly Slot _sustained = new();
    private readonly Slot _wrong = new();
    private CpuBenchmarkProgress? _last;
    private HistoryChart? _sustainedChart;

    public CpuStation(TestSuiteRunner runner) : base(runner)
    {
        Content = Running();
        Runner.Telemetry.Updated += RefreshTiles;
    }

    protected override void Start()
    {
        _recording = Runner.Telemetry.BeginRecording();
        var engine = new CpuBenchmarkEngine(new CpuBenchmarkConfiguration { DurationSeconds = Runner.Settings.PerformanceTestSeconds });
        RunInBackground(() =>
        {
            try
            {
                var result = engine.Run(() => _cancelled, p => OnUi(() => Show(p)));
                OnUi(() => Finish(result));
            }
            catch (Exception e) when (!_cancelled)
            {
                OnUi(() => ShowError(e.Message));
            }
            catch
            {
                // Cancelled: nothing to report.
            }
        }, "Kavosh CPU benchmark");
    }

    protected override void Stop()
    {
        _cancelled = true;
        Runner.Telemetry.Updated -= RefreshTiles;
        if (_recording is { } id) Runner.Telemetry.EndRecording(id);
        _recording = null;
    }

    private Control Running()
    {
        var scoreRow = Kv.Row(10, _liveScore, Kv.Text(T("live score", "امتیاز زنده"), 13, FontWeight.Bold, Palette.Muted, wrap: false));
        scoreRow.FlowDirection = Loc.Language.IsRightToLeft() ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var card = Kv.Card(Kv.Column(14, _stageLabelHost, _stageDetail, scoreRow, _phaseBar,
            Kv.CaptionedBar(T("Overall", "کل"), _overallBar, _elapsed)));
        Show(new CpuBenchmarkProgress { Phase = CpuBenchmarkPhase.Preparing, SustainedSamples = Array.Empty<double>() });
        return Kv.Column(18, card, _tiles, _sustained, _wrong);
    }

    private void Show(CpuBenchmarkProgress p)
    {
        _last = p;
        _stageLabelHost.Content = Kv.SectionLabel(StageTitle(p.Phase), Icon.Cpu);
        _stageDetail.Text = StageDetail(p.Phase);
        _liveScore.Text = p.LiveScore is { } live ? Fmt.Number(live, 0) : "…";
        _phaseBar.Value = p.PhaseFraction;
        _overallBar.Value = p.OverallFraction;
        _elapsed.Text = Fmt.Duration(p.Elapsed);
        RefreshTiles();
        if (p.SustainedSamples is { Count: > 0 } samples)
        {
            if (_sustainedChart is null)
            {
                _sustainedChart = new HistoryChart { Height = 90, LineColor = Palette.AccentColor };
                _sustained.Content = Kv.Card(Kv.Column(8, Kv.SectionLabel(T("Sustained score over time", "امتیاز پایدار در طول زمان"), Icon.Chart), _sustainedChart));
            }
            _sustainedChart.MaxValue = (p.MultiCoreScore ?? 1000) * 1.15;
            _sustainedChart.Capacity = Math.Max(12, samples.Count);
            _sustainedChart.Values = samples;
        }
        _wrong.Content = p.WrongResults > 0
            ? Kv.Label(T($"The CPU returned {p.WrongResults} wrong result(s) so far", $"پردازنده تاکنون {p.WrongResults} نتیجه اشتباه داده است"), Icon.Warning, Palette.Red)
            : null;
    }

    private void RefreshTiles()
    {
        if (_tiles.Tag is "result") return;
        var p = _last;
        var latest = Runner.Telemetry.Latest;
        double? power = latest?.SystemPowerWatts ?? latest?.CpuPowerWatts;
        _tiles.Content = Kv.Columns(14,
            Kv.StatTile(T("Multi-core score", "امتیاز چندهسته‌ای"), p?.MultiCoreScore is { } m ? Fmt.Number(m, 0) : "—", icon: Icon.Grid),
            Kv.StatTile(T("Single-core score", "امتیاز تک‌هسته‌ای"), p?.SingleCoreScore is { } s ? Fmt.Number(s, 0) : "—", icon: Icon.Square),
            Kv.StatTile(T("CPU temperature", "دمای پردازنده"), Fmt.Celsius(latest?.CpuTemperatureCelsius),
                color: new SolidColorBrush(Palette.TemperatureColor(latest?.CpuTemperatureCelsius)), icon: Icon.Thermometer),
            Kv.StatTile(latest?.SystemPowerWatts is null ? T("CPU power", "توان پردازنده") : T("System power", "توان سیستم"), Fmt.Watts(power), icon: Icon.Bolt));
    }

    private void Finish(CpuBenchmarkResult result)
    {
        if (_recording is { } id)
        {
            var summary = Runner.Telemetry.EndRecording(id);
            result.PeakTemperatureCelsius = summary.PeakCpuTemperature;
            result.AveragePowerWatts = summary.AveragePower;
            if (summary.WorstThermal >= ThermalLevel.Serious) result.ThermalThrottleDetected = true;
        }
        _recording = null;
        _tiles.Tag = "result";
        Content = Kv.Column(18, ResultCard(result),
            new ContinueBar(Runner.IsLastStation, AutoAdvance, () => Runner.Complete(TestModule.Cpu, r => r.Cpu = result)));
    }

    private void ShowError(string message)
    {
        Content = StationError.Make(T("The CPU test stopped", "تست پردازنده متوقف شد"), message, () => Runner.Fail(TestModule.Cpu, message));
    }

    private static string StageTitle(CpuBenchmarkPhase phase) => phase switch
    {
        CpuBenchmarkPhase.MultiCore => T("Stage 1 of 3 — multi-core", "مرحله ۱ از ۳ — چندهسته‌ای"),
        CpuBenchmarkPhase.SingleCore => T("Stage 2 of 3 — single-core", "مرحله ۲ از ۳ — تک‌هسته‌ای"),
        CpuBenchmarkPhase.Sustained => T("Stage 3 of 3 — sustained load", "مرحله ۳ از ۳ — بار پایدار"),
        CpuBenchmarkPhase.Finished => T("Finishing", "در حال پایان"),
        _ => T("Preparing", "در حال آماده‌سازی"),
    };

    private static string StageDetail(CpuBenchmarkPhase phase) => phase switch
    {
        CpuBenchmarkPhase.MultiCore => T("A fixed amount of work spread over every core. Takes the same time whatever duration you picked.",
                                         "مقدار ثابتی کار روی همه هسته‌ها. مدت آن به مدت انتخابی شما بستگی ندارد."),
        CpuBenchmarkPhase.SingleCore => T("A fixed amount of work on one core.", "مقدار ثابتی کار روی یک هسته."),
        CpuBenchmarkPhase.Sustained => T("Sustained load for the rest of the chosen duration, to see whether the CPU holds its speed as it heats up.",
                                         "بار پایدار تا پایان مدت انتخابی، برای دیدن این‌که آیا پردازنده با گرم شدن سرعتش را حفظ می‌کند."),
        _ => T("Getting the workload ready…", "آماده‌سازی بار کاری…"),
    };

    public static Control ResultCard(CpuBenchmarkResult result)
    {
        var stack = Kv.Column(16, Kv.SectionLabel(T("CPU benchmark results", "نتایج بنچمارک پردازنده"), Icon.Cpu),
            Kv.Columns(14,
                Kv.StatTile(T("Multi-core score", "امتیاز چندهسته‌ای"), Fmt.Number(result.Score, 0),
                    T($"{result.ThreadsUsed} threads", $"{result.ThreadsUsed} رشته"), Palette.Accent),
                Kv.StatTile(T("Single-core score", "امتیاز تک‌هسته‌ای"), Fmt.Number(result.SingleCoreScore, 0), color: Palette.Accent),
                Kv.StatTile(T("Sustained", "پایدار"), result.SustainedScore is { } s ? Fmt.Number(s, 0) : "—",
                    result.SustainedScore is null ? T("Raise the duration to measure it", "برای اندازه‌گیری، مدت را بیشتر کنید") : null),
                Kv.StatTile(T("Peak CPU temperature", "بیشترین دمای پردازنده"), Fmt.Celsius(result.PeakTemperatureCelsius),
                    color: new SolidColorBrush(Palette.TemperatureColor(result.PeakTemperatureCelsius)))));
        stack.Children.Add(result.WrongResults == 0
            ? Kv.Label(T("Every calculation came back correct.", "همه محاسبات درست برگشتند."), Icon.Seal, Palette.Green)
            : Kv.Label(T($"The CPU returned wrong results in {result.WrongResults} calculation(s). This points to instability (an overclock or undervolt) or a hardware fault — the score isn't valid.",
                         $"پردازنده در {result.WrongResults} محاسبه نتیجه اشتباه داد. این نشانه ناپایداری (اورکلاک یا آندرولت) یا ایراد سخت‌افزاری است — امتیاز معتبر نیست."),
                Icon.Cross, Palette.Red));
        if (result.ThermalThrottleDetected)
        {
            stack.Children.Add(Kv.Label(T("Thermal throttle detected — this PC did not hold its speed under sustained load.",
                                          "کاهش سرعت حرارتی تشخیص داده شد — این رایانه زیر بار پایدار سرعتش را حفظ نکرد."), Icon.Flame, Palette.Orange));
        }
        stack.Children.Add(Kv.Text(T($"Reference {CpuBenchmarkEngine.ReferenceName}: 1,000 multi-core, 100 single-core — the same scale as Kavosh for Mac. A CPU that finishes the same work in half the time scores double. Sustained = the multi-core rate at the end of the soak; it depends on the duration you chose.",
                                     $"مرجع {CpuBenchmarkEngine.ReferenceName}: ۱۰۰۰ چندهسته‌ای و ۱۰۰ تک‌هسته‌ای — همان مقیاس کاوش برای مک. پردازنده‌ای که همین کار را در نصف زمان انجام دهد دو برابر امتیاز می‌گیرد. امتیاز پایدار = سرعت چندهسته‌ای در پایان بار پایدار؛ به مدت انتخابی شما بستگی دارد."),
            11, FontWeight.Medium, Palette.Muted));
        return Kv.Card(stack);
    }
}
