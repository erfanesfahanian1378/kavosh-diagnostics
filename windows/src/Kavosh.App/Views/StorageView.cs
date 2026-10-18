using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kavosh.App.Ui;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Views;

/// <summary>SSD speed test, measured the way CrystalDiskMark does.</summary>
public sealed class StorageView : UserControl, IDisposable
{
    private readonly AppModel _model;
    private readonly MainWindow _window;
    private string _folder = Path.GetTempPath();
    private bool _running;
    private volatile bool _cancelled;
    private StorageBenchmarkProgress? _progress;
    private readonly List<double> _liveHistory = new();
    private StorageBenchmarkResult? _result;
    private string? _error;

    private readonly Slot _setup = new();
    private readonly Slot _errorSlot = new();
    private readonly Slot _live = new();
    private readonly Slot _results = new();
    private readonly HistoryChart _chart = new() { Height = 110, Capacity = 160, LineColor = Palette.AccentColor };
    private readonly RetroProgressBar _bar = new(14, Palette.AccentColor);
    private readonly TextBlock _liveValue = Kv.Hardware("—", 18, FontWeight.ExtraBold, Palette.Accent, wrap: false);
    private readonly TextBlock _stage = Kv.Text("", 14, FontWeight.Bold);
    private readonly RetroButton _back;

    public StorageView(AppModel model, MainWindow window)
    {
        _model = model;
        _window = window;
        _back = new RetroButton(T("Back to Dashboard", "بازگشت به داشبورد"), Loc.Language.IsRightToLeft() ? Icon.ArrowRight : Icon.ArrowLeft,
            ButtonKind.Secondary, ButtonSize.Small, () =>
            {
                if (!_running) _model.GoToDashboard();
            });
        var header = Kv.Row(14, _back, Kv.Column(2, Kv.SectionLabel(T("SSD speed test", "تست سرعت SSD"), Icon.Speedometer), Kv.Text(T("Drive speed", "سرعت درایو"), 26, FontWeight.ExtraBold)));
        _live.Content = null;
        var footer = Kv.Text(T("Measured the way CrystalDiskMark measures: a pre-filled test file, uncached direct I/O, incompressible random data and several timed runs per test — the best run is reported. 1 MB/s = 1,000,000 bytes per second (CrystalDiskMark's default unit); 1 MiB/s = 1,048,576. The time in ms is the average wait per request.",
                               "به روش CrystalDiskMark اندازه‌گیری می‌شود: یک فایل تست ازپیش‌پرشده، ورودی/خروجی مستقیم بدون کش، داده تصادفی غیرقابل فشرده‌سازی و چند اجرای زمان‌دار برای هر تست — بهترین اجرا گزارش می‌شود. ۱ MB/s = ۱٬۰۰۰٬۰۰۰ بایت در ثانیه؛ ۱ MiB/s = ۱٬۰۴۸٬۵۷۶. زمان بر حسب ms میانگین انتظار برای هر درخواست است."),
            11, FontWeight.Medium, Palette.Muted);
        Content = Kv.Scroll(Kv.Column(20, header, _setup, _errorSlot, _live, _results, footer), new Thickness(24));
        Refresh();
    }

    public void Dispose() => _cancelled = true;

