using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Kavosh.App.Ui;
using Kavosh.App.Views;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Stations;

public sealed class InputStation : Station
{
    private enum Step { Typing, Keys, Result }

    private readonly MainWindow _window;
    private Step _step = Step.Typing;
    private string _typed = "";
    private KeyPressTracker _tracker = new();
    private bool _capturing;
    private bool _moved, _clicked, _scrolled;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DateTime _lastAltF4;

    // Step 2 live controls.
    private readonly Dictionary<string, KeyCap> _caps = new();
    private readonly RetroProgressBar _keysBar = new(14, Palette.GreenColor);
    private readonly TextBlock _keysCount = Kv.Hardware("0 / 0", 14, FontWeight.ExtraBold, wrap: false);
    private readonly TextBlock _lastKey = Kv.Text("", 13, FontWeight.SemiBold, wrap: false);

    public InputStation(TestSuiteRunner runner, MainWindow window) : base(runner)
    {
        _window = window;
        Render();
    }

    protected override void Start()
    {
        _window.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.Deactivated += OnDeactivated;
        _window.Closing += OnClosing;
    }

    protected override void Stop()
    {
        _capturing = false;
        _window.RemoveHandler(KeyDownEvent, OnKeyDown);
        _window.RemoveHandler(KeyUpEvent, OnKeyUp);
        _window.Deactivated -= OnDeactivated;
        _window.Closing -= OnClosing;
    }

    // ── Key capture ──

