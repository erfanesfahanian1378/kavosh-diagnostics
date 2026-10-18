using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kavosh.App.Ui;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Views;

public sealed class ResultView : UserControl
{
    private readonly AppModel _model;
    private readonly FinalReport _report;

    public ResultView(AppModel model, FinalReport report)
    {
        _model = model;
        _report = report;
        var evaluation = BenchmarkScoring.Evaluate(report);

        var left = Kv.Column(20);
        if (evaluation.Components.Count > 0) left.Children.Add(BreakdownCard(evaluation));
        if (report.Failures.Count > 0 || report.Skipped.Count > 0) left.Children.Add(NotRunCard());
        left.Children.Add(DetailsCard());

        var right = Kv.Column(20, PerformanceCard());
        if (report.Drives is { Drives.Count: > 0 } drives)
        {
            var stack = Kv.Column(10, Kv.SectionLabel(T("Drives (SMART)", "درایوها (SMART)"), Icon.Drive));
            foreach (var drive in drives.Drives) stack.Children.Add(DashboardView.DriveRow(drive));
            right.Children.Add(Kv.Card(stack));
        }
        var columns = Kv.Columns(20, left, right);
        foreach (var child in columns.Children) child.VerticalAlignment = VerticalAlignment.Top;

        Content = Kv.Scroll(Kv.Column(20, VerdictCard(evaluation), columns), new Thickness(24));
    }

    private Control VerdictCard(HealthEvaluation evaluation)
    {
        var info = Kv.Column(8, Kv.SectionLabel(T("Device health", "سلامت دستگاه"), Icon.Heart));
        if (evaluation.Score is { } score && evaluation.Grade is { } grade)
        {
            info.Children.Add(Kv.Text(grade.Verdict(), 28, FontWeight.ExtraBold));
            info.Children.Add(Kv.Text(T($"Health score {Fmt.Fixed(score, 1)} of 100", $"نمره سلامت {Fmt.Fixed(score, 1)} از ۱۰۰"), 16, FontWeight.Bold, Palette.Muted));
            info.Children.Add(Kv.Text(T("Based only on the hardware checks — keyboard, display, speakers, mic & camera, battery, memory errors and drive (SMART) health. CPU and GPU speed don't affect it.",
                                        "فقط بر اساس بررسی‌های سخت‌افزاری — کیبورد، نمایشگر، بلندگو، میکروفون و دوربین، باتری، خطای حافظه و سلامت درایو (SMART). سرعت پردازنده و گرافیک در آن اثری ندارد."),
                12, FontWeight.Medium, Palette.Muted));
            if (evaluation.BatteryPenaltySteps > 0)
            {
                info.Children.Add(Kv.Label(evaluation.BatteryPenaltySteps == 1
                    ? T("The grade is one step below the health score because the battery has worn below 80% of its design capacity.",
                        "نمره یک پله پایین‌تر از نمره سلامت است چون باتری زیر ۸۰٪ ظرفیت طراحی فرسوده شده است.")
                    : T("The grade is two steps below the health score because the battery has worn below 70% of its design capacity.",
                        "نمره دو پله پایین‌تر از نمره سلامت است چون باتری زیر ۷۰٪ ظرفیت طراحی فرسوده شده است."), Icon.Battery, Palette.Orange, 12));
            }
            foreach (var kind in evaluation.CappedBy)
                info.Children.Add(Kv.Label(T($"{kind.Title()} limit the grade.", $"{kind.Title()} نمره را محدود می‌کند."), Icon.Cross, Palette.Red, 12));
        }
        else
        {
            info.Children.Add(Kv.Text(T("Performance test only", "فقط تست کارایی"), 28, FontWeight.ExtraBold));
            info.Children.Add(Kv.Text(T("No hardware checks were run, so there's no health verdict. The CPU and GPU scores are below.",
                                        "هیچ بررسی سخت‌افزاری اجرا نشد، پس نتیجه سلامتی وجود ندارد. امتیاز پردازنده و گرافیک در پایین آمده است."), 13, FontWeight.Medium, Palette.Muted));
        }
        var badges = new List<Control>();
        if (_report.Specs is { } specs)
        {
            badges.Add(Kv.Badge(specs.ModelName, icon: Icon.Laptop));
            badges.Add(Kv.Badge(specs.CpuName, icon: Icon.Cpu));
        }
        badges.Add(Kv.Badge(Kv.DateText(_report.FinishedAt ?? _report.StartedAt), icon: Icon.Timer));
        info.Children.Add(Kv.Flow(6, badges.ToArray()));

        var actions = Kv.Column(10,
            new RetroButton(T("Export CSV", "خروجی CSV"), Icon.Download, ButtonKind.Secondary, ButtonSize.Regular, () => _model.ExportCsv(_report)) { HorizontalAlignment = HorizontalAlignment.Stretch },
            new RetroButton(T("Run again", "اجرای دوباره"), Icon.Refresh, ButtonKind.Secondary, ButtonSize.Regular, () => _model.StartRun(_report.Plan)) { HorizontalAlignment = HorizontalAlignment.Stretch },
            new RetroButton(T("Back to Dashboard", "بازگشت به داشبورد"), Icon.Laptop, ButtonKind.Primary, ButtonSize.Regular, _model.GoToDashboard) { HorizontalAlignment = HorizontalAlignment.Stretch });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var badge = Kv.GradeBadge(evaluation.Grade, 120);
        badge.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(badge);
        info.Margin = new Thickness(24, 0);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        actions.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        return Kv.Card(grid, padding: 22);
    }

