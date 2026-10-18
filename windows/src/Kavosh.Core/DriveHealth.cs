namespace Kavosh.Core;

/// <summary>The NVMe "SMART / Health Information" log page (512 bytes, NVMe spec section 5.14.1.2).</summary>
public sealed class NVMeSmartLog
{
    public byte CriticalWarning { get; private init; }
    public ushort TemperatureKelvin { get; private init; }
    public byte AvailableSpare { get; private init; }
    public byte AvailableSpareThreshold { get; private init; }
    public byte PercentageUsed { get; private init; }
    public double DataUnitsRead { get; private init; }
    public double DataUnitsWritten { get; private init; }
    public double PowerCycles { get; private init; }
    public double PowerOnHours { get; private init; }
    public double UnsafeShutdowns { get; private init; }
    public double MediaErrors { get; private init; }
    public double ErrorLogEntries { get; private init; }

    /// <summary>Parses the page; null when it is too short or the drive left it blank.</summary>
    public static NVMeSmartLog? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 512) return null;
        // An all-zero page means the drive didn't fill it in.
        if (bytes[..192].IndexOfAnyExcept((byte)0) < 0) return null;
        return new NVMeSmartLog
        {
            CriticalWarning = bytes[0],
            TemperatureKelvin = (ushort)(bytes[1] | (bytes[2] << 8)),
            AvailableSpare = bytes[3],
            AvailableSpareThreshold = bytes[4],
            PercentageUsed = bytes[5],
            DataUnitsRead = UInt128(bytes, 32),
            DataUnitsWritten = UInt128(bytes, 48),
            PowerCycles = UInt128(bytes, 112),
            PowerOnHours = UInt128(bytes, 128),
            UnsafeShutdowns = UInt128(bytes, 144),
            MediaErrors = UInt128(bytes, 160),
            ErrorLogEntries = UInt128(bytes, 176),
        };
    }

    /// <summary>16-byte little-endian counter, as a double (exact up to 2^53).</summary>
    private static double UInt128(ReadOnlySpan<byte> b, int offset)
    {
        double value = 0;
        for (int i = 15; i >= 0; i--) value = value * 256 + b[offset + i];
        return value;
    }

    /// <summary>One data unit is 1,000 × 512 bytes.</summary>
    public double TerabytesWritten => DataUnitsWritten * 512_000 / 1e12;
    public double TerabytesRead => DataUnitsRead * 512_000 / 1e12;

    public double? TemperatureCelsius => TemperatureKelvin > 0 ? TemperatureKelvin - 273.15 : null;

    public DriveReadings ToReadings() => new()
    {
        Source = "NVMe health log",
        CriticalWarning = CriticalWarning,
        AvailableSparePercent = AvailableSpare,
        AvailableSpareThresholdPercent = AvailableSpareThreshold,
        PercentageUsed = PercentageUsed,
        DataWrittenTB = TerabytesWritten,
        DataReadTB = TerabytesRead,
        PowerOnHours = (long)PowerOnHours,
        PowerCycles = (long)PowerCycles,
        UnsafeShutdowns = (long)UnsafeShutdowns,
        MediaErrors = (long)MediaErrors,
        TemperatureCelsius = TemperatureCelsius,
    };
}

/// <summary>One attribute from an ATA (SATA) drive's SMART table.</summary>
public readonly record struct AtaSmartAttribute(byte Id, byte Current, byte Worst, ulong Raw, byte Threshold);

/// <summary>
/// The ATA SMART attribute table (the "SMART READ DATA" layout: a 2-byte revision, then 30 entries
/// of 12 bytes). Windows hands it out through WMI (MSStorageDriver_ATAPISmartData).
/// </summary>
public sealed class AtaSmartData
{
    public IReadOnlyList<AtaSmartAttribute> Attributes { get; private init; } = Array.Empty<AtaSmartAttribute>();

    /// <param name="data">The attribute table (362 or 512 bytes).</param>
    /// <param name="thresholds">The matching threshold table (same layout; byte 1 of each entry is the threshold), if known.</param>
    public static AtaSmartData? Parse(ReadOnlySpan<byte> data, ReadOnlySpan<byte> thresholds = default)
    {
        if (data.Length < 2 + 12) return null;
        var limits = new Dictionary<byte, byte>();
        for (int offset = 2; offset + 12 <= thresholds.Length && offset < 2 + 30 * 12; offset += 12)
        {
            if (thresholds[offset] != 0) limits[thresholds[offset]] = thresholds[offset + 1];
        }
        var list = new List<AtaSmartAttribute>();
        for (int offset = 2; offset + 12 <= data.Length && offset < 2 + 30 * 12; offset += 12)
        {
            byte id = data[offset];
            if (id == 0) continue;
            ulong raw = 0;
            for (int i = 5; i >= 0; i--) raw = (raw << 8) | data[offset + 5 + i];
            list.Add(new AtaSmartAttribute(id, data[offset + 3], data[offset + 4], raw, limits.GetValueOrDefault(id)));
        }
        return list.Count == 0 ? null : new AtaSmartData { Attributes = list };
    }

