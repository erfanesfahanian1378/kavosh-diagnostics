using System.Runtime.InteropServices;
using Avalonia;

namespace Kavosh.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
        {
            if (args[0] == "--screenshots") return Screenshots.Run(args.Skip(1).ToArray());
            ConsoleAttach.Ensure();
            return Cli.Run(args);
        }
        App.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }
}

/// <summary>
/// Kavosh is a Windows GUI program; when it's started from a terminal with a command-line flag it
/// writes to that terminal instead.
/// </summary>
internal static class ConsoleAttach
{
    private const int StdOutputHandle = -11;
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    public static void Ensure()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            // Redirected output (pipes, CI logs) already works; only an interactive console needs attaching.
            IntPtr handle = GetStdHandle(StdOutputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                if (AttachConsole(AttachParentProcess))
                {
                    var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                    Console.SetOut(stdout);
                    Console.SetError(stdout);
                    Console.WriteLine();
                }
            }
        }
        catch
        {
            // No console to write to.
        }
    }
}
