#if os(macOS)
import Foundation
import KavoshSensors

/// Finds and reads the SMC's temperature, fan and power sensors.
///
/// Key names differ between Mac generations, so instead of hard-coding one list the app
/// enumerates every key once, keeps the temperature keys that belong to the CPU and GPU
/// and that return a believable value, and averages them.
public final class SMCSensors: @unchecked Sendable {
    public static let shared = SMCSensors()

    public struct Reading: Sendable {
        public var cpuTemperature: Double?
        public var cpuTemperatureMax: Double?
        public var gpuTemperature: Double?
        public var systemPower: Double?
        public var fanRPM: [Double]
        public var fanCount: Int?
    }

    private let lock = NSLock()
    private var discovered = false
    private var available = false
    private var cpuKeys: [String] = []
    private var gpuKeys: [String] = []
    private var fanCount: Int?
    private var powerKey: String?

    // M3-family keys that use the "Tf" prefix for both CPU and GPU.
    private static let m3CPUKeys: Set<String> = ["Tf04", "Tf09", "Tf0A", "Tf0B", "Tf0D", "Tf0E",
                                                 "Tf44", "Tf49", "Tf4A", "Tf4B", "Tf4D", "Tf4E"]
    private static let m3GPUKeys: Set<String> = ["Tf14", "Tf18", "Tf19", "Tf1A", "Tf24", "Tf28", "Tf29", "Tf2A"]

    private init() {}

    public var isAvailable: Bool {
        lock.lock()
        defer { lock.unlock() }
        return available
    }

    public var keySummary: String {
        lock.lock()
        defer { lock.unlock() }
        return "cpu=\(cpuKeys.joined(separator: ",")) gpu=\(gpuKeys.joined(separator: ",")) fans=\(fanCount ?? -1) power=\(powerKey ?? "none")"
    }

    /// Opens the SMC and works out which keys to read. Safe to call repeatedly.
    public func discoverIfNeeded() {
        lock.lock()
        defer { lock.unlock() }
        guard !discovered else { return }
        discovered = true
        guard kv_smc_open() == 0 else { return }

        let total = kv_smc_key_count()
        var allKeys: [String] = []
        allKeys.reserveCapacity(Int(total))
        var buffer = [CChar](repeating: 0, count: 5)
        var index: UInt32 = 0
        while index < total {
            if kv_smc_key_at_index(index, &buffer) == 0 {
                allKeys.append(buffer.withUnsafeBufferPointer { String(cString: $0.baseAddress!) })
            }
            index += 1
        }

        let appleSilicon = (Sysctl.int("hw.optional.arm64") ?? 0) == 1
        var cpuCandidates: [String] = []
        var gpuCandidates: [String] = []
        for key in allKeys where key.count == 4 && key.hasPrefix("T") {
            if appleSilicon {
                if key.hasPrefix("Tp") || key.hasPrefix("Te") || SMCSensors.m3CPUKeys.contains(key) {
                    cpuCandidates.append(key)
                } else if key.hasPrefix("Tg") || SMCSensors.m3GPUKeys.contains(key) {
                    gpuCandidates.append(key)
                }
            } else {
                if key.hasPrefix("TC") {
                    cpuCandidates.append(key)
                } else if key.hasPrefix("TG") {
                    gpuCandidates.append(key)
                }
            }
        }
        if allKeys.isEmpty {
            // Enumeration failed; fall back to well-known keys.
            cpuCandidates = ["Tp09", "Tp0T", "Tp01", "Tp05", "Tp0D", "Tp0H", "Tp0L", "Tp0P", "Tp0X", "Tp0b",
                             "Te05", "Te0L", "Te0P", "Te0S", "TC0P", "TC0D", "TC0E", "TC0F", "TCXC"]
                + Array(SMCSensors.m3CPUKeys)
            gpuCandidates = ["Tg05", "Tg0D", "Tg0L", "Tg0T", "Tg0f", "Tg0j", "TG0P", "TG0D"] + Array(SMCSensors.m3GPUKeys)
        }

        cpuKeys = cpuCandidates.filter { SMCSensors.isPlausibleTemperature(SMCSensors.read($0)) }
        gpuKeys = gpuCandidates.filter { SMCSensors.isPlausibleTemperature(SMCSensors.read($0)) }

        if let fans = SMCSensors.read("FNum"), fans >= 0, fans < 10 {
            fanCount = Int(fans)
        }
        for key in ["PSTR", "PDTR", "PCPC", "PC0C"] {
            if let watts = SMCSensors.read(key), watts.isFinite, watts > 0, watts < 2000 {
                powerKey = key
                break
            }
        }
        available = !cpuKeys.isEmpty || !gpuKeys.isEmpty || powerKey != nil || (fanCount ?? 0) > 0
    }

    public func read() -> Reading {
        lock.lock()
        let cpu = cpuKeys
        let gpu = gpuKeys
        let fans = fanCount
        let power = powerKey
        lock.unlock()

        let cpuValues = cpu.compactMap { SMCSensors.read($0) }.filter { SMCSensors.isPlausibleTemperature($0) }
        let gpuValues = gpu.compactMap { SMCSensors.read($0) }.filter { SMCSensors.isPlausibleTemperature($0) }
        var rpm: [Double] = []
        if let fans, fans > 0 {
            for i in 0..<fans {
                if let value = SMCSensors.read("F\(i)Ac"), value.isFinite, value >= 0, value < 20000 {
                    rpm.append(value)
                }
            }
        }
        var watts: Double?
        if let power, let value = SMCSensors.read(power), value.isFinite, value >= 0, value < 2000 {
            watts = value
        }
        return Reading(
            cpuTemperature: cpuValues.isEmpty ? nil : cpuValues.reduce(0, +) / Double(cpuValues.count),
            cpuTemperatureMax: cpuValues.max(),
            gpuTemperature: gpuValues.isEmpty ? nil : gpuValues.reduce(0, +) / Double(gpuValues.count),
            systemPower: watts,
            fanRPM: rpm,
            fanCount: fans
        )
    }

    static func read(_ key: String) -> Double? {
        var value: Double = 0
        let rc = key.withCString { kv_smc_read_number($0, &value) }
        return rc == 0 && value.isFinite ? value : nil
    }

    static func isPlausibleTemperature(_ value: Double?) -> Bool {
        guard let value else { return false }
        return value > 5 && value < 125
    }
}
#endif
