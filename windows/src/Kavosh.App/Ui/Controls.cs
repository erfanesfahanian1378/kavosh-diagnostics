using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Kavosh.App.Ui;

public enum ButtonKind { Primary, Secondary, Danger, Warning, Success, Ghost }

public enum ButtonSize { Small, Regular, Large }

/// <summary>Button with an ink outline and a hard shadow that it "presses into".</summary>
public sealed class RetroButton : Button
{
    private Border? _body;
    private readonly double _offset;
    private readonly ButtonKind _kind;

    protected override Type StyleKeyOverride => typeof(RetroButton);

    public RetroButton(string? text, Icon? icon = null, ButtonKind kind = ButtonKind.Secondary, ButtonSize size = ButtonSize.Regular,
        Action? onClick = null, bool fullWidth = false)
    {
        _kind = kind;
        _offset = size == ButtonSize.Small ? 2 : 3;
        Cursor = new Cursor(StandardCursorType.Hand);
        Background = Brushes.Transparent;
        HorizontalAlignment = fullWidth ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = true;
        if (onClick is not null) Click += (_, _) => onClick();

        double fontSize = size switch { ButtonSize.Small => 12, ButtonSize.Large => 17, _ => 14 };
        var weight = size == ButtonSize.Large ? FontWeight.ExtraBold : FontWeight.Bold;
        var padding = size switch
        {
            ButtonSize.Small => new Thickness(10, 5),
            ButtonSize.Large => new Thickness(22, 13),
            _ => new Thickness(16, 8),
        };
        var foreground = kind switch
        {
            ButtonKind.Primary or ButtonKind.Danger or ButtonKind.Success => Palette.OnAccent,
            ButtonKind.Warning => Palette.OnYellow,
            _ => Palette.Ink,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
        if (icon is { } i) row.Children.Add(Icons.Make(i, fontSize + 1, foreground, 2.6));
        if (!string.IsNullOrEmpty(text))
        {
            row.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = Palette.Text,
                FontSize = fontSize,
                FontWeight = weight,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
            });
        }
        var fill = kind switch
        {
            ButtonKind.Primary => Palette.Accent,
            ButtonKind.Danger => Palette.Red,
            ButtonKind.Warning => Palette.Yellow,
            ButtonKind.Success => Palette.Green,
            ButtonKind.Ghost => Palette.SurfaceAlt,
            _ => Palette.Surface,
        };

        Template = new FuncControlTemplate<RetroButton>((_, _) =>
        {
            _body = new Border
            {
                Background = fill,
                BorderBrush = Palette.Ink,
                BorderThickness = new Thickness(Palette.Border),
                CornerRadius = new CornerRadius(10),
                Padding = padding,
                Child = row,
            };
            UpdateVisual();
            return _body;
        });
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPressedProperty || change.Property == IsEnabledProperty || change.Property == IsPointerOverProperty)
            UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_body is null) return;
        bool pressed = IsPressed && IsEnabled;
        double shadow = pressed ? 0 : (IsPointerOver && IsEnabled ? _offset + 1 : _offset);
        _body.BoxShadow = new BoxShadows(new BoxShadow { OffsetX = shadow, OffsetY = shadow, Color = Palette.ShadowColor });
        _body.RenderTransform = pressed ? new TranslateTransform(_offset, _offset) : null;
        Opacity = IsEnabled ? 1 : 0.45;
    }
}

/// <summary>Checkbox in the retro look: a square box and a wrapping label.</summary>
public sealed class RetroCheckBox : Button
{
    private bool _isChecked;
    private Border? _box;
    private Control? _mark;

    public event Action<bool>? CheckedChanged;

    protected override Type StyleKeyOverride => typeof(RetroCheckBox);

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            _isChecked = value;
            UpdateVisual();
        }
    }

    public RetroCheckBox(string text, bool isChecked, Action<bool>? changed = null)
    {
        _isChecked = isChecked;
        Cursor = new Cursor(StandardCursorType.Hand);
        Background = Brushes.Transparent;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        if (changed is not null) CheckedChanged += changed;
        Click += (_, _) =>
        {
            IsChecked = !IsChecked;
            CheckedChanged?.Invoke(IsChecked);
        };
        Template = new FuncControlTemplate<RetroCheckBox>((_, _) =>
        {
            _mark = Icons.Make(Icon.Check, 14, Palette.OnAccent, 3.2);
            _box = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(5),
                BorderBrush = Palette.Ink,
                BorderThickness = new Thickness(Palette.Border),
                Child = _mark,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var label = Kv.Text(text, 13, FontWeight.Medium);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Background = Brushes.Transparent };
            grid.Children.Add(_box);
            Grid.SetColumn(label, 1);
            label.Margin = new Thickness(10, 0, 0, 0);
            grid.Children.Add(label);
            UpdateVisual();
            return grid;
        });
    }

    private void UpdateVisual()
    {
        if (_box is null || _mark is null) return;
        _box.Background = _isChecked ? Palette.Accent : Palette.Surface;
        _mark.Opacity = _isChecked ? 1 : 0;
    }
}

