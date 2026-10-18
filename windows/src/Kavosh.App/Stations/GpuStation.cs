using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kavosh.App.Ui;
using Kavosh.App.Views;
using Kavosh.Core;
using Kavosh.Hardware.Gpu;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Stations;

public sealed class GpuStation : Station
{
    private volatile bool _cancelled;
    private Guid? _recording;
    private readonly GpuBenchmarkConfiguration _configuration;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly Slot _placeholder = new();
    private readonly Slot _adapter = new();
    private readonly TextBlock _stage = Kv.Text("", 13, FontWeight.SemiBold);
    private readonly RetroProgressBar _progress = new(14, Palette.TealColor);
    private readonly Slot _below = new();
    private WriteableBitmap? _bitmap;
    private GpuBenchmarkProgress? _last;

    public GpuStation(TestSuiteRunner runner) : base(runner)
    {
        _configuration = new GpuBenchmarkConfiguration { TotalSeconds = runner.Settings.PerformanceTestSeconds };
        _placeholder.Content = Kv.Column(8, new BusySpinner(18, Brushes.White),
            Kv.Text(T("The first image appears once the GPU finishes a frame.", "اولین تصویر وقتی ظاهر می‌شود که پردازنده گرافیکی یک فریم را تمام کند."),
                12, FontWeight.Medium, Palette.WithOpacity(Colors.White, 0.8)));
        ((Control)_placeholder.Content).HorizontalAlignment = HorizontalAlignment.Center;
        ((Control)_placeholder.Content).VerticalAlignment = VerticalAlignment.Center;
        var screen = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(8),
            BorderBrush = Palette.Ink,
            BorderThickness = new Thickness(Palette.Border),
            ClipToBounds = true,
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new Panel { Children = { _preview, _placeholder } },
        };
        // Keep 16:9.
        screen.SizeChanged += (_, e) => screen.Height = e.NewSize.Width * 9 / 16;
        var preview = Kv.Card(Kv.Column(10,
            Kv.Spread(Kv.SectionLabel(T("Live render", "رندر زنده"), Icon.Sparkles), _adapter),
            screen, _stage, _progress), padding: 12);
        Content = Kv.Column(18, preview, _below);
        ShowStage(null);
        RefreshTiles();
        Runner.Telemetry.Updated += RefreshTiles;
    }

    protected override void Start()
    {
        _recording = Runner.Telemetry.BeginRecording();
        RunInBackground(() =>
        {
            try
            {
                using var benchmark = new D3D11RaymarchBenchmark(_configuration);
                string name = benchmark.AdapterName;
                OnUi(() => _adapter.Content = Kv.Badge(T($"GPU under test: {name}", $"گرافیک در حال تست: {name}")));
                var result = benchmark.Run(() => _cancelled, p => OnUi(() => Show(p)));
                OnUi(() => Finish(result));
            }
            catch (Exception e) when (!_cancelled)
            {
                string message = e is GpuBenchmarkException ? e.Message : T($"The GPU test failed: {e.Message}", $"تست گرافیک ناموفق بود: {e.Message}");
                OnUi(() => ShowError(message));
            }
            catch
            {
                // Cancelled.
            }
        }, "Kavosh GPU benchmark");
    }

    protected override void Stop()
    {
        _cancelled = true;
        Runner.Telemetry.Updated -= RefreshTiles;
        if (_recording is { } id) Runner.Telemetry.EndRecording(id);
        _recording = null;
    }

    private void Show(GpuBenchmarkProgress p)
    {
        _last = p;
        ShowStage(p);
        _progress.Value = p.Fraction;
        if (p.Preview is { } pixels) ShowPreview(pixels);
        RefreshTiles();
    }

    private void ShowPreview(byte[] pixels)
    {
        int width = _configuration.PreviewWidth, height = _configuration.PreviewHeight;
        if (pixels.Length < width * height * 4) return;
        _bitmap ??= new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = _bitmap.Lock())
        {
            for (int row = 0; row < height; row++)
                Marshal.Copy(pixels, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
        }
        _preview.Source = null;
        _preview.Source = _bitmap;
        _placeholder.Content = null;
    }

    private void ShowStage(GpuBenchmarkProgress? p)
    {
        _stage.Text = p?.Stage switch
        {
            GpuBenchmarkStage.WarmingUp => T($"Warming up until clocks settle — {p.Value.Frame} frames", $"گرم کردن تا ثابت شدن فرکانس — {p.Value.Frame} فریم"),
            GpuBenchmarkStage.Scored => T($"Scored run — frame {p.Value.Frame} of {p.Value.Of}", $"اجرای امتیازدار — فریم {p.Value.Frame} از {p.Value.Of}"),
            GpuBenchmarkStage.Sustained => T($"Sustained load — {p.Value.Frame} frames so far", $"بار پایدار — تاکنون {p.Value.Frame} فریم"),
            GpuBenchmarkStage.Finished => T("Done", "تمام شد"),
            _ => T("Preparing the GPU (compiling the shader, allocating a 5120×2880 render target)…", "آماده‌سازی گرافیک (کامپایل شیدر و ساخت تصویر ۵۱۲۰×۲۸۸۰)…"),
        };
    }

    private void RefreshTiles()
    {
        if (_below.Tag is "result") return;
        var latest = Runner.Telemetry.Latest;
        _below.Content = Kv.Columns(14,
            Kv.StatTile(T("Live score", "امتیاز زنده"), _last?.LiveScore is { } s ? Fmt.Number(s, 0) : "…", color: Palette.Teal, icon: Icon.Speedometer),
            Kv.StatTile(T("ms per frame", "میلی‌ثانیه در هر فریم"), _last?.LastFrameMs is { } ms ? Fmt.Fixed(ms, 1) : "—", icon: Icon.Timer),
            Kv.StatTile(T("GPU temperature", "دمای گرافیک"), Fmt.Celsius(latest?.GpuTemperatureCelsius),
                color: new SolidColorBrush(Palette.TemperatureColor(latest?.GpuTemperatureCelsius)), icon: Icon.Thermometer),
            Kv.StatTile(T("GPU usage", "مصرف گرافیک"), latest?.GpuLoadPercent is { } load ? Fmt.Percent(load) : "—", icon: Icon.Chart));
    }

    private void Finish(GpuBenchmarkResult result)
    {
        if (_recording is { } id) result.PeakTemperatureCelsius = Runner.Telemetry.EndRecording(id).PeakGpuTemperature;
        _recording = null;
        _below.Tag = "result";
        _below.Content = Kv.Column(18, ResultCard(result),
            new ContinueBar(Runner.IsLastStation, AutoAdvance, () => Runner.Complete(TestModule.Gpu, r => r.Gpu = result)));
    }

    private void ShowError(string message)
    {
        Content = StationError.Make(T("The GPU test couldn't run", "تست گرافیک اجرا نشد"), message, () => Runner.Fail(TestModule.Gpu, message));
    }

    public static Control ResultCard(GpuBenchmarkResult result)
    {
        return Kv.Card(Kv.Column(16,
            Kv.SectionLabel(T("GPU benchmark results", "نتایج بنچمارک گرافیک"), Icon.Gpu),
            Kv.Columns(14,
                Kv.StatTile(T("GPU score", "امتیاز گرافیک"), Fmt.Number(result.Score, 0), result.AdapterName, Palette.Teal),
                Kv.StatTile(T("Average frame", "میانگین فریم"), $"{Fmt.Fixed(result.AverageFrameTimeMs, 1)} ms", $"{Fmt.Fixed(result.AverageFps, 2)} fps"),
                Kv.StatTile(T("Sustained score", "امتیاز پایدار"), result.SustainedScore is { } s ? Fmt.Number(s, 0) : "—",
                    result.SustainedScore is null ? T("No sustained phase — raise the test duration to measure throttling.", "مرحله پایدار نداشت — برای سنجش افت سرعت، مدت تست را بیشتر کنید.") : null),
                Kv.StatTile(T("Peak GPU temperature", "بیشترین دمای گرافیک"), Fmt.Celsius(result.PeakTemperatureCelsius),
                    color: new SolidColorBrush(Palette.TemperatureColor(result.PeakTemperatureCelsius)))),
            Kv.Columns(18,
                Kv.KeyValue(T("Slowest / fastest", "کندترین / سریع‌ترین"), $"{Fmt.Fixed(result.MinFps, 2)} / {Fmt.Fixed(result.MaxFps, 2)} fps"),
                Kv.KeyValue(T("1% low", "۱٪ پایین"), $"{Fmt.Fixed(result.OnePercentLowFps, 2)} fps"),
                Kv.KeyValue(T("Frames", "فریم‌ها"), Fmt.Number(result.TotalFramesRendered))),
            Kv.Text(T($"Off-screen Direct3D 11 raymarch — {result.OutputWidth}×{result.OutputHeight}, {result.SamplesPerPixel} samples per pixel. Reference {D3D11RaymarchBenchmark.ReferenceName} = 1,000 (the same scene and scale as Kavosh for Mac).",
                      $"رِی‌مارچ Direct3D 11 خارج از صفحه — {result.OutputWidth}×{result.OutputHeight}، {result.SamplesPerPixel} نمونه در هر پیکسل. مرجع {D3D11RaymarchBenchmark.ReferenceName} = ۱۰۰۰ (همان صحنه و مقیاس کاوش برای مک)."),
                11, FontWeight.Medium, Palette.Muted)));
    }
}
