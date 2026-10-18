using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Ui;

public enum StatusState { Pass, Fail, Warn, Skipped, Pending }

/// <summary>Factory helpers for the retro building blocks, mirroring the macOS app's components.</summary>
public static class Kv
{
    // ── Surfaces ──

    /// <summary>Applies the ink outline and hard offset shadow to a border.</summary>
    public static Border Surface(Control? child, IBrush? fill = null, double corner = Palette.Corner, double shadow = Palette.ShadowOffset,
        Thickness? padding = null, IBrush? border = null)
    {
        return new Border
        {
            Background = fill ?? Palette.Surface,
            BorderBrush = border ?? Palette.Ink,
            BorderThickness = new Thickness(Palette.Border),
            CornerRadius = new CornerRadius(corner),
            BoxShadow = shadow > 0 ? new BoxShadows(new BoxShadow { OffsetX = shadow, OffsetY = shadow, Color = Palette.ShadowColor }) : default,
            Padding = padding ?? new Thickness(0),
            Child = child,
            Margin = new Thickness(0, 0, shadow, shadow),
        };
    }

    /// <summary>A card with an ink outline and a hard offset shadow.</summary>
    public static Border Card(Control content, IBrush? fill = null, double padding = 16, double shadow = Palette.ShadowOffset) =>
        Surface(content, fill, Palette.Corner, shadow, new Thickness(padding));

    /// <summary>Soft inner panel (no shadow), used inside cards.</summary>
    public static Border Inset(Control content, double padding = 9, double corner = 8)
    {
        return new Border
        {
            Background = Palette.WithOpacity(Palette.SurfaceAltColor, 0.6),
            BorderBrush = Palette.WithOpacity(Palette.InkColor, 0.3),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(corner),
            Padding = new Thickness(padding),
            Child = content,
        };
    }

    // ── Layout ──

    public static StackPanel Column(double spacing, params Control?[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var child in children) if (child is not null) panel.Children.Add(child);
        return panel;
    }

    public static StackPanel Row(double spacing, params Control?[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var child in children) if (child is not null) panel.Children.Add(child);
        return panel;
    }

