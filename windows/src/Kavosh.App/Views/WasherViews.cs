using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Kavosh.App.Ui;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Views;

public sealed class WasherSetupView : UserControl, IDisposable
{
    private readonly AppModel _model;
    private readonly MainWindow _window;
    private bool _acknowledged;
    private bool? _lastRunFinished;
    private FullscreenWindow? _fullscreen;

    public WasherSetupView(AppModel model, MainWindow window)
    {
        _model = model;
        _window = window;
        _model.SettingsChanged += Render;
        Render();
    }

    public void Dispose()
    {
        _model.SettingsChanged -= Render;
        _fullscreen?.Close();
    }

    private bool WillFlash => _model.Settings.WasherPattern.IsFlashing() || _model.Settings.WasherRotate;

    private void Render()
    {
        var settings = _model.Settings;
        var back = new RetroButton(T("Back to Dashboard", "بازگشت به داشبورد"), Loc.Language.IsRightToLeft() ? Icon.ArrowRight : Icon.ArrowLeft,
            ButtonKind.Secondary, ButtonSize.Small, _model.GoToDashboard);
        var header = Kv.Row(14, back, Kv.Column(2, Kv.SectionLabel(T("Maintenance tools", "ابزارهای نگهداری"), Icon.Sparkles),
            Kv.Text(T("Screen Washer & Burn-in Fixer", "صفحه‌شوی و رفع سوختگی"), 26, FontWeight.ExtraBold)));
        var stack = Kv.Column(20, header,
            Kv.Text(T("Rapidly cycles every pixel through its full range to help clear temporary image retention, and exercises the subpixel drivers on an OLED or LCD panel.",
                      "هر پیکسل را به‌سرعت در کل محدوده‌اش می‌چرخاند تا اثر تصویر موقت پاک شود و زیرپیکسل‌های پنل OLED یا LCD را فعال می‌کند."), 14));

        if (_lastRunFinished is { } finished)
        {
            stack.Children.Add(Kv.Card(Kv.Label(finished
                    ? T("Wash finished. Look at the inspection slides to check whether the retention faded.", "شستشو تمام شد. اسلایدهای بررسی را ببینید تا مطمئن شوید اثر کم شده است.")
                    : T("The wash was stopped.", "شستشو متوقف شد."),
                finished ? Icon.Seal : Icon.Stop, Palette.Ink, 14), finished ? Palette.WithOpacity(Palette.GreenColor, 0.2) : Palette.Surface));
        }

        var warning = Kv.Card(Kv.Column(8,
            Kv.SectionLabel(T("Photosensitivity warning", "هشدار حساسیت به نور"), Icon.Warning, Palette.Red),
            Kv.Text(T("Two of the washing patterns fill the entire screen with rapidly changing colour and high-frequency flashing noise. Do not use them if you or anyone who can see this screen has photosensitive epilepsy or is sensitive to flashing light.",
                      "دو الگوی شستشو کل صفحه را با رنگ‌های سریع‌تغییر و نویز چشمک‌زن پر می‌کنند. اگر شما یا هر کسی که این صفحه را می‌بیند صرع حساس به نور دارد یا به نور چشمک‌زن حساس است، از آن‌ها استفاده نکنید."), 13),
            Kv.Text(T("If you are unsure, choose \"Gradient sweep\" as the starting pattern and turn off \"Rotate through all patterns\" below — that pattern moves smoothly and never flashes.",
                      "اگر مطمئن نیستید، «گرادیان متحرک» را به‌عنوان الگوی شروع انتخاب کنید و «چرخش بین همه الگوها» را خاموش کنید — آن الگو نرم حرکت می‌کند و هرگز چشمک نمی‌زند."), 12, FontWeight.SemiBold)),
            Palette.WithOpacity(Palette.RedColor, 0.14));
        var time = Kv.Card(Kv.Column(8,
            Kv.SectionLabel(T("This takes time to work", "این کار زمان می‌برد"), Icon.Timer, Palette.Ink),
            Kv.Text(T("Clearing image retention is gradual — plan on leaving this running for at least 5 minutes, and up to 30 for a stubborn ghost. Your screen will be completely covered for the whole run and the PC will be kept awake, so start it when you don't need the computer for anything else.",
                      "پاک شدن اثر تصویر تدریجی است — دست‌کم ۵ دقیقه و برای اثرهای سمج تا ۳۰ دقیقه اجرا کنید. در تمام مدت صفحه کاملاً پوشیده می‌ماند و رایانه بیدار نگه داشته می‌شود، پس وقتی به کامپیوتر نیاز ندارید شروعش کنید."), 13)),
            Palette.WithOpacity(Palette.YellowColor, 0.25));
        stack.Children.Add(TopAligned(Kv.Columns(20, warning, time)));

        var durations = Kv.Column(10, Kv.SectionLabel(T("How long", "چه مدت"), Icon.Timer));
        foreach (var option in ScreenWasherOptions.Durations)
        {
            var o = option;
            durations.Children.Add(OptionRow(o.Title(), o.Detail(), settings.WasherDuration == o, false, () => _model.UpdateSettings(s => s.WasherDuration = o)));
        }
        var patterns = Kv.Column(10, Kv.SectionLabel(T("Starting pattern", "الگوی شروع"), Icon.Sparkles));
        foreach (var option in ScreenWasherOptions.Patterns)
        {
            var o = option;
            patterns.Children.Add(OptionRow(o.Title(), o.Detail(), settings.WasherPattern == o, o.IsFlashing(), () => _model.UpdateSettings(s => s.WasherPattern = o)));
        }
        patterns.Children.Add(new RetroCheckBox(T("Rotate through all patterns while running (recommended)", "چرخش بین همه الگوها در حین اجرا (توصیه‌شده)"),
            settings.WasherRotate, on => _model.UpdateSettings(s => s.WasherRotate = on)));
        stack.Children.Add(TopAligned(Kv.Columns(20, Kv.Card(durations), Kv.Card(patterns))));

        var start = new RetroButton(T("Start Screen Washer", "شروع صفحه‌شوی"), Icon.Play, ButtonKind.Primary, ButtonSize.Large, () => Start(false))
        {
            IsEnabled = !WillFlash || _acknowledged,
        };
        var run = Kv.Column(12,
            Kv.Text(T("While it's running: move the mouse to bring back the controls — press Esc or click Stop to end it at any time — press ← → to step through the inspection slides.",
                      "در حین اجرا: موس را حرکت دهید تا کنترل‌ها برگردند — هر زمان با Esc یا دکمه توقف تمامش کنید — با ← → بین اسلایدهای بررسی جابه‌جا شوید."), 13),
            Kv.Text(T("Tip: look at the inspection slides before and after the wash — that's the only reliable way to tell whether the retention actually faded.",
                      "نکته: اسلایدهای بررسی را قبل و بعد از شستشو ببینید — تنها راه مطمئن برای فهمیدن این‌که اثر واقعاً کم شده است."), 12, FontWeight.Medium, Palette.Muted));
        if (WillFlash)
        {
            run.Children.Add(new RetroCheckBox(T("I've read the warning above and I understand this will flash the screen.", "هشدار بالا را خواندم و می‌دانم که صفحه چشمک خواهد زد."),
                _acknowledged, on =>
                {
                    _acknowledged = on;
                    start.IsEnabled = on;
                }));
        }
        run.Children.Add(Kv.Flow(12, start,
            new RetroButton(T("Uniformity slides", "اسلایدهای یکنواختی"), Icon.Eye, ButtonKind.Secondary, ButtonSize.Regular, () => Start(true))));
        stack.Children.Add(Kv.Card(run));

        Content = Kv.Scroll(stack, new Thickness(24));
    }