    private static string CodeOf(KeyEventArgs e) => e.PhysicalKey != PhysicalKey.None ? e.PhysicalKey.ToString() : e.Key.ToString();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        if (e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) _lastAltF4 = DateTime.UtcNow;
        Press(CodeOf(e));
        // Swallow keys so shortcuts (Tab, Alt, Enter on buttons…) don't fire during the test.
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        string code = CodeOf(e);
        // Windows only reports Print Screen when it is released.
        if (code == "PrintScreen" && !_tracker.IsPressed(code)) Press(code);
        _tracker.KeyUp(code);
        e.Handled = true;
    }

    private void Press(string code)
    {
        _tracker.KeyDown(code, _clock.Elapsed.TotalSeconds);
        if (_caps.TryGetValue(code, out var cap)) cap.SetState(_tracker.IsPressed(code), _tracker.DoublePressed.Contains(code));
        UpdateKeyProgress();
    }

    private void OnDeactivated(object? sender, EventArgs e) => _tracker.ReleaseAll();

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Alt+F4 pressed while testing keys shouldn't close the app.
        if (_capturing && (DateTime.UtcNow - _lastAltF4).TotalSeconds < 1) e.Cancel = true;
    }

    // ── Steps ──

    private void Render()
    {
        if (Runner.Report.HasNoBuiltInInputs)
        {
            Content = Kv.Column(18,
                Kv.Card(Kv.Column(8, Kv.SectionLabel(T("Keyboard / touchpad", "کیبورد / تاچ‌پد"), Icon.Keyboard),
                    Kv.Text(T("Skipped on a PC without a built-in keyboard/touchpad — not counted as a failure and not scored.",
                              "روی رایانه‌ای بدون کیبورد/تاچ‌پد داخلی رد شد — خرابی حساب نمی‌شود و امتیازی ندارد."), 14))),
                new ContinueBar(Runner.IsLastStation, 3, () => Runner.Complete(TestModule.Input, r => r.Keyboard = new KeyboardTestResult { Bypassed = true })));
            return;
        }
        Content = _step switch
        {
            Step.Typing => TypingStep(),
            Step.Keys => KeysStep(),
            _ => ResultStep(),
        };
    }

    private Control TypingStep()
    {
        var prompt = new TextBlock
        {
            FontFamily = Palette.Mono,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = FlowDirection.LeftToRight,
        };
        var promptBox = new Border { Background = Palette.SurfaceAlt, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = prompt, FlowDirection = FlowDirection.LeftToRight };
        var status = new Slot();
        var field = new TextBox
        {
            Text = _typed,
            Watermark = T("Type the sentence here", "جمله را اینجا تایپ کنید"),
            FontFamily = Palette.Mono,
            FontSize = 20,
            FontWeight = FontWeight.Medium,
            FlowDirection = FlowDirection.LeftToRight,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Palette.Ink,
            Padding = new Thickness(4),
        };
        foreach (string key in new[] { "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver", "TextControlBorderBrush" })
            field.Resources[key] = Brushes.Transparent;
        foreach (string key in new[] { "TextControlBackgroundFocused", "TextControlBackgroundPointerOver", "TextControlBackground" })
            field.Resources[key] = Brushes.Transparent;
        field.Resources["TextControlForegroundFocused"] = Palette.Ink;
        field.Resources["TextControlForegroundPointerOver"] = Palette.Ink;
        var fieldBox = Kv.Surface(field, Palette.Surface, 8, 3, new Thickness(8));
        void Update()
        {
            _typed = field.Text ?? "";
            var wrong = TypingPrompt.MismatchedIndices(_typed);
            prompt.Inlines = new InlineCollection();
            for (int i = 0; i < TypingPrompt.Sentence.Length; i++)
            {
                var color = wrong.Contains(i) ? Palette.Red : i < _typed.Length ? Palette.Green : Palette.Ink;
                prompt.Inlines.Add(new Run(TypingPrompt.Sentence[i].ToString()) { Foreground = color });
            }
            status.Content = _typed.Length == 0 ? null
                : TypingPrompt.Matches(_typed)
                    ? Kv.Label(T("Matches", "مطابقت دارد"), Icon.Check, Palette.Green)
                    : Kv.Label(T($"{_typed.Length} of {TypingPrompt.Sentence.Length} characters", $"{_typed.Length} از {TypingPrompt.Sentence.Length} نویسه"), Icon.Keyboard, Palette.Muted);
        }
        field.TextChanged += (_, _) => Update();
        field.AttachedToVisualTree += (_, _) => Avalonia.Threading.DispatcherTimer.RunOnce(() => field.Focus(), TimeSpan.FromMilliseconds(200));
        Update();
        var next = new RetroButton(T("Next — check every key", "بعدی — بررسی همه کلیدها"), Loc.Language.IsRightToLeft() ? Icon.ArrowLeft : Icon.ArrowRight,
            ButtonKind.Primary, ButtonSize.Regular, BeginKeyCapture);
        return Kv.Card(Kv.Column(14,
            Kv.SectionLabel(T("Step 1 of 3 — type this sentence", "مرحله ۱ از ۳ — این جمله را تایپ کنید"), Icon.Keyboard),
            promptBox, fieldBox, Kv.Spread(status, next)));
    }

    private void BeginKeyCapture()
    {
        _tracker = new KeyPressTracker();
        _step = Step.Keys;
        Render();
        _capturing = true;
        _window.Focus();
    }

    private Control KeysStep()
    {
        _caps.Clear();
        var rows = new StackPanel { Spacing = 5, FlowDirection = FlowDirection.LeftToRight };
        foreach (var row in PcKeyboardLayout.Rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            foreach (var key in row)
            {
                var cap = new KeyCap(key);
                _caps[key.Code] = cap;
                line.Children.Add(cap);
            }
            rows.Children.Add(line);
        }
        var keyboard = new Border
        {
            Background = Palette.SurfaceAlt,
            BorderBrush = Palette.Ink,
            BorderThickness = new Thickness(Palette.Border),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = rows,
            FlowDirection = FlowDirection.LeftToRight,
        };
        var scale = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = keyboard, HorizontalAlignment = HorizontalAlignment.Left };
        UpdateKeyProgress();
        var progressRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        progressRow.Children.Add(_keysBar);
        _keysCount.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_keysCount, 1);
        progressRow.Children.Add(_keysCount);
        _lastKey.FlowDirection = FlowDirection.LeftToRight;
        return Kv.Card(Kv.Column(14,
            Kv.SectionLabel(T("Step 2 of 3 — press every key on your keyboard", "مرحله ۲ از ۳ — همه کلیدهای کیبورد را بزنید"), Icon.Keyboard),
            Kv.Text(T("Press each key once. It lights up when it registers. Function keys, Print Screen, fn and the right Ctrl (a Copilot key on newer laptops) are optional — hold fn if the top row controls brightness and volume. The Windows key may open the Start menu: just click back here.",
                      "هر کلید را یک بار بزنید. وقتی ثبت شود روشن می‌شود. کلیدهای تابعی، Print Screen، fn و Ctrl راست (در لپ‌تاپ‌های جدید کلید Copilot) اختیاری‌اند — اگر ردیف بالا روشنایی و صدا را کنترل می‌کند fn را نگه دارید. کلید ویندوز ممکن است منوی Start را باز کند: فقط دوباره روی همین‌جا کلیک کنید."),
                13),
            scale, progressRow,
            Kv.Spread(_lastKey, new RetroButton(T("Finish key check", "پایان بررسی کلیدها"), Icon.Check, ButtonKind.Primary, ButtonSize.Regular, FinishKeys))));
    }

    private void UpdateKeyProgress()
    {
        var (pressed, total) = _tracker.RequiredProgress;
        _keysBar.Value = (double)pressed / Math.Max(1, total);
        _keysCount.Text = $"{pressed} / {total}";
        _lastKey.Text = T($"Last key: {_tracker.LastKeyLabel ?? "—"}", $"آخرین کلید: {_tracker.LastKeyLabel ?? "—"}");
    }

    private void FinishKeys()
    {
        _capturing = false;
        _step = Step.Result;
        Render();
    }

    private KeyboardTestResult Result() => _tracker.Result(TypingPrompt.Matches(_typed), _moved, _clicked, _scrolled);

    private Control ResultStep()
    {
        var result = Result();
        var stack = Kv.Column(12, Kv.SectionLabel(T("Step 3 of 3 — keyboard result", "مرحله ۳ از ۳ — نتیجه کیبورد"), Icon.List));
        if (result.MissingKeys.Count == 0 && result.DoublePressedKeys.Count == 0)
            stack.Children.Add(Kv.Label(T("No missing or double-pressed keys.", "هیچ کلید ازکارافتاده یا دوبارثبت‌شده‌ای نیست."), Icon.Seal, Palette.Green, 15));
        if (result.MissingKeys.Count > 0)
        {
            stack.Children.Add(Kv.Text(T($"Keys that didn't register ({result.MissingKeys.Count}):", $"کلیدهایی که ثبت نشدند ({result.MissingKeys.Count}):"), 13, FontWeight.Bold));
            stack.Children.Add(Kv.Flow(6, result.MissingKeys.Select(k => (Control)Kv.Badge(k, Palette.RedColor, Brushes.White)).ToArray()));
        }
        if (result.DoublePressedKeys.Count > 0)
        {
            stack.Children.Add(Kv.Text(T("Keys that registered twice for one press:", "کلیدهایی که با یک فشار دوبار ثبت شدند:"), 13, FontWeight.Bold));
            stack.Children.Add(Kv.Flow(6, result.DoublePressedKeys.Select(k => (Control)Kv.Badge(k, Palette.OrangeColor, Palette.OnYellow)).ToArray()));
        }
        if (!result.SentenceCorrect)
            stack.Children.Add(Kv.Label(T("The typed sentence didn't match the prompt.", "جمله تایپ‌شده با متن مطابقت نداشت."), Icon.Warning, Palette.Orange));
        stack.Children.Add(new RetroButton(T("Check keys again", "بررسی دوباره کلیدها"), null, ButtonKind.Ghost, ButtonSize.Small, BeginKeyCapture));

        var badges = new Slot();
        var detected = Kv.Text("", 12, FontWeight.SemiBold, wrap: false);
        void UpdateBadges()
        {
            Control Badge(string text, bool on) => Kv.Badge(text, on ? Palette.GreenColor : Palette.SurfaceAltColor, on ? Brushes.White : Palette.Ink, on ? Icon.Check : Icon.Dot);
            badges.Content = Kv.Row(8, Badge(T("Move", "حرکت"), _moved), Badge(T("Click", "کلیک"), _clicked), Badge(T("Scroll", "اسکرول"), _scrolled));
            int passed = (_moved ? 1 : 0) + (_clicked ? 1 : 0) + (_scrolled ? 1 : 0);
            detected.Text = T($"Movement, click and scroll detected: {passed} of 3", $"حرکت، کلیک و اسکرول تشخیص داده شد: {passed} از ۳");
        }
        var area = new TouchpadArea { Height = 170 };
        area.Detected += kind =>
        {
            switch (kind)
            {
                case 0: _moved = true; break;
                case 1: _clicked = true; break;
                default: _scrolled = true; break;
            }
            UpdateBadges();
        };
        UpdateBadges();
        var touchpad = Kv.Card(Kv.Column(12,
            Kv.SectionLabel(T("Touchpad — move, click and scroll inside this area", "تاچ‌پد — داخل این ناحیه حرکت، کلیک و اسکرول کنید"), Icon.Mouse),
            area, Kv.Spread(badges, detected)));

        var next = new RetroButton(Runner.IsLastStation ? T("Finish", "پایان") : T("Next test", "تست بعدی"),
            Loc.Language.IsRightToLeft() ? Icon.ArrowLeft : Icon.ArrowRight, ButtonKind.Primary, ButtonSize.Large, () =>
            {
                var final = Result();
                Runner.Complete(TestModule.Input, r => r.Keyboard = final);
            });
        return Kv.Column(18, Kv.Card(stack), touchpad, Kv.Spread(new Panel(), next));
    }
}