    private string FolderDisplayName
    {
        get
        {
            string volume = StorageBenchmarkEngine.VolumeName(_folder) ?? "—";
            if (Path.GetFullPath(_folder).TrimEnd('\\', '/') == Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\', '/'))
                return T($"{volume} (Windows drive)", $"{volume} (درایو ویندوز)");
            return $"{volume} — {_folder}";
        }
    }

    private void Refresh()
    {
        RefreshSetup();
        _errorSlot.Content = _error is null ? null : Kv.Card(Kv.Column(8,
            Kv.SectionLabel(T("The test couldn't run", "تست اجرا نشد"), Icon.Warning, Palette.Red), Kv.Text(_error, 14)), Palette.WithOpacity(Palette.RedColor, 0.12));
        _live.Content = _running ? LiveCard() : null;
        var result = _result ?? (_running ? null : _model.LastStorage);
        _results.Content = result is not null || (_running && _progress is not null) ? ResultsCard(_running ? null : result, _running ? _progress : null) : null;
        _back.IsEnabled = !_running;
    }

    private void RefreshSetup()
    {
        var folderText = Kv.Hardware(FolderDisplayName, 14, FontWeight.Bold, wrap: false);
        var choose = new RetroButton(T("Choose…", "انتخاب…"), Icon.Folder, ButtonKind.Secondary, ButtonSize.Small, ChooseFolder) { IsEnabled = !_running };
        var drives = new List<Control>();
        foreach (var drive in SafeDrives())
        {
            string root = drive.RootDirectory.FullName;
            string target = string.Equals(Path.GetPathRoot(Path.GetTempPath()), root, StringComparison.OrdinalIgnoreCase) ? Path.GetTempPath() : root;
            string label = drive.Name.TrimEnd('\\');
            bool selected = string.Equals(Path.GetPathRoot(Path.GetFullPath(_folder)), root, StringComparison.OrdinalIgnoreCase);
            drives.Add(new RetroButton(label, Icon.Drive, selected ? ButtonKind.Primary : ButtonKind.Ghost, ButtonSize.Small, () =>
            {
                _folder = target;
                Refresh();
            }) { IsEnabled = !_running });
        }

        var profile = Kv.Segmented(new[]
        {
            ("standard", StorageTestProfile.Standard.Title + " · 1 GiB · 5×5 s"),
            ("quick", StorageTestProfile.Quick.Title + " · 512 MiB · 2×3 s"),
        }, _model.Settings.StorageProfile, value =>
        {
            if (_running) return;
            _model.UpdateSettings(s => s.StorageProfile = value);
            RefreshSetup();
        });

        Control action = _running
            ? new RetroButton(T("Stop", "توقف"), Icon.Stop, ButtonKind.Danger, ButtonSize.Regular, () => _cancelled = true)
            : new RetroButton(T("Start", "شروع"), Icon.Play, ButtonKind.Primary, ButtonSize.Large, Start);

        var stack = Kv.Column(14,
            Kv.Spread(Kv.Column(3, Kv.Text(T("Tested folder", "پوشه تست"), 11, FontWeight.Bold, Palette.Muted), folderText), choose));
        if (drives.Count > 0) stack.Children.Add(Kv.Row(10, Kv.Text(T("Drives", "درایوها"), 11, FontWeight.Bold, Palette.Muted), Kv.Flow(6, drives.ToArray())));
        stack.Children.Add(Kv.Spread(Kv.Row(12, Kv.Text(T("Profile", "حالت"), 11, FontWeight.Bold, Palette.Muted), profile), action));
        stack.Children.Add(Kv.Text(T("The test writes a temporary file (about 1 GiB) and deletes it afterwards. It needs about 1.3 GB of free space.",
                                     "تست یک فایل موقت (حدود ۱ گیگابایت) می‌نویسد و بعد پاکش می‌کند. حدود ۱٫۳ گیگابایت فضای خالی لازم دارد."), 11, FontWeight.Medium, Palette.Muted));
        _setup.Content = Kv.Card(stack);
    }

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<DriveInfo>();
        try
        {
            return DriveInfo.GetDrives().Where(d =>
            {
                try { return d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable; }
                catch { return false; }
            }).ToList();
        }
        catch
        {
            return Array.Empty<DriveInfo>();
        }
    }

