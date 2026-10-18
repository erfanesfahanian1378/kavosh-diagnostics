using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Kavosh.App.Ui;

/// <summary>Line icons drawn on a 24×24 grid, in the same spirit as the macOS app's SF Symbols.</summary>
public enum Icon
{
    Cpu, Gpu, Memory, Battery, Display, Speaker, Camera, Keyboard, Drive, Play, Speedometer, Timer, Pulse, Grid,
    Heart, Wrench, Sparkles, Refresh, Warning, Check, Cross, Minus, Laptop, Tag, List, Thermometer, Pause, Mic,
    Bolt, Folder, Stop, ArrowRight, ArrowLeft, Shield, Flame, Fan, Chart, Seal, Dot, Info, Mouse, Eye, Square,
    Squares, Download, Globe,
}

public static class Icons
{
    private static readonly Dictionary<Icon, (string Path, bool Filled)> Paths = new()
    {
        [Icon.Cpu] = ("M7 7H17V17H7Z M10 10H14V14H10Z M9 2V5 M15 2V5 M9 19V22 M15 19V22 M2 9H5 M2 15H5 M19 9H22 M19 15H22", false),
        [Icon.Gpu] = ("M12 2.5L20.5 7.25V16.75L12 21.5L3.5 16.75V7.25Z M3.5 7.25L12 12L20.5 7.25 M12 12V21.5", false),
        [Icon.Memory] = ("M2.5 7H21.5V15H2.5Z M6 10H8V12H6Z M11 10H13V12H11Z M16 10H18V12H16Z M5 15V18 M9 15V18 M15 15V18 M19 15V18", false),
        [Icon.Battery] = ("M2.5 7H18.5V17H2.5Z M21 10.5V13.5 M5.5 10H12.5V14H5.5Z", false),
        [Icon.Display] = ("M3 4H21V16H3Z M8 21H16 M12 16V21", false),
        [Icon.Speaker] = ("M4 9H8L13 5V19L8 15H4Z M16.5 9.5A3.5 3.5 0 0 1 16.5 14.5 M19 7A7 7 0 0 1 19 17", false),
        [Icon.Camera] = ("M5 10A7 7 0 1 0 19 10A7 7 0 1 0 5 10Z M9 10A3 3 0 1 0 15 10A3 3 0 1 0 9 10Z M8 21H16 M12 17V21", false),
        [Icon.Keyboard] = ("M2 6H22V18H2Z M6 10H6.01 M10 10H10.01 M14 10H14.01 M18 10H18.01 M7 14H17", false),
        [Icon.Drive] = ("M3 6H21V18H3Z M3 13H21 M7 15.5H7.01 M14 15.5H18", false),
        [Icon.Play] = ("M7 4L20 12L7 20Z", true),
        [Icon.Speedometer] = ("M3.5 18A8.5 8.5 0 1 1 20.5 18 M12 18L16.5 11 M6.5 13.5L7.5 14 M12 8V9 M17.5 13.5L16.5 14", false),
        [Icon.Timer] = ("M5 13.5A7 7 0 1 0 19 13.5A7 7 0 1 0 5 13.5Z M12 10V13.5L14.5 15 M9.5 2.5H14.5 M12 2.5V6.5", false),
        [Icon.Pulse] = ("M2.5 12H7L9.5 6L13.5 18L16 12H21.5", false),
        [Icon.Grid] = ("M4 4H10.5V10.5H4Z M13.5 4H20V10.5H13.5Z M4 13.5H10.5V20H4Z M13.5 13.5H20V20H13.5Z", false),
        [Icon.Heart] = ("M12 20C12 20 3.5 15 3.5 9A4.25 4.25 0 0 1 12 7.5A4.25 4.25 0 0 1 20.5 9C20.5 15 12 20 12 20Z M7 12H9.5L11 9.5L13 14L14.5 12H17", false),
        [Icon.Wrench] = ("M14.5 3.5A5 5 0 0 0 9.5 9.5L3.5 15.5A2.1 2.1 0 0 0 8.5 20.5L14.5 14.5A5 5 0 0 0 20.5 9.5L17 13L13.5 12.5L11 10L11 7Z", false),
        [Icon.Sparkles] = ("M10 3L11.8 8.2L17 10L11.8 11.8L10 17L8.2 11.8L3 10L8.2 8.2Z M18 14L18.9 16.1L21 17L18.9 17.9L18 20L17.1 17.9L15 17L17.1 16.1Z", false),
        [Icon.Refresh] = ("M20 12A8 8 0 1 1 17.66 6.34 M20 3.5V8H15.5", false),
        [Icon.Warning] = ("M12 3L22 20.5H2Z M12 9.5V14 M12 17.5H12.01", false),
        [Icon.Check] = ("M5 12.5L10 17.5L19.5 7", false),
        [Icon.Cross] = ("M6.5 6.5L17.5 17.5 M17.5 6.5L6.5 17.5", false),
        [Icon.Minus] = ("M6 12H18", false),
        [Icon.Laptop] = ("M5 5H19V15.5H5Z M2 19.5H22", false),
        [Icon.Tag] = ("M3.5 3.5H11.5L20.5 12.5L12.5 20.5L3.5 11.5Z M8 8H8.01", false),
        [Icon.List] = ("M9 6H20.5 M9 12H20.5 M9 18H20.5 M4 6H4.01 M4 12H4.01 M4 18H4.01", false),
        [Icon.Thermometer] = ("M10 4.5A2 2 0 0 1 14 4.5V14.2A4 4 0 1 1 10 14.2Z M12 9V16", false),
        [Icon.Pause] = ("M8.5 5V19 M15.5 5V19", false),
        [Icon.Mic] = ("M9 5.5A3 3 0 0 1 15 5.5V11.5A3 3 0 0 1 9 11.5Z M5.5 11A6.5 6.5 0 0 0 18.5 11 M12 17.5V21.5 M8.5 21.5H15.5", false),
        [Icon.Bolt] = ("M13.5 2L4.5 13.5H11.5L10.5 22L19.5 10.5H12.5Z", false),
        [Icon.Folder] = ("M3 6H9.5L11.5 8.5H21V19H3Z", false),
        [Icon.Stop] = ("M6 6H18V18H6Z", true),
        [Icon.ArrowRight] = ("M4 12H20 M14 6L20 12L14 18", false),
        [Icon.ArrowLeft] = ("M20 12H4 M10 6L4 12L10 18", false),
        [Icon.Shield] = ("M12 2.5L20 5.5V11.5C20 16.5 16.5 20 12 21.5C7.5 20 4 16.5 4 11.5V5.5Z M8.5 12L11 14.5L15.5 9.5", false),
        [Icon.Flame] = ("M12 21.5C8 21.5 5.5 18.5 5.5 15C5.5 10.5 10 9 10.5 3.5C14 6 18.5 10 18.5 15C18.5 18.5 16 21.5 12 21.5Z M12 21.5C10.3 21.5 9.5 20 9.5 18.5C9.5 16.5 11.5 15.5 12 13.5C13.5 15 14.5 16.5 14.5 18.5C14.5 20 13.7 21.5 12 21.5Z", false),
        [Icon.Fan] = ("M12 12C12 7 10 3.5 7.5 4.5C5 5.5 7.5 10 12 12Z M12 12C17 12 20.5 10 19.5 7.5C18.5 5 14 7.5 12 12Z M12 12C12 17 14 20.5 16.5 19.5C19 18.5 16.5 14 12 12Z M12 12C7 12 3.5 14 4.5 16.5C5.5 19 10 16.5 12 12Z", false),
        [Icon.Chart] = ("M3.5 3.5V20.5H20.5 M7 15.5L11 10.5L14 13.5L19.5 7", false),
        [Icon.Seal] = ("M12 2L14.6 4.1L17.9 3.9L18.8 7.1L21.6 9L20.3 12L21.6 15L18.8 16.9L17.9 20.1L14.6 19.9L12 22L9.4 19.9L6.1 20.1L5.2 16.9L2.4 15L3.7 12L2.4 9L5.2 7.1L6.1 3.9L9.4 4.1Z M8 12L11 15L16.5 9.5", false),
        [Icon.Dot] = ("M8 12A4 4 0 1 0 16 12A4 4 0 1 0 8 12Z", true),
        [Icon.Info] = ("M3 12A9 9 0 1 0 21 12A9 9 0 1 0 3 12Z M12 11V16.5 M12 7.5H12.01", false),
        [Icon.Mouse] = ("M6.5 9A5.5 5.5 0 0 1 17.5 9V15A5.5 5.5 0 0 1 6.5 15Z M12 3.5V9 M6.5 9H17.5", false),
        [Icon.Eye] = ("M2 12C4.5 7.5 8 5.5 12 5.5C16 5.5 19.5 7.5 22 12C19.5 16.5 16 18.5 12 18.5C8 18.5 4.5 16.5 2 12Z M9 12A3 3 0 1 0 15 12A3 3 0 1 0 9 12Z", false),
        [Icon.Square] = ("M6 6H18V18H6Z", false),
        [Icon.Squares] = ("M3.5 3.5H9.5V9.5H3.5Z M14.5 3.5H20.5V9.5H14.5Z M3.5 14.5H9.5V20.5H3.5Z M14.5 14.5H20.5V20.5H14.5Z M9 9L15 15", false),
        [Icon.Download] = ("M12 3.5V15 M7 10.5L12 15.5L17 10.5 M4 19.5H20", false),
        [Icon.Globe] = ("M3 12A9 9 0 1 0 21 12A9 9 0 1 0 3 12Z M3 12H21 M12 3C9.5 6 9.5 18 12 21 M12 3C14.5 6 14.5 18 12 21", false),
    };

    /// <summary>An icon control of the given size.</summary>
    public static Control Make(Icon icon, double size = 16, IBrush? brush = null, double strokeWidth = 2.2)
    {
        var (data, filled) = Paths[icon];
        var color = brush ?? Palette.Ink;
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = color,
            StrokeThickness = strokeWidth,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Fill = filled ? color : null,
        };
        var canvas = new Canvas { Width = 24, Height = 24, Children = { path } };
        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            FlowDirection = Avalonia.Media.FlowDirection.LeftToRight,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
    }

    /// <summary>Icon for a test module.</summary>
    public static Icon For(Kavosh.Core.TestModule module) => module switch
    {
        Kavosh.Core.TestModule.Cpu => Icon.Cpu,
        Kavosh.Core.TestModule.Gpu => Icon.Gpu,
        Kavosh.Core.TestModule.Memory => Icon.Memory,
        Kavosh.Core.TestModule.Battery => Icon.Battery,
        Kavosh.Core.TestModule.Display => Icon.Display,
        Kavosh.Core.TestModule.Speakers => Icon.Speaker,
        Kavosh.Core.TestModule.MicCamera => Icon.Camera,
        Kavosh.Core.TestModule.Input => Icon.Keyboard,
        _ => Icon.Drive,
    };
}
