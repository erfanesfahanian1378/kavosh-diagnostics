using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kavosh.App.Ui;
using Kavosh.App.Views;
using Kavosh.Core;
using Kavosh.Hardware.Gpu;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Stations;

public sealed class DisplayStation : Station
{
    private readonly MainWindow _window;
    private DisplayTestResult? _result;
    private bool _exitedEarly;
    private FullscreenWindow? _fullscreen;

    public DisplayStation(TestSuiteRunner runner, MainWindow window) : base(runner)
    {
        _window = window;
        Render();
    }

    protected override void Stop()
    {
        _fullscreen?.Close();
        _fullscreen = null;
    }

    private void Render()
    {
        if (_result is { } result)
        {
            var stack = Kv.Column(12, Kv.SectionLabel(T("Display test result", "نتیجه تست نمایشگر"), Icon.Display));
            if (result.Issues.Count == 0)
            {
                stack.Children.Add(Kv.Label(T("No issues reported", "ایرادی گزارش نشد"), Icon.Seal, Palette.Green, 16));
            }
            else
            {
                stack.Children.Add(Kv.Text(T($"{result.Issues.Count} issue(s) reported", $"{result.Issues.Count} ایراد گزارش شد"), 16, FontWeight.Bold, Palette.Orange));
                foreach (var issue in result.Issues) stack.Children.Add(Kv.Label(issue.ShortText(), Icon.Warning, Palette.Ink));
            }
            if (result.MeasuredRefreshRateHz is { } hz)
                stack.Children.Add(Kv.KeyValue(T("Measured refresh rate", "نرخ تازه‌سازی اندازه‌گیری‌شده"), $"{Fmt.Fixed(hz, 0)} Hz"));
            stack.Children.Add(new RetroButton(T("Run the display test again", "اجرای دوباره تست نمایشگر"), null, ButtonKind.Ghost, ButtonSize.Small, StartTest));
            Content = Kv.Column(18, Kv.Card(stack),
                new ContinueBar(Runner.IsLastStation, null, () => Runner.Complete(TestModule.Display, r => r.Display = result)));
            return;
        }

        var intro = Kv.Column(14,
            Kv.SectionLabel(T("Fullscreen display test", "تست تمام‌صفحه نمایشگر"), Icon.Display),
            Kv.Instruction(T("The display test fills the whole screen with solid colors, gradients and a moving square. Look closely at each screen; at the end you'll tick any problems you noticed.",
                             "تست نمایشگر کل صفحه را با رنگ‌های یکدست، گرادیان‌ها و یک مربع متحرک پر می‌کند. هر صفحه را دقیق نگاه کنید؛ در پایان هر ایرادی دیدید را علامت می‌زنید.")));
        if (_exitedEarly)
        {
            intro.Children.Add(Kv.Text(T("The display test was closed early. Start it again to finish it.", "تست نمایشگر زودتر بسته شد. برای کامل کردن، دوباره شروعش کنید."),
                13, FontWeight.SemiBold, Palette.Orange));
        }
        intro.Children.Add(Kv.Flow(12,
            new RetroButton(T("Start the fullscreen display test", "شروع تست تمام‌صفحه نمایشگر"), Icon.Display, ButtonKind.Primary, ButtonSize.Large, StartTest),
            new RetroButton(T("Skip", "رد شدن"), null, ButtonKind.Ghost, ButtonSize.Regular, () => Runner.Skip(TestModule.Display))));
        Content = Kv.Card(intro);
    }

    private void StartTest()
    {
        _fullscreen?.Close();
        var window = new FullscreenWindow(_window);
        var test = new DisplayTest(window, outcome =>
        {
            if (outcome is not null)
            {
                _result = outcome;
                _exitedEarly = false;
            }
            else
            {
                _exitedEarly = true;
            }
            _fullscreen = null;
            if (!IsDisposed) Render();
        });
        window.Content = test;
        window.KeyHandler = test.HandleKey;
        _fullscreen = window;
        window.Show();
    }
}

