import Foundation

// MARK: - System specs

public struct CoreCluster: Codable, Hashable, Sendable {
    public var name: String
    public var cores: Int

    public init(name: String, cores: Int) {
        self.name = name
        self.cores = cores
    }

    /// "P" for performance cores, "E" for efficiency cores, otherwise the first letter.
    public var shortLetter: String {
        let lower = name.lowercased()
        if lower.contains("perf") || lower.contains("super") { return "P" }
        if lower.contains("eff") { return "E" }
        return String(name.prefix(1)).uppercased()
    }
}

public struct StorageDeviceInfo: Codable, Hashable, Identifiable, Sendable {
    public var id: String { bsdName }
    public var bsdName: String
    public var model: String
    public var vendor: String?
    public var sizeBytes: UInt64
    public var medium: String?
    public var interconnect: String?
    public var location: String?
    public var nvmeSmartCapable: Bool
    public var ataSmartCapable: Bool
    public var removable: Bool

    public init(bsdName: String, model: String, vendor: String? = nil, sizeBytes: UInt64, medium: String? = nil,
                interconnect: String? = nil, location: String? = nil, nvmeSmartCapable: Bool = false,
                ataSmartCapable: Bool = false, removable: Bool = false) {
        self.bsdName = bsdName
        self.model = model
        self.vendor = vendor
        self.sizeBytes = sizeBytes
        self.medium = medium
        self.interconnect = interconnect
        self.location = location
        self.nvmeSmartCapable = nvmeSmartCapable
        self.ataSmartCapable = ataSmartCapable
        self.removable = removable
    }

    public var isInternal: Bool { (location ?? "").lowercased() == "internal" }
    public var isSolidState: Bool { (medium ?? "").lowercased().contains("solid") }

    public var displayName: String {
        let trimmedModel = model.trimmingCharacters(in: .whitespaces)
        let trimmedVendor = (vendor ?? "").trimmingCharacters(in: .whitespaces)
        if trimmedModel.isEmpty { return trimmedVendor.isEmpty ? bsdName : trimmedVendor }
        if trimmedVendor.isEmpty || trimmedModel.lowercased().hasPrefix(trimmedVendor.lowercased()) {
            return trimmedModel
        }
        return "\(trimmedVendor) \(trimmedModel)"
    }
}

public struct SystemSpecs: Codable, Equatable, Sendable {
    public var modelName: String
    public var modelIdentifier: String
    public var cpuName: String
    public var architecture: String
    public var cpuCores: Int
    public var cpuThreads: Int
    public var coreClusters: [CoreCluster]
    public var cpuMaxClockGHz: Double?
    public var gpuName: String
    public var gpuCoreCount: Int?
    public var gpuMemoryGB: Double?
    public var hasUnifiedMemory: Bool
    public var metalVersion: String?
    public var ramCapacityGB: Double
    public var ramType: String?
    public var ramManufacturer: String?
    public var ramSpeedMHz: Int?
    public var ramModuleCount: Int?
    public var displayName: String?
    public var displayResolution: String?
    public var displayRefreshRateHz: Int?
    public var operatingSystem: String
    public var firmwareVersion: String?
    public var storageDevices: [StorageDeviceInfo]

    public init(modelName: String = "Mac", modelIdentifier: String = "", cpuName: String = "", architecture: String = "",
                cpuCores: Int = 0, cpuThreads: Int = 0, coreClusters: [CoreCluster] = [], cpuMaxClockGHz: Double? = nil,
                gpuName: String = "", gpuCoreCount: Int? = nil, gpuMemoryGB: Double? = nil, hasUnifiedMemory: Bool = false,
                metalVersion: String? = nil, ramCapacityGB: Double = 0, ramType: String? = nil, ramManufacturer: String? = nil,
                ramSpeedMHz: Int? = nil, ramModuleCount: Int? = nil, displayName: String? = nil, displayResolution: String? = nil,
                displayRefreshRateHz: Int? = nil, operatingSystem: String = "", firmwareVersion: String? = nil,
                storageDevices: [StorageDeviceInfo] = []) {
        self.modelName = modelName
        self.modelIdentifier = modelIdentifier
        self.cpuName = cpuName
        self.architecture = architecture
        self.cpuCores = cpuCores
        self.cpuThreads = cpuThreads
        self.coreClusters = coreClusters
        self.cpuMaxClockGHz = cpuMaxClockGHz
        self.gpuName = gpuName
        self.gpuCoreCount = gpuCoreCount
        self.gpuMemoryGB = gpuMemoryGB
        self.hasUnifiedMemory = hasUnifiedMemory
        self.metalVersion = metalVersion
        self.ramCapacityGB = ramCapacityGB
        self.ramType = ramType
        self.ramManufacturer = ramManufacturer
        self.ramSpeedMHz = ramSpeedMHz
        self.ramModuleCount = ramModuleCount
        self.displayName = displayName
        self.displayResolution = displayResolution
        self.displayRefreshRateHz = displayRefreshRateHz
        self.operatingSystem = operatingSystem
        self.firmwareVersion = firmwareVersion
        self.storageDevices = storageDevices
    }

