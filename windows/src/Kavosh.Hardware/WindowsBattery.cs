using System.Runtime.InteropServices;
using System.Text;
using Kavosh.Core;
using Microsoft.Win32.SafeHandles;

namespace Kavosh.Hardware;

/// <summary>One battery reading. Windows reports capacities in mWh and power in mW.</summary>
public sealed class BatteryReading
{
    public int DesignCapacityMwh { get; init; }
    public int FullChargeCapacityMwh { get; init; }
    public int RemainingCapacityMwh { get; init; }
    public int CycleCount { get; init; }
    public int ChargePercent { get; init; }
    public bool IsCharging { get; init; }
    public bool IsPluggedIn { get; init; }
    public bool FullyCharged { get; init; }
    /// <summary>Negative while discharging; null when the battery doesn't say.</summary>
    public int? RateMw { get; init; }
    public int? VoltageMv { get; init; }
    public double? TemperatureCelsius { get; init; }
    public string? Manufacturer { get; init; }
    public string? DeviceName { get; init; }
    public string? Chemistry { get; init; }

    public double HealthPercent => DesignCapacityMwh > 0 ? Math.Min(100, 100.0 * FullChargeCapacityMwh / DesignCapacityMwh) : 0;

    /// <summary>Power drawn from the battery, in watts (only while discharging).</summary>
    public double? DischargeWatts => RateMw is < 0 ? -RateMw.Value / 1000.0 : null;

    /// <summary>Turns a reading into the result stored for the run.</summary>
    public static BatteryHealthResult Result(BatteryReading? r, double? dischargeUnderLoad)
    {
        if (r is null || r.DesignCapacityMwh <= 0) return new BatteryHealthResult { BatteryPresent = false };
        double health = r.HealthPercent;
        return new BatteryHealthResult
        {
            BatteryPresent = true,
            DesignCapacityMwh = r.DesignCapacityMwh,
            FullChargeCapacityMwh = r.FullChargeCapacityMwh,
            HealthPercent = health,
            CycleCount = r.CycleCount,
            CurrentChargePercent = r.ChargePercent,
            IsCharging = r.IsCharging,
            IsPluggedIn = r.IsPluggedIn,
            DischargeRateWatts = dischargeUnderLoad,
            TemperatureCelsius = r.TemperatureCelsius,
            Manufacturer = r.Manufacturer,
            DeviceName = r.DeviceName,
            Chemistry = r.Chemistry,
            Issues = BatteryHealthResult.EvaluateIssues(health, r.CycleCount, r.IsPluggedIn, r.IsCharging, r.ChargePercent, r.FullyCharged),
        };
    }
}

/// <summary>Reads the battery through the Windows battery class driver (no administrator rights needed).</summary>
internal static unsafe class WindowsBattery
{
    public static BatteryReading? Read()
    {
        try
        {
            foreach (string path in BatteryDevicePaths())
            {
                var reading = ReadDevice(path);
                if (reading is not null) return reading;
            }
        }
        catch
        {
            // Fall through: no battery information.
        }
        return null;
    }

