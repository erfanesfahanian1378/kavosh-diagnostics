using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Kavosh.App.Stations;

/// <summary>
/// Base for one test station. A station starts its work the first time it appears and releases
/// everything (threads, camera, microphone, audio) when it is disposed — when the run moves on or
/// the user presses Stop.
/// </summary>
public abstract class Station : UserControl, IDisposable
{
    protected TestSuiteRunner Runner { get; }
    protected bool IsDisposed { get; private set; }
    private bool _started;

    protected Station(TestSuiteRunner runner)
    {
        Runner = runner;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_started) return;
        _started = true;
        Start();
    }

    /// <summary>Called once, when the station first appears.</summary>
    protected virtual void Start() { }

    /// <summary>Releases the station's resources. Safe to call more than once.</summary>
    protected virtual void Stop() { }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Stop();
    }

    /// <summary>Runs on the UI thread, unless the station has been disposed by then.</summary>
    protected void OnUi(Action action) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsDisposed) action();
    });

    /// <summary>Starts background work on its own thread.</summary>
    protected static void RunInBackground(Action work, string name)
    {
        var thread = new Thread(() => work()) { IsBackground = true, Name = name };
        thread.Start();
    }

    protected int? AutoAdvance => Runner.IsSingleTest ? null : 4;
}