    private static Grid TopAligned(Grid grid)
    {
        foreach (var child in grid.Children) child.VerticalAlignment = VerticalAlignment.Top;
        return grid;
    }

    private static Control OptionRow(string title, string detail, bool selected, bool flashing, Action select)
    {
        var dot = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = selected ? Palette.Accent : Palette.Surface,
            BorderBrush = Palette.Ink,
            BorderThickness = new Thickness(Palette.Border),
            VerticalAlignment = VerticalAlignment.Top,
            Child = selected ? new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Brushes.White } : null,
        };
        var titleRow = Kv.Row(6, Kv.Text(title, 13, FontWeight.Bold, wrap: false));
        if (flashing) titleRow.Children.Add(Kv.Badge(T("Flashing", "چشمک‌زن"), Palette.RedColor, Brushes.White, Icon.Bolt));
        var text = Kv.Column(2, titleRow, Kv.Text(detail, 11, FontWeight.Medium, Palette.Muted));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(dot);
        text.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var button = new Button
        {
            Content = grid,
            Background = selected ? Palette.WithOpacity(Palette.AccentColor, 0.12) : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Click += (_, _) => select();
        return button;
    }

    private void Start(bool inspectOnly)
    {
        _fullscreen?.Close();
        var settings = _model.Settings;
        var window = new FullscreenWindow(_window);
        var washer = new ScreenWasher(window, settings.WasherDuration, settings.WasherPattern, settings.WasherRotate && !inspectOnly, inspectOnly, completed =>
        {
            _fullscreen = null;
            _lastRunFinished = inspectOnly ? null : completed;
            Render();
        });
        window.Content = washer;
        window.KeyHandler = washer.HandleKey;
        _fullscreen = window;
        window.Show();
    }
}

