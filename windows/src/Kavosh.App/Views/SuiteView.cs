using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kavosh.App.Stations;
using Kavosh.App.Ui;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Views;

public sealed class SuiteView : UserControl, IDisposable
{
    private readonly TestSuiteRunner _runner;
    private readonly MainWindow _window;
    private readonly Slot _header = new();
    private readonly Slot _station = new();
    private readonly Slot _list = new();
    private readonly TelemetryPanel _telemetry;

    public SuiteView(TestSuiteRunner runner, MainWindow window)
    {
        _runner = runner;
        _window = window;
        _telemetry = new TelemetryPanel(runner.Telemetry, compact: true);

        var left = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        _header.Margin = new Thickness(0, 0, 0, 18);
        left.Children.Add(_header);
        left.Children.Add(Kv.Scroll(_station, new Thickness(0, 0, 8, 8)));

        var right = Kv.Scroll(Kv.Column(16, _list, _telemetry), new Thickness(0, 0, 8, 8));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,300"), Margin = new Thickness(24) };
        grid.Children.Add(left);
        right.Margin = new Thickness(22, 0, 0, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;

        _runner.Changed += Refresh;
        Refresh();
    }

    public void Dispose()
    {
        _runner.Changed -= Refresh;
        (_station.Content as IDisposable)?.Dispose();
        _telemetry.Dispose();
    }

    private void Refresh()
    {
        (_station.Content as IDisposable)?.Dispose();
        _station.Content = _runner.Current switch
        {
            TestModule.Cpu => new CpuStation(_runner),
            TestModule.Gpu => new GpuStation(_runner),
            TestModule.Memory => new MemoryStation(_runner),
            TestModule.Battery => new BatteryStation(_runner),
            TestModule.Display => new DisplayStation(_runner, _window),
            TestModule.Speakers => new AudioStation(_runner),
            TestModule.MicCamera => new MicCameraStation(_runner),
            TestModule.Input => new InputStation(_runner, _window),
            TestModule.Drives => new DriveStation(_runner),
            _ => new BusySpinner(24),
        };
        _header.Content = Header();
        _list.Content = StationList();
    }

    private Control Header()
    {
        if (_runner.Current is not { } module) return new Panel();
        var icon = Kv.Surface(Icons.Make(Icons.For(module), 26, Palette.OnYellow), Palette.Yellow, 12, 3);
        icon.Width = 57;
        icon.Height = 57;
        var text = Kv.Column(4,
            Kv.SectionLabel(_runner.IsSingleTest
                ? T("Individual test", "تست جداگانه")
                : T($"Test suite · step {_runner.StepNumber} of {_runner.Plan.Count}", $"مجموعه تست · مرحله {_runner.StepNumber} از {_runner.Plan.Count}")),
            Kv.Text(module.Title(), 26, FontWeight.ExtraBold),
            Kv.Text(module.Subtitle(), 13, FontWeight.Medium, Palette.Muted));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(icon);
        text.Margin = new Thickness(16, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var stop = new RetroButton(T("Stop Test", "توقف تست"), Icon.Stop, ButtonKind.Danger, ButtonSize.Regular, ConfirmStop);
        Grid.SetColumn(stop, 2);
        grid.Children.Add(stop);
        return grid;
    }

    private async void ConfirmStop()
    {
        int choice = await _window.Dialog(T("Stop the test now?", "تست الان متوقف شود؟"),
            T("The current station's resources (camera, mic, memory buffer, CPU load) are released immediately. Stations that already finished are kept, and you can resume the rest later from the Dashboard.",
              "منابع ایستگاه فعلی (دوربین، میکروفون، حافظه، بار پردازنده) فوراً آزاد می‌شوند. ایستگاه‌های تمام‌شده حفظ می‌شوند و می‌توانید بقیه را بعداً از داشبورد ادامه دهید."),
            new[] { (T("Stop Test", "توقف تست"), ButtonKind.Danger), (T("Keep testing", "ادامه تست"), ButtonKind.Secondary) });
        if (choice != 0) return;
        (_station.Content as IDisposable)?.Dispose();
        _station.Content = null;
        _runner.StopNow();
    }

    private Control StationList()
    {
        var stack = Kv.Column(10, Kv.SectionLabel(T("Stations", "ایستگاه‌ها"), Icon.List));
        foreach (var module in _runner.Plan)
        {
            var state = _runner.StateOf(module);
            var icon = Kv.StatusIcon(state switch
            {
                StationState.Done => StatusState.Pass,
                StationState.Failed => StatusState.Fail,
                StationState.Skipped => StatusState.Skipped,
                _ => StatusState.Pending,
            });
            bool current = module == _runner.Current;
            var title = Kv.Text(module.Title(), 12, current ? FontWeight.ExtraBold : FontWeight.Medium, current ? Palette.Ink : Palette.Muted);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            row.Children.Add(icon);
            title.Margin = new Thickness(10, 0);
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            if (current)
            {
                var spinner = new BusySpinner(14);
                Grid.SetColumn(spinner, 2);
                row.Children.Add(spinner);
            }
            stack.Children.Add(row);
        }
        if (_runner.Resumed)
        {
            stack.Children.Add(Kv.Text(T("Resumed — stations finished in the interrupted run are kept.",
                                         "ادامه داده شد — ایستگاه‌های تمام‌شده در اجرای قبلی حفظ شده‌اند."), 11, FontWeight.Medium, Palette.Muted));
        }
        return Kv.Card(stack, padding: 14);
    }
}

/// <summary>
/// Shown when a station is done: a "Next" button, and for automatic stations a short countdown
/// that continues by itself.
/// </summary>
public sealed class ContinueBar : UserControl
{
    private readonly Action _action;
    private readonly TextBlock _countdown;
    private readonly RetroButton _wait;
    private DispatcherTimer? _timer;
    private int _remaining;
    private bool _fired;

    public ContinueBar(bool isLast, int? autoAdvanceSeconds, Action action)
    {
        _action = action;
        _countdown = Kv.Text("", 12, FontWeight.Medium, Palette.Muted, wrap: false);
        _wait = new RetroButton(T("Wait", "صبر کن"), null, ButtonKind.Ghost, ButtonSize.Small, StopCountdown);
        var next = new RetroButton(isLast ? T("See results", "مشاهده نتایج") : T("Next test", "تست بعدی"),
            isLast ? Icon.Seal : (Loc.Language.IsRightToLeft() ? Icon.ArrowLeft : Icon.ArrowRight), ButtonKind.Primary, ButtonSize.Large, Fire);
        Content = Kv.Spread(Kv.Row(12, _countdown, _wait), next);
        _remaining = autoAdvanceSeconds ?? 0;
        UpdateText();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_remaining <= 0) return;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) =>
        {
            _remaining--;
            UpdateText();
            if (_remaining <= 0) Fire();
        });
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopCountdown();
    }

    private void UpdateText()
    {
        bool counting = _remaining > 0;
        _countdown.IsVisible = counting;
        _wait.IsVisible = counting;
        _countdown.Text = T($"Continuing in {_remaining} s…", $"ادامه در {_remaining} ثانیه…");
    }

    private void StopCountdown()
    {
        _timer?.Stop();
        _timer = null;
        _remaining = 0;
        UpdateText();
    }

    private void Fire()
    {
        if (_fired) return;
        _fired = true;
        StopCountdown();
        _action();
    }
}

/// <summary>Error box with a way to move on.</summary>
public static class StationError
{
    public static Control Make(string title, string message, Action onContinue)
    {
        return Kv.Card(Kv.Column(12,
            Kv.SectionLabel(T("The test couldn't run", "تست اجرا نشد"), Icon.Warning, Palette.Red),
            Kv.Text(title, 16, FontWeight.Bold),
            Kv.Text(message, 13, FontWeight.Medium),
            Kv.Spread(new Panel(), new RetroButton(T("Continue", "ادامه"), null, ButtonKind.Primary, ButtonSize.Regular, onContinue))),
            Palette.WithOpacity(Palette.RedColor, 0.12));
    }
}