    /// "14C (10P+4E)" style summary of the CPU cores.
    public var clusterShorthand: String {
        let parts = coreClusters.filter { $0.cores > 0 }.map { "\($0.cores)\($0.shortLetter)" }
        if parts.count > 1 { return "\(cpuCores)C (\(parts.joined(separator: "+")))" }
        if cpuThreads > cpuCores { return "\(cpuCores)C / \(cpuThreads)T" }
        return "\(cpuCores)C"
    }

    public var isAppleSilicon: Bool { architecture == "arm64" }

    public static let unknown = SystemSpecs()
}

// MARK: - Live sensors

public enum ThermalLevel: Int, Codable, Sendable, Comparable {
    case nominal = 0
    case fair = 1
    case serious = 2
    case critical = 3

    public static func < (lhs: ThermalLevel, rhs: ThermalLevel) -> Bool { lhs.rawValue < rhs.rawValue }

    public var title: String {
        switch self {
        case .nominal: return T("Normal", "عادی")
        case .fair: return T("Warm", "گرم")
        case .serious: return T("Hot — slowed down", "داغ — کند شده")
        case .critical: return T("Critical — heavily slowed", "بحرانی — به‌شدت کند شده")
        }
    }
}

public enum MemoryPressureLevel: Int, Codable, Sendable {
    case normal = 0
    case warning = 1
    case critical = 2

    public var title: String {
        switch self {
        case .normal: return T("Pressure: normal", "فشار حافظه: عادی")
        case .warning: return T("Pressure: high", "فشار حافظه: زیاد")
        case .critical: return T("Pressure: critical", "فشار حافظه: بحرانی")
        }
    }
}

public struct SensorSnapshot: Codable, Equatable, Sendable {
    public var timestamp: Date
    public var cpuLoadPercent: Double
    public var perCoreLoadPercent: [Double]
    public var ramUsedGB: Double
    public var ramTotalGB: Double
    public var memoryPressure: MemoryPressureLevel
    public var gpuLoadPercent: Double?
    public var cpuTemperatureCelsius: Double?
    public var cpuTemperatureMaxCelsius: Double?
    public var gpuTemperatureCelsius: Double?
    public var systemPowerWatts: Double?
    public var fanRPM: [Double]
    public var fanCount: Int?
    public var thermalState: ThermalLevel
    public var hasSensorAccess: Bool

    public init(timestamp: Date = Date(), cpuLoadPercent: Double = 0, perCoreLoadPercent: [Double] = [],
                ramUsedGB: Double = 0, ramTotalGB: Double = 0, memoryPressure: MemoryPressureLevel = .normal,
                gpuLoadPercent: Double? = nil, cpuTemperatureCelsius: Double? = nil, cpuTemperatureMaxCelsius: Double? = nil,
                gpuTemperatureCelsius: Double? = nil, systemPowerWatts: Double? = nil, fanRPM: [Double] = [],
                fanCount: Int? = nil, thermalState: ThermalLevel = .nominal, hasSensorAccess: Bool = false) {
        self.timestamp = timestamp
        self.cpuLoadPercent = cpuLoadPercent
        self.perCoreLoadPercent = perCoreLoadPercent
        self.ramUsedGB = ramUsedGB
        self.ramTotalGB = ramTotalGB
        self.memoryPressure = memoryPressure
        self.gpuLoadPercent = gpuLoadPercent
        self.cpuTemperatureCelsius = cpuTemperatureCelsius
        self.cpuTemperatureMaxCelsius = cpuTemperatureMaxCelsius
        self.gpuTemperatureCelsius = gpuTemperatureCelsius
        self.systemPowerWatts = systemPowerWatts
        self.fanRPM = fanRPM
        self.fanCount = fanCount
        self.thermalState = thermalState
        self.hasSensorAccess = hasSensorAccess
    }

    public var ramLoadPercent: Double {
        ramTotalGB > 0 ? min(100, ramUsedGB / ramTotalGB * 100) : 0
    }
}

// MARK: - Test modules

public enum TestModule: String, Codable, CaseIterable, Identifiable, Sendable {
    case cpu
    case gpu
    case memory
    case battery
    case display
    case speakers
    case micCamera
    case input
    case drives

    public var id: String { rawValue }

    /// Order of a full run.
    public static let fullRun: [TestModule] = [.cpu, .gpu, .memory, .battery, .display, .speakers, .micCamera, .input, .drives]
    /// Performance-only run.
    public static let performanceRun: [TestModule] = [.cpu, .gpu, .memory]

    public var title: String {
        switch self {
        case .cpu: return T("CPU Benchmark", "بنچمارک پردازنده")
        case .gpu: return T("GPU Render Test", "تست رندر گرافیک")
        case .memory: return T("Memory Test", "تست حافظه")
        case .battery: return T("Battery Health", "سلامت باتری")
        case .display: return T("Display Check", "بررسی نمایشگر")
        case .speakers: return T("Speaker Check", "بررسی بلندگوها")
        case .micCamera: return T("Mic & Camera Check", "بررسی میکروفون و دوربین")
        case .input: return T("Keyboard & Trackpad", "کیبورد و ترک‌پد")
        case .drives: return T("Drive Health (SMART)", "سلامت درایو (SMART)")
        }
    }

