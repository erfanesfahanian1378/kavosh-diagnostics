using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Kavosh.App.Audio;
using Kavosh.App.Ui;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Stations;

public sealed class MicCameraStation : Station
{
    private enum DeviceState { Checking, Missing, Running, Failed }

    private DeviceState _camera = DeviceState.Checking, _mic = DeviceState.Checking;
    private string? _cameraName, _micName, _cameraProblem;
    private double _levelDb = -160, _peakDb = -160;
    private double? _pitch;
    private readonly List<double> _levelHistory = new();
    private bool _cameraWorks, _micWorks;
    private DateTime _lastHistory = DateTime.MinValue;

    private CameraFeed? _feed;
    private MicMonitor? _monitor;

    // Live controls, updated in place.
    private readonly Image _preview = new() { Stretch = Stretch.UniformToFill };
    private readonly Slot _cameraOverlay = new();
    private readonly Slot _cameraBadge = new();
    private readonly Slot _micBadge = new();
    private readonly Slot _cameraCheck = new();
    private readonly Slot _micBody = new();
    private readonly Slot _micCheck = new();
    private readonly RetroProgressBar _meter = new(22, Palette.GreenColor);
    private readonly TextBlock _db = Kv.Hardware("— dB", 20, FontWeight.ExtraBold, wrap: false);
    private readonly TextBlock _pitchText = Kv.Hardware("—", 16, FontWeight.Bold, Palette.Purple, wrap: false);
    private readonly HistoryChart _history = new() { Height = 70, Capacity = 120, LineColor = Palette.GreenColor };
    private RetroCheckBox? _micCheckBox;

    public MicCameraStation(TestSuiteRunner runner) : base(runner)
    {
        Build();
        RefreshCamera();
        RefreshMic();
    }

    protected override void Start()
    {
        StartCamera();
        StartMicrophone();
    }

    protected override void Stop()
    {
        _monitor?.Dispose();
        _monitor = null;
        var feed = _feed;
        _feed = null;
        if (feed is not null) _ = Task.Run(async () => await feed.DisposeAsync());
    }

    // ── Devices ──

