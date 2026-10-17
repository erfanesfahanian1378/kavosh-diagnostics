import Foundation

/// Where the app keeps its files: ~/Library/Application Support/Kavosh Diagnostics.
public enum AppPaths {
    public static let folderName = "Kavosh Diagnostics"

    public static var supportDirectory: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Library/Application Support")
        let url = base.appendingPathComponent(folderName, isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    public static var settingsFile: URL { supportDirectory.appendingPathComponent("settings.json") }
    public static var lastReportFile: URL { supportDirectory.appendingPathComponent("last-report.json") }
    public static var unfinishedRunFile: URL { supportDirectory.appendingPathComponent("unfinished-run.json") }
    public static var lastStorageFile: URL { supportDirectory.appendingPathComponent("last-ssd-test.json") }
    public static var historyCSV: URL { supportDirectory.appendingPathComponent("results.csv") }
}

public enum ScreenWasherPattern: String, Codable, CaseIterable, Sendable {
    case noise
    case rgbw
    case gradient

    public var isFlashing: Bool { self != .gradient }

    public var title: String {
        switch self {
        case .noise: return T("Static noise", "نویز ثابت")
        case .rgbw: return T("RGBW cycle", "چرخه RGBW")
        case .gradient: return T("Gradient sweep", "گرادیان متحرک")
        }
    }

    public var detail: String {
        switch self {
        case .noise: return T("strongest effect (flashes)", "قوی‌ترین اثر (چشمک می‌زند)")
        case .rgbw: return T("exercises subpixels (flashes)", "زیرپیکسل‌ها را فعال می‌کند (چشمک می‌زند)")
        case .gradient: return T("gentle, no flashing", "ملایم، بدون چشمک")
        }
    }
}

public enum ScreenWasherDuration: Int, Codable, CaseIterable, Sendable {
    case quick = 300
    case standard = 900
    case deep = 1800
    case infinite = 0

    public var seconds: Double? { self == .infinite ? nil : Double(rawValue) }

    public var title: String {
        switch self {
        case .quick: return T("5 minutes — Quick Clean", "۵ دقیقه — پاک‌سازی سریع")
        case .standard: return T("15 minutes — Standard", "۱۵ دقیقه — استاندارد")
        case .deep: return T("30 minutes — Deep Cycle", "۳۰ دقیقه — چرخه عمیق")
        case .infinite: return T("Infinite — stop it manually", "نامحدود — دستی متوقف کنید")
        }
    }

    public var detail: String {
        switch self {
        case .quick: return T("Runs for 5 minutes, then stops on its own. Good for faint retention that appeared in the last few minutes.",
                              "۵ دقیقه اجرا می‌شود و خودش متوقف می‌شود. برای اثر کم‌رنگی که در چند دقیقه اخیر ایجاد شده مناسب است.")
        case .standard: return T("Runs for 15 minutes, then stops on its own. The usual choice for retention that's still visible after a quick pass.",
                                 "۱۵ دقیقه اجرا می‌شود و خودش متوقف می‌شود. انتخاب معمول برای اثری که بعد از پاک‌سازی سریع هنوز دیده می‌شود.")
        case .deep: return T("Runs for 30 minutes, then stops on its own. For stubborn retention from a static image that was on screen for hours.",
                             "۳۰ دقیقه اجرا می‌شود و خودش متوقف می‌شود. برای اثر سمج تصویری که ساعت‌ها روی صفحه بوده است.")
        case .infinite: return T("Runs until you stop it with Esc or the Stop button. Nothing will end it automatically.",
                                 "تا وقتی با Esc یا دکمه توقف متوقفش کنید اجرا می‌شود. خودبه‌خود تمام نمی‌شود.")
        }
    }
}

/// User preferences, saved as JSON.
public struct AppSettings: Codable, Equatable, Sendable {
    public var language: AppLanguage
    /// CPU and GPU test length in seconds (includes the sustained phase).
    public var performanceTestSeconds: Int
    public var storageProfile: String
    public var washerDuration: ScreenWasherDuration
    public var washerPattern: ScreenWasherPattern
    public var washerRotate: Bool
    public var noBuiltInInputs: Bool