/// <summary>The fullscreen slides of the display test.</summary>
internal sealed class DisplayTest : UserControl
{
    private enum SlideKind { Intro, Solid, Gradient, Refresh, Checklist }

    private sealed record Slide(SlideKind Kind, string Name = "", Color Color = default, string Hint = "", bool DarkLabel = false);

    private readonly FullscreenWindow _window;
    private readonly Action<DisplayTestResult?> _onDone;
    private readonly List<Slide> _slides;
    private readonly HashSet<DisplayIssue> _issues = new();
    private int _index;
    private double? _measuredHz;
    private DispatcherTimer? _labelTimer;
    private Control? _label;
    private VBlankMeter? _meter;
    private TextBlock? _hzText;
    private bool _closed;

    public DisplayTest(FullscreenWindow window, Action<DisplayTestResult?> onDone)
    {
        _window = window;
        _onDone = onDone;
        string lookFor = T("look for dots that don't match", "دنبال نقطه‌هایی بگردید که هم‌رنگ نیستند");
        string blotches = T("look for blotches and uneven areas", "دنبال لکه و ناحیه ناهموار بگردید");
        _slides = new List<Slide>
        {
            new(SlideKind.Intro),
            new(SlideKind.Solid, T("Red", "قرمز"), Color.FromRgb(255, 0, 0), lookFor),
            new(SlideKind.Solid, T("Green", "سبز"), Color.FromRgb(0, 255, 0), lookFor, true),
            new(SlideKind.Solid, T("Blue", "آبی"), Color.FromRgb(0, 0, 255), lookFor),
            new(SlideKind.Solid, T("White", "سفید"), Colors.White, T("look for dark dots and uneven brightness", "دنبال نقطه تیره و روشنایی ناهموار بگردید"), true),
            new(SlideKind.Solid, T("Black", "سیاه"), Colors.Black, T("look for bright dots and light bleeding in from the edges", "دنبال نقطه روشن و نشت نور از لبه‌ها بگردید")),
            new(SlideKind.Solid, T("50% gray", "خاکستری ۵۰٪"), Gray(0.5), T("look for a faint image that stays (burn-in)", "دنبال تصویر کم‌رنگ ماندگار (سوختگی) بگردید"), true),
            new(SlideKind.Solid, T("20% gray", "خاکستری ۲۰٪"), Gray(0.2), blotches),
            new(SlideKind.Solid, T("10% gray", "خاکستری ۱۰٪"), Gray(0.1), blotches),
            new(SlideKind.Solid, T("5% gray", "خاکستری ۵٪"), Gray(0.05), blotches),
            new(SlideKind.Gradient),
            new(SlideKind.Refresh),
            new(SlideKind.Checklist),
        };
        Focusable = true;
        Show();
        window.Closed += (_, _) => Finish(null);
    }

    private static Color Gray(double level)
    {
        // The same sRGB gray level the macOS app uses (Color(white:)).
        byte v = (byte)Math.Round(level * 255);
        return Color.FromRgb(v, v, v);
    }

    private Slide Current => _slides[_index];
    private string StepText => T($"Step {_index + 1} of {_slides.Count}", $"مرحله {_index + 1} از {_slides.Count}");