    private void StartCamera()
    {
        Task.Run(async () =>
        {
            var descriptor = CameraFeed.FindCamera();
            if (descriptor is null)
            {
                OnUi(() =>
                {
                    _camera = DeviceState.Missing;
                    RefreshCamera();
                });
                return;
            }
            var feed = new CameraFeed();
            feed.Frame += bytes => Dispatcher.UIThread.Post(() => ShowFrame(feed, bytes));
            try
            {
                await feed.StartAsync(descriptor);
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsDisposed)
                    {
                        // The station closed while the camera was starting: turn it off again.
                        _ = Task.Run(async () => await feed.DisposeAsync());
                        return;
                    }
                    _feed = feed;
                    _cameraName = feed.DeviceName;
                    _camera = DeviceState.Running;
                    RefreshCamera();
                });
            }
            catch (Exception e)
            {
                await feed.DisposeAsync();
                OnUi(() =>
                {
                    _cameraName = descriptor.Name;
                    _cameraProblem = e.Message;
                    _camera = DeviceState.Failed;
                    RefreshCamera();
                });
            }
        });
    }

    private void ShowFrame(CameraFeed feed, byte[] bytes)
    {
        try
        {
            if (IsDisposed) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            var old = _preview.Source as Bitmap;
            _preview.Source = bitmap;
            old?.Dispose();
            _cameraOverlay.Content = null;
        }
        catch
        {
            // A damaged frame; the next one replaces it.
        }
        finally
        {
            feed.FrameDone();
        }
    }

    private void StartMicrophone()
    {
        var monitor = new MicMonitor();
        monitor.Level += (db, pitch) => OnUi(() => UpdateLevel(db, pitch));
        monitor.Failed += message => OnUi(() =>
        {
            _mic = DeviceState.Failed;
            RefreshMic();
        });
        try
        {
            if (monitor.Start())
            {
                _monitor = monitor;
                _micName = monitor.DeviceName;
                _mic = DeviceState.Running;
            }
            else
            {
                _mic = DeviceState.Missing;
            }
        }
        catch
        {
            monitor.Dispose();
            _mic = DeviceState.Failed;
        }
        RefreshMic();
    }

    private void UpdateLevel(double db, double? pitch)
    {
        _levelDb = db;
        _peakDb = Math.Max(_peakDb, db);
        if (pitch is not null) _pitch = pitch;
        // Loud enough sound means the microphone clearly works.
        if (db > -38 && !_micWorks)
        {
            _micWorks = true;
            if (_micCheckBox is not null) _micCheckBox.IsChecked = true;
        }
        if ((DateTime.UtcNow - _lastHistory).TotalSeconds > 0.08)
        {
            _lastHistory = DateTime.UtcNow;
            _levelHistory.Add(AudioAnalysis.MeterLevel(db) * 100);
            if (_levelHistory.Count > 120) _levelHistory.RemoveRange(0, _levelHistory.Count - 120);
            _history.Values = _levelHistory.ToArray();
        }
        _meter.BarColor = db > -12 ? Palette.RedColor : Palette.GreenColor;
        _meter.Value = AudioAnalysis.MeterLevel(db);
        _db.Text = db > -159 ? $"{Fmt.Fixed(db, 0)} dB" : "— dB";
        _pitchText.Text = _pitch is { } p ? $"{Fmt.Fixed(p, 0)} Hz · {AudioAnalysis.NoteName(p)}" : "—";
    }

    // ── View ──

    private void Build()
    {
        var how = Kv.Card(Kv.Column(8, Kv.SectionLabel(T("How it works", "روش کار"), Icon.Camera),
            Kv.Instruction(T("Check that you can see yourself, then speak: the level should move with your voice.",
                             "بررسی کنید که خودتان را می‌بینید، سپس حرف بزنید: سطح صدا باید با صدای شما حرکت کند."))));

        var screen = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(8),
            BorderBrush = Palette.Ink,
            BorderThickness = new Thickness(Palette.Border),
            ClipToBounds = true,
            Child = new Panel { Children = { _preview, _cameraOverlay } },
        };
        screen.SizeChanged += (_, e) => screen.Height = e.NewSize.Width * 9 / 16;
        var camera = Kv.Card(Kv.Column(10, Kv.Spread(Kv.SectionLabel(T("Camera", "دوربین"), Icon.Camera), _cameraBadge), screen, _cameraCheck), padding: 12);

        var micCard = Kv.Card(Kv.Column(12, Kv.Spread(Kv.SectionLabel(T("Microphone", "میکروفون"), Icon.Mic), _micBadge), _micBody, _micCheck), padding: 12);

        var row = Kv.Columns(18, camera, micCard);
        row.VerticalAlignment = VerticalAlignment.Top;
        foreach (var child in row.Children) child.VerticalAlignment = VerticalAlignment.Top;

        var bottom = Kv.Spread(
            ReportIssue.Button(new[]
            {
                T("The camera image is black or frozen", "تصویر دوربین سیاه یا ثابت است"),
                T("The camera image is blurry or discolored", "تصویر دوربین تار یا بی‌رنگ است"),
                T("The microphone doesn't react", "میکروفون واکنش نشان نمی‌دهد"),
                T("The microphone sounds noisy", "میکروفون نویز دارد"),
            }, Finish),
            new RetroButton(Runner.IsLastStation ? T("Finish", "پایان") : T("Next test", "تست بعدی"),
                Loc.Language.IsRightToLeft() ? Icon.ArrowLeft : Icon.ArrowRight, ButtonKind.Primary, ButtonSize.Large, () => Finish(null)));

        Content = Kv.Column(18, how, row,
            Kv.Text(T("If the camera or microphone stays blank, Windows may be blocking it: open Settings — Privacy & security — Camera / Microphone and allow desktop apps to use it, then run this test again.",
                      "اگر دوربین یا میکروفون کار نکرد، ممکن است ویندوز جلوی آن را گرفته باشد: تنظیمات — حریم خصوصی و امنیت — دوربین / میکروفون را باز کنید و اجازه دسترسی برنامه‌های دسکتاپ را بدهید، سپس دوباره این تست را اجرا کنید."),
                12, FontWeight.Medium, Palette.Muted),
            bottom);
    }

    private void RefreshCamera()
    {
        _cameraBadge.Content = _cameraName is { } name ? Kv.Badge(name) : null;
        Control? overlay = _camera switch
        {
            DeviceState.Checking => new BusySpinner(22, Brushes.White),
            DeviceState.Missing => OverlayText(T("No webcam on this device — the camera check is skipped and nothing is deducted for it.",
                                                 "این دستگاه وب‌کم ندارد — بررسی دوربین رد می‌شود و امتیازی کم نمی‌شود.")),
            DeviceState.Failed => OverlayText(T($"The camera couldn't be started, so there is no preview. Another app may be using it, or Windows privacy settings may block it. Report an issue to move on. ({_cameraProblem})",
                                                $"دوربین شروع نشد، پس پیش‌نمایشی نیست. ممکن است برنامه دیگری از آن استفاده کند یا تنظیمات حریم خصوصی ویندوز جلویش را گرفته باشد. برای ادامه ایراد را گزارش دهید. ({_cameraProblem})")),
            _ => _preview.Source is null ? new BusySpinner(22, Brushes.White) : null,
        };
        if (overlay is not null)
        {
            overlay.HorizontalAlignment = HorizontalAlignment.Center;
            overlay.VerticalAlignment = VerticalAlignment.Center;
        }
        _cameraOverlay.Content = overlay;
        _cameraCheck.Content = _camera == DeviceState.Missing ? null
            : new RetroCheckBox(T("Camera works — I can see myself clearly", "دوربین کار می‌کند — خودم را واضح می‌بینم"), _cameraWorks, on => _cameraWorks = on);
    }

    private static Control OverlayText(string text)
    {
        var t = Kv.Text(text, 13, FontWeight.Medium, Brushes.White);
        t.TextAlignment = TextAlignment.Center;
        t.Margin = new Thickness(16);
        return t;
    }

    private void RefreshMic()
    {
        _micBadge.Content = _micName is { } name ? Kv.Badge(name) : null;
        switch (_mic)
        {
            case DeviceState.Missing:
                _micBody.Content = Kv.Text(T("No microphone was found on this PC.", "میکروفونی روی این رایانه پیدا نشد."), 13);
                break;
            case DeviceState.Failed:
                _micBody.Content = Kv.Text(T("The microphone couldn't be started, so its level can't be shown. Report an issue to move on.",
                                             "میکروفون شروع نشد، پس سطح صدا نشان داده نمی‌شود. برای ادامه ایراد را گزارش دهید."), 13);
                break;
            default:
                var pitchBlock = Kv.Column(1, Kv.Text(T("Estimated pitch — speak or make noise", "زیر و بمی تخمینی — حرف بزنید یا صدا ایجاد کنید"), 10, FontWeight.Bold, Palette.Muted), _pitchText);
                _micBody.Content = Kv.Column(10, _meter, Kv.Spread(_db, pitchBlock), _history);
                break;
        }
        _micCheckBox = _mic == DeviceState.Missing ? null
            : new RetroCheckBox(T("Microphone works — the level moves with my voice", "میکروفون کار می‌کند — سطح با صدایم حرکت می‌کند"), _micWorks, on => _micWorks = on);
        _micCheck.Content = _micCheckBox;
    }

    private void Finish(string? issue)
    {
        var result = new MicCameraTestResult
        {
            CameraPresent = _camera != DeviceState.Missing,
            CameraWorks = _camera != DeviceState.Missing && _cameraWorks,
            MicrophonePresent = _mic != DeviceState.Missing,
            MicrophoneWorks = _mic != DeviceState.Missing && _micWorks,
            PeakLevelDb = _peakDb > -160 ? _peakDb : null,
            ReportedIssue = issue,
        };
        Dispose();
        Runner.Complete(TestModule.MicCamera, r => r.MicCamera = result);
    }
}