    public init(language: AppLanguage = .en, performanceTestSeconds: Int = 60, storageProfile: String = "standard",
                washerDuration: ScreenWasherDuration = .standard, washerPattern: ScreenWasherPattern = .gradient,
                washerRotate: Bool = true, noBuiltInInputs: Bool = false) {
        self.language = language
        self.performanceTestSeconds = performanceTestSeconds
        self.storageProfile = storageProfile
        self.washerDuration = washerDuration
        self.washerPattern = washerPattern
        self.washerRotate = washerRotate
        self.noBuiltInInputs = noBuiltInInputs
    }

    public static let durationChoices = [30, 60, 180, 300, 600]

    public var storageTestProfile: StorageTestProfile {
        storageProfile == "quick" ? .quick : .standard
    }

    // Tolerate older or partial files.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let defaults = AppSettings()
        language = (try? c.decode(AppLanguage.self, forKey: .language)) ?? defaults.language
        performanceTestSeconds = (try? c.decode(Int.self, forKey: .performanceTestSeconds)) ?? defaults.performanceTestSeconds
        storageProfile = (try? c.decode(String.self, forKey: .storageProfile)) ?? defaults.storageProfile
        washerDuration = (try? c.decode(ScreenWasherDuration.self, forKey: .washerDuration)) ?? defaults.washerDuration
        washerPattern = (try? c.decode(ScreenWasherPattern.self, forKey: .washerPattern)) ?? defaults.washerPattern
        washerRotate = (try? c.decode(Bool.self, forKey: .washerRotate)) ?? defaults.washerRotate
        noBuiltInInputs = (try? c.decode(Bool.self, forKey: .noBuiltInInputs)) ?? defaults.noBuiltInInputs
    }
}

/// Reads and writes the app's JSON files.
public enum ResultStore {
    static var encoder: JSONEncoder {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        e.dateEncodingStrategy = .iso8601
        return e
    }

    static var decoder: JSONDecoder {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        return d
    }

    public static func save<T: Encodable>(_ value: T, to url: URL) throws {
        let data = try encoder.encode(value)
        try data.write(to: url, options: .atomic)
    }

    public static func load<T: Decodable>(_ type: T.Type, from url: URL) -> T? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? decoder.decode(type, from: data)
    }

    public static func remove(_ url: URL) {
        try? FileManager.default.removeItem(at: url)
    }

    public static func loadSettings() -> AppSettings {
        load(AppSettings.self, from: AppPaths.settingsFile) ?? AppSettings()
    }

    public static func saveSettings(_ settings: AppSettings) {
        try? save(settings, to: AppPaths.settingsFile)
    }

    /// Appends a run to the history CSV, writing the header first if the file is new.
    public static func appendToHistory(report: FinalReport, evaluation: HealthEvaluation,
                                       storage: StorageBenchmarkResult?) throws {
        let url = AppPaths.historyCSV
        var text = ""
        if !FileManager.default.fileExists(atPath: url.path) {
            text += CSVExport.header.joined(separator: ",") + "\n"
        }
        text += CSVExport.row(report: report, evaluation: evaluation, storage: storage) + "\n"
        if let handle = try? FileHandle(forWritingTo: url) {
            defer { try? handle.close() }
            handle.seekToEndOfFile()
            handle.write(Data(text.utf8))
        } else {
            try Data(text.utf8).write(to: url, options: .atomic)
        }
    }
}