    public bool HandleKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Finish(null);
                return true;
            case Key.Space or Key.Right or Key.Enter:
                if (Current.Kind == SlideKind.Checklist) return false;
                Next();
                return true;
            case Key.Left:
                Previous();
                return true;
            default:
                return Current.Kind != SlideKind.Checklist;
        }
    }

    private void Next()
    {
        if (_index >= _slides.Count - 1) return;
        _index++;
        Show();
    }

    private void Previous()
    {
        if (_index <= 0) return;
        _index--;
        Show();
    }

    private void Show()
    {
        _labelTimer?.Stop();
        if (Current.Kind != SlideKind.Refresh)
        {
            _meter?.Dispose();
            _meter = null;
        }
        Control content = Current.Kind switch
        {
            SlideKind.Intro => IntroSlide(),
            SlideKind.Solid => SolidSlide(Current),
            SlideKind.Gradient => GradientSlide(),
            SlideKind.Refresh => RefreshSlide(),
            _ => ChecklistSlide(),
        };
        var root = new Panel { Background = Brushes.Black };
        root.Children.Add(content);
        if (Current.Kind is SlideKind.Solid or SlideKind.Gradient or SlideKind.Refresh)
        {
            var close = new Button
            {
                Content = Icons.Make(Icon.Cross, 14, Brushes.White, 3.2),
                Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(9),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(18),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(close, T("Exit the display test", "خروج از تست نمایشگر"));
            close.Click += (_, e) =>
            {
                e.Handled = true;
                Finish(null);
            };
            root.Children.Add(close);
        }
        if (Current.Kind != SlideKind.Checklist)
        {
            root.PointerPressed += (_, e) =>
            {
                if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is not null) return;
                Next();
            };
        }
        root.PointerMoved += (_, _) => _window.Cursor = Cursor.Default;
        _window.Cursor = Current.Kind is SlideKind.Checklist or SlideKind.Intro ? Cursor.Default : new Cursor(StandardCursorType.None);
        Content = root;
        Focus();
    }

    private void FlashLabel(Control label)
    {
        _label = label;
        label.Opacity = 1;
        label.Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(400) },
        };
        _labelTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1800), DispatcherPriority.Normal, (_, _) =>
        {
            _labelTimer?.Stop();
            if (_label is not null) _label.Opacity = 0;
        });
        _labelTimer.Start();
    }

    private static Control SlideLabel(string title, string subtitle, bool dark)
    {
        var foreground = dark ? Brushes.Black : Brushes.White;
        var stack = Kv.Column(6, Kv.Text(title, 34, FontWeight.ExtraBold, foreground), Kv.Text(subtitle, 15, FontWeight.Medium, foreground));
        foreach (var child in stack.Children.OfType<TextBlock>()) child.TextAlignment = TextAlignment.Center;
        return new Border
        {
            Background = new SolidColorBrush(dark ? Colors.White : Colors.Black, 0.35),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(26, 16),
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    private Control IntroSlide()
    {
        var white = Brushes.White;
        var faded = new SolidColorBrush(Colors.White, 0.75);
        TextBlock Centered(TextBlock t)
        {
            t.TextAlignment = TextAlignment.Center;
            t.MaxWidth = 760;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            return t;
        }
        var begin = new Border
        {
            Background = Palette.Yellow,
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(18, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Kv.Text(T("PRESS SPACE, CLICK OR TAP TO BEGIN", "برای شروع SPACE را بزنید یا کلیک کنید"), 14, FontWeight.ExtraBold, Palette.OnYellow, wrap: false),
        };
        var icon = Icons.Make(Icon.Display, 56, white, 2);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var stack = Kv.Column(22,
            icon,
            Centered(Kv.Text(T("Display test", "تست نمایشگر"), 40, FontWeight.ExtraBold, white)),
            Centered(Kv.Text(T("This screen will fill your entire display with solid colors, one at a time, so you can spot dead or stuck pixels, ghosting, brightness issues and light bleeding across the whole panel.",
                               "این صفحه کل نمایشگر را یکی‌یکی با رنگ‌های یکدست پر می‌کند تا پیکسل سوخته یا گیرکرده، سایه‌افتادن، مشکل روشنایی و نشت نور را در کل پنل ببینید."), 18, FontWeight.Medium, white)),
            Centered(Kv.Text(T("Press SPACE, click, or tap anywhere to move to each next screen, all the way to the end — on the last one you'll be asked which issues, if any, you noticed. Press ESC to exit early.",
                               "برای رفتن به صفحه بعد SPACE را بزنید یا هرجا کلیک کنید تا آخر — در صفحه آخر می‌پرسیم چه ایرادهایی دیدید. برای خروج زودهنگام ESC را بزنید."), 15, FontWeight.Medium, faded)),
            begin,
            Centered(Kv.Text(StepText, 12, FontWeight.Medium, new SolidColorBrush(Colors.White, 0.5))));
        stack.VerticalAlignment = VerticalAlignment.Center;
        stack.Margin = new Thickness(40);
        return new Border { Background = new SolidColorBrush(Color.FromRgb(0x17, 0x14, 0x1C)), Child = stack };
    }

    private Control SolidSlide(Slide slide)
    {
        var label = SlideLabel(slide.Name, $"{slide.Hint} · {StepText}", slide.DarkLabel);
        FlashLabel(label);
        return new Panel { Background = new SolidColorBrush(slide.Color), Children = { label } };
    }

    private Control GradientSlide()
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,*,*,*"), FlowDirection = FlowDirection.LeftToRight };
        Color[] ends = { Colors.White, Color.FromRgb(255, 0, 0), Color.FromRgb(0, 255, 0), Color.FromRgb(0, 0, 255) };
        for (int i = 0; i < ends.Length; i++)
        {
            var band = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(ends[i], 1) },
                },
            };
            Grid.SetRow(band, i);
            grid.Children.Add(band);
        }
        var label = SlideLabel(T("Gradients", "گرادیان‌ها"),
            T($"smooth ramps — visible steps or bands mean poor color depth · {StepText}", $"شیب نرم — پله یا نوار قابل‌دیدن یعنی عمق رنگ ضعیف · {StepText}"), false);
        FlashLabel(label);
        Grid.SetRowSpan(label, 4);
        grid.Children.Add(label);
        return grid;
    }

    private Control RefreshSlide()
    {
        var square = new MovingSquare();
        _hzText = Kv.Hardware(_measuredHz is { } hz ? $"{Fmt.Fixed(hz, 0)} Hz" : "… Hz", 64, FontWeight.Black, Brushes.White, wrap: false);
        _hzText.HorizontalAlignment = HorizontalAlignment.Center;
        var text = Kv.Column(10,
            Kv.Text(T("REFRESH RATE CHECK", "بررسی نرخ تازه‌سازی"), 15, FontWeight.ExtraBold, Palette.Yellow),
            Kv.Text(T("Watch the square move — smoother, less blurry motion means a higher effective refresh rate. The number below is the refresh rate actually measured on this screen right now, not a value read from Windows settings.",
                      "حرکت مربع را تماشا کنید — حرکت نرم‌تر و کم‌تارتر یعنی نرخ تازه‌سازی مؤثر بالاتر. عدد زیر نرخ تازه‌سازی است که همین حالا روی این صفحه اندازه‌گیری می‌شود، نه مقداری از تنظیمات ویندوز."),
                15, FontWeight.Medium, Brushes.White),
            _hzText,
            Kv.Text(T("PRESS SPACE, CLICK OR TAP WHEN YOU'RE DONE", "وقتی تمام شد SPACE را بزنید یا کلیک کنید"), 13, FontWeight.ExtraBold, new SolidColorBrush(Colors.White, 0.7)),
            Kv.Text(StepText, 12, FontWeight.Medium, new SolidColorBrush(Colors.White, 0.5)));
        foreach (var t in text.Children.OfType<TextBlock>())
        {
            t.TextAlignment = TextAlignment.Center;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.MaxWidth = 720;
        }
        text.Margin = new Thickness(0, 60, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Top;
        text.IsHitTestVisible = false;

        // Preferred: time the monitor's own vertical blanks. Fallback: time our animation frames.
        _meter?.Dispose();
        _meter = new VBlankMeter(_window.ScreenPoint.X, _window.ScreenPoint.Y);
        _meter.Measured += hz => Dispatcher.UIThread.Post(() => SetHz(hz));
        if (!_meter.Start())
        {
            _meter = null;
            square.Measured += SetHz;
        }
        return new Panel { Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)), Children = { square, text } };
    }

    private void SetHz(double hz)
    {
        if (_closed) return;
        _measuredHz = hz;
        if (_hzText is not null) _hzText.Text = $"{Fmt.Fixed(hz, 0)} Hz";
    }

    private Control ChecklistSlide()
    {
        var stack = Kv.Column(16, Kv.SectionLabel(T("Did you notice any of these?", "هیچ‌کدام از این‌ها را دیدید؟"), Icon.List));
        var finish = new RetroButton(_issues.Count == 0 ? T("No issues — Finish Display Test", "بدون ایراد — پایان تست نمایشگر") : T("Finish Display Test", "پایان تست نمایشگر"),
            Icon.Check, ButtonKind.Primary, ButtonSize.Large, () => Finish(new DisplayTestResult
            {
                Issues = DisplayIssues.All.Where(_issues.Contains).ToList(),
                MeasuredRefreshRateHz = _measuredHz,
            }));
        foreach (var issue in DisplayIssues.All)
        {
            var i = issue;
            stack.Children.Add(new RetroCheckBox(i.Text(), _issues.Contains(i), on =>
            {
                if (on) _issues.Add(i);
                else _issues.Remove(i);
                Show();
            }));
        }
        if (_measuredHz is { } hz) stack.Children.Add(Kv.KeyValue(T("Measured refresh rate", "نرخ تازه‌سازی اندازه‌گیری‌شده"), $"{Fmt.Fixed(hz, 0)} Hz"));
        stack.Children.Add(Kv.Spread(new RetroButton(T("Back", "قبلی"), null, ButtonKind.Secondary, ButtonSize.Regular, Previous), finish));
        var card = Kv.Card(stack, padding: 26);
        card.MaxWidth = 720;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Background = Palette.Background, Child = card };
    }

    private void Finish(DisplayTestResult? result)
    {
        if (_closed) return;
        _closed = true;
        _labelTimer?.Stop();
        _meter?.Dispose();
        _meter = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (_window.IsVisible) _window.Close();
            _onDone(result);
        });
    }
}