    public var shortTitle: String {
        switch self {
        case .cpu: return "CPU"
        case .gpu: return "GPU"
        case .memory: return "RAM"
        case .battery: return T("Battery", "باتری")
        case .display: return T("Display", "نمایشگر")
        case .speakers: return T("Speakers", "بلندگو")
        case .micCamera: return T("Mic & Camera", "میکروفون و دوربین")
        case .input: return T("Input", "ورودی")
        case .drives: return T("Drives", "درایوها")
        }
    }

    public var subtitle: String {
        switch self {
        case .cpu: return T("Fixed multi-core and single-core workloads, then a sustained load",
                            "بار کاری ثابت چندهسته‌ای و تک‌هسته‌ای، سپس بار پایدار")
        case .gpu: return T("Off-screen Metal raymarch at 2560×1440 with supersampling",
                            "رندر رِی‌مارچ Metal خارج از صفحه در ۲۵۶۰×۱۴۴۰ با سوپرسمپلینگ")
        case .memory: return T("Pattern write/verify integrity check + throughput",
                               "نوشتن و بررسی الگو برای سلامت حافظه + سرعت")
        case .battery: return T("Design vs. full-charge capacity + discharge rate under load",
                                "ظرفیت طراحی در برابر ظرفیت شارژ کامل + نرخ تخلیه زیر بار")
        case .display: return T("Fullscreen color sweep, dead-pixel scan, refresh-rate check, and a defect checklist",
                                "رنگ‌های تمام‌صفحه، پیکسل سوخته، نرخ تازه‌سازی و فهرست ایرادها")
        case .speakers: return T("Left / right channel tone playback", "پخش صدای کانال چپ / راست")
        case .micCamera: return T("Live webcam preview + microphone level/frequency",
                                  "پیش‌نمایش زنده دوربین + سطح و فرکانس میکروفون")
        case .input: return T("Type a prompt, press every key, then try the trackpad",
                              "یک جمله تایپ کنید، همه کلیدها را بزنید، سپس ترک‌پد را امتحان کنید")
        case .drives: return T("Read from each drive's own SMART / NVMe health log",
                               "خواندن گزارش سلامت SMART / NVMe هر درایو")
        }
    }

    public var symbol: String {
        switch self {
        case .cpu: return "cpu"
        case .gpu: return "cube.transparent"
        case .memory: return "memorychip"
        case .battery: return "battery.75percent"
        case .display: return "display"
        case .speakers: return "speaker.wave.2"
        case .micCamera: return "web.camera"
        case .input: return "keyboard"
        case .drives: return "internaldrive"
        }
    }

    /// Runs without user interaction.
    public var isAutomatic: Bool {
        switch self {
        case .cpu, .gpu, .memory, .battery, .drives: return true
        case .display, .speakers, .micCamera, .input: return false
        }
    }
}

// MARK: - CPU

public struct CpuBenchmarkResult: Codable, Equatable, Sendable {
    public var score: Double
    public var singleCoreScore: Double
    public var sustainedScore: Double?
    public var sustainedSamples: [Double]
    public var multiCoreBatchMs: Double
    public var singleCoreBatchMs: Double
    public var multiCoreUnits: Int
    public var singleCoreUnits: Int
    public var threadsUsed: Int
    public var durationSeconds: Double
    public var wrongResults: Int
    public var thermalThrottleDetected: Bool
    public var peakTemperatureCelsius: Double?
    public var averagePowerWatts: Double?
    public var scoringVersion: Int

    public init(score: Double, singleCoreScore: Double, sustainedScore: Double?, sustainedSamples: [Double],
                multiCoreBatchMs: Double, singleCoreBatchMs: Double, multiCoreUnits: Int, singleCoreUnits: Int,
                threadsUsed: Int, durationSeconds: Double, wrongResults: Int, thermalThrottleDetected: Bool,
                peakTemperatureCelsius: Double? = nil, averagePowerWatts: Double? = nil, scoringVersion: Int) {
        self.score = score
        self.singleCoreScore = singleCoreScore
        self.sustainedScore = sustainedScore
        self.sustainedSamples = sustainedSamples
        self.multiCoreBatchMs = multiCoreBatchMs
        self.singleCoreBatchMs = singleCoreBatchMs
        self.multiCoreUnits = multiCoreUnits
        self.singleCoreUnits = singleCoreUnits
        self.threadsUsed = threadsUsed
        self.durationSeconds = durationSeconds
        self.wrongResults = wrongResults
        self.thermalThrottleDetected = thermalThrottleDetected
        self.peakTemperatureCelsius = peakTemperatureCelsius
        self.averagePowerWatts = averagePowerWatts
        self.scoringVersion = scoringVersion
    }