    private static Control BreakdownCard(HealthEvaluation evaluation)
    {
        var stack = Kv.Column(12, Kv.SectionLabel(T("See the full results behind this grade", "نتایج کامل پشت این نمره"), Icon.List));
        for (int i = 0; i < evaluation.Components.Count; i++)
        {
            var c = evaluation.Components[i];
            var state = c.Score >= 90 ? StatusState.Pass : c.Score >= 60 ? StatusState.Warn : StatusState.Fail;
            var title = Kv.Row(10, Kv.StatusIcon(state), Kv.Text(c.Kind.Title(), 14, FontWeight.Bold));
            var block = Kv.Column(4, Kv.Spread(title, Kv.ScoreBadge(c.Score)));
            if (c.Issues.Count == 0)
            {
                var none = Kv.Text(T("No issues found.", "مشکلی پیدا نشد."), 12, FontWeight.Medium, Palette.Muted);
                none.Margin = new Thickness(32, 0, 0, 0);
                block.Children.Add(none);
            }
            foreach (string issue in c.Issues)
            {
                var line = Kv.Text("• " + issue, 12);
                line.Margin = new Thickness(32, 0, 0, 0);
                block.Children.Add(line);
            }
            stack.Children.Add(block);
            if (i < evaluation.Components.Count - 1) stack.Children.Add(Kv.Divider());
        }
        return Kv.Card(stack);
    }

    private Control NotRunCard()
    {
        var stack = Kv.Column(10, Kv.SectionLabel(T("These tests couldn't run", "این تست‌ها اجرا نشدند"), Icon.Warning));
        foreach (var failure in _report.Failures)
            stack.Children.Add(Kv.Column(2, Kv.Text(failure.Module.Title(), 13, FontWeight.Bold), Kv.Text(failure.Message, 12, FontWeight.Medium, Palette.Muted)));
        foreach (var module in _report.Skipped)
            stack.Children.Add(Kv.Spread(Kv.Text(module.Title(), 13, FontWeight.Bold), Kv.Badge(T("Skipped", "رد شد"))));
        return Kv.Card(stack, Palette.WithOpacity(Palette.YellowColor, 0.2));
    }

    private Control PerformanceCard()
    {
        var stack = Kv.Column(14, Kv.SectionLabel(T("Performance", "کارایی"), Icon.Speedometer));
        if (_report.Cpu is { } cpu)
        {
            stack.Children.Add(Block(T("CPU", "پردازنده"), Icon.Cpu,
                (T("Multi-core score", "امتیاز چندهسته‌ای"), Fmt.Number(cpu.Score, 0)),
                (T("Single-core score", "امتیاز تک‌هسته‌ای"), Fmt.Number(cpu.SingleCoreScore, 0)),
                (T("Sustained", "پایدار"), cpu.SustainedScore is { } s ? Fmt.Number(s, 0) : "—"),
                (T("Peak temperature", "بیشترین دما"), Fmt.Celsius(cpu.PeakTemperatureCelsius)),
                (T("Calculation errors", "خطای محاسبه"), cpu.WrongResults == 0 ? T("None", "هیچ") : cpu.WrongResults.ToString()),
                (T("Thermal throttling", "کاهش سرعت حرارتی"), cpu.ThermalThrottleDetected ? T("Detected", "تشخیص داده شد") : T("Not detected", "تشخیص داده نشد"))));
        }
        if (_report.Gpu is { } gpu)
        {
            stack.Children.Add(Block(T("GPU", "گرافیک"), Icon.Gpu,
                (T("GPU score", "امتیاز گرافیک"), Fmt.Number(gpu.Score, 0)),
                (T("Average", "میانگین"), $"{Fmt.Fixed(gpu.AverageFps, 2)} fps · {Fmt.Fixed(gpu.AverageFrameTimeMs, 1)} ms"),
                (T("Sustained", "پایدار"), gpu.SustainedScore is { } s ? Fmt.Number(s, 0) : "—"),
                (T("Peak temperature", "بیشترین دما"), Fmt.Celsius(gpu.PeakTemperatureCelsius))));
        }
        if (_report.Memory is { } memory)
        {
            stack.Children.Add(Block(T("Memory", "حافظه"), Icon.Memory,
                (T("Write", "نوشتن"), Fmt.MBps(memory.AverageWriteThroughputMBps)),
                (T("Read", "خواندن"), Fmt.MBps(memory.AverageReadThroughputMBps)),
                (T("Errors detected", "خطاهای پیدا‌شده"), memory.ErrorsDetected.ToString())));
        }
        if (_report.Cpu is null && _report.Gpu is null && _report.Memory is null)
            stack.Children.Add(Kv.Text(T("No performance tests were run.", "هیچ تست کارایی اجرا نشد."), 12, FontWeight.Medium, Palette.Muted));
        return Kv.Card(stack);
    }

