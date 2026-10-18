using Avalonia.Media;
using Kavosh.Core;

namespace Kavosh.App.Ui;

/// <summary>
/// The app's "retro" look: warm paper background, ink outlines, hard offset shadows and a few loud
/// accent colors. Every color has a light and a dark variant; the window rebuilds its views when
/// Windows switches between light and dark mode.
/// </summary>
public static class Palette
{
    public static bool IsDark { get; set; }

    private static Color Pick(uint light, uint dark) => Color.FromUInt32(0xFF000000 | (IsDark ? dark : light));

    // ── Colors ──

    public static Color BackgroundColor => Pick(0xF4EEE1, 0x121218);
    public static Color SurfaceColor => Pick(0xFFFDF8, 0x1C1C24);
    public static Color SurfaceAltColor => Pick(0xEFE7D6, 0x25252F);
    public static Color InkColor => Pick(0x17141C, 0xECE8DF);
    public static Color MutedColor => Pick(0x6E6676, 0x9C98A6);
    public static Color ShadowColor => Pick(0x17141C, 0x000000);
    public static Color GridColor => Pick(0xE2D9C6, 0x2E2E3A);

    public static Color AccentColor => Pick(0x2F6BFF, 0x5B8CFF);
    public static Color RedColor => Pick(0xF0452A, 0xFF6A4D);
    public static Color YellowColor => Pick(0xFFC93C, 0xFFD45E);
    public static Color GreenColor => Pick(0x1FAF6B, 0x3DD68C);
    public static Color TealColor => Pick(0x0FA89A, 0x2DD4BF);
    public static Color PurpleColor => Pick(0x7C4DFF, 0x9F7BFF);
    public static Color OrangeColor => Pick(0xFF8A1F, 0xFFA24D);

    public static IBrush Background => new SolidColorBrush(BackgroundColor);
    public static IBrush Surface => new SolidColorBrush(SurfaceColor);
    public static IBrush SurfaceAlt => new SolidColorBrush(SurfaceAltColor);
    public static IBrush Ink => new SolidColorBrush(InkColor);
    public static IBrush Muted => new SolidColorBrush(MutedColor);
    public static IBrush Shadow => new SolidColorBrush(ShadowColor);
    public static IBrush Grid => new SolidColorBrush(GridColor);
    public static IBrush Accent => new SolidColorBrush(AccentColor);
    public static IBrush Red => new SolidColorBrush(RedColor);
    public static IBrush Yellow => new SolidColorBrush(YellowColor);
    public static IBrush Green => new SolidColorBrush(GreenColor);
    public static IBrush Teal => new SolidColorBrush(TealColor);
    public static IBrush Purple => new SolidColorBrush(PurpleColor);
    public static IBrush Orange => new SolidColorBrush(OrangeColor);

    /// <summary>Text on top of an accent-colored fill.</summary>
    public static IBrush OnAccent => Brushes.White;
    public static IBrush OnYellow => new SolidColorBrush(Color.FromUInt32(0xFF17141C));

    public static IBrush WithOpacity(Color color, double opacity) =>
        new SolidColorBrush(color, opacity);

    public static Color ToneColor(GradeTone tone) => tone switch
    {
        GradeTone.Excellent => GreenColor,
        GradeTone.Good => TealColor,
        GradeTone.Fair => YellowColor,
        GradeTone.Poor => OrangeColor,
        _ => RedColor,
    };

    /// <summary>Green → yellow → red for a 0–100 "goodness" score.</summary>
    public static Color ScoreColor(double score) => score switch
    {
        >= 90 => GreenColor,
        >= 75 => TealColor,
        >= 60 => YellowColor,
        >= 40 => OrangeColor,
        _ => RedColor,
    };

    /// <summary>Cool → hot for a temperature.</summary>
    public static Color TemperatureColor(double? celsius) => celsius switch
    {
        null => MutedColor,
        < 60 => GreenColor,
        < 80 => YellowColor,
        < 95 => OrangeColor,
        _ => RedColor,
    };

    public static Color ThermalColor(ThermalLevel? level) => level switch
    {
        ThermalLevel.Nominal => GreenColor,
        ThermalLevel.Fair => YellowColor,
        ThermalLevel.Serious => OrangeColor,
        ThermalLevel.Critical => RedColor,
        _ => SurfaceAltColor,
    };

    // ── Metrics ──

    public const double Corner = 12;
    public const double Border = 2;
    public const double ShadowOffset = 4;

    // ── Fonts ──

    private const string FontFolder = "avares://Kavosh/Assets/Fonts";

    /// <summary>Rounded Latin face (Nunito); Persian falls back to Vazirmatn.</summary>
    public static readonly FontFamily Latin = new($"{FontFolder}#Nunito, {FontFolder}#Vazirmatn");

    /// <summary>Persian face (Vazirmatn) with Nunito for any Latin text inside it.</summary>
    public static readonly FontFamily Persian = new($"{FontFolder}#Vazirmatn, {FontFolder}#Nunito");

    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>The UI face for the current language.</summary>
    public static FontFamily Text => Loc.Language == AppLanguage.Fa ? Persian : Latin;

    /// <summary>Numbers and hardware names stay in the Latin face in both languages.</summary>
    public static FontFamily Numbers => Latin;
}