/// <summary>Rounded progress bar with an ink outline; can also run as an indeterminate "busy" bar.</summary>
public sealed class RetroProgressBar : Control
{
    private double _value;
    private double _phase;
    private DispatcherTimer? _timer;

    public Color BarColor { get; set; } = Palette.AccentColor;
    public bool Indeterminate { get; set; }

    public double Value
    {
        get => _value;
        set
        {
            double v = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (Math.Abs(v - _value) < 0.0005) return;
            _value = v;
            InvalidateVisual();
        }
    }

    public RetroProgressBar(double height = 14, Color? color = null)
    {
        Height = height;
        BarColor = color ?? Palette.AccentColor;
        FlowDirection = FlowDirection.LeftToRight;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!Indeterminate) return;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
        {
            _phase = (_phase + 0.025) % 2;
            InvalidateVisual();
        });
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        double radius = bounds.Height / 2;
        var outer = new RoundedRect(bounds, radius);
        context.DrawRectangle(Palette.SurfaceAlt, null, outer);
        using (context.PushClip(outer))
        {
            var fill = new SolidColorBrush(BarColor);
            if (Indeterminate)
            {
                double t = _phase <= 1 ? _phase : 2 - _phase;
                double eased = t * t * (3 - 2 * t);
                double w = bounds.Width * 0.3;
                context.DrawRectangle(fill, null, new RoundedRect(new Rect(eased * (bounds.Width - w), 0, w, bounds.Height), radius));
            }
            else if (_value > 0)
            {
                double w = Math.Max(bounds.Height, bounds.Width * _value);
                context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 0, Math.Min(w, bounds.Width), bounds.Height), radius));
            }
        }
        var inset = bounds.Deflate(Palette.Border / 2);
        context.DrawRectangle(null, new Pen(Palette.Ink, Palette.Border), new RoundedRect(inset, Math.Max(0, radius - Palette.Border / 2)));
    }
}

/// <summary>A simple area chart of the last N samples, newest on the right.</summary>
public sealed class HistoryChart : Control
{
    private IReadOnlyList<double> _values = Array.Empty<double>();

    public double MaxValue { get; set; } = 100;
    public int Capacity { get; set; } = 90;
    public Color LineColor { get; set; } = Palette.AccentColor;
    public bool ShowGrid { get; set; } = true;

    public IReadOnlyList<double> Values
    {
        get => _values;
        set
        {
            _values = value;
            InvalidateVisual();
        }
    }

    public HistoryChart()
    {
        FlowDirection = FlowDirection.LeftToRight;
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        if (ShowGrid)
        {
            var dashed = new Pen(Palette.Grid, 1, new DashStyle(new double[] { 3, 4 }, 0));
            for (int i = 1; i < 4; i++)
            {
                double y = Math.Round(h * i / 4) + 0.5;
                context.DrawLine(dashed, new Point(0, y), new Point(w, y));
            }
        }
        var values = _values;
        if (values.Count < 2) return;
        double top = Math.Max(Math.Max(MaxValue, values.Max()), 1);
        double step = w / Math.Max(1, Capacity - 1);
        double offset = (Capacity - values.Count) * step;
        var points = new Point[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            double v = double.IsFinite(values[i]) ? Math.Clamp(values[i], 0, top) : 0;
            points[i] = new Point(offset + i * step, h - v / top * (h - 2) - 1);
        }

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, h), true);
            foreach (var p in points) g.LineTo(p);
            g.LineTo(new Point(points[^1].X, h));
            g.EndFigure(true);
        }
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(115, LineColor.R, LineColor.G, LineColor.B), 0),
                new GradientStop(Color.FromArgb(13, LineColor.R, LineColor.G, LineColor.B), 1),
            },
        };
        context.DrawGeometry(gradient, null, area);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], false);
            for (int i = 1; i < points.Length; i++) g.LineTo(points[i]);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(LineColor), 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
    }
}

/// <summary>A row of bars, e.g. per-core CPU load.</summary>
public sealed class BarStrip : Control
{
    private IReadOnlyList<double> _values = Array.Empty<double>();

    public double MaxValue { get; set; } = 100;
    public Color BarColor { get; set; } = Palette.AccentColor;

    public IReadOnlyList<double> Values
    {
        get => _values;
        set
        {
            _values = value;
            InvalidateVisual();
        }
    }

    public BarStrip()
    {
        FlowDirection = FlowDirection.LeftToRight;
    }

    public override void Render(DrawingContext context)
    {
        int count = Math.Max(1, _values.Count);
        const double spacing = 2;
        double barWidth = Math.Max(1, (Bounds.Width - spacing * (count - 1)) / count);
        var brush = new SolidColorBrush(BarColor);
        for (int i = 0; i < _values.Count; i++)
        {
            double fraction = Math.Clamp(_values[i] / MaxValue, 0, 1);
            double height = Math.Max(2, Bounds.Height * fraction);
            context.DrawRectangle(brush, null, new RoundedRect(new Rect(i * (barWidth + spacing), Bounds.Height - height, barWidth, height), 2));
        }
    }
}