/// <summary>The fullscreen washer: animated patterns, auto-hiding controls and inspection slides.</summary>
internal sealed class ScreenWasher : UserControl
{
    private static readonly (string Name, Color Color)[] Slides =
    {
        (T("5% gray", "خاکستری ۵٪"), Gray(0.05)),
        (T("10% gray", "خاکستری ۱۰٪"), Gray(0.10)),
        (T("20% gray", "خاکستری ۲۰٪"), Gray(0.20)),
        (T("50% gray", "خاکستری ۵۰٪"), Gray(0.50)),
        (T("White", "سفید"), Colors.White),
        (T("Red", "قرمز"), Color.FromRgb(255, 0, 0)),
        (T("Green", "سبز"), Color.FromRgb(0, 255, 0)),
        (T("Blue", "آبی"), Color.FromRgb(0, 0, 255)),
    };

    private readonly FullscreenWindow _window;
    private readonly ScreenWasherDuration _duration;
    private readonly bool _rotate;
    private readonly Action<bool> _onDone;
    private readonly WasherCanvas _canvas = new();
    private readonly Slot _controls = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _ticker;
    private ScreenWasherPattern _pattern;
    private int? _inspection;
    private double _lastRotation;
    private DateTime _lastPointer = DateTime.UtcNow;
    private bool _closed;
    private TextBlock? _elapsedText, _remainingText;