    public var resultsVerified: Bool { wrongResults == 0 }
}

// MARK: - GPU

public struct GpuBenchmarkResult: Codable, Equatable, Sendable {
    public var adapterName: String
    public var score: Double
    public var sustainedScore: Double?
    public var averageFrameTimeMs: Double
    public var sustainedAverageFrameTimeMs: Double?
    public var averageFps: Double
    public var minFps: Double
    public var maxFps: Double
    public var onePercentLowFps: Double
    public var outputWidth: Int
    public var outputHeight: Int
    public var renderWidth: Int
    public var renderHeight: Int
    public var samplesPerPixel: Int
    public var totalFramesRendered: Int
    public var durationSeconds: Double
    public var peakTemperatureCelsius: Double?
    public var scoringVersion: Int

    public init(adapterName: String, score: Double, sustainedScore: Double?, averageFrameTimeMs: Double,
                sustainedAverageFrameTimeMs: Double?, averageFps: Double, minFps: Double, maxFps: Double,
                onePercentLowFps: Double, outputWidth: Int, outputHeight: Int, renderWidth: Int, renderHeight: Int,
                samplesPerPixel: Int, totalFramesRendered: Int, durationSeconds: Double,
                peakTemperatureCelsius: Double? = nil, scoringVersion: Int) {
        self.adapterName = adapterName
        self.score = score
        self.sustainedScore = sustainedScore
        self.averageFrameTimeMs = averageFrameTimeMs
        self.sustainedAverageFrameTimeMs = sustainedAverageFrameTimeMs
        self.averageFps = averageFps
        self.minFps = minFps
        self.maxFps = maxFps
        self.onePercentLowFps = onePercentLowFps
        self.outputWidth = outputWidth
        self.outputHeight = outputHeight
        self.renderWidth = renderWidth
        self.renderHeight = renderHeight
        self.samplesPerPixel = samplesPerPixel
        self.totalFramesRendered = totalFramesRendered
        self.durationSeconds = durationSeconds
        self.peakTemperatureCelsius = peakTemperatureCelsius
        self.scoringVersion = scoringVersion
    }

    /// Builds a result from measured frame times.
    /// - Parameters:
    ///   - frameTimesMs: GPU time of each frame in the scored run.
    ///   - sustainedMeanMs: mean frame time of the sustained phase, if there was one.
    ///   - referenceFrameMs: the reference machine's frame time; it scores 1,000.
    public static func make(frameTimesMs: [Double], sustainedMeanMs: Double?, totalFrames: Int, seconds: Double,
                            adapterName: String, outputWidth: Int, outputHeight: Int, renderWidth: Int,
                            renderHeight: Int, samplesPerPixel: Int, referenceFrameMs: Double,
                            scoringVersion: Int) -> GpuBenchmarkResult {
        let valid = frameTimesMs.filter { $0.isFinite && $0 > 0 }
        let mean = valid.isEmpty ? 0 : valid.reduce(0, +) / Double(valid.count)
        let fps = valid.map { 1000 / $0 }
        let sortedFps = fps.sorted()
        let lowCount = max(1, Int((Double(sortedFps.count) * 0.01).rounded(.up)))
        let onePercentLow = sortedFps.isEmpty ? 0 : sortedFps.prefix(lowCount).reduce(0, +) / Double(min(lowCount, sortedFps.count))
        let score = mean > 0 ? 1000 * referenceFrameMs / mean : 0
        var sustainedScore: Double?
        if let s = sustainedMeanMs, s > 0 { sustainedScore = 1000 * referenceFrameMs / s }
        return GpuBenchmarkResult(
            adapterName: adapterName,
            score: score,
            sustainedScore: sustainedScore,
            averageFrameTimeMs: mean,
            sustainedAverageFrameTimeMs: sustainedMeanMs,
            averageFps: mean > 0 ? 1000 / mean : 0,
            minFps: sortedFps.first ?? 0,
            maxFps: sortedFps.last ?? 0,
            onePercentLowFps: onePercentLow,
            outputWidth: outputWidth,
            outputHeight: outputHeight,
            renderWidth: renderWidth,
            renderHeight: renderHeight,
            samplesPerPixel: samplesPerPixel,
            totalFramesRendered: totalFrames,
            durationSeconds: seconds,
            scoringVersion: scoringVersion
        )
    }
}

// MARK: - Memory

public struct MemoryTestResult: Codable, Equatable, Sendable {
    public var testedBytes: Int
    public var passes: Int
    public var averageWriteThroughputMBps: Double
    public var averageReadThroughputMBps: Double
    public var errorsDetected: Int
    public var durationSeconds: Double

    public init(testedBytes: Int, passes: Int, averageWriteThroughputMBps: Double, averageReadThroughputMBps: Double,
                errorsDetected: Int, durationSeconds: Double) {
        self.testedBytes = testedBytes
        self.passes = passes
        self.averageWriteThroughputMBps = averageWriteThroughputMBps
        self.averageReadThroughputMBps = averageReadThroughputMBps
        self.errorsDetected = errorsDetected
        self.durationSeconds = durationSeconds
    }