    private static Control Block(string title, Icon icon, params (string Key, string Value)[] rows)
    {
        var stack = Kv.Column(6, Kv.Row(8, Icons.Make(icon, 16), Kv.Text(title, 14, FontWeight.ExtraBold)));
        foreach (var (key, value) in rows) stack.Children.Add(Kv.KeyValue(key, value));
        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(9),
            Background = Palette.WithOpacity(Palette.SurfaceAltColor, 0.5),
            Child = stack,
        };
    }

    private Control DetailsCard()
    {
        var rows = new List<(string, string)>();
        if (_report.Battery is { } battery) rows.Add((T("Battery", "باتری"), BatteryText(battery)));
        if (_report.Display is { } display) rows.Add((T("Display", "نمایشگر"), DisplayText(display)));
        if (_report.Audio is { } audio) rows.Add((T("Speakers", "بلندگوها"), AudioText(audio)));
        if (_report.MicCamera is { } mc) rows.Add((T("Mic & Camera", "میکروفون و دوربین"), MicCameraText(mc)));
        if (_report.Keyboard is { } keyboard) rows.Add((T("Keyboard & Touchpad", "کیبورد و تاچ‌پد"), KeyboardText(keyboard)));

        var stack = Kv.Column(12, Kv.SectionLabel(T("Hardware checks", "بررسی‌های سخت‌افزاری"), Icon.List));
        foreach (var (title, value) in rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
            var key = Kv.Text(title, 13, FontWeight.Bold);
            key.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(key);
            var text = Kv.Text(value, 13, FontWeight.Medium, Palette.Muted);
            text.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            stack.Children.Add(grid);
        }
        if (rows.Count == 0) stack.Children.Add(Kv.Text(T("No hardware checks in this run.", "در این اجرا بررسی سخت‌افزاری نبود."), 12, FontWeight.Medium, Palette.Muted));
        return Kv.Card(stack);
    }

    private static string BatteryText(BatteryHealthResult b)
    {
        if (!b.BatteryPresent) return T("No battery", "بدون باتری");
        var parts = new List<string>
        {
            T($"{Fmt.Fixed(b.HealthPercent, 1)}% health", $"سلامت {Fmt.Fixed(b.HealthPercent, 1)}٪"),
            T($"{b.CycleCount} cycles", $"{b.CycleCount} سیکل"),
        };
        if (b.DischargeRateWatts is { } w) parts.Add(T($"{Fmt.Fixed(w, 1)} W discharge", $"تخلیه {Fmt.Fixed(w, 1)} وات"));
        return string.Join(" · ", parts);
    }

    private static string DisplayText(DisplayTestResult d)
    {
        if (d.Issues.Count == 0)
        {
            string text = T("No issues reported", "ایرادی گزارش نشد");
            if (d.MeasuredRefreshRateHz is { } hz) text += $" · {Fmt.Fixed(hz, 0)} Hz";
            return text;
        }
        return string.Join(", ", d.Issues.Select(i => i.ShortText()));
    }

    private static string Mark(bool ok) => ok ? "✓" : "✗";

    private static string AudioText(AudioTestResult a)
    {
        string text = $"{T("Left", "چپ")} {Mark(a.LeftHeard)} · {T("Right", "راست")} {Mark(a.RightHeard)}";
        if (a.ReportedIssue is { } issue) text += $" · {issue}";
        return text;
    }

    private static string MicCameraText(MicCameraTestResult mc)
    {
        string na = T("N/A", "ندارد");
        string text = $"{T("Camera", "دوربین")} {(mc.CameraPresent ? Mark(mc.CameraWorks) : na)} · {T("Microphone", "میکروفون")} {(mc.MicrophonePresent ? Mark(mc.MicrophoneWorks) : na)}";
        if (mc.ReportedIssue is { } issue) text += $" · {issue}";
        return text;
    }

    private static string KeyboardText(KeyboardTestResult k) => k.Bypassed
        ? T("N/A (bypassed by user)", "ندارد (رد شده توسط کاربر)")
        : T($"{k.PressedKeys} of {k.TotalKeys} keys · touchpad {k.TrackpadChecksPassed}/3", $"{k.PressedKeys} از {k.TotalKeys} کلید · تاچ‌پد {k.TrackpadChecksPassed}/۳");
}