    private async void ChooseFolder()
    {
        string? folder = await _window.PickFolderAsync(T("Select a drive or folder to test (SSD Speed Test)", "یک درایو یا پوشه برای تست انتخاب کنید (تست سرعت SSD)"));
        if (folder is null) return;
        _folder = folder;
        Refresh();
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        _cancelled = false;
        _error = null;
        _result = null;
        _progress = null;
        _liveHistory.Clear();
        var engine = new StorageBenchmarkEngine(_model.Settings.StorageTestProfile);
        string folder = _folder;
        Refresh();
        var thread = new Thread(() =>
        {
            try
            {
                var result = engine.Run(folder, () => _cancelled, p => Dispatcher.UIThread.Post(() => Apply(p)));
                Dispatcher.UIThread.Post(() =>
                {
                    _running = false;
                    _result = result;
                    _model.StorageFinished(result);
                    Refresh();
                });
            }
            catch (StorageBenchmarkException e)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _running = false;
                    _error = e.Message;
                    Refresh();
                });
            }
            catch (Exception e)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _running = false;
                    _error = e.Message;
                    Refresh();
                });
            }
        }) { IsBackground = true, Name = "Kavosh SSD test" };
        thread.Start();
    }

    private void Apply(StorageBenchmarkProgress p)
    {
        if (!_running) return;
        var previous = _progress;
        _progress = p;
        if (p.Stage == StorageStage.Running && p.LiveMBps is { } live)
        {
            _liveHistory.Add(live);
            if (_liveHistory.Count > 160) _liveHistory.RemoveRange(0, _liveHistory.Count - 160);
        }
        _chart.MaxValue = Math.Max(100, (_liveHistory.Count > 0 ? _liveHistory.Max() : 0) * 1.1);
        _chart.Values = _liveHistory.ToArray();
        _bar.Value = p.OverallFraction;
        _liveValue.Text = p.LiveMBps is { } mbps ? Fmt.MBps(mbps) : "—";
        _stage.Text = StageText(p);
        // Rebuild the results grid only when the highlighted cell or a finished value changes.
        if (previous is null || previous.Value.Completed.Count != p.Completed.Count || previous.Value.Kind != p.Kind ||
            previous.Value.Operation != p.Operation || previous.Value.Stage != p.Stage)
            _results.Content = ResultsCard(null, p);
    }

    private Control LiveCard()
    {
        return Kv.Card(Kv.Column(12,
            Kv.Spread(Kv.SectionLabel(T("Throughput — live", "سرعت — زنده"), Icon.Pulse), _liveValue),
            _stage, _chart, _bar));
    }

    private static string StageText(StorageBenchmarkProgress? p) => p?.Stage switch
    {
        StorageStage.Settling => T("Letting the drive settle before the next test", "استراحت درایو پیش از تست بعدی"),
        StorageStage.Running => $"{p.Value.Kind.Title()} {p.Value.Operation.Title()} — {p.Value.Kind.Label()} — " + T($"run {p.Value.Run} of {p.Value.Of}", $"اجرای {p.Value.Run} از {p.Value.Of}"),
        StorageStage.CleaningUp => T("Removing the test file", "حذف فایل تست"),
        StorageStage.Finished => T("Done", "تمام شد"),
        _ => T("Preparing a test file", "آماده‌سازی فایل تست"),
    };

    /// <summary>CrystalDiskMark-style grid: three tests × read/write.</summary>
    public static Control ResultsCard(StorageBenchmarkResult? result, StorageBenchmarkProgress? live)
    {
        StorageTestMeasurement? Measurement(StorageTestKind kind, StorageOperation op) =>
            live is { } l ? l.Completed?.FirstOrDefault(m => m.Kind == kind && m.Operation == op) : result?.Measurement(kind, op);
        bool Active(StorageTestKind kind, StorageOperation op) => live is { Stage: StorageStage.Running } l && l.Kind == kind && l.Operation == op;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        void Add(Control c, int row, int column)
        {
            c.Margin = new Thickness(column == 0 ? 0 : 6, row == 0 ? 0 : 6, column == 2 ? 0 : 6, 6);
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
            grid.Children.Add(c);
        }
        Add(new Panel(), 0, 0);
        Add(Kv.Text(T("Read", "خواندن"), 12, FontWeight.ExtraBold, Palette.Muted), 0, 1);
        Add(Kv.Text(T("Write", "نوشتن"), 12, FontWeight.ExtraBold, Palette.Muted), 0, 2);
        int r = 1;
        foreach (var kind in StorageBenchmarkEngine.Tests)
        {
            var title = Kv.Column(2, Kv.Hardware(kind.Label(), 14, FontWeight.ExtraBold), Kv.Text(kind.Title(), 11, FontWeight.Medium, Palette.Muted));
            title.VerticalAlignment = VerticalAlignment.Center;
            Add(title, r, 0);
            Add(SpeedCell(Measurement(kind, StorageOperation.Read), Active(kind, StorageOperation.Read)), r, 1);
            Add(SpeedCell(Measurement(kind, StorageOperation.Write), Active(kind, StorageOperation.Write)), r, 2);
            r++;
        }
        var stack = Kv.Column(14,
            Kv.Spread(Kv.SectionLabel(T("SSD speed test — results", "تست سرعت SSD — نتایج"), Icon.Grid),
                result is not null ? Kv.Text(Kv.DateText(result.Date) + (result.VolumeName is { } v ? " · " + v : ""), 11, FontWeight.Medium, Palette.Muted, wrap: false) : null),
            grid);
        if (result?.Measurement(StorageTestKind.Rnd4kQ1T1, StorageOperation.Read) is { } rnd)
        {
            stack.Children.Add(Kv.Text(T($"Random, one request at a time (RND4K Q1T1) is the closest of these numbers to how everyday use feels: opening files, launching apps. Read: {Fmt.MBps(rnd.MbPerSecond)}.",
                                         $"تصادفی، یک درخواست در هر زمان (RND4K Q1T1) نزدیک‌ترین عدد به حس استفاده روزمره است: باز کردن فایل‌ها و اجرای برنامه‌ها. خواندن: {Fmt.MBps(rnd.MbPerSecond)}."), 12));
        }
        return Kv.Card(stack);
    }

    private static Control SpeedCell(StorageTestMeasurement? m, bool active)
    {
        var value = Kv.Hardware(m is not null ? Fmt.Number(m.MbPerSecond, m.MbPerSecond < 100 ? 2 : 0) : active ? "…" : "—", 26, FontWeight.Black,
            m is null ? Palette.Muted : Palette.Ink, wrap: false);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        var detail = Kv.Hardware(m is not null ? $"{Fmt.Number(m.MibPerSecond, 0)} MiB/s · {Fmt.Number(m.Iops, 0)} IOPS · {Fmt.Fixed(m.AverageLatencyMs, 3)} ms" : "MB/s",
            10, FontWeight.Medium, Palette.Muted, wrap: false);
        detail.HorizontalAlignment = HorizontalAlignment.Right;
        var fill = active ? Palette.WithOpacity(Palette.YellowColor, 0.5) : m is null ? Palette.SurfaceAlt : Palette.WithOpacity(Palette.TealColor, 0.25);
        var cell = Kv.Surface(Kv.Column(3, value, detail), fill, 10, 3, new Thickness(12));
        cell.MinWidth = 220;
        cell.FlowDirection = FlowDirection.LeftToRight;
        return cell;
    }
}
