import Foundation

/// The NVMe "SMART / Health Information" log page (512 bytes, NVMe spec section 5.14.1.2).
public struct NVMeSmartLog: Equatable, Sendable {
    public var criticalWarning: UInt8
    public var temperatureKelvin: UInt16
    public var availableSpare: UInt8
    public var availableSpareThreshold: UInt8
    public var percentageUsed: UInt8
    public var dataUnitsRead: Double
    public var dataUnitsWritten: Double
    public var powerCycles: Double
    public var powerOnHours: Double
    public var unsafeShutdowns: Double
    public var mediaErrors: Double
    public var errorLogEntries: Double

    public init?(bytes: [UInt8]) {
        guard bytes.count >= 512 else { return nil }
        // An all-zero page means the drive didn't fill it in.
        guard bytes.prefix(192).contains(where: { $0 != 0 }) else { return nil }
        criticalWarning = bytes[0]
        temperatureKelvin = UInt16(bytes[1]) | (UInt16(bytes[2]) << 8)
        availableSpare = bytes[3]
        availableSpareThreshold = bytes[4]
        percentageUsed = bytes[5]
        dataUnitsRead = NVMeSmartLog.uint128(bytes, 32)
        dataUnitsWritten = NVMeSmartLog.uint128(bytes, 48)
        powerCycles = NVMeSmartLog.uint128(bytes, 112)
        powerOnHours = NVMeSmartLog.uint128(bytes, 128)
        unsafeShutdowns = NVMeSmartLog.uint128(bytes, 144)
        mediaErrors = NVMeSmartLog.uint128(bytes, 160)
        errorLogEntries = NVMeSmartLog.uint128(bytes, 176)
    }

    /// 16-byte little-endian counter, as a Double (exact up to 2^53).
    static func uint128(_ b: [UInt8], _ offset: Int) -> Double {
        var value = 0.0
        for i in stride(from: 15, through: 0, by: -1) {
            value = value * 256 + Double(b[offset + i])
        }
        return value
    }

    /// One data unit is 1,000 × 512 bytes.
    public var terabytesWritten: Double { dataUnitsWritten * 512_000 / 1e12 }
    public var terabytesRead: Double { dataUnitsRead * 512_000 / 1e12 }

    public var temperatureCelsius: Double? {
        temperatureKelvin > 0 ? Double(temperatureKelvin) - 273.15 : nil
    }
}

public enum DriveHealthEvaluator {
    /// Builds the health entry for one drive from its SMART log and the status macOS reports
    /// (`diskutil info`'s "SMARTStatus": Verified / Failing / Not Supported).
    public static func evaluate(device: StorageDeviceInfo, smart: NVMeSmartLog?, macOSStatus: String?) -> DriveHealthInfo {
        var info = DriveHealthInfo(id: device.bsdName, model: device.displayName, sizeBytes: device.sizeBytes,
                                   busType: device.interconnect, mediaType: device.medium, isInternal: device.isInternal,
                                   macOSSmartStatus: macOSStatus)
        var issues: [DriveHealthIssue] = []
        let status = (macOSStatus ?? "").lowercased()
        let macOSKnown = status == "verified" || status.contains("fail") || status.contains("warn")

        if status.contains("fail") {
            issues.append(.macOSFailing)
        } else if status.contains("warn") || status == "about to fail" {
            issues.append(.macOSWarning)
        }

        if let log = smart {
            info.criticalWarning = Int(log.criticalWarning)
            info.availableSparePercent = Int(log.availableSpare)
            info.availableSpareThresholdPercent = Int(log.availableSpareThreshold)
            info.percentageUsed = Int(log.percentageUsed)
            info.dataWrittenTB = log.terabytesWritten
            info.dataReadTB = log.terabytesRead
            info.powerOnHours = Int(log.powerOnHours)
            info.powerCycles = Int(log.powerCycles)
            info.unsafeShutdowns = Int(log.unsafeShutdowns)
            info.mediaErrors = Int(log.mediaErrors)
            info.temperatureCelsius = log.temperatureCelsius

            let warning = log.criticalWarning
            if warning & 0x01 != 0 { issues.append(.spareBelowThreshold) }
            if warning & 0x02 != 0 { issues.append(.temperatureWarning) }
            if warning & 0x04 != 0 { issues.append(.reliabilityDegraded) }
            if warning & 0x08 != 0 { issues.append(.readOnly) }
            if warning & 0x10 != 0 { issues.append(.backupPowerFailed) }
            if log.percentageUsed >= 100 {
                issues.append(.enduranceUsedUp)
            } else if log.percentageUsed >= 90 {
                issues.append(.enduranceAlmostUsedUp)
            }
            if log.mediaErrors > 0 { issues.append(.mediaErrors(Int(log.mediaErrors))) }
        }

        info.issues = issues

        if smart != nil || macOSKnown {
            var score = 100.0
            if let used = info.percentageUsed {
                // Normal wear costs little; it only starts to matter past 10%.
                score -= max(0, Double(used) - 10) * 0.5
            }
            for issue in issues { score -= issue.deduction }
            if issues.contains(.macOSFailing) { score = 0 }
            info.score = max(0, min(100, score))
            if info.score! < 50 || issues.contains(.macOSFailing) || issues.contains(.readOnly) {
                info.status = .failing
            } else if !issues.isEmpty || info.score! < 80 {
                info.status = .warning
            } else {
                info.status = .good
            }
        } else {
            info.status = .unknown
            info.score = nil
        }
        return info
    }

    /// Apple silicon Macs show the internal SSD as several namespaces (disk0, disk1, disk2).
    /// Keep one entry per physical drive: the largest of the entries with the same model.
    public static func uniquePhysicalDrives(_ devices: [StorageDeviceInfo]) -> [StorageDeviceInfo] {
        var bestByKey: [String: StorageDeviceInfo] = [:]
        var order: [String] = []
        for device in devices {
            let key = "\(device.model)|\(device.interconnect ?? "")|\(device.location ?? "")"
            if let existing = bestByKey[key] {
                if device.sizeBytes > existing.sizeBytes { bestByKey[key] = device }
            } else {
                bestByKey[key] = device
                order.append(key)
            }
        }
        return order.compactMap { bestByKey[$0] }
    }

    /// Disk images and other virtual devices aren't physical drives.
    public static func isPhysical(_ device: StorageDeviceInfo) -> Bool {
        let bus = (device.interconnect ?? "").lowercased()
        if bus.contains("virtual") { return false }
        if device.model.lowercased().contains("disk image") { return false }
        return true
    }
}
