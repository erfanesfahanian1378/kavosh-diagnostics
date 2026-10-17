#if os(macOS)
import Foundation
import IOKit
import IOKit.ps

/// Reads the battery's own report from the AppleSmartBattery driver.
public enum BatteryReader {
    public struct Reading: Sendable {
        public var designCapacityMah: Int
        public var fullChargeCapacityMah: Int
        public var cycleCount: Int
        public var chargePercent: Int
        public var isCharging: Bool
        public var isPluggedIn: Bool
        public var fullyCharged: Bool
        public var amperageMa: Int
        public var voltageMv: Int
        public var temperatureCelsius: Double?
        public var condition: String?

        public var healthPercent: Double {
            designCapacityMah > 0 ? Double(fullChargeCapacityMah) / Double(designCapacityMah) * 100 : 0
        }

        /// Power flowing out of the battery right now, in watts (nil while charging).
        public var dischargeWatts: Double? {
            guard amperageMa < 0, voltageMv > 0 else { return nil }
            return Double(-amperageMa) * Double(voltageMv) / 1_000_000
        }
    }

    /// Returns nil on Macs without a battery.
    public static func read() -> Reading? {
        let service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSmartBattery"))
        guard service != 0 else { return nil }
        defer { IOObjectRelease(service) }

        var unmanaged: Unmanaged<CFMutableDictionary>?
        guard IORegistryEntryCreateCFProperties(service, &unmanaged, kCFAllocatorDefault, 0) == KERN_SUCCESS,
              let props = unmanaged?.takeRetainedValue() as? [String: Any] else { return nil }

        func int(_ key: String) -> Int? {
            (props[key] as? NSNumber).map { Int(truncatingIfNeeded: $0.int64Value) }
        }
        func bool(_ key: String) -> Bool? {
            if let b = props[key] as? Bool { return b }
            return (props[key] as? NSNumber)?.boolValue
        }

        guard bool("BatteryInstalled") ?? true else { return nil }
        let design = int("DesignCapacity") ?? 0
        var fullCharge = int("NominalChargeCapacity") ?? int("AppleRawMaxCapacity") ?? 0
        if fullCharge == 0, let max = int("MaxCapacity"), max > 200 { fullCharge = max }

        var percent = 0
        let current = int("CurrentCapacity") ?? 0
        let maxCapacity = int("MaxCapacity") ?? 0
        if maxCapacity == 100 {
            percent = current
        } else if maxCapacity > 0 {
            percent = Int((Double(current) / Double(maxCapacity) * 100).rounded())
        }

        var condition: String?
        if let ps = powerSourceInfo() {
            if let p = ps.percent { percent = p }
            condition = ps.health
        }

        // Amperage is signed; older macOS versions store it as a wrapped unsigned value.
        var amperage = int("InstantAmperage") ?? int("Amperage") ?? 0
        if amperage > Int(Int32.max) { amperage = Int(Int32(truncatingIfNeeded: amperage)) }
        let temperature = int("Temperature").map { Double($0) / 100 }

        return Reading(
            designCapacityMah: design,
            fullChargeCapacityMah: fullCharge,
            cycleCount: int("CycleCount") ?? 0,
            chargePercent: max(0, min(100, percent)),
            isCharging: bool("IsCharging") ?? false,
            isPluggedIn: bool("ExternalConnected") ?? false,
            fullyCharged: bool("FullyCharged") ?? false,
            amperageMa: amperage,
            voltageMv: int("Voltage") ?? 0,
            temperatureCelsius: temperature.flatMap { $0 > 0 && $0 < 100 ? $0 : nil },
            condition: condition
        )
    }

    private static func powerSourceInfo() -> (percent: Int?, health: String?)? {
        guard let blob = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let list = IOPSCopyPowerSourcesList(blob)?.takeRetainedValue() as? [CFTypeRef] else { return nil }
        for source in list {
            guard let description = IOPSGetPowerSourceDescription(blob, source)?.takeUnretainedValue() as? [String: Any],
                  (description[kIOPSTypeKey] as? String) == kIOPSInternalBatteryType else { continue }
            var percent: Int?
            if let current = description[kIOPSCurrentCapacityKey] as? Int,
               let max = description[kIOPSMaxCapacityKey] as? Int, max > 0 {
                percent = Int((Double(current) / Double(max) * 100).rounded())
            }
            return (percent, description["BatteryHealth"] as? String)
        }
        return nil
    }

    /// Turns a reading into the result stored for the run.
    public static func result(from reading: Reading?, dischargeUnderLoad: Double?) -> BatteryHealthResult {
        guard let r = reading, r.designCapacityMah > 0 else {
            return BatteryHealthResult(batteryPresent: false)
        }
        let health = r.healthPercent
        return BatteryHealthResult(
            batteryPresent: true,
            designCapacityMah: r.designCapacityMah,
            fullChargeCapacityMah: r.fullChargeCapacityMah,
            healthPercent: health,
            cycleCount: r.cycleCount,
            currentChargePercent: r.chargePercent,
            isCharging: r.isCharging,
            isPluggedIn: r.isPluggedIn,
            dischargeRateWatts: dischargeUnderLoad,
            temperatureCelsius: r.temperatureCelsius,
            condition: r.condition,
            issues: BatteryHealthResult.evaluateIssues(healthPercent: health, cycleCount: r.cycleCount,
                                                       isPluggedIn: r.isPluggedIn, isCharging: r.isCharging,
                                                       chargePercent: r.chargePercent, fullyCharged: r.fullyCharged)
        )
    }
}
#endif