    /// <summary>Puts <paramref name="leading"/> at the start and <paramref name="trailing"/> at the end of a row.</summary>
    public static Grid Spread(Control leading, Control? trailing, VerticalAlignment align = VerticalAlignment.Center)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        leading.VerticalAlignment = align;
        grid.Children.Add(leading);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = align;
            trailing.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(trailing, 1);
            grid.Children.Add(trailing);
        }
        return grid;
    }

    /// <summary>Equal-width columns.</summary>
    public static Grid Columns(double spacing, params Control[] children)
    {
        var grid = new Grid();
        for (int i = 0; i < children.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var child = children[i];
            var own = child.Margin; // keeps a card's shadow room
            child.Margin = new Thickness(i == 0 ? 0 : spacing / 2, own.Top, (i == children.Length - 1 ? 0 : spacing / 2) + own.Right, own.Bottom);
            Grid.SetColumn(child, i);
            grid.Children.Add(child);
        }
        return grid;
    }

    public static WrapPanel Flow(double spacing, params Control[] children)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            child.Margin = new Thickness(0, 0, spacing, spacing);
            panel.Children.Add(child);
        }
        return panel;
    }

    /// <summary>Vertical scrolling page. The margin sits inside the scrolled content so nothing runs past the edge.</summary>
    public static ScrollViewer Scroll(Control content, Thickness margin) => new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        Content = new Border { Margin = margin, Child = content },
    };

    public static Border Divider() => new() { Height = 1, Background = Palette.WithOpacity(Palette.InkColor, 0.15), Margin = new Thickness(0, 2) };

    // ── Text ──

    /// <summary>UI text in the current language's face. Wraps by default.</summary>
    public static TextBlock Text(string text, double size = 13, FontWeight? weight = null, IBrush? color = null, bool wrap = true)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = Palette.Text,
            FontSize = size,
            FontWeight = weight ?? FontWeight.Medium,
            Foreground = color ?? Palette.Ink,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Hardware names and numbers: always left-to-right, Latin face.</summary>
    public static TextBlock Hardware(string text, double size = 13, FontWeight? weight = null, IBrush? color = null, bool wrap = true)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = Palette.Numbers,
            FontSize = size,
            FontWeight = weight ?? FontWeight.SemiBold,
            Foreground = color ?? Palette.Ink,
            FlowDirection = FlowDirection.LeftToRight,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = Loc.Language.IsRightToLeft() ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
    }

    /// <summary>Small uppercase heading used above every block ("LIVE RESOURCE USAGE").</summary>
    public static Control SectionLabel(string text, Icon? icon = null, IBrush? color = null)
    {
        var brush = color ?? Palette.Muted;
        bool persian = Loc.Language == AppLanguage.Fa;
        var label = new TextBlock
        {
            Text = persian ? text : text.ToUpperInvariant(),
            FontFamily = Palette.Text,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = brush,
            LetterSpacing = persian ? 0 : 1.1,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        if (icon is null) return label;
        return Row(6, Icons.Make(icon.Value, 12, brush, 2.6), label);
    }

    /// <summary>Instruction paragraph at the top of interactive stations.</summary>
    public static TextBlock Instruction(string text) => Text(text, 14, FontWeight.Medium);

    // ── Badges ──

    public static Border Badge(string text, Color? fill = null, IBrush? textColor = null, Icon? icon = null)
    {
        var foreground = textColor ?? Palette.Ink;
        var label = new TextBlock
        {
            Text = text,
            FontFamily = Palette.Text,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Control content = icon is { } i ? Row(4, Icons.Make(i, 11, foreground, 3), label) : label;
        return new Border
        {
            Background = new SolidColorBrush(fill ?? Palette.SurfaceAltColor),
            BorderBrush = Palette.Ink,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 2.5),
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Badge for a 0–100 score, colored by how good it is.</summary>
    public static Border ScoreBadge(double score)
    {
        var badge = Badge(Fmt.Fixed(score, 0), Palette.ScoreColor(score), Palette.OnYellow);
        badge.FlowDirection = FlowDirection.LeftToRight;
        return badge;
    }

    public static Border GradeBadge(DeviceGrade? grade, double size = 96)
    {
        var color = grade is { } g ? Palette.ToneColor(g.Tone()) : Palette.SurfaceAltColor;
        var letter = new TextBlock
        {
            Text = grade?.Letter() ?? "—",
            FontFamily = Palette.Numbers,
            FontSize = size * 0.5,
            FontWeight = FontWeight.Black,
            Foreground = Palette.OnYellow,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var surface = Surface(letter, new SolidColorBrush(color), size * 0.18, size > 60 ? 5 : 3);
        surface.Width = size + surface.Margin.Right;
        surface.Height = size + surface.Margin.Bottom;
        surface.FlowDirection = FlowDirection.LeftToRight;
        return surface;
    }

    public static Border StatusIcon(StatusState state)
    {
        var (icon, fill, foreground) = state switch
        {
            StatusState.Pass => (Icon.Check, Palette.Green, Brushes.White),
            StatusState.Fail => (Icon.Cross, Palette.Red, Brushes.White),
            StatusState.Warn => (Icon.Warning, Palette.Yellow, Palette.OnYellow),
            StatusState.Skipped => (Icon.Minus, Palette.SurfaceAlt, Palette.Ink),
            _ => (Icon.Dot, Palette.SurfaceAlt, Palette.Muted),
        };
        return new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = fill,
            BorderBrush = Palette.Ink,
            BorderThickness = new Thickness(1.5),
            Child = Icons.Make(icon, state == StatusState.Pending ? 8 : 12, foreground, 3.2),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Label + big value, used for live readings.</summary>
    public static Border StatTile(string title, string value, string? detail = null, IBrush? color = null, Icon? icon = null)
    {
        var valueText = Hardware(value, 22, FontWeight.ExtraBold, color, wrap: false);
        var stack = Column(4, SectionLabel(title, icon), new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = Loc.Language.IsRightToLeft() ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = valueText,
        });
        if (detail is not null) stack.Children.Add(Text(detail, 11, FontWeight.Medium, Palette.Muted));
        return Surface(stack, Palette.Surface, 10, 3, new Thickness(12));
    }

    /// <summary>"Label ........ value" row.</summary>
    public static Grid KeyValue(string key, string value, IBrush? valueColor = null)
    {
        var valueText = Hardware(value, 13, FontWeight.SemiBold, valueColor);
        valueText.TextAlignment = TextAlignment.Right;
        valueText.MaxWidth = 260;
        return Spread(Text(key, 13, FontWeight.Medium, Palette.Muted), valueText, VerticalAlignment.Top);
    }

    /// <summary>Icon + colored text line, for verdicts like "Every calculation came back correct."</summary>
    public static Control Label(string text, Icon icon, IBrush color, double size = 13)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var glyph = Icons.Make(icon, size + 2, color, 2.6);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, 1, 8, 0);
        var label = Text(text, size, FontWeight.Bold, color);
        Grid.SetColumn(label, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(label);
        return grid;
    }

    /// <summary>Segmented picker drawn as a row of retro buttons.</summary>
    public static WrapPanel Segmented<T>(IEnumerable<(T Value, string Title)> options, T selected, Action<T> onSelect)
    {
        var buttons = options.Select(option =>
            (Control)new RetroButton(option.Title, null,
                EqualityComparer<T>.Default.Equals(option.Value, selected) ? ButtonKind.Primary : ButtonKind.Secondary,
                ButtonSize.Small, () => onSelect(option.Value))).ToArray();
        return Flow(8, buttons);
    }

    /// <summary>Progress row with a caption and elapsed time ("Overall ▬▬▬▬ 0:42").</summary>
    public static Grid CaptionedBar(string caption, RetroProgressBar bar, TextBlock trailing)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var label = Text(caption, 11, FontWeight.Bold, Palette.Muted, wrap: false);
        label.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(label);
        Grid.SetColumn(bar, 1);
        grid.Children.Add(bar);
        trailing.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);
        return grid;
    }

    /// <summary>A big centered "busy" block with a message.</summary>
    public static Control Busy(string message) =>
        Row(10, new BusySpinner(18), Text(message, 13, FontWeight.Medium, Palette.Muted));

    public static string DateText(DateTime utc)
    {
        var local = utc.ToLocalTime();
        if (Loc.Language == AppLanguage.Fa)
        {
            var culture = new System.Globalization.CultureInfo("fa-IR");
            return local.ToString("d MMMM yyyy، HH:mm", culture);
        }
        return local.ToString("MMM d, yyyy 'at' h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string DurationLabel(int seconds) => seconds < 60 ? $"{seconds} s" : $"{seconds / 60} min";

    public static string Ok => T("OK", "باشه");
}