    public ScreenWasher(FullscreenWindow window, ScreenWasherDuration duration, ScreenWasherPattern pattern, bool rotate, bool inspectOnly, Action<bool> onDone)
    {
        _window = window;
        _duration = duration;
        _pattern = pattern;
        _rotate = rotate;
        _onDone = onDone;
        if (inspectOnly)
        {
            _inspection = 0;
            _clock.Stop();
        }
        Content = new Panel { Children = { _canvas, _controls } };
        Apply();
        _ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => Tick());
        _ticker.Start();
        PointerMoved += (_, _) => PointerActivity();
        PointerPressed += (_, _) => PointerActivity();
        window.Closed += (_, _) => Stop(false);
    }

    private static Color Gray(double level)
    {
        byte v = (byte)Math.Round(level * 255);
        return Color.FromRgb(v, v, v);
    }

    /// <summary>Washing time; the clock is paused while the inspection slides are up.</summary>
    private double Elapsed => _clock.Elapsed.TotalSeconds;

    private double? Remaining => _duration.Seconds() is { } total ? Math.Max(0, total - Elapsed) : null;

    private void Tick()
    {
        if (_closed) return;
        if (_inspection is null)
        {
            double elapsed = _clock.Elapsed.TotalSeconds;
            if (_rotate && elapsed - _lastRotation >= 60)
            {
                _lastRotation = elapsed;
                int index = Array.IndexOf(ScreenWasherOptions.Patterns, _pattern);
                SetPattern(ScreenWasherOptions.Patterns[(index + 1) % ScreenWasherOptions.Patterns.Length]);
            }
            if (_duration.Seconds() is { } total && elapsed >= total)
            {
                Stop(true);
                return;
            }
        }
        if (_elapsedText is not null) _elapsedText.Text = T($"{Fmt.Duration(Elapsed)} elapsed", $"{Fmt.Duration(Elapsed)} گذشته");
        if (_remainingText is not null && Remaining is { } left) _remainingText.Text = T($"{Fmt.Duration(left)} left", $"{Fmt.Duration(left)} مانده");
        if (_controls.Content is not null && (DateTime.UtcNow - _lastPointer).TotalSeconds > 3)
        {
            _controls.Content = null;
            _window.Cursor = new Cursor(StandardCursorType.None);
        }
    }

    private void PointerActivity()
    {
        _lastPointer = DateTime.UtcNow;
        if (_controls.Content is null) ShowControls();
        _window.Cursor = Cursor.Default;
    }

    private void SetPattern(ScreenWasherPattern pattern)
    {
        if (_inspection is not null) ResumeWashing();
        _pattern = pattern;
        Apply();
    }

    private void ResumeWashing()
    {
        _inspection = null;
        _clock.Start();
    }

    private void Apply()
    {
        if (_inspection is { } index)
        {
            _canvas.Show(WasherMode.Solid, Slides[index].Color);
        }
        else
        {
            _canvas.Show(_pattern switch
            {
                ScreenWasherPattern.Noise => WasherMode.Noise,
                ScreenWasherPattern.Rgbw => WasherMode.Rgbw,
                _ => WasherMode.Gradient,
            }, Colors.Black);
        }
        ShowControls();
    }

    private void NextSlide()
    {
        if (_inspection is { } index)
        {
            _inspection = Math.Min(Slides.Length - 1, index + 1);
        }
        else
        {
            _clock.Stop();
            _inspection = 0;
        }
        Apply();
    }

    private void PreviousSlide()
    {
        if (_inspection is not { } index) return;
        if (index == 0) ResumeWashing();
        else _inspection = index - 1;
        Apply();
    }

    public bool HandleKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Stop(false); break;
            case Key.Right: NextSlide(); break;
            case Key.Left: PreviousSlide(); break;
        }
        PointerActivity();
        return true;
    }

    private void ShowControls()
    {
        var white = Brushes.White;
        var title = _inspection is { } i
            ? Kv.Text(T($"Inspect: {Slides[i].Name} ({i + 1} of {Slides.Length})", $"بررسی: {Slides[i].Name} ({i + 1} از {Slides.Length})"), 16, FontWeight.Bold, white)
            : Kv.Text(_pattern.Title(), 16, FontWeight.Bold, white);
        _elapsedText = Kv.Hardware("", 14, FontWeight.Bold, white, wrap: false);
        _remainingText = Kv.Hardware("", 12, FontWeight.Medium, new SolidColorBrush(Colors.White, 0.75), wrap: false);
        _remainingText.IsVisible = _duration.Seconds() is not null;
        var times = Kv.Column(2, _elapsedText, _remainingText);
        var top = Panelled(Kv.Spread(Kv.Column(2, Kv.Text(T("SCREEN WASHER", "صفحه‌شوی"), 12, FontWeight.ExtraBold, Palette.Yellow), title), times));
        top.VerticalAlignment = VerticalAlignment.Top;

        var buttons = Kv.Row(10);
        foreach (var pattern in ScreenWasherOptions.Patterns)
        {
            var p = pattern;
            buttons.Children.Add(new RetroButton(p.Title(), null, _pattern == p && _inspection is null ? ButtonKind.Warning : ButtonKind.Secondary, ButtonSize.Small, () => SetPattern(p)));
        }
        buttons.Children.Add(new RetroButton(T("Inspect", "بررسی"), Icon.Eye, _inspection is not null ? ButtonKind.Warning : ButtonKind.Secondary, ButtonSize.Small, NextSlide));
        var right = Kv.Row(12, Kv.Text(T("← → to change slide", "← → برای تغییر اسلاید"), 12, FontWeight.Medium, new SolidColorBrush(Colors.White, 0.8), wrap: false),
            new RetroButton(T("STOP  (Esc)", "توقف (Esc)"), Icon.Stop, ButtonKind.Danger, ButtonSize.Regular, () => Stop(false)));
        var bottom = Panelled(Kv.Spread(buttons, right));
        bottom.VerticalAlignment = VerticalAlignment.Bottom;

        _controls.Content = new Panel { Margin = new Thickness(28), Children = { top, bottom } };
        Tick();
    }

    private static Border Panelled(Control content) => new()
    {
        Background = new SolidColorBrush(Colors.Black, 0.72),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(16, 14),
        Child = content,
    };

    private void Stop(bool completed)
    {
        if (_closed) return;
        _closed = true;
        _ticker.Stop();
        _canvas.Stop();
        Dispatcher.UIThread.Post(() =>
        {
            if (_window.IsVisible) _window.Close();
            _onDone(completed);
        });
    }
}