/// <summary>One key on the on-screen keyboard: lights up green when pressed, orange when it chattered.</summary>
internal sealed class KeyCap : Control
{
    private const double Unit = 40, Gap = 5;
    private readonly KeyboardKey _key;
    private bool _pressed, _doubled;

    public KeyCap(KeyboardKey key)
    {
        _key = key;
        Width = Unit * key.Width + Gap * (key.Width - 1);
        Height = Unit;
    }

    public void SetState(bool pressed, bool doubled)
    {
        _pressed = pressed;
        _doubled = doubled;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size).Deflate(0.75);
        IBrush fill = _doubled ? Palette.Orange : _pressed ? Palette.Green : _key.Optional ? Palette.SurfaceAlt : Palette.Surface;
        var pen = new Pen(_key.Optional ? Palette.WithOpacity(Palette.InkColor, 0.4) : Palette.Ink, 1.5,
            _key.Optional ? new DashStyle(new double[] { 2, 1.4 }, 0) : null);
        context.DrawRectangle(fill, pen, new RoundedRect(rect, 7));
        var text = new FormattedText(_key.Label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Palette.Numbers, FontStyle.Normal, FontWeight.Bold), _key.Label.Length > 2 ? 10 : 13,
            _pressed ? Brushes.White : Palette.Ink);
        context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
    }
}

/// <summary>Area that notices pointer movement (0), clicks (1) and scrolling (2).</summary>
internal sealed class TouchpadArea : Control
{
    private Point? _pointer;

    public event Action<int>? Detected;

    public TouchpadArea()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        FlowDirection = FlowDirection.LeftToRight;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer = e.GetPosition(this);
        Detected?.Invoke(0);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Detected?.Invoke(1);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Math.Abs(e.Delta.Y) > 0.01 || Math.Abs(e.Delta.X) > 0.01) Detected?.Invoke(2);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size).Deflate(1);
        context.DrawRectangle(Palette.Surface, new Pen(Palette.WithOpacity(Palette.InkColor, 0.6), 2, new DashStyle(new double[] { 3, 2 }, 0)),
            new RoundedRect(rect, 10));
        if (_pointer is { } p) context.DrawEllipse(Palette.Accent, null, p, 7, 7);
    }
}
