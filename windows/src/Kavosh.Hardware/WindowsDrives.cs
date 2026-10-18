using Kavosh.Core;
using Microsoft.Win32.SafeHandles;

namespace Kavosh.Hardware;

/// <summary>Lists physical disks and reads each one's own health data.</summary>
internal static unsafe class WindowsDrives
{
    // MSFT_PhysicalDisk.BusType values.
    private static string BusName(long bus) => bus switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "FireWire",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "File Backed Virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Unknown",
    };

    private static bool IsExternalBus(string bus) => bus is "USB" or "SD" or "MMC" or "FireWire";

    public static List<StorageDeviceInfo> AllDevices()
    {
        var devices = new List<StorageDeviceInfo>();
        var disks = Wmi.Query("SELECT DeviceId, FriendlyName, Model, Manufacturer, SerialNumber, FirmwareVersion, Size, MediaType, BusType FROM MSFT_PhysicalDisk", Wmi.Storage);
        foreach (var d in disks)
        {
            string id = d.Str("DeviceId") ?? "";
            if (!int.TryParse(id, out _)) continue;
            string bus = BusName(d.Long("BusType") ?? 0);
            long media = d.Long("MediaType") ?? 0;
            devices.Add(new StorageDeviceInfo
            {
                Id = id,
                Model = d.Str("Model") ?? d.Str("FriendlyName") ?? "",
                Vendor = d.Str("Manufacturer"),
                SerialNumber = d.Str("SerialNumber"),
                Firmware = d.Str("FirmwareVersion"),
                SizeBytes = d.ULong("Size") ?? 0,
                Medium = media switch { 3 => "Rotational", 4 => "Solid State", 5 => "Storage Class Memory", _ => bus == "NVMe" ? "Solid State" : null },
                Interconnect = bus,
                Location = IsExternalBus(bus) ? "External" : "Internal",
                Removable = IsExternalBus(bus),
            });
        }
        if (devices.Count == 0)
        {
            // Older systems: the classic WMI class.
            foreach (var d in Wmi.Query("SELECT Index, Model, Size, InterfaceType, MediaType, SerialNumber, FirmwareRevision FROM Win32_DiskDrive"))
            {
                string bus = (d.Str("InterfaceType") ?? "").ToUpperInvariant() switch
                {
                    "USB" => "USB",
                    "IDE" => "SATA",
                    "SCSI" => (d.Str("Model") ?? "").Contains("NVMe", StringComparison.OrdinalIgnoreCase) ? "NVMe" : "SCSI",
                    var other => other,
                };
                string mediaType = d.Str("MediaType") ?? "";
                bool external = bus == "USB" || mediaType.Contains("External", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("Removable", StringComparison.OrdinalIgnoreCase);
                devices.Add(new StorageDeviceInfo
                {
                    Id = (d.Long("Index") ?? devices.Count).ToString(),
                    Model = d.Str("Model") ?? "",
                    SerialNumber = d.Str("SerialNumber"),
                    Firmware = d.Str("FirmwareRevision"),
                    SizeBytes = d.ULong("Size") ?? 0,
                    Interconnect = bus,
                    Location = external ? "External" : "Internal",
                    Removable = external,
                });
            }
        }
        return devices.OrderBy(d => int.TryParse(d.Id, out int n) ? n : 999).ToList();
    }

    public static List<StorageDeviceInfo> PhysicalDrives() =>
        DriveHealthEvaluator.UniquePhysicalDrives(AllDevices().Where(DriveHealthEvaluator.IsPhysical));

    public static DriveHealthReport ReadHealth()
    {
        var drives = PhysicalDrives();
        var health = Wmi.Query("SELECT DeviceId, HealthStatus FROM MSFT_PhysicalDisk", Wmi.Storage)
            .Where(r => r.Str("DeviceId") is not null)
            .GroupBy(r => r.Str("DeviceId")!)
            .ToDictionary(g => g.Key, g => g.First().Long("HealthStatus"));
        var reliability = ReadReliabilityCounters();

        var report = new DriveHealthReport();
        foreach (var device in drives)
        {
            DriveReadings? readings = null;
            if (device.IsNvme || device.Interconnect is "SCSI" or "RAID" or "Unknown")
                readings = ReadNvmeLog(device.Id)?.ToReadings();
            if (readings is null && !device.IsNvme)
                readings = ReadAtaSmart(device.Id);
            if (reliability.TryGetValue(device.Id, out var counters))
                readings = readings is null ? counters : readings.FillFrom(counters);

            string? osStatus = health.TryGetValue(device.Id, out long? code) ? code switch
            {
                0 => "Healthy",
                1 => "Warning",
                2 => "Unhealthy",
                _ => null,
            } : null;
            report.Drives.Add(DriveHealthEvaluator.Evaluate(device, readings, osStatus));
        }
        return report;
    }

    private static SafeFileHandle? OpenDisk(string id, bool forWriting)
    {
        uint access = forWriting ? Native.GENERIC_READ | Native.GENERIC_WRITE : 0;
        var handle = Native.CreateFileW($@"\\.\PhysicalDrive{id}", access, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }

    /// <summary>Asks the Windows NVMe driver for the drive's SMART / Health Information log (needs administrator).</summary>
    public static NVMeSmartLog? ReadNvmeLog(string id)
    {
        const int headerSize = 8;          // STORAGE_PROPERTY_QUERY up to AdditionalParameters
        const int protocolSize = 40;       // STORAGE_PROTOCOL_SPECIFIC_DATA
        const int logSize = 512;
        const int total = headerSize + protocolSize + logSize;
        byte* buffer = stackalloc byte[total];
        foreach (bool forWriting in new[] { true, false })
        {
            using var handle = OpenDisk(id, forWriting);
            if (handle is null) continue;
            new Span<byte>(buffer, total).Clear();
            *(int*)(buffer + 0) = Native.StorageDeviceProtocolSpecificProperty;
            *(int*)(buffer + 4) = Native.PropertyStandardQuery;
            uint* protocol = (uint*)(buffer + headerSize);
            protocol[0] = Native.ProtocolTypeNvme;
            protocol[1] = Native.NVMeDataTypeLogPage;
            protocol[2] = Native.NVME_LOG_PAGE_HEALTH_INFO;
            protocol[3] = 0;                   // request sub-value
            protocol[4] = protocolSize;        // data offset (from the protocol data)
            protocol[5] = logSize;             // data length
            if (!Native.DeviceIoControl(handle, Native.IOCTL_STORAGE_QUERY_PROPERTY, buffer, total, buffer, total, out uint returned, IntPtr.Zero))
                continue;
            // Output: STORAGE_PROTOCOL_DATA_DESCRIPTOR { Version, Size, STORAGE_PROTOCOL_SPECIFIC_DATA }.
            uint version = *(uint*)buffer;
            uint size = *(uint*)(buffer + 4);
            if (version != headerSize + protocolSize || size != headerSize + protocolSize) continue;
            uint dataOffset = *(uint*)(buffer + headerSize + 16);
            uint dataLength = *(uint*)(buffer + headerSize + 20);
            if (dataLength < logSize || headerSize + dataOffset + logSize > total) continue;
            var log = NVMeSmartLog.Parse(new ReadOnlySpan<byte>(buffer + headerSize + dataOffset, logSize));
            if (log is not null) return log;
        }
        return null;
    }

    /// <summary>
    /// ATA SMART attributes. IOCTL_STORAGE_PREDICT_FAILURE returns the drive's attribute table in
    /// its vendor-specific block; WMI adds the thresholds when we are administrator.
    /// </summary>
    public static DriveReadings? ReadAtaSmart(string id)
    {
        byte[]? data = null;
        bool predictsFailure = false;
        using (var handle = OpenDisk(id, forWriting: false) ?? OpenDisk(id, forWriting: true))
        {
            if (handle is not null)
            {
                const int size = 4 + 512;
                byte* buffer = stackalloc byte[size];
                new Span<byte>(buffer, size).Clear();
                if (Native.DeviceIoControl(handle, Native.IOCTL_STORAGE_PREDICT_FAILURE, null, 0, buffer, size, out uint returned, IntPtr.Zero) && returned >= size)
                {
                    predictsFailure = *(uint*)buffer != 0;
                    data = new ReadOnlySpan<byte>(buffer + 4, 512).ToArray();
                }
            }
        }

        byte[]? thresholds = null;
        string? pnp = Wmi.Query($"SELECT PNPDeviceID FROM Win32_DiskDrive WHERE Index = {int.Parse(id)}").FirstOrDefault()?.Str("PNPDeviceID");
        if (pnp is not null)
        {
            static bool Matches(Dictionary<string, object?> row, string pnpId) =>
                (row.Str("InstanceName") ?? "").StartsWith(pnpId, StringComparison.OrdinalIgnoreCase);
            if (data is null || data.All(b => b == 0))
            {
                var smart = Wmi.Query("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData", Wmi.RootWmi).FirstOrDefault(r => Matches(r, pnp));
                data = smart?.Bytes("VendorSpecific") ?? data;
            }
            var limits = Wmi.Query("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictThresholds", Wmi.RootWmi).FirstOrDefault(r => Matches(r, pnp));
            thresholds = limits?.Bytes("VendorSpecific");
            var status = Wmi.Query("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", Wmi.RootWmi).FirstOrDefault(r => Matches(r, pnp));
            if (status?.Bool("PredictFailure") == true) predictsFailure = true;
        }

        if (data is null || data.All(b => b == 0)) return predictsFailure ? new DriveReadings { Source = "ATA SMART", PredictsFailure = true } : null;
        var parsed = AtaSmartData.Parse(data, thresholds ?? ReadOnlySpan<byte>.Empty);
        if (parsed is null) return predictsFailure ? new DriveReadings { Source = "ATA SMART", PredictsFailure = true } : null;
        var readings = parsed.ToReadings();
        readings.PredictsFailure |= predictsFailure;
        return readings;
    }

    /// <summary>Windows' own reliability counters (Storage Spaces API, needs administrator).</summary>
    private static Dictionary<string, DriveReadings> ReadReliabilityCounters()
    {
        var result = new Dictionary<string, DriveReadings>();
        foreach (var row in Wmi.Query("SELECT DeviceId, Temperature, Wear, PowerOnHours, StartStopCycleCount, ReadErrorsUncorrected, WriteErrorsUncorrected FROM MSFT_StorageReliabilityCounter", Wmi.Storage))
        {
            if (row.Str("DeviceId") is not { } id) continue;
            var r = new DriveReadings { Source = "Windows reliability counters" };
            if (row.Long("Temperature") is { } t && t is > 0 and < 120) r.TemperatureCelsius = t;
            if (row.Long("Wear") is { } wear && wear is >= 0 and <= 255 && row["Wear"] is not null) r.PercentageUsed = (int)wear;
            if (row.Long("PowerOnHours") is { } hours && hours > 0) r.PowerOnHours = hours;
            if (row.Long("StartStopCycleCount") is { } cycles && cycles > 0) r.PowerCycles = cycles;
            long? readErrors = row.Long("ReadErrorsUncorrected");
            long? writeErrors = row.Long("WriteErrorsUncorrected");
            if (readErrors is not null || writeErrors is not null) r.MediaErrors = (readErrors ?? 0) + (writeErrors ?? 0);
            result[id] = r;
        }
        return result;
    }
}