    public var passed: Bool { errorsDetected == 0 }
    public var testedMiB: Int { testedBytes / (1024 * 1024) }
}

// MARK: - Battery

public enum BatteryIssue: Codable, Equatable, Hashable, Sendable {
    case highCycleCount(Int)
    case healthBelow90(Double)
    case healthCritical(Double)
    case notCharging

    public var text: String {
        switch self {
        case .highCycleCount(let cycles):
            return T("Cycle count is high (\(cycles) cycles, rated for about 1,000)",
                     "تعداد سیکل شارژ زیاد است (\(cycles) سیکل، عمر اسمی حدود ۱۰۰۰)")
        case .healthBelow90(let p):
            return T("Battery health is below 90% (\(Fmt.fixed(p, 1))%)", "سلامت باتری زیر ۹۰٪ است (\(Fmt.fixed(p, 1))٪)")
        case .healthCritical(let p):
            return T("Battery health is critically low (\(Fmt.fixed(p, 1))%) — service recommended",
                     "سلامت باتری به‌شدت پایین است (\(Fmt.fixed(p, 1))٪) — سرویس توصیه می‌شود")
        case .notCharging:
            return T("Plugged in but not charging properly", "به برق وصل است اما درست شارژ نمی‌شود")
        }
    }
}

public struct BatteryHealthResult: Codable, Equatable, Sendable {
    public var batteryPresent: Bool
    public var designCapacityMah: Int
    public var fullChargeCapacityMah: Int
    public var healthPercent: Double
    public var cycleCount: Int
    public var currentChargePercent: Int
    public var isCharging: Bool
    public var isPluggedIn: Bool
    public var dischargeRateWatts: Double?
    public var temperatureCelsius: Double?
    public var condition: String?
    public var issues: [BatteryIssue]

    public init(batteryPresent: Bool = false, designCapacityMah: Int = 0, fullChargeCapacityMah: Int = 0,
                healthPercent: Double = 0, cycleCount: Int = 0, currentChargePercent: Int = 0, isCharging: Bool = false,
                isPluggedIn: Bool = false, dischargeRateWatts: Double? = nil, temperatureCelsius: Double? = nil,
                condition: String? = nil, issues: [BatteryIssue] = []) {
        self.batteryPresent = batteryPresent
        self.designCapacityMah = designCapacityMah
        self.fullChargeCapacityMah = fullChargeCapacityMah
        self.healthPercent = healthPercent
        self.cycleCount = cycleCount
        self.currentChargePercent = currentChargePercent
        self.isCharging = isCharging
        self.isPluggedIn = isPluggedIn
        self.dischargeRateWatts = dischargeRateWatts
        self.temperatureCelsius = temperatureCelsius
        self.condition = condition
        self.issues = issues
    }

    public var needsService: Bool { batteryPresent && healthPercent > 0 && healthPercent < 80 }

    /// Works out the issue list from the readings.
    public static func evaluateIssues(healthPercent: Double, cycleCount: Int, isPluggedIn: Bool, isCharging: Bool,
                                      chargePercent: Int, fullyCharged: Bool) -> [BatteryIssue] {
        var issues: [BatteryIssue] = []
        if healthPercent > 0 && healthPercent < 80 {
            issues.append(.healthCritical(healthPercent))
        } else if healthPercent > 0 && healthPercent < 90 {
            issues.append(.healthBelow90(healthPercent))
        }
        if cycleCount >= 1000 {
            issues.append(.highCycleCount(cycleCount))
        }
        // macOS pauses charging around 80% when "Optimized Battery Charging" is on, so only
        // flag it when the battery is clearly low.
        if isPluggedIn && !isCharging && !fullyCharged && chargePercent < 75 {
            issues.append(.notCharging)
        }
        return issues
    }
}

// MARK: - Display

public enum DisplayIssue: String, Codable, CaseIterable, Sendable {
    case deadPixels
    case ghosting
    case brightness
    case burnIn
    case lightBleed
    case refreshRate

    public var text: String {
        switch self {
        case .deadPixels: return T("Small black or stuck-color dots on the color/white screens (dead or stuck pixels)",
                                   "نقطه‌های سیاه یا رنگی ثابت روی صفحه‌های رنگی/سفید (پیکسل سوخته یا گیرکرده)")
        case .ghosting: return T("Ghosting or motion blur", "سایه‌افتادن (Ghosting) یا تاری حرکت")
        case .brightness: return T("Brightness looked too low or uneven", "روشنایی کم یا ناهموار بود")
        case .burnIn: return T("Burn-in (a faint image staying on screen)", "سوختگی صفحه (تصویر کم‌رنگی که روی صفحه می‌ماند)")
        case .lightBleed: return T("Light bleeding in from the edges on the black screen", "نشت نور از لبه‌ها روی صفحه سیاه")
        case .refreshRate: return T("Refresh rate looked wrong or stuttered on the motion test",
                                    "نرخ تازه‌سازی در تست حرکت درست نبود یا لرزش داشت")
        }
    }

