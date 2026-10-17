#if os(macOS)
import Darwin
import Foundation
import IOKit

/// Takes one reading of everything the live dashboard shows. Call `sample()` about once a
/// second from a background queue; CPU load is measured between consecutive calls.
public final class TelemetrySampler {
    private struct Ticks {
        var user: UInt64
        var system: UInt64
        var idle: UInt64
        var nice: UInt64
        var total: UInt64 { user + system + idle + nice }
        var busy: UInt64 { user + system + nice }
    }

    private var previousTicks: [Ticks] = []

    public init() {
        SMCSensors.shared.discoverIfNeeded()
        previousTicks = TelemetrySampler.readTicks()
    }

    public func sample() -> SensorSnapshot {
        let (overall, perCore) = cpuLoad()
        let memory = TelemetrySampler.memoryUsage()
        let smc = SMCSensors.shared.read()
        return SensorSnapshot(
            timestamp: Date(),
            cpuLoadPercent: overall,
            perCoreLoadPercent: perCore,
            ramUsedGB: memory.used,
            ramTotalGB: memory.total,
            memoryPressure: TelemetrySampler.memoryPressure(),
            gpuLoadPercent: TelemetrySampler.gpuUtilization(),
            cpuTemperatureCelsius: smc.cpuTemperature,
            cpuTemperatureMaxCelsius: smc.cpuTemperatureMax,
            gpuTemperatureCelsius: smc.gpuTemperature,
            systemPowerWatts: smc.systemPower,
            fanRPM: smc.fanRPM,
            fanCount: smc.fanCount,
            thermalState: TelemetrySampler.thermalLevel(),
            hasSensorAccess: SMCSensors.shared.isAvailable
        )
    }

    // MARK: CPU

    private func cpuLoad() -> (Double, [Double]) {
        let current = TelemetrySampler.readTicks()
        defer { previousTicks = current }
        guard current.count == previousTicks.count, !current.isEmpty else { return (0, []) }
        var perCore: [Double] = []
        var busyTotal: UInt64 = 0
        var allTotal: UInt64 = 0
        for (now, before) in zip(current, previousTicks) {
            let total = now.total &- before.total
            let busy = now.busy &- before.busy
            busyTotal &+= busy
            allTotal &+= total
            perCore.append(total > 0 ? min(100, Double(busy) / Double(total) * 100) : 0)
        }
        let overall = allTotal > 0 ? min(100, Double(busyTotal) / Double(allTotal) * 100) : 0
        return (overall, perCore)
    }

    private static func readTicks() -> [Ticks] {
        var cpuCount: natural_t = 0
        var info: processor_info_array_t?
        var infoCount: mach_msg_type_number_t = 0
        let kr = host_processor_info(mach_host_self(), PROCESSOR_CPU_LOAD_INFO, &cpuCount, &info, &infoCount)
        guard kr == KERN_SUCCESS, let info else { return [] }
        defer {
            vm_deallocate(mach_task_self_, vm_address_t(bitPattern: info),
                          vm_size_t(Int(infoCount) * MemoryLayout<integer_t>.stride))
        }
        var result: [Ticks] = []
        result.reserveCapacity(Int(cpuCount))
        for cpu in 0..<Int(cpuCount) {
            let base = Int(CPU_STATE_MAX) * cpu
            result.append(Ticks(
                user: UInt64(UInt32(bitPattern: info[base + Int(CPU_STATE_USER)])),
                system: UInt64(UInt32(bitPattern: info[base + Int(CPU_STATE_SYSTEM)])),
                idle: UInt64(UInt32(bitPattern: info[base + Int(CPU_STATE_IDLE)])),
                nice: UInt64(UInt32(bitPattern: info[base + Int(CPU_STATE_NICE)]))
            ))
        }
        return result
    }

    // MARK: Memory

    /// Used memory the way Activity Monitor counts it: app memory + wired + compressed.
    public static func memoryUsage() -> (used: Double, total: Double) {
        let total = Double(ProcessInfo.processInfo.physicalMemory) / 1_073_741_824
        var stats = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.stride / MemoryLayout<integer_t>.stride)
        let kr = withUnsafeMutablePointer(to: &stats) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &count)
            }
        }
        guard kr == KERN_SUCCESS else { return (0, total) }
        let page = Double(vm_kernel_page_size)
        let appPages = Double(stats.internal_page_count) - Double(stats.purgeable_count)
        let used = (max(0, appPages) + Double(stats.wire_count) + Double(stats.compressor_page_count)) * page
        return (min(total, used / 1_073_741_824), total)
    }

    public static func memoryPressure() -> MemoryPressureLevel {
        switch Sysctl.int("kern.memorystatus_vm_pressure_level") ?? 1 {
        case 4: return .critical
        case 2: return .warning
        default: return .normal
        }
    }

    // MARK: GPU

    /// Busy percentage reported by the GPU driver.
    public static func gpuUtilization() -> Double? {
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOAccelerator"), &iterator) == KERN_SUCCESS else {
            return nil
        }
        defer { IOObjectRelease(iterator) }
        var best: Double?
        var service = IOIteratorNext(iterator)
        while service != 0 {
            if let property = IORegistryEntryCreateCFProperty(service, "PerformanceStatistics" as CFString, kCFAllocatorDefault, 0)?
                .takeRetainedValue(),
               let stats = property as? [String: Any] {
                let value = (stats["Device Utilization %"] as? NSNumber)?.doubleValue
                    ?? (stats["GPU Activity(%)"] as? NSNumber)?.doubleValue
                if let value {
                    best = max(best ?? 0, min(100, value))
                }
            }
            IOObjectRelease(service)
            service = IOIteratorNext(iterator)
        }
        return best
    }

    // MARK: Thermal

    public static func thermalLevel() -> ThermalLevel {
        switch ProcessInfo.processInfo.thermalState {
        case .nominal: return .nominal
        case .fair: return .fair
        case .serious: return .serious
        case .critical: return .critical
        @unknown default: return .nominal
        }
    }
}
#endif