    public AtaSmartAttribute? this[byte id]
    {
        get
        {
            foreach (var a in Attributes) if (a.Id == id) return a;
            return null;
        }
    }

    /// <summary>An attribute whose normalised value has dropped to its failure threshold.</summary>
    public bool AnyAttributeFailing => Attributes.Any(a => a.Threshold > 0 && a.Threshold != 0xFE && a.Current <= a.Threshold);

    public DriveReadings ToReadings()
    {
        var r = new DriveReadings { Source = "ATA SMART" };
        if (this[5] is { } realloc) r.ReallocatedSectors = (long)(realloc.Raw & 0xFFFFFFFF);
        if (this[9] is { } poh) r.PowerOnHours = (long)(poh.Raw & 0xFFFFFFFF);
        if (this[12] is { } cycles) r.PowerCycles = (long)(cycles.Raw & 0xFFFFFFFF);
        if ((this[174] ?? this[192]) is { } unsafeOff) r.UnsafeShutdowns = (long)(unsafeOff.Raw & 0xFFFFFFFF);
        if (this[197] is { } pending) r.PendingSectors = (long)(pending.Raw & 0xFFFFFFFF);
        long uncorrectable = 0;
        bool hasUncorrectable = false;
        foreach (byte id in new byte[] { 187, 198 })
        {
            if (this[id] is { } u)
            {
                uncorrectable += (long)(u.Raw & 0xFFFF);
                hasUncorrectable = true;
            }
        }
        if (hasUncorrectable) r.MediaErrors = uncorrectable;
        if ((this[194] ?? this[190]) is { } temp)
        {
            int celsius = (int)(temp.Raw & 0xFF);
            if (celsius is > 0 and < 120) r.TemperatureCelsius = celsius;
        }
        // Wear: these attributes' normalised value counts down from 100 (life left).
        foreach (byte id in new byte[] { 231, 233, 177, 202, 169 })
        {
            if (this[id] is { } wear && wear.Current is > 0 and <= 100)
            {
                r.PercentageUsed = 100 - wear.Current;
                break;
            }
        }
        r.PredictsFailure = AnyAttributeFailing;
        return r;
    }
}

/// <summary>Health numbers for one drive, whichever source they came from.</summary>
public sealed class DriveReadings
{
    public string Source { get; set; } = "";
    public int? CriticalWarning { get; set; }
    public int? AvailableSparePercent { get; set; }
    public int? AvailableSpareThresholdPercent { get; set; }
    public int? PercentageUsed { get; set; }
    public double? DataWrittenTB { get; set; }
    public double? DataReadTB { get; set; }
    public long? PowerOnHours { get; set; }
    public long? PowerCycles { get; set; }
    public long? UnsafeShutdowns { get; set; }
    public long? MediaErrors { get; set; }
    public long? ReallocatedSectors { get; set; }
    public long? PendingSectors { get; set; }
    public double? TemperatureCelsius { get; set; }
    public bool PredictsFailure { get; set; }

    /// <summary>Fills the values this source lacks from a second source.</summary>
    public DriveReadings FillFrom(DriveReadings? other)
    {
        if (other is null) return this;
        CriticalWarning ??= other.CriticalWarning;
        AvailableSparePercent ??= other.AvailableSparePercent;
        AvailableSpareThresholdPercent ??= other.AvailableSpareThresholdPercent;
        PercentageUsed ??= other.PercentageUsed;
        DataWrittenTB ??= other.DataWrittenTB;
        DataReadTB ??= other.DataReadTB;
        PowerOnHours ??= other.PowerOnHours;
        PowerCycles ??= other.PowerCycles;
        UnsafeShutdowns ??= other.UnsafeShutdowns;
        MediaErrors ??= other.MediaErrors;
        ReallocatedSectors ??= other.ReallocatedSectors;
        PendingSectors ??= other.PendingSectors;
        TemperatureCelsius ??= other.TemperatureCelsius;
        PredictsFailure |= other.PredictsFailure;
        if (string.IsNullOrEmpty(Source)) Source = other.Source;
        return this;
    }
}

