using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kavosh.App.Ui;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Views;

/// <summary>Who made the app. Shown on the launch screen, in the header and in the exe's file details.</summary>
public static class AppCredits
{
    public const string DeveloperName = "Erfan Esfahanian";
    public const string DeveloperNamePersian = "عرفان اصفهانیان";
}

/// <summary>Shown for a moment when the app opens: the app name and its developer. A click skips it.</summary>
public sealed class LaunchSplash : Border
{
    public LaunchSplash()
    {
        Background = Palette.Background;
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(450) },
        };

        var logo = new Image { Width = 112, Height = 112, HorizontalAlignment = HorizontalAlignment.Center };
        try { logo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://Kavosh/Assets/kavosh-256.png"))); } catch { /* no logo */ }

        var title = new TextBlock
        {
            Text = "Kavosh",
            FontFamily = Palette.Numbers,
            FontSize = 44,
            FontWeight = FontWeight.Black,
            Foreground = Palette.Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var subtitle = Centered(Kv.SectionLabel(T("Diagnostics & Benchmark", "عیب‌یابی و بنچمارک")));
        var divider = new Border { Width = 220, Height = 2, Background = Palette.WithOpacity(Palette.InkColor, 0.15), HorizontalAlignment = HorizontalAlignment.Center };
        var developedBy = Centered(Kv.SectionLabel(T("Developed by", "توسعه‌دهنده"), Icon.Wrench, Palette.Accent));

        var credit = Kv.Column(4, developedBy);
        bool persian = Loc.Language == AppLanguage.Fa;
        if (persian)
        {
            var persianName = Kv.Text(AppCredits.DeveloperNamePersian, 28, FontWeight.ExtraBold, wrap: false);
            persianName.HorizontalAlignment = HorizontalAlignment.Center;
            credit.Children.Add(persianName);
        }
        var name = Kv.Hardware(AppCredits.DeveloperName, persian ? 16 : 28, persian ? FontWeight.Bold : FontWeight.ExtraBold,
            persian ? Palette.Muted : Palette.Ink, wrap: false);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        credit.Children.Add(name);

        var card = Kv.Card(Kv.Column(14, logo, title, subtitle, divider, credit), padding: 40, shadow: 6);
        card.CornerRadius = new CornerRadius(22);
        card.MinWidth = 420;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        Child = card;
    }

    private static Control Centered(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Center;
        return control;
    }
}