    public var shortText: String {
        switch self {
        case .deadPixels: return T("Dead or stuck pixels", "پیکسل سوخته یا گیرکرده")
        case .ghosting: return T("Ghosting / motion blur", "سایه‌افتادن / تاری حرکت")
        case .brightness: return T("Brightness / backlight issue", "مشکل روشنایی / نور پس‌زمینه")
        case .burnIn: return T("Screen burn-in", "سوختگی صفحه")
        case .lightBleed: return T("Screen bleed", "نشت نور")
        case .refreshRate: return T("Refresh rate issue", "مشکل نرخ تازه‌سازی")
        }
    }

    /// Points taken off the display score for this issue.
    public var deduction: Double {
        switch self {
        case .deadPixels: return 25
        case .ghosting: return 10
        case .brightness: return 20
        case .burnIn: return 20
        case .lightBleed: return 15
        case .refreshRate: return 15
        }
    }
}

public struct DisplayTestResult: Codable, Equatable, Sendable {
    public var issues: [DisplayIssue]
    public var measuredRefreshRateHz: Double?

    public init(issues: [DisplayIssue] = [], measuredRefreshRateHz: Double? = nil) {
        self.issues = issues
        self.measuredRefreshRateHz = measuredRefreshRateHz
    }
}

// MARK: - Speakers

public struct AudioTestResult: Codable, Equatable, Sendable {
    public var leftHeard: Bool
    public var rightHeard: Bool
    public var reportedIssue: String?

    public init(leftHeard: Bool = false, rightHeard: Bool = false, reportedIssue: String? = nil) {
        self.leftHeard = leftHeard
        self.rightHeard = rightHeard
        self.reportedIssue = reportedIssue
    }
}

// MARK: - Mic & camera

public struct MicCameraTestResult: Codable, Equatable, Sendable {
    public var cameraPresent: Bool
    public var cameraWorks: Bool
    public var microphonePresent: Bool
    public var microphoneWorks: Bool
    public var peakLevelDb: Double?
    public var reportedIssue: String?

    public init(cameraPresent: Bool = false, cameraWorks: Bool = false, microphonePresent: Bool = false,
                microphoneWorks: Bool = false, peakLevelDb: Double? = nil, reportedIssue: String? = nil) {
        self.cameraPresent = cameraPresent
        self.cameraWorks = cameraWorks
        self.microphonePresent = microphonePresent
        self.microphoneWorks = microphoneWorks
        self.peakLevelDb = peakLevelDb
        self.reportedIssue = reportedIssue
    }
}

// MARK: - Keyboard & trackpad

public struct KeyboardTestResult: Codable, Equatable, Sendable {
    public var bypassed: Bool
    public var sentenceCorrect: Bool
    public var totalKeys: Int
    public var pressedKeys: Int
    public var missingKeys: [String]
    public var doublePressedKeys: [String]
    public var trackpadMoved: Bool
    public var trackpadClicked: Bool
    public var trackpadScrolled: Bool

    public init(bypassed: Bool = false, sentenceCorrect: Bool = false, totalKeys: Int = 0, pressedKeys: Int = 0,
                missingKeys: [String] = [], doublePressedKeys: [String] = [], trackpadMoved: Bool = false,
                trackpadClicked: Bool = false, trackpadScrolled: Bool = false) {
        self.bypassed = bypassed
        self.sentenceCorrect = sentenceCorrect
        self.totalKeys = totalKeys
        self.pressedKeys = pressedKeys
        self.missingKeys = missingKeys
        self.doublePressedKeys = doublePressedKeys
        self.trackpadMoved = trackpadMoved
        self.trackpadClicked = trackpadClicked
        self.trackpadScrolled = trackpadScrolled
    }

    public var trackpadChecksPassed: Int {
        [trackpadMoved, trackpadClicked, trackpadScrolled].filter { $0 }.count
    }

    public var issueCount: Int {
        if bypassed { return 0 }
        return missingKeys.count + doublePressedKeys.count + (sentenceCorrect ? 0 : 1) + (3 - trackpadChecksPassed)
    }
}

// MARK: - Drive health

public enum DriveHealthStatus: String, Codable, Sendable {
    case good
    case warning
    case failing
    case unknown

    public var title: String {
        switch self {
        case .good: return T("Good", "خوب")
        case .warning: return T("Warning", "هشدار")
        case .failing: return T("Failing", "در حال خرابی")
        case .unknown: return T("No data", "بدون داده")
        }
    }
}

public enum DriveHealthIssue: Codable, Equatable, Hashable, Sendable {
    case spareBelowThreshold
    case temperatureWarning
    case reliabilityDegraded
    case readOnly
    case backupPowerFailed
    case enduranceUsedUp
    case enduranceAlmostUsedUp
    case mediaErrors(Int)
    case macOSFailing
    case macOSWarning