/// <summary>A white square sliding across the screen once every 1.4 seconds, one step per rendered frame.</summary>
internal sealed class MovingSquare : Control
{
    private double _position;
    private double _direction = 1;
    private TimeSpan _last;
    private readonly Queue<double> _intervals = new();
    private TimeSpan _lastReport;
    private bool _running;

    /// <summary>Frame rate of the animation itself (fallback when the monitor can't be timed directly).</summary>
    public event Action<double>? Measured;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _running = true;
        Request();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _running = false;
    }

    private void Request() => TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);

    private void Frame(TimeSpan now)
    {
        if (!_running) return;
        double dt = 1.0 / 60;
        if (_last != TimeSpan.Zero)
        {
            dt = (now - _last).TotalSeconds;
            if (dt is > 0 and < 0.25)
            {
                _intervals.Enqueue(dt);
                if (_intervals.Count > 120) _intervals.Dequeue();
            }
        }
        _last = now;
        double size = Math.Max(40, Math.Min(Bounds.Width, Bounds.Height) * 0.12);
        double travel = Math.Max(1, Bounds.Width - size);
        _position += _direction * travel / 1.4 * Math.Min(dt, 0.1);
        if (_position >= travel) { _position = travel; _direction = -1; }
        if (_position <= 0) { _position = 0; _direction = 1; }
        if ((now - _lastReport).TotalSeconds > 0.5 && _intervals.Count >= 20)
        {
            _lastReport = now;
            double average = _intervals.Average();
            if (average > 0) Measured?.Invoke(1 / average);
        }
        InvalidateVisual();
        Request();
    }

    public override void Render(DrawingContext context)
    {
        double size = Math.Max(40, Math.Min(Bounds.Width, Bounds.Height) * 0.12);
        double y = Bounds.Height / 2 - size / 2 + Bounds.Height * 0.12;
        context.DrawRectangle(Brushes.White, null, new Rect(_position, y, size, size));
    }
}
