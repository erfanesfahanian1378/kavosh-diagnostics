using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kavosh.App.Ui;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Views;

public sealed class DashboardView : UserControl, IDisposable
{
    private readonly AppModel _model;
    private readonly MainWindow _window;
    private readonly Slot _device = new();
    private readonly Slot _specs = new();
    private readonly Slot _drives = new();
    private readonly Slot _resume = new();
    private readonly Slot _results = new();
    private readonly TelemetryPanel _telemetry;

    public DashboardView(AppModel model, MainWindow window)
    {
        _model = model;
        _window = window;
        _telemetry = new TelemetryPanel(model.Telemetry, compact: false);

        var left = Kv.Column(20, _device, _specs, _drives);
        var middle = Kv.Column(20, _resume, LauncherCard(), _results, MaintenanceCard());
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("340,*,320"), Margin = new Thickness(24) };
        grid.Children.Add(left);
        middle.Margin = new Thickness(22, 0);
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);
        Grid.SetColumn(_telemetry, 2);
        grid.Children.Add(_telemetry);
        Content = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        _model.SpecsChanged += RefreshSpecs;
        _model.DriveHealthChanged += RefreshDrives;
        _model.ResultsChanged += RefreshResults;
        _model.SettingsChanged += RefreshLauncher;
        RefreshSpecs();
        RefreshDrives();
        RefreshResults();
    }

    public void Dispose()
    {
        _model.SpecsChanged -= RefreshSpecs;
        _model.DriveHealthChanged -= RefreshDrives;
        _model.ResultsChanged -= RefreshResults;
        _model.SettingsChanged -= RefreshLauncher;
        _telemetry.Dispose();
    }

    // ── Device and specs ──

    private void RefreshSpecs()
    {
        var specs = _model.Specs;
        Control heading;
        if (specs is not null)
        {
            heading = Kv.Column(8,
                Kv.SectionLabel(T("This PC", "این رایانه"), Icon.Laptop, Palette.WithOpacity(Colors.White, 0.85)),
                Kv.Hardware(specs.ModelName, 22, FontWeight.ExtraBold, Palette.OnAccent),
                Kv.Hardware($"{specs.CpuName} · {Fmt.MemoryGB(specs.RamCapacityGB)}", 14, FontWeight.SemiBold, Palette.WithOpacity(Colors.White, 0.92)),
                Kv.Hardware(specs.OperatingSystem, 12, FontWeight.Medium, Palette.WithOpacity(Colors.White, 0.8)));
        }
        else
        {
            heading = Kv.Column(8,
                Kv.Text(T("Loading system specs…", "در حال خواندن مشخصات سیستم…"), 15, FontWeight.Bold, Palette.OnAccent),
                new BusySpinner(16, Palette.OnAccent));
        }
        _device.Content = Kv.Card(heading, Palette.Accent);
        _specs.Content = SpecsCard(specs);
    }

    private Control SpecsCard(SystemSpecs? s)
    {
        var stack = Kv.Column(14, Kv.SectionLabel(T("System specs", "مشخصات سیستم"), Icon.List));
        if (s is null)
        {
            stack.Children.Add(new BusySpinner(16));
            return Kv.Card(stack);
        }
        stack.Children.Add(SpecGroup(T("Processor", "پردازنده"), Icon.Cpu,
            Kv.Hardware(s.CpuName, 14, FontWeight.Bold), Kv.Hardware(CpuLine(s), 12, FontWeight.Medium, Palette.Muted)));
        var gpu = new List<Control> { Kv.Hardware(string.IsNullOrEmpty(s.GpuName) ? "—" : s.GpuName, 14, FontWeight.Bold), Kv.Hardware(GpuLine(s), 12, FontWeight.Medium, Palette.Muted) };
        foreach (var other in s.OtherGpus) gpu.Add(Kv.Hardware("+ " + other, 12, FontWeight.Medium, Palette.Muted));
        stack.Children.Add(SpecGroup(T("Graphics", "گرافیک"), Icon.Gpu, gpu.ToArray()));
        stack.Children.Add(SpecGroup(T("Memory", "حافظه"), Icon.Memory,
            Kv.Hardware(Fmt.MemoryGB(s.RamCapacityGB) + (s.RamType is { } type ? " " + type : ""), 14, FontWeight.Bold),
            Kv.Hardware(MemoryLine(s), 12, FontWeight.Medium, Palette.Muted)));
        if (s.DisplayResolution is { } resolution)
        {
            stack.Children.Add(SpecGroup(T("Display", "نمایشگر"), Icon.Display,
                Kv.Hardware(s.DisplayName ?? T("Display", "نمایشگر"), 14, FontWeight.Bold),
                Kv.Hardware(resolution + (s.DisplayRefreshRateHz is { } hz ? $" @ {hz} Hz" : ""), 12, FontWeight.Medium, Palette.Muted)));
        }
        if (s.StorageDevices.Count > 0)
        {
            var drives = new List<Control>();
            foreach (var drive in s.StorageDevices)
            {
                string line = string.Join(" · ", new[] { Fmt.StorageSize(drive.SizeBytes), drive.Interconnect, drive.Location }.Where(p => !string.IsNullOrEmpty(p)));
                drives.Add(Kv.Column(1, Kv.Hardware(drive.DisplayName, 13, FontWeight.Bold), Kv.Hardware(line, 12, FontWeight.Medium, Palette.Muted)));
            }
            stack.Children.Add(SpecGroup(T("Storage", "ذخیره‌سازی"), Icon.Drive, drives.ToArray()));
        }
        var model = new List<Control> { Kv.Hardware(string.IsNullOrEmpty(s.ModelIdentifier) ? "—" : s.ModelIdentifier, 13, FontWeight.Bold) };
        if (s.FirmwareVersion is { } firmware) model.Add(Kv.Hardware($"BIOS {firmware}", 12, FontWeight.Medium, Palette.Muted));
        stack.Children.Add(SpecGroup(T("Model", "مدل"), Icon.Tag, model.ToArray()));
        return Kv.Card(stack);
    }

    private static Control SpecGroup(string title, Icon icon, params Control[] content)
    {
        var box = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(7),
            Background = Palette.SurfaceAlt,
            BorderBrush = Palette.Ink,
            BorderThickness = new Thickness(1.5),
            Child = Icons.Make(icon, 15),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var column = Kv.Column(2, Kv.Text(title, 11, FontWeight.Bold, Palette.Muted));
        foreach (var c in content) column.Children.Add(c);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(box);
        column.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(column, 1);
        grid.Children.Add(column);
        return grid;
    }

    internal static string CpuLine(SystemSpecs s)
    {
        var parts = new List<string> { s.ClusterShorthand };
        if (s.CpuMaxClockGHz is { } ghz) parts.Add($"{Fmt.Fixed(ghz, 2)} GHz");
        parts.Add(s.IsArm ? "ARM64" : "x64");
        return string.Join(" · ", parts);
    }

    internal static string GpuLine(SystemSpecs s)
    {
        var parts = new List<string>();
        if (s.GpuCoreCount is { } cores) parts.Add($"{cores}-core GPU");
        if (s.HasUnifiedMemory) parts.Add("Integrated · shared memory");
        else if (s.GpuMemoryGB is { } vram) parts.Add($"{Fmt.Fixed(vram, 1)} GB VRAM");
        if (s.GraphicsApi is { } api) parts.Add(api);
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    internal static string MemoryLine(SystemSpecs s)
    {
        var parts = new List<string>();
        if (s.RamManufacturer is { } maker) parts.Add(maker);
        if (s.RamSpeedMHz is { } speed) parts.Add($"{speed} MT/s");
        if (s.RamModuleCount is { } modules) parts.Add(modules == 1 ? "1 module" : $"{modules} modules");
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    // ── Drive health ──

    private void RefreshDrives()
    {
        var refresh = new RetroButton(null, Icon.Refresh, ButtonKind.Ghost, ButtonSize.Small, _model.RefreshDriveHealth)
        {
            IsEnabled = !_model.DriveHealthLoading,
        };
        ToolTip.SetTip(refresh, T("Read again", "خواندن دوباره"));
        var stack = Kv.Column(12,
            Kv.Spread(Kv.SectionLabel(T("SSD / Drive health (SMART)", "سلامت SSD / درایو (SMART)"), Icon.Drive), refresh),
            Kv.Text(T("Read from each drive's own SMART / NVMe health log. It counts toward the health verdict — the worst drive decides. Drive speed doesn't.",
                      "از گزارش سلامت SMART / NVMe خود هر درایو خوانده می‌شود و در نتیجه سلامت حساب می‌شود — بدترین درایو تعیین‌کننده است. سرعت درایو حساب نمی‌شود."),
                11, FontWeight.Medium, Palette.Muted));
        if (_model.DriveHealth is { } report && !_model.DriveHealthLoading)
        {
            if (report.Drives.Count == 0) stack.Children.Add(Kv.Text(T("No drives found.", "درایوی پیدا نشد."), 13));
            foreach (var drive in report.Drives) stack.Children.Add(DriveRow(drive));
        }
        else
        {
            stack.Children.Add(Kv.Busy(T("Reading drive health (SMART)…", "در حال خواندن سلامت درایو (SMART)…")));
        }
        _drives.Content = Kv.Card(stack);
    }

    internal static Control DriveRow(DriveHealthInfo drive)
    {
        var status = drive.Status switch
        {
            DriveHealthStatus.Good => Palette.GreenColor,
            DriveHealthStatus.Warning => Palette.YellowColor,
            DriveHealthStatus.Failing => Palette.RedColor,
            _ => Palette.SurfaceAltColor,
        };
        var title = Kv.Column(2, Kv.Hardware(drive.Model, 13, FontWeight.Bold),
            Kv.Hardware(string.Join(" · ", new[] { Fmt.StorageSize(drive.SizeBytes), drive.BusType }.Where(p => !string.IsNullOrEmpty(p))), 11, FontWeight.Medium, Palette.Muted));
        var stack = Kv.Column(8, Kv.Spread(title, Kv.Badge(drive.Status.Title(), status, Palette.OnYellow), VerticalAlignment.Top));
        if (drive.HasHealthData)
        {
            var chips = new List<Control>();
            if (drive.LifeLeftPercent is { } life) chips.Add(Kv.Badge(T($"Life left {life}%", $"عمر باقی‌مانده {life}٪")));
            if (drive.AvailableSparePercent is { } spare) chips.Add(Kv.Badge(T($"Spare {spare}%", $"یدک {spare}٪")));
            if (drive.DataWrittenTB is { } tb) chips.Add(Kv.Badge(T($"{Fmt.Fixed(tb, 1)} TB written", $"{Fmt.Fixed(tb, 1)} ترابایت نوشته‌شده")));
            if (drive.PowerOnHours is { } hours) chips.Add(Kv.Badge(T($"{Fmt.Number(hours)} h powered on", $"{Fmt.Number(hours)} ساعت روشن")));
            if (drive.TemperatureCelsius is { } temp) chips.Add(Kv.Badge(Fmt.Celsius(temp)));
            if (drive.MediaErrors is > 0) chips.Add(Kv.Badge(T($"{drive.MediaErrors} media errors", $"{drive.MediaErrors} خطای رسانه"), Palette.RedColor, Brushes.White));
            if (drive.ReallocatedSectors is > 0) chips.Add(Kv.Badge(T($"{drive.ReallocatedSectors} reallocated sectors", $"{drive.ReallocatedSectors} سکتور جایگزین‌شده"), Palette.OrangeColor, Palette.OnYellow));
            if (chips.Count > 0) stack.Children.Add(Kv.Flow(6, chips.ToArray()));
            foreach (var issue in drive.Issues) stack.Children.Add(Kv.Label(issue.Text, Icon.Warning, Palette.Red, 11));
        }
        else
        {
            stack.Children.Add(Kv.Text(T("This drive didn't report any health data (common behind USB adapters, RAID/RST controllers, or without administrator rights), so it isn't counted.",
                                         "این درایو داده سلامتی گزارش نکرد (پشت مبدل‌های USB، کنترلرهای RAID/RST یا بدون دسترسی مدیر رایج است)، پس حساب نمی‌شود."),
                11, FontWeight.Medium, Palette.Muted));
        }
        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(9),
            Background = Palette.WithOpacity(Palette.SurfaceAltColor, 0.5),
            BorderBrush = Palette.WithOpacity(Palette.InkColor, 0.25),
            BorderThickness = new Thickness(1),
            Child = stack,
        };
    }

    // ── Launcher ──

    private readonly Slot _launcher = new();

    private Control LauncherCard()
    {
        RefreshLauncher();
        return _launcher;
    }

    private void RefreshLauncher()
    {
        var settings = _model.Settings;
        var stack = Kv.Column(16, Kv.SectionLabel(T("Diagnostic & benchmark", "عیب‌یابی و بنچمارک"), Icon.Pulse));
        stack.Children.Add(Kv.Flow(14,
            new RetroButton(T("Run All Tests", "اجرای همه تست‌ها"), Icon.Play, ButtonKind.Primary, ButtonSize.Large, () => _model.StartRun(TestModules.FullRun)),
            new RetroButton(T("Performance test only", "فقط تست کارایی"), Icon.Speedometer, ButtonKind.Secondary, ButtonSize.Regular,
                () => _model.StartRun(TestModules.PerformanceRun))));
        stack.Children.Add(Kv.Text(T("Runs every check one after another: CPU, GPU and memory first, then battery, display, speakers, mic & camera, keyboard & touchpad and drive health. The interactive steps tell you exactly what to do.",
                                     "همه بررسی‌ها پشت سر هم اجرا می‌شوند: اول پردازنده، گرافیک و حافظه، سپس باتری، نمایشگر، بلندگو، میکروفون و دوربین، کیبورد و تاچ‌پد و سلامت درایو. مراحل تعاملی دقیقاً می‌گویند چه کار کنید."),
            12, FontWeight.Medium, Palette.Muted));

        var durations = Kv.Segmented(AppSettings.DurationChoices.Select(s => (s, Kv.DurationLabel(s))), settings.PerformanceTestSeconds,
            seconds => _model.UpdateSettings(s => s.PerformanceTestSeconds = seconds));
        durations.FlowDirection = FlowDirection.LeftToRight;
        durations.HorizontalAlignment = Language.IsRightToLeft() ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        stack.Children.Add(Kv.Column(8,
            Kv.SectionLabel(T("CPU/GPU test duration", "مدت تست پردازنده/گرافیک"), Icon.Timer),
            durations,
            Kv.Text(T("Longer runs add a sustained phase that shows whether this PC holds its speed as it heats up.",
                      "اجرای طولانی‌تر یک مرحله بار پایدار اضافه می‌کند که نشان می‌دهد آیا این رایانه با گرم شدن سرعتش را حفظ می‌کند."),
                11, FontWeight.Medium, Palette.Muted)));

        stack.Children.Add(new RetroCheckBox(T("This PC has no built-in keyboard, touchpad or camera to test (a desktop PC).",
                                               "این رایانه کیبورد، تاچ‌پد یا دوربین داخلی برای تست ندارد (رایانه رومیزی)."),
            settings.NoBuiltInInputs, on => _model.UpdateSettings(s => s.NoBuiltInInputs = on)));
        stack.Children.Add(Kv.Divider());
        stack.Children.Add(Kv.SectionLabel(T("Or run an individual test", "یا یک تست جداگانه اجرا کنید"), Icon.Grid));

        var tiles = new AdaptiveGrid { MinItemWidth = 175, Spacing = 12 };
        foreach (var module in TestModules.FullRun)
        {
            var m = module;
            tiles.Children.Add(new TileButton(Icons.For(m), m.Title(), m.Subtitle(), () => _model.StartRun(new[] { m })));
        }
        tiles.Children.Add(new TileButton(Icon.Speedometer, T("SSD Speed Test", "تست سرعت SSD"),
            T("Sequential and random read/write, measured like CrystalDiskMark", "خواندن/نوشتن ترتیبی و تصادفی، مثل CrystalDiskMark"),
            () => _model.Show(Screen.Storage)));
        stack.Children.Add(tiles);
        _launcher.Content = Kv.Card(stack);
    }

    // ── Results ──

    private void RefreshResults()
    {
        _resume.Content = _model.UnfinishedRun is { } run ? ResumeCard(run) : null;
        var grid = new AdaptiveGrid { MinItemWidth = 270, Spacing = 20 };
        grid.Children.Add(LastRunCard());
        grid.Children.Add(SsdCard());
        _results.Content = grid;
    }

    private Control ResumeCard(FinalReport run)
    {
        int finished = run.Completed.Count + run.Skipped.Count + run.Failures.Count;
        var discard = new RetroButton(T("Discard", "کنار گذاشتن"), null, ButtonKind.Secondary, ButtonSize.Regular, _model.DiscardUnfinished);
        ToolTip.SetTip(discard, T("Stop offering to resume.", "دیگر پیشنهاد ادامه نده."));
        return Kv.Card(Kv.Column(10,
            Kv.SectionLabel(T("Unfinished test run", "اجرای ناتمام تست"), Icon.Pause, Palette.OnYellow),
            Kv.Text(T($"{finished} of {run.Plan.Count} stations finished before the run stopped. Resume to run only the rest — finished results are kept exactly as measured.",
                      $"{finished} از {run.Plan.Count} ایستگاه پیش از توقف اجرا تمام شد. با ادامه، فقط بقیه اجرا می‌شوند — نتایج تمام‌شده دقیقاً همان‌طور که اندازه‌گیری شده حفظ می‌شوند."),
                13, FontWeight.Medium, Palette.OnYellow),
            Kv.Row(10, new RetroButton(T("Resume", "ادامه"), Icon.Play, ButtonKind.Primary, ButtonSize.Regular, _model.ResumeUnfinished), discard)),
            Palette.Yellow);
    }

    private Control LastRunCard()
    {
        var stack = Kv.Column(12, Kv.SectionLabel(T("Device health (last run)", "سلامت دستگاه (آخرین اجرا)"), Icon.Heart));
        if (_model.LastReport is { } report)
        {
            var evaluation = BenchmarkScoring.Evaluate(report);
            var details = Kv.Column(4);
            if (evaluation.Score is { } score)
            {
                details.Children.Add(Kv.Hardware($"{Fmt.Fixed(score, 1)} / 100", 18, FontWeight.ExtraBold));
                details.Children.Add(Kv.Text(evaluation.Grade?.Verdict() ?? "", 13, FontWeight.Bold));
            }
            else
            {
                details.Children.Add(Kv.Text(T("Performance test only", "فقط تست کارایی"), 13, FontWeight.Bold));
            }
            details.Children.Add(Kv.Text(Kv.DateText(report.FinishedAt ?? report.StartedAt), 11, FontWeight.Medium, Palette.Muted));
            stack.Children.Add(Kv.Row(14, Kv.GradeBadge(evaluation.Grade, 64), details));
            stack.Children.Add(Kv.Row(8,
                new RetroButton(T("View results", "مشاهده نتایج"), null, ButtonKind.Secondary, ButtonSize.Small, () => _model.ShowReport(report)),
                new RetroButton(T("Open CSV", "باز کردن CSV"), null, ButtonKind.Ghost, ButtonSize.Small, _model.OpenHistoryCsv)));
        }
        else
        {
            stack.Children.Add(Kv.Text(T("Not run yet. Run all tests to get a health grade for this PC.",
                                         "هنوز اجرا نشده. همه تست‌ها را اجرا کنید تا نمره سلامت این رایانه را بگیرید."), 12, FontWeight.Medium, Palette.Muted));
        }
        return Kv.Card(stack);
    }

    private Control SsdCard()
    {
        var stack = Kv.Column(10, Kv.SectionLabel(T("SSD speed (last run)", "سرعت SSD (آخرین اجرا)"), Icon.Speedometer));
        if (_model.LastStorage is { } result)
        {
            string Speed(StorageTestKind k, StorageOperation o) => result.Measurement(k, o) is { } m ? Fmt.MBps(m.MbPerSecond) : "—";
            stack.Children.Add(Kv.Column(6,
                Kv.KeyValue(T("Sequential read", "خواندن ترتیبی"), Speed(StorageTestKind.Seq1mQ8T1, StorageOperation.Read)),
                Kv.KeyValue(T("Sequential write", "نوشتن ترتیبی"), Speed(StorageTestKind.Seq1mQ8T1, StorageOperation.Write)),
                Kv.KeyValue(T("Random read", "خواندن تصادفی"), Speed(StorageTestKind.Rnd4kQ1T1, StorageOperation.Read)),
                Kv.KeyValue(T("Random write", "نوشتن تصادفی"), Speed(StorageTestKind.Rnd4kQ1T1, StorageOperation.Write))));
            stack.Children.Add(new RetroButton(T("Click for full results", "نتایج کامل"), null, ButtonKind.Secondary, ButtonSize.Small, () => _model.Show(Screen.Storage)));
        }
        else
        {
            stack.Children.Add(Kv.Text(T("Not run yet.", "هنوز اجرا نشده."), 12, FontWeight.Medium, Palette.Muted));
            stack.Children.Add(new RetroButton(T("Run SSD speed test", "اجرای تست سرعت SSD"), null, ButtonKind.Secondary, ButtonSize.Small, () => _model.Show(Screen.Storage)));
        }
        return Kv.Card(stack);
    }

    private Control MaintenanceCard()
    {
        var icon = Kv.Surface(Icons.Make(Icon.Sparkles, 24, Brushes.White), Palette.Purple, 10, 3);
        icon.Width = 49;
        icon.Height = 49;
        var text = Kv.Column(3,
            Kv.Text(T("Screen Washer & Burn-in Fixer", "صفحه‌شوی و رفع سوختگی"), 14, FontWeight.Bold),
            Kv.Text(T("Clears temporary image retention on OLED/LCD panels. Warning: flashes the screen.",
                      "اثر تصویر موقت روی پنل‌های OLED/LCD را پاک می‌کند. هشدار: صفحه چشمک می‌زند."), 12, FontWeight.Medium, Palette.Muted));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(icon);
        text.Margin = new Thickness(14, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var open = new RetroButton(T("Open", "باز کردن"), null, ButtonKind.Secondary, ButtonSize.Regular, () => _model.Show(Screen.WasherSetup));
        Grid.SetColumn(open, 2);
        grid.Children.Add(open);
        return Kv.Card(Kv.Column(12, Kv.SectionLabel(T("Maintenance tools", "ابزارهای نگهداری"), Icon.Wrench), grid));
    }
}