    public var text: String {
        switch self {
        case .spareBelowThreshold: return T("Spare blocks have fallen below the manufacturer's safe threshold",
                                            "بلوک‌های یدکی از حد امن سازنده پایین‌تر رفته‌اند")
        case .temperatureWarning: return T("The drive reports an over-temperature warning", "درایو هشدار دمای بالا گزارش می‌دهد")
        case .reliabilityDegraded: return T("The drive reports its reliability is degraded", "درایو کاهش قابلیت اطمینان را گزارش می‌دهد")
        case .readOnly: return T("The drive has switched itself to read-only", "درایو خودش را فقط‌خواندنی کرده است")
        case .backupPowerFailed: return T("The drive's power-loss protection has failed", "محافظت درایو در برابر قطع برق از کار افتاده است")
        case .enduranceUsedUp: return T("Rated write endurance used up (100% or more)", "عمر نوشتن اسمی تمام شده است (۱۰۰٪ یا بیشتر)")
        case .enduranceAlmostUsedUp: return T("Rated write endurance almost used up (90% or more)", "عمر نوشتن اسمی تقریباً تمام شده است (۹۰٪ یا بیشتر)")
        case .mediaErrors(let n): return T("Unrecovered data errors have been recorded (\(n))", "خطاهای داده غیرقابل بازیابی ثبت شده است (\(n))")
        case .macOSFailing: return T("macOS reports this drive as failing", "macOS این درایو را در حال خرابی گزارش می‌کند")
        case .macOSWarning: return T("macOS reports a warning for this drive", "macOS برای این درایو هشدار می‌دهد")
        }
    }

    var deduction: Double {
        switch self {
        case .spareBelowThreshold: return 40
        case .temperatureWarning: return 10
        case .reliabilityDegraded: return 50
        case .readOnly: return 60
        case .backupPowerFailed: return 20
        case .enduranceUsedUp: return 40
        case .enduranceAlmostUsedUp: return 20
        case .mediaErrors: return 30
        case .macOSFailing: return 100
        case .macOSWarning: return 30
        }
    }
}

public struct DriveHealthInfo: Codable, Equatable, Identifiable, Sendable {
    public var id: String
    public var model: String
    public var sizeBytes: UInt64
    public var busType: String?
    public var mediaType: String?
    public var isInternal: Bool
    public var status: DriveHealthStatus
    public var macOSSmartStatus: String?
    public var percentageUsed: Int?
    public var availableSparePercent: Int?
    public var availableSpareThresholdPercent: Int?
    public var dataWrittenTB: Double?
    public var dataReadTB: Double?
    public var powerOnHours: Int?
    public var powerCycles: Int?
    public var unsafeShutdowns: Int?
    public var mediaErrors: Int?
    public var temperatureCelsius: Double?
    public var criticalWarning: Int?
    public var issues: [DriveHealthIssue]
    public var score: Double?

    public init(id: String, model: String, sizeBytes: UInt64, busType: String? = nil, mediaType: String? = nil,
                isInternal: Bool = false, status: DriveHealthStatus = .unknown, macOSSmartStatus: String? = nil,
                percentageUsed: Int? = nil, availableSparePercent: Int? = nil, availableSpareThresholdPercent: Int? = nil,
                dataWrittenTB: Double? = nil, dataReadTB: Double? = nil, powerOnHours: Int? = nil, powerCycles: Int? = nil,
                unsafeShutdowns: Int? = nil, mediaErrors: Int? = nil, temperatureCelsius: Double? = nil,
                criticalWarning: Int? = nil, issues: [DriveHealthIssue] = [], score: Double? = nil) {
        self.id = id
        self.model = model
        self.sizeBytes = sizeBytes
        self.busType = busType
        self.mediaType = mediaType
        self.isInternal = isInternal
        self.status = status
        self.macOSSmartStatus = macOSSmartStatus
        self.percentageUsed = percentageUsed
        self.availableSparePercent = availableSparePercent
        self.availableSpareThresholdPercent = availableSpareThresholdPercent
        self.dataWrittenTB = dataWrittenTB
        self.dataReadTB = dataReadTB
        self.powerOnHours = powerOnHours
        self.powerCycles = powerCycles
        self.unsafeShutdowns = unsafeShutdowns
        self.mediaErrors = mediaErrors
        self.temperatureCelsius = temperatureCelsius
        self.criticalWarning = criticalWarning
        self.issues = issues
        self.score = score
    }

    public var lifeLeftPercent: Int? {
        percentageUsed.map { max(0, 100 - $0) }
    }

    public var hasHealthData: Bool { score != nil }
}

public struct DriveHealthReport: Codable, Equatable, Sendable {
    public var drives: [DriveHealthInfo]
    public var readAt: Date

    public init(drives: [DriveHealthInfo], readAt: Date = Date()) {
        self.drives = drives
        self.readAt = readAt
    }

    /// The worst drive decides.
    public var worstScore: Double? {
        drives.compactMap { $0.score }.min()
    }
}

// MARK: - SSD speed

public enum StorageTestKind: String, Codable, CaseIterable, Sendable {
    case seq1mQ8T1
    case rnd4kQ32T1
    case rnd4kQ1T1

