using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Kavosh.Core;

namespace Kavosh.App.Ui;

/// <summary>
/// Borderless window covering the whole screen the app is on, above the taskbar. Used by the
/// display test and the screen washer. Keys go to <see cref="KeyHandler"/> first.
/// </summary>
public sealed class FullscreenWindow : Window
{
    /// <summary>Returns true when it handled the key.</summary>
    public Func<KeyEventArgs, bool>? KeyHandler { get; set; }

    private readonly DisplayAwake _awake = new();

    public FullscreenWindow(Window owner)
    {
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Black;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        FlowDirection = Loc.Language.IsRightToLeft() ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        FontFamily = Palette.Text;
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is not null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = screen.Bounds.Position;
            Width = screen.Bounds.Width / screen.Scaling;
            Height = screen.Bounds.Height / screen.Scaling;
        }
        Opened += (_, _) =>
        {
            WindowState = WindowState.FullScreen;
            Activate();
            Focus();
        };
        _awake.Begin();
        Closed += (_, _) => _awake.End();
    }

    /// <summary>A point on the screen this window covers, in desktop pixels.</summary>
    public PixelPoint ScreenPoint => new(Position.X + 10, Position.Y + 10);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (KeyHandler?.Invoke(e) == true)
        {
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}

/// <summary>Keeps the display from sleeping while something full-screen is running.</summary>
public sealed class DisplayAwake
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsDisplayRequired = 0x00000002;
    private const uint EsSystemRequired = 0x00000001;
    private bool _active;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    public void Begin()
    {
        if (_active || !OperatingSystem.IsWindows()) return;
        try
        {
            SetThreadExecutionState(EsContinuous | EsDisplayRequired | EsSystemRequired);
            _active = true;
        }
        catch
        {
            // Not fatal.
        }
    }

    public void End()
    {
        if (!_active) return;
        try { SetThreadExecutionState(EsContinuous); } catch { /* ignore */ }
        _active = false;
    }
}
