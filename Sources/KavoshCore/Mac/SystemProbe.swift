#if os(macOS)
import Foundation
import IOKit
#if canImport(Metal)
import Metal
#endif

/// Collects the Mac's hardware specs.
public enum SystemProbe {
    /// Fast readings only (sysctl, IORegistry, Metal). Takes a few milliseconds.
    public static func quickSpecs() -> SystemSpecs {
        var specs = SystemSpecs()
        specs.modelIdentifier = Sysctl.string("hw.model") ?? ""
        specs.cpuName = Sysctl.string("machdep.cpu.brand_string") ?? "Unknown CPU"
        let isArm = (Sysctl.int("hw.optional.arm64") ?? 0) == 1
        specs.architecture = isArm ? "arm64" : "x86_64"
        specs.cpuCores = Sysctl.int("hw.physicalcpu") ?? ProcessInfo.processInfo.processorCount
        specs.cpuThreads = Sysctl.int("hw.logicalcpu") ?? ProcessInfo.processInfo.activeProcessorCount

        var clusters: [CoreCluster] = []
        let levels = Sysctl.int("hw.nperflevels") ?? 0
        if levels > 1 {
            for level in 0..<levels {
                let name = Sysctl.string("hw.perflevel\(level).name") ?? (level == 0 ? "Performance" : "Efficiency")
                let cores = Sysctl.int("hw.perflevel\(level).physicalcpu") ?? 0
                if cores > 0 { clusters.append(CoreCluster(name: name, cores: cores)) }
            }
        }
        specs.coreClusters = clusters

        if let hz = Sysctl.uint64("hw.cpufrequency_max"), hz > 0 {
            specs.cpuMaxClockGHz = Double(hz) / 1_000_000_000
        }
        specs.ramCapacityGB = Double(ProcessInfo.processInfo.physicalMemory) / 1_073_741_824

        let version = ProcessInfo.processInfo.operatingSystemVersion
        var osText = "macOS \(version.majorVersion).\(version.minorVersion)"
        if version.patchVersion > 0 { osText += ".\(version.patchVersion)" }
        if let build = Sysctl.string("kern.osversion") { osText += " (\(build))" }
        specs.operatingSystem = osText

        specs.modelName = marketingName() ?? specs.modelIdentifier

        #if canImport(Metal)
        if let device = MTLCreateSystemDefaultDevice() {
            specs.gpuName = device.name
            specs.hasUnifiedMemory = device.hasUnifiedMemory
            specs.gpuMemoryGB = Double(device.recommendedMaxWorkingSetSize) / 1_073_741_824
            specs.metalVersion = device.supportsFamily(.metal3) ? "Metal 3" : "Metal 2"
        }
        #endif
        specs.gpuCoreCount = gpuCoreCount()
        specs.storageDevices = DriveReader.physicalDrives()
        return specs
    }

    /// Adds details that need `system_profiler` (memory type, display, firmware). Takes 1–3 s.
    public static func addSlowDetails(to specs: inout SystemSpecs) {
        guard let data = ProcessRunner.run("/usr/sbin/system_profiler",
                                           ["-json", "SPHardwareDataType", "SPMemoryDataType", "SPDisplaysDataType"]),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }

        if let hardware = (root["SPHardwareDataType"] as? [[String: Any]])?.first {
            if let firmware = hardware["boot_rom_version"] as? String { specs.firmwareVersion = firmware }
            if specs.modelName == specs.modelIdentifier || specs.modelName.isEmpty,
               let machine = hardware["machine_name"] as? String {
                specs.modelName = specs.modelIdentifier.isEmpty ? machine : "\(machine) (\(specs.modelIdentifier))"
            }
        }