public static class DriveHealthEvaluator
{
    /// <summary>
    /// Builds the health entry for one drive from its own health readings and the status Windows
    /// reports (MSFT_PhysicalDisk.HealthStatus: Healthy / Warning / Unhealthy).
    /// </summary>
    public static DriveHealthInfo Evaluate(StorageDeviceInfo device, DriveReadings? readings, string? osHealthStatus)
    {
        var info = new DriveHealthInfo
        {
            Id = device.Id,
            Model = device.DisplayName,
            SizeBytes = device.SizeBytes,
            BusType = device.Interconnect,
            MediaType = device.Medium,
            IsInternal = device.IsInternal,
            OsHealthStatus = osHealthStatus,
            Source = readings?.Source,
        };
        var issues = new List<DriveHealthIssue>();
        string status = (osHealthStatus ?? "").Trim().ToLowerInvariant();
        bool osFailing = status.Contains("unhealthy") || status.Contains("fail");
        bool osWarning = !osFailing && status.Contains("warn");
        bool osKnown = status == "healthy" || status == "verified" || osFailing || osWarning;

        if (osFailing) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.OsFailing));
        else if (osWarning) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.OsWarning));

        if (readings is not null)
        {
            info.CriticalWarning = readings.CriticalWarning;
            info.AvailableSparePercent = readings.AvailableSparePercent;
            info.AvailableSpareThresholdPercent = readings.AvailableSpareThresholdPercent;
            info.PercentageUsed = readings.PercentageUsed;
            info.DataWrittenTB = readings.DataWrittenTB;
            info.DataReadTB = readings.DataReadTB;
            info.PowerOnHours = readings.PowerOnHours;
            info.PowerCycles = readings.PowerCycles;
            info.UnsafeShutdowns = readings.UnsafeShutdowns;
            info.MediaErrors = readings.MediaErrors;
            info.ReallocatedSectors = readings.ReallocatedSectors;
            info.PendingSectors = readings.PendingSectors;
            info.TemperatureCelsius = readings.TemperatureCelsius;

            int warning = readings.CriticalWarning ?? 0;
            if ((warning & 0x01) != 0) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.SpareBelowThreshold));
            if ((warning & 0x02) != 0) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.TemperatureWarning));
            if ((warning & 0x04) != 0) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.ReliabilityDegraded));
            if ((warning & 0x08) != 0) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.ReadOnly));
            if ((warning & 0x10) != 0) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.BackupPowerFailed));
            if (readings.PercentageUsed is >= 100) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.EnduranceUsedUp));
            else if (readings.PercentageUsed is >= 90) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.EnduranceAlmostUsedUp));
            if (readings.MediaErrors is > 0) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.MediaErrors, readings.MediaErrors.Value));
            if (readings.ReallocatedSectors is > 0)
                issues.Add(new DriveHealthIssue(DriveHealthIssueKind.ReallocatedSectors, readings.ReallocatedSectors.Value));
            if (readings.PendingSectors is > 0)
                issues.Add(new DriveHealthIssue(DriveHealthIssueKind.PendingSectors, readings.PendingSectors.Value));
            if (readings.PredictsFailure) issues.Add(new DriveHealthIssue(DriveHealthIssueKind.PredictsFailure));
        }

        info.Issues = issues;

        if (readings is not null || osKnown)
        {
            double score = 100;
            // Normal wear costs little; it only starts to matter past 10%.
            if (info.PercentageUsed is { } used) score -= Math.Max(0, used - 10) * 0.5;
            foreach (var issue in issues) score -= issue.Deduction;
            bool fatal = issues.Any(i => i.Kind is DriveHealthIssueKind.OsFailing or DriveHealthIssueKind.PredictsFailure);
            if (fatal) score = 0;
            info.Score = Math.Clamp(score, 0, 100);
            if (info.Score < 50 || fatal || issues.Any(i => i.Kind == DriveHealthIssueKind.ReadOnly))
                info.Status = DriveHealthStatus.Failing;
            else if (issues.Count > 0 || info.Score < 80)
                info.Status = DriveHealthStatus.Warning;
            else
                info.Status = DriveHealthStatus.Good;
        }
        else
        {
            info.Status = DriveHealthStatus.Unknown;
            info.Score = null;
        }
        return info;
    }

    /// <summary>Keep one entry per physical drive: the largest of the entries with the same model and bus.</summary>
    public static List<StorageDeviceInfo> UniquePhysicalDrives(IEnumerable<StorageDeviceInfo> devices)
    {
        var best = new Dictionary<string, StorageDeviceInfo>();
        var order = new List<string>();
        foreach (var device in devices)
        {
            string key = $"{device.Model}|{device.SerialNumber}|{device.Interconnect}|{device.Location}";
            if (best.TryGetValue(key, out var existing))
            {
                if (device.SizeBytes > existing.SizeBytes) best[key] = device;
            }
            else
            {
                best[key] = device;
                order.Add(key);
            }
        }
        return order.Select(k => best[k]).ToList();
    }

    /// <summary>Virtual disks (VHDs, Storage Spaces, RAM disks) aren't physical drives.</summary>
    public static bool IsPhysical(StorageDeviceInfo device)
    {
        string bus = (device.Interconnect ?? "").ToLowerInvariant();
        if (bus.Contains("virtual") || bus.Contains("file backed") || bus.Contains("spaces")) return false;
        string model = device.Model.ToLowerInvariant();
        if (model.Contains("virtual disk") || model.Contains("disk image")) return false;
        return true;
    }
}