    public var label: String {
        switch self {
        case .seq1mQ8T1: return "SEQ1M Q8T1"
        case .rnd4kQ32T1: return "RND4K Q32T1"
        case .rnd4kQ1T1: return "RND4K Q1T1"
        }
    }

    public var blockSize: Int {
        switch self {
        case .seq1mQ8T1: return 1024 * 1024
        case .rnd4kQ32T1, .rnd4kQ1T1: return 4096
        }
    }

    public var queueDepth: Int {
        switch self {
        case .seq1mQ8T1: return 8
        case .rnd4kQ32T1: return 32
        case .rnd4kQ1T1: return 1
        }
    }

    public var isSequential: Bool { self == .seq1mQ8T1 }

    public var title: String {
        switch self {
        case .seq1mQ8T1: return T("Sequential", "ترتیبی")
        case .rnd4kQ32T1: return T("Random", "تصادفی")
        case .rnd4kQ1T1: return T("Random, one at a time", "تصادفی، یکی‌یکی")
        }
    }
}

public enum StorageOperation: String, Codable, CaseIterable, Sendable {
    case read
    case write

    public var title: String {
        switch self {
        case .read: return T("Read", "خواندن")
        case .write: return T("Write", "نوشتن")
        }
    }
}

public struct StorageTestMeasurement: Codable, Equatable, Sendable {
    public var kind: StorageTestKind
    public var operation: StorageOperation
    public var mbPerSecond: Double
    public var iops: Double
    public var averageLatencyMs: Double

    public init(kind: StorageTestKind, operation: StorageOperation, mbPerSecond: Double, iops: Double, averageLatencyMs: Double) {
        self.kind = kind
        self.operation = operation
        self.mbPerSecond = mbPerSecond
        self.iops = iops
        self.averageLatencyMs = averageLatencyMs
    }

    /// Binary megabytes (1 MiB = 1,048,576 bytes).
    public var mibPerSecond: Double { mbPerSecond * 1_000_000 / 1_048_576 }
}

public struct StorageBenchmarkResult: Codable, Equatable, Sendable {
    public var targetPath: String
    public var volumeName: String?
    public var profileName: String
    public var fileSizeBytes: Int64
    public var runsPerTest: Int
    public var measurements: [StorageTestMeasurement]
    public var date: Date

    public init(targetPath: String, volumeName: String?, profileName: String, fileSizeBytes: Int64, runsPerTest: Int,
                measurements: [StorageTestMeasurement], date: Date = Date()) {
        self.targetPath = targetPath
        self.volumeName = volumeName
        self.profileName = profileName
        self.fileSizeBytes = fileSizeBytes
        self.runsPerTest = runsPerTest
        self.measurements = measurements
        self.date = date
    }

    public func measurement(_ kind: StorageTestKind, _ op: StorageOperation) -> StorageTestMeasurement? {
        measurements.first { $0.kind == kind && $0.operation == op }
    }
}

// MARK: - A complete run

public struct ModuleFailure: Codable, Equatable, Sendable {
    public var module: TestModule
    public var message: String

    public init(module: TestModule, message: String) {
        self.module = module
        self.message = message
    }
}

public struct FinalReport: Codable, Equatable, Identifiable, Sendable {
    public var id: UUID
    public var startedAt: Date
    public var finishedAt: Date?
    public var plan: [TestModule]
    public var completed: [TestModule]
    public var skipped: [TestModule]
    public var failures: [ModuleFailure]
    public var specs: SystemSpecs?
    public var cpu: CpuBenchmarkResult?
    public var gpu: GpuBenchmarkResult?
    public var memory: MemoryTestResult?
    public var battery: BatteryHealthResult?
    public var display: DisplayTestResult?
    public var audio: AudioTestResult?
    public var micCamera: MicCameraTestResult?
    public var keyboard: KeyboardTestResult?
    public var drives: DriveHealthReport?
    public var hasNoBuiltInInputs: Bool

    public init(id: UUID = UUID(), startedAt: Date = Date(), plan: [TestModule], specs: SystemSpecs? = nil,
                hasNoBuiltInInputs: Bool = false) {
        self.id = id
        self.startedAt = startedAt
        self.finishedAt = nil
        self.plan = plan
        self.completed = []
        self.skipped = []
        self.failures = []
        self.specs = specs
        self.hasNoBuiltInInputs = hasNoBuiltInInputs
    }

    public var remaining: [TestModule] {
        plan.filter { module in
            !completed.contains(module) && !skipped.contains(module) && !failures.contains { $0.module == module }
        }
    }

    public var isFinished: Bool { remaining.isEmpty }

    public func failure(for module: TestModule) -> ModuleFailure? {
        failures.first { $0.module == module }
    }

    /// Hardware checks only (no CPU/GPU speed).
    public var ranAnyHardwareCheck: Bool {
        [TestModule.battery, .display, .speakers, .micCamera, .input, .drives, .memory].contains { completed.contains($0) }
    }
}
