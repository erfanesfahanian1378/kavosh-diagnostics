using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Kavosh.Core;
using Microsoft.Win32;

namespace Kavosh.Hardware;

/// <summary>Why some sensors may be missing, shown on the dashboard.</summary>
public sealed record SensorStatus(bool IsAdministrator, bool PawnIoInstalled, string? PawnIoVersion, bool SensorLibraryLoaded, string? Problem)
{
    /// <summary>CPU temperature and power need administrator rights and the PawnIO driver.</summary>
    public bool FullAccess => IsAdministrator && PawnIoInstalled && SensorLibraryLoaded;
}

public interface ITelemetrySource : IDisposable
{
    SensorSnapshot Sample();
    SensorStatus Status { get; }
}

/// <summary>
/// The one entry point the app uses for hardware. On Windows it reads the real hardware; on other
/// systems (development and tests) it falls back to what Linux /proc offers.
/// </summary>
public static class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Fast specs (no WMI). Safe on the UI thread.</summary>
    public static SystemSpecs QuickSpecs()
    {
        try
        {
            return IsWindows ? WindowsSystemProbe.QuickSpecs() : FallbackProbe.QuickSpecs();
        }
        catch
        {
            return FallbackProbe.QuickSpecs();
        }
    }

    /// <summary>Adds the slow details (WMI: model name, memory modules, display, drives). Background thread.</summary>
    public static void AddSlowDetails(SystemSpecs specs)
    {
        try
        {
            if (IsWindows) WindowsSystemProbe.AddSlowDetails(specs);
            else FallbackProbe.AddSlowDetails(specs);
        }
        catch
        {
            // Keep the quick specs.
        }
    }

    public static ITelemetrySource CreateTelemetry() => IsWindows ? new WindowsTelemetry() : new FallbackTelemetry();

    public static BatteryReading? ReadBattery() => IsWindows ? WindowsBattery.Read() : FallbackProbe.ReadBattery();

    public static DriveHealthReport ReadDriveHealth()
    {
        try
        {
            return IsWindows ? WindowsDrives.ReadHealth() : FallbackProbe.ReadDriveHealth();
        }
        catch
        {
            return new DriveHealthReport();
        }
    }

    public static List<StorageDeviceInfo> PhysicalDrives() => IsWindows ? WindowsDrives.PhysicalDrives() : new List<StorageDeviceInfo>();

    public static List<GpuAdapterInfo> GpuList() => GpuAdapters.List();

    /// <summary>Total installed memory in bytes.</summary>
    public static ulong PhysicalMemoryBytes
    {
        get
        {
            if (IsWindows)
            {
                var memory = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
                if (Native.GlobalMemoryStatusEx(ref memory)) return memory.ullTotalPhys;
            }
            return (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
    }

    /// <summary>
    /// Asks Windows not to slow this process down (EcoQoS) when its window is in the background, so
    /// a benchmark keeps running at full speed while the user looks at something else.
    /// </summary>
    public static unsafe void DisablePowerThrottling()
    {
        if (!IsWindows) return;
        try
        {
            var state = new Native.PROCESS_POWER_THROTTLING_STATE
            {
                Version = Native.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = 0,
            };
            Native.SetProcessInformation(Native.GetCurrentProcess(), Native.ProcessPowerThrottling, &state, (uint)sizeof(Native.PROCESS_POWER_THROTTLING_STATE));
        }
        catch
        {
            // Windows 10 before 1709 doesn't have this; nothing to do.
        }
    }
}

public static class Elevation
{
    public static bool IsAdministrator
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Starts a new copy of the app with administrator rights. Returns false if the user said no.</summary>
    public static bool RestartElevated(string arguments = "")
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe is null) return false;
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch
        {
            return false; // The UAC prompt was cancelled.
        }
    }
}

/// <summary>
/// PawnIO is the signed, open-source driver LibreHardwareMonitor uses to read CPU sensors. The app
/// never installs it by itself; it only checks whether the user installed it.
/// </summary>
public static class PawnIo
{
    public const string DownloadUrl = "https://pawnio.eu/";

    public static bool IsInstalled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                if (service is not null) return true;
            }
            catch
            {
                // Fall through to the library's own check.
            }
            try { return LhmSensors.PawnIoInstalled; } catch { return false; }
        }
    }

    public static string? Version
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return null;
            try { return LhmSensors.PawnIoVersion; } catch { return null; }
        }
    }
}
