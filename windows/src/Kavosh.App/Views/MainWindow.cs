using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Kavosh.App.Ui;
using Kavosh.Core;
using Kavosh.Hardware;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Views;

public sealed class MainWindow : Window
{
    private readonly AppModel _model;
    private readonly Panel _root = new();
    private readonly Panel _overlay = new() { IsVisible = false };
    private readonly ContentControl _content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private HeaderBar? _header;

    public AppModel Model => _model;

    public MainWindow(AppModel model)
    {
        _model = model;
        Title = "Kavosh Diagnostics";
        Width = 1440;
        Height = 900;
        MinWidth = 1100;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Kavosh/Assets/kavosh.ico"))); } catch { /* no icon */ }

        Content = _root;
        _model.ScreenChanged += ShowScreen;
        _model.LanguageChanged += Rebuild;
        _model.Notice += message => _ = Dialog(T("Notice", "پیام"), message, new[] { (Kv.Ok, ButtonKind.Primary) });
        _model.SaveFileHandler = SaveFileAsync;
        ActualThemeVariantChanged += (_, _) => Rebuild();
        Rebuild();
    }

    /// <summary>Rebuilds everything (language or light/dark mode changed).</summary>
    public void Rebuild()
    {
        Palette.IsDark = ActualThemeVariant == ThemeVariant.Dark;
        FlowDirection = _model.Settings.Language.IsRightToLeft() ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Background = Palette.Background;
        FontFamily = Palette.Text;
        _header?.Detach();
        _header = new HeaderBar(_model);
        var dock = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        dock.Children.Add(_header);
        dock.Children.Add(_content);
        _root.Children.Clear();
        _root.Children.Add(dock);
        _root.Children.Add(_overlay);
        ShowScreen();
    }

    private void ShowScreen()
    {
        if (_content.Content is IDisposable old) old.Dispose();
        _content.Content = _model.Screen switch
        {
            Screen.Suite when _model.Runner is not null => new SuiteView(_model.Runner, this),
            Screen.Results when _model.ShownReport is not null => new ResultView(_model, _model.ShownReport),
            Screen.Storage => new StorageView(_model, this),
            Screen.WasherSetup => new WasherSetupView(_model, this),
            _ => new DashboardView(_model, this),
        };
        _header?.Refresh();
    }

    // ── Dialogs drawn inside the window ──

    /// <summary>Shows a message with buttons and returns the index of the button pressed.</summary>
    public Task<int> Dialog(string title, string message, (string Title, ButtonKind Kind)[] buttons, Control? extra = null)
    {
        var done = new TaskCompletionSource<int>();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = buttons.Length - 1; i >= 0; i--)
        {
            int index = i;
            row.Children.Add(new RetroButton(buttons[i].Title, null, buttons[i].Kind, ButtonSize.Regular, () =>
            {
                _overlay.IsVisible = false;
                _overlay.Children.Clear();
                done.TrySetResult(index);
            }));
        }
        var body = Kv.Column(14, Kv.Text(title, 18, FontWeight.ExtraBold), Kv.Text(message, 13, FontWeight.Medium), extra, row);
        var card = Kv.Card(body, padding: 22);
        card.MaxWidth = 520;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        _overlay.Children.Clear();
        _overlay.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)) });
        _overlay.Children.Add(card);
        _overlay.IsVisible = true;
        return done.Task;
    }

    private async Task<string?> SaveFileAsync(string suggestedName, string contents)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
            ShowOverwritePrompt = true,
        });
        if (file is null) return null;
        await using (var stream = await file.OpenWriteAsync())
        {
            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(contents)).ToArray();
            await stream.WriteAsync(bytes);
        }
        return file.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _overlay.IsVisible)
        {
            // Esc closes a dialog the same way as its last (cancel) button, which sits first in the row.
            if (_overlay.Children.LastOrDefault() is Border { Child: StackPanel body } && body.Children.LastOrDefault() is StackPanel buttons &&
                buttons.Children.FirstOrDefault() is RetroButton cancel)
            {
                cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }
}

/// <summary>App name, status line, sensor access and the language switch.</summary>
public sealed class HeaderBar : Border
{
    private readonly AppModel _model;
    private readonly Slot _status = new();
    private readonly Slot _trailing = new();

    public HeaderBar(AppModel model)
    {
        _model = model;
        Background = Palette.Surface;
        BorderBrush = Palette.Ink;
        BorderThickness = new Thickness(0, 0, 0, Palette.Border);
        Padding = new Thickness(24, 12);

        var logo = new Image { Width = 36, Height = 36 };
        try { logo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://Kavosh/Assets/kavosh-256.png"))); } catch { /* no logo */ }
        var name = new TextBlock { Text = "Kavosh", FontFamily = Palette.Numbers, FontSize = 21, FontWeight = FontWeight.Black, Foreground = Palette.Ink };
        var brand = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = Kv.Row(12, logo, Kv.Column(1, name, Kv.SectionLabel(T("Diagnostics & Benchmark", "عیب‌یابی و بنچمارک")))),
        };
        brand.Click += (_, _) => _model.GoToDashboard();

        var right = Kv.Row(14, _status, _trailing);
        right.VerticalAlignment = VerticalAlignment.Center;
        Child = Kv.Spread(brand, right);
        _model.SpecsChanged += Refresh;
        _model.Telemetry.Updated += RefreshStatus;
        Refresh();
    }