/// <summary>Small spinning arc, the "busy" indicator.</summary>
public sealed class BusySpinner : Control
{
    private double _angle;
    private DispatcherTimer? _timer;
    private readonly IBrush? _brush;

    public BusySpinner(double size = 16, IBrush? brush = null)
    {
        Width = size;
        Height = size;
        _brush = brush;
        VerticalAlignment = VerticalAlignment.Center;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) =>
        {
            _angle = (_angle + 18) % 360;
            InvalidateVisual();
        });
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
    }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        double r = size / 2 - 1.5;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var track = new Pen(Palette.Grid, 2.5);
        context.DrawEllipse(null, track, center, r, r);
        double start = _angle * Math.PI / 180, end = start + Math.PI * 1.2;
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(new Point(center.X + r * Math.Cos(start), center.Y + r * Math.Sin(start)), false);
            g.ArcTo(new Point(center.X + r * Math.Cos(end), center.Y + r * Math.Sin(end)), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(_brush ?? Palette.Ink, 2.5, lineCap: PenLineCap.Round), arc);
    }
}

/// <summary>A clickable card with a hover lift, used for the individual test tiles.</summary>
public sealed class TileButton : Button
{
    private Border? _body;
    private Border? _iconBox;

    protected override Type StyleKeyOverride => typeof(TileButton);

    public TileButton(Icon icon, string title, string subtitle, Action onClick)
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Click += (_, _) => onClick();
        Template = new FuncControlTemplate<TileButton>((_, _) =>
        {
            _iconBox = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                BorderBrush = Palette.Ink,
                BorderThickness = new Thickness(1.5),
                Background = Palette.SurfaceAlt,
                Child = Icons.Make(icon, 18),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var stack = new StackPanel { Spacing = 6 };
            stack.Children.Add(_iconBox);
            stack.Children.Add(Kv.Text(title, 13, FontWeight.Bold));
            var sub = Kv.Text(subtitle, 11, FontWeight.Medium, Palette.Muted);
            sub.MaxLines = 3;
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(sub);
            _body = new Border
            {
                Background = Palette.Surface,
                BorderBrush = Palette.Ink,
                BorderThickness = new Thickness(Palette.Border),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                MinHeight = 128,
                Child = stack,
            };
            UpdateVisual();
            return _body;
        });
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty) UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (_body is null || _iconBox is null) return;
        bool hover = IsPointerOver;
        double shadow = IsPressed ? 1 : hover ? 5 : 3;
        _body.BoxShadow = new BoxShadows(new BoxShadow { OffsetX = shadow, OffsetY = shadow, Color = Palette.ShadowColor });
        _body.RenderTransform = hover && !IsPressed ? new TranslateTransform(-1, -1) : null;
        _iconBox.Background = hover ? Palette.Yellow : Palette.SurfaceAlt;
    }
}

/// <summary>Grid that puts children in as many equal columns as fit (like SwiftUI's adaptive grid).</summary>
public sealed class AdaptiveGrid : Panel
{
    public double MinItemWidth { get; set; } = 175;
    public double Spacing { get; set; } = 12;

    private int Columns(double width) =>
        Math.Max(1, (int)Math.Floor((width + Spacing) / (MinItemWidth + Spacing)));

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? MinItemWidth * 3 + Spacing * 2 : availableSize.Width;
        int columns = Math.Min(Columns(width), Math.Max(1, Children.Count));
        double itemWidth = (width - Spacing * (columns - 1)) / columns;
        double total = 0, rowHeight = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);
            if (i % columns == columns - 1 || i == Children.Count - 1)
            {
                total += rowHeight + (i == Children.Count - 1 ? 0 : Spacing);
                rowHeight = 0;
            }
        }
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = Math.Min(Columns(finalSize.Width), Math.Max(1, Children.Count));
        double itemWidth = (finalSize.Width - Spacing * (columns - 1)) / columns;
        double y = 0;
        for (int start = 0; start < Children.Count; start += columns)
        {
            double rowHeight = 0;
            for (int i = start; i < Math.Min(Children.Count, start + columns); i++) rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);
            for (int i = start; i < Math.Min(Children.Count, start + columns); i++)
            {
                int column = i - start;
                Children[i].Arrange(new Rect(column * (itemWidth + Spacing), y, itemWidth, rowHeight));
            }
            y += rowHeight + Spacing;
        }
        return finalSize;
    }
}

/// <summary>A content host that takes no space (and adds no panel spacing) while it is empty.</summary>
public sealed class Slot : ContentControl
{
    public Slot()
    {
        IsVisible = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    protected override Type StyleKeyOverride => typeof(ContentControl);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty) IsVisible = change.NewValue is not null;
    }
}