        if let memory = root["SPMemoryDataType"] as? [[String: Any]] {
            var types: [String] = []
            var makers: [String] = []
            var speeds: [Int] = []
            var modules = 0
            func visit(_ item: [String: Any]) {
                if let type = item["dimm_type"] as? String, !type.lowercased().contains("empty") { types.append(type) }
                if let maker = item["dimm_manufacturer"] as? String, !maker.lowercased().contains("empty") { makers.append(maker) }
                if let speedText = item["dimm_speed"] as? String,
                   let speed = Int(speedText.components(separatedBy: CharacterSet.decimalDigits.inverted).joined()), speed > 0 {
                    speeds.append(speed)
                }
                if let size = item["dimm_size"] as? String, !size.lowercased().contains("empty") { modules += 1 }
                for child in (item["_items"] as? [[String: Any]]) ?? [] { visit(child) }
            }
            memory.forEach(visit)
            specs.ramType = types.first
            specs.ramManufacturer = makers.first
            specs.ramSpeedMHz = speeds.first
            specs.ramModuleCount = modules > 0 ? modules : nil
        }

        if let gpus = root["SPDisplaysDataType"] as? [[String: Any]] {
            for gpu in gpus {
                if specs.gpuCoreCount == nil, let coresText = gpu["sppci_cores"] as? String, let cores = Int(coresText) {
                    specs.gpuCoreCount = cores
                }
                if let family = gpu["spdisplays_mtlgpufamilysupport"] as? String {
                    let lower = family.lowercased()
                    if lower.contains("metal4") { specs.metalVersion = "Metal 4" }
                    else if lower.contains("metal3") { specs.metalVersion = "Metal 3" }
                    else if lower.contains("metal2") { specs.metalVersion = specs.metalVersion ?? "Metal 2" }
                }
                let displays = (gpu["spdisplays_ndrvs"] as? [[String: Any]]) ?? []
                let main = displays.first { ($0["spdisplays_main"] as? String) == "spdisplays_yes" } ?? displays.first
                if let main, specs.displayResolution == nil {
                    specs.displayName = main["_name"] as? String
                    if let pixels = main["_spdisplays_pixels"] as? String {
                        specs.displayResolution = pixels.replacingOccurrences(of: " x ", with: " × ")
                    }
                    if let resolution = main["_spdisplays_resolution"] as? String,
                       let at = resolution.range(of: "@") {
                        let hzText = resolution[at.upperBound...].trimmingCharacters(in: .whitespaces)
                            .replacingOccurrences(of: "Hz", with: "")
                        if let hz = Double(hzText) { specs.displayRefreshRateHz = Int(hz.rounded()) }
                    }
                }
            }
        }
    }

    /// "MacBook Pro (14-inch, 2024)" — read from the device tree on Apple silicon, or from the
    /// System Information cache on Intel Macs.
    public static func marketingName() -> String? {
        let entry = IORegistryEntryFromPath(kIOMainPortDefault, "IODeviceTree:/product")
        if entry != 0 {
            defer { IOObjectRelease(entry) }
            if let value = IORegistryEntryCreateCFProperty(entry, "product-name" as CFString, kCFAllocatorDefault, 0)?
                .takeRetainedValue() {
                if let data = value as? Data {
                    let name = String(decoding: data.prefix { $0 != 0 }, as: UTF8.self)
                        .trimmingCharacters(in: .whitespacesAndNewlines)
                    if !name.isEmpty { return name }
                } else if let name = value as? String, !name.isEmpty {
                    return name
                }
            }
        }
        if let defaults = UserDefaults(suiteName: "com.apple.SystemProfiler"),
           let names = defaults.dictionary(forKey: "CPU Names") as? [String: String],
           let name = names.values.first, !name.isEmpty {
            return name
        }
        return nil
    }

    /// Number of GPU cores on Apple silicon.
    public static func gpuCoreCount() -> Int? {
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("AGXAccelerator"), &iterator) == KERN_SUCCESS else {
            return nil
        }
        defer { IOObjectRelease(iterator) }
        var result: Int?
        var service = IOIteratorNext(iterator)
        while service != 0 {
            if result == nil,
               let value = IORegistryEntryCreateCFProperty(service, "gpu-core-count" as CFString, kCFAllocatorDefault, 0)?
                .takeRetainedValue() as? NSNumber {
                result = value.intValue
            }
            IOObjectRelease(service)
            service = IOIteratorNext(iterator)
        }
        return result
    }
}
#endif