    private static List<string> BatteryDevicePaths()
    {
        var paths = new List<string>();
        var guid = Native.GUID_DEVCLASS_BATTERY;
        IntPtr set = Native.SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return paths;
        try
        {
            for (uint index = 0; index < 8; index++)
            {
                var data = new Native.SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>() };
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data)) break;
                Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, null, 0, out uint required, IntPtr.Zero);
                if (required == 0) continue;
                var detailBuffer = new byte[required + 8];
                fixed (byte* detail = detailBuffer)
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on 64-bit Windows, 6 on 32-bit.
                    *(uint*)detail = IntPtr.Size == 8 ? 8u : 6u;
                    if (!Native.SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, required, out _, IntPtr.Zero)) continue;
                    string? path = Marshal.PtrToStringUni((IntPtr)(detail + 4));
                    if (!string.IsNullOrEmpty(path)) paths.Add(path);
                }
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }
        return paths;
    }

    private static BatteryReading? ReadDevice(string path)
    {
        using SafeFileHandle handle = Native.CreateFileW(path, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        uint timeout = 0, tag = 0;
        if (!Native.DeviceIoControl(handle, Native.IOCTL_BATTERY_QUERY_TAG, &timeout, sizeof(uint), &tag, sizeof(uint), out _, IntPtr.Zero) || tag == 0)
            return null;

        var query = new Native.BATTERY_QUERY_INFORMATION { BatteryTag = tag, InformationLevel = Native.BatteryInformation };
        Native.BATTERY_INFORMATION info;
        if (!Native.DeviceIoControl(handle, Native.IOCTL_BATTERY_QUERY_INFORMATION, &query, (uint)sizeof(Native.BATTERY_QUERY_INFORMATION),
                &info, (uint)sizeof(Native.BATTERY_INFORMATION), out _, IntPtr.Zero))
            return null;
        // Relative-capacity batteries (rare, some UPS units) report percentages, not mWh.
        if ((info.Capabilities & Native.BATTERY_CAPACITY_RELATIVE) != 0 || info.DesignedCapacity == 0) return null;

        var wait = new Native.BATTERY_WAIT_STATUS { BatteryTag = tag };
        Native.BATTERY_STATUS status;
        bool hasStatus = Native.DeviceIoControl(handle, Native.IOCTL_BATTERY_QUERY_STATUS, &wait, (uint)sizeof(Native.BATTERY_WAIT_STATUS),
            &status, (uint)sizeof(Native.BATTERY_STATUS), out _, IntPtr.Zero);

        double? temperature = null;
        query.InformationLevel = Native.BatteryTemperature;
        uint tenthsKelvin = 0;
        if (Native.DeviceIoControl(handle, Native.IOCTL_BATTERY_QUERY_INFORMATION, &query, (uint)sizeof(Native.BATTERY_QUERY_INFORMATION),
                &tenthsKelvin, sizeof(uint), out _, IntPtr.Zero) && tenthsKelvin > 2000)
        {
            double c = tenthsKelvin / 10.0 - 273.15;
            if (c is > 0 and < 90) temperature = c;
        }

        string? manufacturer = QueryString(handle, tag, Native.BatteryManufactureName);
        string? deviceName = QueryString(handle, tag, Native.BatteryDeviceName);

        int chargePercent = 0;
        if (Native.GetSystemPowerStatus(out var power) && power.BatteryLifePercent <= 100) chargePercent = power.BatteryLifePercent;
        else if (hasStatus && status.Capacity != Native.BATTERY_UNKNOWN_CAPACITY && info.FullChargedCapacity > 0)
            chargePercent = (int)Math.Round(100.0 * status.Capacity / info.FullChargedCapacity);

        bool pluggedIn = hasStatus ? (status.PowerState & Native.BATTERY_POWER_ON_LINE) != 0 : power.ACLineStatus == 1;
        bool charging = hasStatus && (status.PowerState & Native.BATTERY_CHARGING) != 0;
        bool fullyCharged = pluggedIn && !charging && chargePercent >= 97;
        int? rate = hasStatus && status.Rate != Native.BATTERY_UNKNOWN_RATE && status.Rate != 0 ? status.Rate : null;
        int? voltage = hasStatus && status.Voltage != Native.BATTERY_UNKNOWN_VOLTAGE && status.Voltage > 0 ? (int)status.Voltage : null;

        return new BatteryReading
        {
            DesignCapacityMwh = (int)info.DesignedCapacity,
            FullChargeCapacityMwh = (int)info.FullChargedCapacity,
            RemainingCapacityMwh = hasStatus && status.Capacity != Native.BATTERY_UNKNOWN_CAPACITY ? (int)status.Capacity : 0,
            CycleCount = (int)info.CycleCount,
            ChargePercent = Math.Clamp(chargePercent, 0, 100),
            IsCharging = charging,
            IsPluggedIn = pluggedIn,
            FullyCharged = fullyCharged,
            RateMw = rate,
            VoltageMv = voltage,
            TemperatureCelsius = temperature,
            Manufacturer = manufacturer,
            DeviceName = deviceName,
            Chemistry = ChemistryName(info.Chemistry),
        };
    }

    private static string? QueryString(SafeFileHandle handle, uint tag, int level)
    {
        var query = new Native.BATTERY_QUERY_INFORMATION { BatteryTag = tag, InformationLevel = level };
        const int size = 256;
        char* text = stackalloc char[size / 2];
        new Span<char>(text, size / 2).Clear();
        if (!Native.DeviceIoControl(handle, Native.IOCTL_BATTERY_QUERY_INFORMATION, &query, (uint)sizeof(Native.BATTERY_QUERY_INFORMATION),
                text, size, out uint returned, IntPtr.Zero) || returned < 2)
            return null;
        string value = new string(text, 0, (int)Math.Min(returned / 2, size / 2)).TrimEnd('\0').Trim();
        return value.Length == 0 ? null : value;
    }

    private static string? ChemistryName(uint raw)
    {
        // Four ASCII characters, e.g. "LION", "LiP ".
        var bytes = BitConverter.GetBytes(raw);
        string code = Encoding.ASCII.GetString(bytes).Trim('\0', ' ').ToUpperInvariant();
        return code switch
        {
            "" => null,
            "LION" or "LI-I" or "LI I" => "Lithium-ion",
            "LIP" or "LIPO" => "Lithium polymer",
            "NIMH" => "Nickel-metal hydride",
            "NICD" => "Nickel-cadmium",
            "PBAC" => "Lead-acid",
            "RAM" => "Rechargeable alkaline",
            _ => code,
        };
    }
}
