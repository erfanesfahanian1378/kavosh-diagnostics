using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kavosh.App.Ui;
using Kavosh.Core;
using Kavosh.Hardware;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Views;

/// <summary>Live CPU / memory / GPU load charts plus temperatures, power and fans.</summary>
public sealed class TelemetryPanel : UserControl, IDisposable
{
    private readonly LiveTelemetry _telemetry;
    private readonly bool _compact;

    private readonly HistoryChart _cpuChart = new() { Height = 54, Capacity = LiveTelemetry.Capacity };
    private readonly HistoryChart _ramChart = new() { Height = 54, Capacity = LiveTelemetry.Capacity };
    private readonly HistoryChart _gpuChart = new() { Height = 54, Capacity = LiveTelemetry.Capacity };
    private readonly BarStrip _cores = new() { Height = 22 };
    private readonly TextBlock _cpuValue, _ramValue, _gpuValue;
    private readonly Slot _pressure = new();
    private readonly TextBlock _cpuTemp, _gpuTemp, _power, _fan, _powerTitle;
    private readonly Slot _thermal = new();
    private readonly TextBlock _note;

    public TelemetryPanel(LiveTelemetry telemetry, bool compact)
    {
        _telemetry = telemetry;
        _compact = compact;
        _cpuChart.LineColor = Palette.AccentColor;
        _ramChart.LineColor = Palette.PurpleColor;
        _gpuChart.LineColor = Palette.TealColor;
        _cores.BarColor = Color.FromArgb(204, Palette.AccentColor.R, Palette.AccentColor.G, Palette.AccentColor.B);

        _cpuValue = Kv.Hardware("—", 14, FontWeight.ExtraBold, Palette.Accent, wrap: false);
        _ramValue = Kv.Hardware("—", 14, FontWeight.ExtraBold, Palette.Purple, wrap: false);
        _gpuValue = Kv.Hardware("—", 14, FontWeight.ExtraBold, Palette.Teal, wrap: false);

        var loads = Kv.Column(14,
            Kv.SectionLabel(T("Live resource usage", "مصرف زنده منابع"), Icon.Pulse),
            ChartBlock(T("CPU load — last 90s", "بار پردازنده — ۹۰ ثانیه اخیر"), _cpuValue, _cpuChart, compact ? null : _cores),
            ChartBlock(T("Memory load", "بار حافظه"), _ramValue, _ramChart, _pressure),
            ChartBlock(T("GPU load", "بار گرافیک"), _gpuValue, _gpuChart, null));

        _cpuTemp = Kv.Hardware("—", 16, FontWeight.ExtraBold, wrap: false);
        _gpuTemp = Kv.Hardware("—", 16, FontWeight.ExtraBold, wrap: false);
        _power = Kv.Hardware("—", 16, FontWeight.ExtraBold, wrap: false);
        _fan = Kv.Hardware("—", 16, FontWeight.ExtraBold, wrap: false);
        _powerTitle = Kv.Text(T("Power", "توان"), 10, FontWeight.Bold, Palette.Muted, wrap: false);
        _note = Kv.Text("", 11, FontWeight.Medium, Palette.Muted);
        var cells = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        AddCell(cells, 0, 0, Kv.Text(T("CPU temperature", "دمای پردازنده"), 10, FontWeight.Bold, Palette.Muted, wrap: false), _cpuTemp);
        AddCell(cells, 0, 1, Kv.Text(T("GPU temperature", "دمای گرافیک"), 10, FontWeight.Bold, Palette.Muted, wrap: false), _gpuTemp);
        AddCell(cells, 1, 0, _powerTitle, _power);
        AddCell(cells, 1, 1, Kv.Text(T("Fans", "فن‌ها"), 10, FontWeight.Bold, Palette.Muted, wrap: false), _fan);

        var sensors = Kv.Column(12,
            Kv.SectionLabel(T("Live telemetry", "حسگرهای زنده"), Icon.Thermometer),
            cells,
            Kv.Spread(Kv.Text(T("Thermal state", "وضعیت حرارتی"), 11, FontWeight.Bold, Palette.Muted), _thermal),
            _note);

        Content = Kv.Column(compact ? 12 : 16, Kv.Card(loads, padding: 14), Kv.Card(sensors, padding: 14));
        _telemetry.Updated += Update;
        Update();
    }

    public void Dispose() => _telemetry.Updated -= Update;