internal enum WasherMode { Noise, Rgbw, Gradient, Solid }

/// <summary>Draws the washer patterns, one step per rendered frame.</summary>
internal sealed class WasherCanvas : Control
{
    private WasherMode _mode = WasherMode.Gradient;
    private Color _solid = Colors.Black;
    private uint _frame;
    private bool _running;
    private WriteableBitmap? _noise;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public void Show(WasherMode mode, Color solid)
    {
        _mode = mode;
        _solid = solid;
        InvalidateVisual();
    }

    public void Stop() => _running = false;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _running = true;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _running = false;
    }

    private void Frame(TimeSpan _)
    {
        if (!_running) return;
        _frame++;
        InvalidateVisual();
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        switch (_mode)
        {
            case WasherMode.Solid:
                context.FillRectangle(new SolidColorBrush(_solid), bounds);
                break;
            case WasherMode.Rgbw:
            {
                // Red, green, blue, white, black — about 10 changes per second at 60 Hz.
                Color[] steps = { Color.FromRgb(255, 0, 0), Color.FromRgb(0, 255, 0), Color.FromRgb(0, 0, 255), Colors.White, Colors.Black };
                context.FillRectangle(new SolidColorBrush(steps[(_frame / 6) % 5]), bounds);
                break;
            }
            case WasherMode.Gradient:
                context.FillRectangle(GradientBrush(bounds.Size), bounds);
                break;
            default:
                DrawNoise(context, bounds);
                break;
        }
    }

    /// <summary>Slowly drifting full-spectrum gradient: color = 0.5 + 0.5·cos(2π(x + phase)).</summary>
    private IBrush GradientBrush(Size size)
    {
        double w = Math.Max(1, size.Width), h = Math.Max(1, size.Height);
        double t = _clock.Elapsed.TotalSeconds;
        // x = px/w + 0.25·py/h + 0.06·t ; one color period = 1 unit of x.
        var g = new Vector(1 / w, 0.25 / h);
        double lengthSquared = g.X * g.X + g.Y * g.Y;
        var unit = new Vector(g.X / lengthSquared, g.Y / lengthSquared);
        double shift = 0.06 * t % 1;
        var start = new Point(-shift * unit.X, -shift * unit.Y);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(start, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(start + unit, RelativeUnit.Absolute),
            SpreadMethod = GradientSpreadMethod.Repeat,
        };
        const int stops = 48;
        for (int i = 0; i <= stops; i++)
        {
            double x = (double)i / stops;
            byte C(double offset) => (byte)Math.Round(255 * (0.5 + 0.5 * Math.Cos(2 * Math.PI * (x + offset))));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(C(0), C(0.33), C(0.67)), x));
        }
        return brush;
    }

    /// <summary>A random color per 2×2 block of physical pixels, new every frame.</summary>
    private unsafe void DrawNoise(DrawingContext context, Rect bounds)
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scaling / 2));
        int height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scaling / 2));
        if (_noise is null || _noise.PixelSize.Width != width || _noise.PixelSize.Height != height)
        {
            _noise?.Dispose();
            _noise = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        uint frame = _frame;
        using (var buffer = _noise.Lock())
        {
            byte* start = (byte*)buffer.Address;
            int rowBytes = buffer.RowBytes;
            Parallel.For(0, height, y =>
            {
                uint* row = (uint*)(start + y * rowBytes);
                for (int x = 0; x < width; x++)
                {
                    uint hash = unchecked((uint)x * 374761393u + (uint)y * 668265263u + frame * 2246822519u);
                    hash = unchecked((hash ^ (hash >> 13)) * 1274126177u);
                    hash ^= hash >> 16;
                    row[x] = 0xFF000000u | (hash & 0x00FFFFFFu);
                }
            });
        }
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            context.DrawImage(_noise, new Rect(0, 0, width, height), bounds);
        }
    }
}