/// Spreadsheet export. Column names stay in English so files from different Macs line up.
public enum CSVExport {
    public static let header: [String] = [
        "Recorded (UTC)", "Model", "Model Identifier", "CPU", "CPU Cores", "Memory (GB)", "GPU", "GPU Cores",
        "macOS", "Health Score", "Grade",
        "CPU Single Core", "CPU Multi Core", "CPU Sustained", "CPU Wrong Results", "CPU Throttled",
        "GPU Score", "GPU Sustained", "GPU (avg fps)",
        "RAM Write (MB/s)", "RAM Read (MB/s)", "Memory Errors",
        "Battery Health (%)", "Battery Cycles", "Battery Discharge (W)",
        "Display Issues", "Keyboard Issues", "Speaker Issues", "Mic/Camera Issues", "Worst Drive Score",
        "SSD Seq Read (MB/s)", "SSD Seq Write (MB/s)", "SSD Random Read (MB/s)", "SSD Random Write (MB/s)",
        "SSD RND4K Q1T1 Read (MB/s)", "SSD RND4K Q1T1 Write (MB/s)"
    ]

    public static func escape(_ field: String) -> String {
        if field.contains(",") || field.contains("\"") || field.contains("\n") || field.contains("\r") {
            return "\"" + field.replacingOccurrences(of: "\"", with: "\"\"") + "\""
        }
        return field
    }

    static func num(_ value: Double?, _ decimals: Int = 1) -> String {
        guard let value, value.isFinite else { return "" }
        return Fmt.fixed(value, decimals)
    }

    public static func row(report: FinalReport, evaluation: HealthEvaluation, storage: StorageBenchmarkResult?) -> String {
        let iso = ISO8601DateFormatter()
        iso.timeZone = TimeZone(identifier: "UTC")
        let specs = report.specs
        let componentIssues: (HealthComponent.Kind) -> String = { kind in
            evaluation.components.first { $0.kind == kind }.map { String($0.issues.count) } ?? ""
        }
        let fields: [String] = [
            iso.string(from: report.finishedAt ?? report.startedAt),
            specs?.modelName ?? "",
            specs?.modelIdentifier ?? "",
            specs?.cpuName ?? "",
            specs.map { String($0.cpuCores) } ?? "",
            num(specs?.ramCapacityGB, 0),
            specs?.gpuName ?? "",
            specs?.gpuCoreCount.map(String.init) ?? "",
            specs?.operatingSystem ?? "",
            num(evaluation.score, 1),
            evaluation.grade?.rawValue ?? "",
            num(report.cpu?.singleCoreScore, 0),
            num(report.cpu?.score, 0),
            num(report.cpu?.sustainedScore, 0),
            report.cpu.map { String($0.wrongResults) } ?? "",
            report.cpu.map { $0.thermalThrottleDetected ? "yes" : "no" } ?? "",
            num(report.gpu?.score, 0),
            num(report.gpu?.sustainedScore, 0),
            num(report.gpu?.averageFps, 2),
            num(report.memory?.averageWriteThroughputMBps, 0),
            num(report.memory?.averageReadThroughputMBps, 0),
            report.memory.map { String($0.errorsDetected) } ?? "",
            report.battery?.batteryPresent == true ? num(report.battery?.healthPercent, 1) : "",
            report.battery?.batteryPresent == true ? String(report.battery!.cycleCount) : "",
            num(report.battery?.dischargeRateWatts, 1),
            componentIssues(.display),
            componentIssues(.keyboard),
            componentIssues(.speakers),
            componentIssues(.micCamera),
            num(report.drives?.worstScore, 0),
            num(storage?.measurement(.seq1mQ8T1, .read)?.mbPerSecond, 1),
            num(storage?.measurement(.seq1mQ8T1, .write)?.mbPerSecond, 1),
            num(storage?.measurement(.rnd4kQ32T1, .read)?.mbPerSecond, 1),
            num(storage?.measurement(.rnd4kQ32T1, .write)?.mbPerSecond, 1),
            num(storage?.measurement(.rnd4kQ1T1, .read)?.mbPerSecond, 1),
            num(storage?.measurement(.rnd4kQ1T1, .write)?.mbPerSecond, 1)
        ]
        return fields.map(escape).joined(separator: ",")
    }

    /// A complete CSV document for one run.
    public static func document(report: FinalReport, evaluation: HealthEvaluation, storage: StorageBenchmarkResult?) -> String {
        header.joined(separator: ",") + "\n" + row(report: report, evaluation: evaluation, storage: storage) + "\n"
    }
}