    public void Detach()
    {
        _model.SpecsChanged -= Refresh;
        _model.Telemetry.Updated -= RefreshStatus;
        if (_model.Runner is { } runner) runner.Changed -= RefreshStatus;
    }

    public void Refresh()
    {
        if (_model.Runner is { } runner)
        {
            runner.Changed -= RefreshStatus;
            runner.Changed += RefreshStatus;
        }
        RefreshStatus();
        var items = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (_model.Screen == Screen.Dashboard)
        {
            var languages = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, FlowDirection = FlowDirection.LeftToRight };
            foreach (var language in new[] { AppLanguage.En, AppLanguage.Fa })
            {
                var button = new RetroButton(language.ShortName(), null, _model.Settings.Language == language ? ButtonKind.Primary : ButtonKind.Secondary,
                    ButtonSize.Small, () => _model.SetLanguage(language));
                ToolTip.SetTip(button, language.NativeName());
                languages.Children.Add(button);
            }
            items.Children.Add(languages);
        }
        _trailing.Content = items;
    }

    private void RefreshStatus()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var latest = _model.Telemetry.Latest;
        if (latest?.ThermalState is { } thermal && thermal >= ThermalLevel.Serious)
            row.Children.Add(Kv.Badge(thermal.Title(), thermal == ThermalLevel.Critical ? Palette.RedColor : Palette.OrangeColor, Palette.OnYellow, Icon.Thermometer));
        if (_model.Telemetry.Status is { } status && Platform.IsWindows && !status.FullAccess)
        {
            var badge = new RetroButton(T("Limited sensors", "حسگرهای محدود"), Icon.Info, ButtonKind.Ghost, ButtonSize.Small, ShowSensorHelp);
            row.Children.Add(badge);
        }
        if (!_model.SpecsComplete)
        {
            row.Children.Add(new BusySpinner(14));
            row.Children.Add(Kv.Text(T("Loading system specs…", "در حال خواندن مشخصات سیستم…"), 12, FontWeight.Medium, Palette.Muted, wrap: false));
        }
        else if (_model.Runner?.Current is { } current)
        {
            row.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Palette.Orange });
            row.Children.Add(Kv.Text(T($"Testing: {current.Title()}", $"در حال تست: {current.Title()}"), 12, FontWeight.SemiBold, wrap: false));
        }
        else
        {
            row.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Palette.Green });
            row.Children.Add(Kv.Text(T("Ready", "آماده"), 12, FontWeight.SemiBold, wrap: false));
        }
        _status.Content = row;
    }

    private async void ShowSensorHelp()
    {
        if (TopLevel.GetTopLevel(this) is not MainWindow window) return;
        var status = _model.Telemetry.Status;
        bool admin = status?.IsAdministrator ?? false;
        bool pawn = status?.PawnIoInstalled ?? false;
        string message;
        var buttons = new List<(string, ButtonKind)>();
        if (!admin)
        {
            message = T("Kavosh is running without administrator rights, so drive health (SMART), CPU temperature and power can't be read. Restart it as administrator to read everything.",
                        "کاوش بدون دسترسی مدیر (Administrator) اجرا شده است، پس سلامت درایو (SMART)، دما و توان پردازنده خوانده نمی‌شوند. برای خواندن همه چیز، آن را با دسترسی مدیر دوباره باز کنید.");
            buttons.Add((T("Restart as administrator", "اجرای دوباره با دسترسی مدیر"), ButtonKind.Primary));
        }
        else if (!pawn)
        {
            message = T("CPU temperature, power and fan readings need PawnIO — a small, signed, open-source driver used by LibreHardwareMonitor. Kavosh never installs drivers by itself. Install PawnIO from its official site, then restart Kavosh. Everything else works without it.",
                        "دما، توان و فن پردازنده به PawnIO نیاز دارند — یک درایور کوچک، امضاشده و متن‌باز که LibreHardwareMonitor از آن استفاده می‌کند. کاوش هرگز خودش درایور نصب نمی‌کند. PawnIO را از سایت رسمی‌اش نصب کنید و کاوش را دوباره باز کنید. بقیه بخش‌ها بدون آن کار می‌کنند.");
            buttons.Add((T("Open the PawnIO site", "باز کردن سایت PawnIO"), ButtonKind.Primary));
        }
        else
        {
            message = T("The sensor library couldn't start, so some readings are missing. " + (status?.Problem ?? ""),
                        "کتابخانه حسگرها شروع نشد، پس بعضی مقادیر در دسترس نیستند. " + (status?.Problem ?? ""));
        }
        buttons.Add((T("Close", "بستن"), ButtonKind.Secondary));
        int choice = await window.Dialog(T("Sensor access", "دسترسی به حسگرها"), message, buttons.ToArray());
        if (choice != 0 || buttons.Count == 1) return;
        if (!admin)
        {
            if (Elevation.RestartElevated()) (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        else
        {
            AppModel.OpenWithShell(PawnIo.DownloadUrl);
        }
    }
}