    private static Control ChartBlock(string title, TextBlock value, HistoryChart chart, Control? extra)
    {
        var frame = new Border
        {
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = Palette.WithOpacity(Palette.SurfaceAltColor, 0.6),
            BorderBrush = Palette.WithOpacity(Palette.InkColor, 0.3),
            BorderThickness = new Thickness(1),
            Child = chart,
        };
        return Kv.Column(6, Kv.Spread(Kv.Text(title, 11, FontWeight.Bold, Palette.Muted), value), frame, extra);
    }

    private static void AddCell(Grid grid, int row, int column, TextBlock title, TextBlock value)
    {
        var viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = Loc.Language.IsRightToLeft() ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = value,
        };
        var cell = Kv.Inset(Kv.Column(3, title, viewbox));
        cell.Margin = new Thickness(column == 0 ? 0 : 5, row == 0 ? 0 : 10, column == 0 ? 5 : 0, 0);
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void Update()
    {
        var s = _telemetry.Latest;
        _cpuChart.Values = _telemetry.CpuHistory.ToArray();
        _ramChart.Values = _telemetry.RamHistory.ToArray();
        _gpuChart.Values = _telemetry.GpuHistory.ToArray();
        if (s is null) return;

        _cpuValue.Text = Fmt.Percent(s.CpuLoadPercent);
        _ramValue.Text = $"{Fmt.Fixed(s.RamUsedGB, 1)} / {Fmt.Fixed(s.RamTotalGB, 0)} GB";
        _gpuValue.Text = s.GpuLoadPercent is { } gpu ? Fmt.Percent(gpu) : "—";
        _cores.Values = s.PerCoreLoadPercent.ToArray();
        var pressureColor = s.MemoryPressure switch
        {
            MemoryPressureLevel.Normal => Palette.GreenColor,
            MemoryPressureLevel.Warning => Palette.YellowColor,
            _ => Palette.RedColor,
        };
        if (_pressure.Tag as MemoryPressureLevel? != s.MemoryPressure)
        {
            _pressure.Tag = s.MemoryPressure;
            _pressure.Content = Kv.Badge(s.MemoryPressure.Title(), pressureColor, Palette.OnYellow);
        }

        _cpuTemp.Text = Fmt.Celsius(s.CpuTemperatureCelsius);
        _cpuTemp.Foreground = new SolidColorBrush(Palette.TemperatureColor(s.CpuTemperatureCelsius));
        _gpuTemp.Text = Fmt.Celsius(s.GpuTemperatureCelsius);
        _gpuTemp.Foreground = new SolidColorBrush(Palette.TemperatureColor(s.GpuTemperatureCelsius));
        if (s.SystemPowerWatts is { } system)
        {
            _powerTitle.Text = T("System power (battery)", "توان سیستم (باتری)");
            _power.Text = Fmt.Watts(system);
        }
        else
        {
            _powerTitle.Text = T("CPU power", "توان پردازنده");
            _power.Text = Fmt.Watts(s.CpuPowerWatts);
        }
        _fan.Text = s.FanRpm.Count > 0 ? string.Join(" / ", s.FanRpm.Take(3).Select(r => $"{Fmt.Number(r, 0)} rpm")) : "—";

        if (_thermal.Tag as ThermalLevel? != s.ThermalState)
        {
            _thermal.Tag = s.ThermalState;
            _thermal.Content = Kv.Badge(s.ThermalState.Title(), Palette.ThermalColor(s.ThermalState), Palette.OnYellow);
        }

        var status = _telemetry.Status;
        string note = "";
        if (status is not null && Platform.IsWindows && !status.FullAccess)
        {
            note = !status.IsAdministrator
                ? T("Temperatures, fans and CPU power need administrator rights. Click “Limited sensors” at the top to restart as administrator.",
                    "دما، فن و توان پردازنده به دسترسی مدیر نیاز دارند. برای اجرای دوباره با دسترسی مدیر، روی «حسگرهای محدود» در بالا بزنید.")
                : !status.PawnIoInstalled
                    ? T("CPU temperature and power need the PawnIO driver. Click “Limited sensors” at the top for details.",
                        "دما و توان پردازنده به درایور PawnIO نیاز دارند. برای جزئیات روی «حسگرهای محدود» در بالا بزنید.")
                    : "";
        }
        else if (!Platform.IsWindows)
        {
            note = T("Hardware sensors are read on Windows only.", "حسگرهای سخت‌افزاری فقط در ویندوز خوانده می‌شوند.");
        }
        if (s.CpuTemperatureCelsius is null && note.Length == 0 && Platform.IsWindows)
            note = T("This PC's CPU doesn't expose its temperature to Windows.", "پردازنده این رایانه دمای خود را به ویندوز گزارش نمی‌دهد.");
        _note.Text = note;
        _note.IsVisible = note.Length > 0;
    }
}
