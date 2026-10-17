#if os(macOS)
import Foundation
import KavoshSensors

/// Lists physical drives and reads their health.
public enum DriveReader {
    /// Every block storage device in the IORegistry (including disk images).
    public static func allDevices() -> [StorageDeviceInfo] {
        var unmanaged: Unmanaged<CFArray>?
        let count = kv_copy_storage_devices(&unmanaged)
        guard count >= 0, let array = unmanaged?.takeRetainedValue() as? [[String: Any]] else { return [] }
        return array.compactMap { item -> StorageDeviceInfo? in
            guard let bsd = item["bsdName"] as? String else { return nil }
            return StorageDeviceInfo(
                bsdName: bsd,
                model: (item["model"] as? String) ?? bsd,
                vendor: item["vendor"] as? String,
                sizeBytes: (item["size"] as? NSNumber)?.uint64Value ?? 0,
                medium: item["medium"] as? String,
                interconnect: item["interconnect"] as? String,
                location: item["location"] as? String,
                nvmeSmartCapable: (item["nvmeSmart"] as? Bool) ?? false,
                ataSmartCapable: (item["ataSmart"] as? Bool) ?? false,
                removable: (item["removable"] as? Bool) ?? false
            )
        }
    }

    /// One entry per physical drive, internal drives first.
    public static func physicalDrives() -> [StorageDeviceInfo] {
        let physical = allDevices().filter(DriveHealthEvaluator.isPhysical)
        return DriveHealthEvaluator.uniquePhysicalDrives(physical)
            .sorted { lhs, rhs in
                if lhs.isInternal != rhs.isInternal { return lhs.isInternal }
                return lhs.bsdName.localizedStandardCompare(rhs.bsdName) == .orderedAscending
            }
    }

    public static func nvmeLog(bsdName: String) -> NVMeSmartLog? {
        var bytes = [UInt8](repeating: 0, count: 512)
        let rc = bsdName.withCString { kv_nvme_read_smart($0, &bytes) }
        guard rc == 0 else { return nil }
        return NVMeSmartLog(bytes: bytes)
    }

    /// "Verified", "Failing", "Not Supported"… as `diskutil info` reports it.
    public static func macOSSmartStatus(bsdName: String) -> String? {
        guard let data = ProcessRunner.run("/usr/sbin/diskutil", ["info", "-plist", bsdName]),
              let plist = try? PropertyListSerialization.propertyList(from: data, options: [], format: nil) as? [String: Any] else {
            return nil
        }
        return plist["SMARTStatus"] as? String
    }

    /// Reads every physical drive's health. Takes well under a second.
    public static func readHealth() -> DriveHealthReport {
        let drives = physicalDrives().map { device -> DriveHealthInfo in
            let log = device.nvmeSmartCapable ? nvmeLog(bsdName: device.bsdName) : nil
            let status = macOSSmartStatus(bsdName: device.bsdName)
            return DriveHealthEvaluator.evaluate(device: device, smart: log, macOSStatus: status)
        }
        return DriveHealthReport(drives: drives)
    }
}
#endif
