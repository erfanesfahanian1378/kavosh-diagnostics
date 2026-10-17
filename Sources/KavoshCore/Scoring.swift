import Foundation

/// Letter grade for the device's overall condition.
public enum DeviceGrade: String, Codable, CaseIterable, Sendable {
    case aPlus = "A+"
    case a = "A"
    case b = "B"
    case c = "C"
    case d = "D"
    case f = "F"

    public enum Tone: String, Sendable {
        case excellent, good, fair, poor, bad
    }

    public init(score: Double) {
        switch score {
        case 97...: self = .aPlus
        case 92..<97: self = .a
        case 85..<92: self = .b
        case 75..<85: self = .c
        case 60..<75: self = .d
        default: self = .f
        }
    }

    public var tone: Tone {
        switch self {
        case .aPlus, .a: return .excellent
        case .b: return .good
        case .c: return .fair
        case .d: return .poor
        case .f: return .bad
        }
    }

    /// The grade `steps` letters lower (never below F).
    public func lowered(by steps: Int) -> DeviceGrade {
        let all = DeviceGrade.allCases
        guard let index = all.firstIndex(of: self) else { return self }
        return all[min(all.count - 1, index + max(0, steps))]
    }

    /// This grade, but no better than `cap`.
    public func cappedAt(_ cap: DeviceGrade) -> DeviceGrade {
        let all = DeviceGrade.allCases
        let mine = all.firstIndex(of: self) ?? 0
        let other = all.firstIndex(of: cap) ?? 0
        return all[max(mine, other)]
    }

    public var verdict: String {
        switch self {
        case .aPlus: return T("Excellent — like new", "عالی — مثل نو")
        case .a: return T("Excellent condition", "وضعیت عالی")
        case .b: return T("Good condition", "وضعیت خوب")
        case .c: return T("Fair — some issues found", "متوسط — چند ایراد پیدا شد")
        case .d: return T("Poor — needs attention", "ضعیف — نیاز به رسیدگی")
        case .f: return T("Needs service", "نیاز به تعمیر")
        }
    }
}

public struct HealthComponent: Codable, Equatable, Identifiable, Sendable {
    public enum Kind: String, Codable, CaseIterable, Sendable {
        case keyboard, display, speakers, micCamera, battery, memory, drives, cpuStability

        public var title: String {
            switch self {
            case .keyboard: return T("Keyboard & Trackpad", "کیبورد و ترک‌پد")
            case .display: return T("Display", "نمایشگر")
            case .speakers: return T("Speakers", "بلندگوها")
            case .micCamera: return T("Mic & Camera", "میکروفون و دوربین")
            case .battery: return T("Battery", "باتری")
            case .memory: return T("Memory errors", "خطاهای حافظه")
            case .drives: return T("Drive health (SMART)", "سلامت درایو (SMART)")
            case .cpuStability: return T("CPU calculation errors", "خطاهای محاسبه پردازنده")
            }
        }

        var weight: Double {
            switch self {
            case .keyboard: return 1.0
            case .display: return 1.2
            case .speakers: return 0.8
            case .micCamera: return 0.8
            case .battery: return 1.2
            case .memory: return 1.0
            case .drives: return 1.0
            case .cpuStability: return 1.0
            }
        }
    }

    public var kind: Kind
    public var score: Double
    public var issues: [String]

    public var id: String { kind.rawValue }

    public init(kind: Kind, score: Double, issues: [String]) {
        self.kind = kind
        self.score = max(0, min(100, score))
        self.issues = issues
    }
}

public struct HealthEvaluation: Equatable, Sendable {
    /// 0–100, nil when no hardware check ran.
    public var score: Double?
    public var grade: DeviceGrade?
    /// The grade before any battery penalty or cap.
    public var gradeFromScore: DeviceGrade?
    public var components: [HealthComponent]
    public var batteryPenaltySteps: Int
    /// Problems serious enough to limit the grade (memory errors, CPU errors, a failing drive).
    public var cappedBy: [HealthComponent.Kind]

    public var totalIssues: Int { components.reduce(0) { $0 + $1.issues.count } }
}

/// Turns a run's measurements into a health score and grade.
///
/// Only the hardware checks count: keyboard, display, speakers, mic & camera, battery,
/// memory errors, CPU calculation errors and drive health. CPU and GPU *speed* never
/// affect the grade.
public enum BenchmarkScoring {
    public static func evaluate(_ report: FinalReport) -> HealthEvaluation {
        var components: [HealthComponent] = []

        if let keyboard = report.keyboard, !keyboard.bypassed {
            var score = 100.0
            var issues: [String] = []
            if !keyboard.missingKeys.isEmpty {
                score -= min(60, Double(keyboard.missingKeys.count) * 8)
                issues.append(T("Keys that didn't register: \(keyboard.missingKeys.joined(separator: " "))",
                                "کلیدهایی که کار نکردند: \(keyboard.missingKeys.joined(separator: " "))"))
            }
            if !keyboard.doublePressedKeys.isEmpty {
                score -= min(40, Double(keyboard.doublePressedKeys.count) * 10)
                issues.append(T("Keys that registered twice: \(keyboard.doublePressedKeys.joined(separator: " "))",
                                "کلیدهایی که دوبار ثبت شدند: \(keyboard.doublePressedKeys.joined(separator: " "))"))
            }
            if !keyboard.sentenceCorrect {
                score -= 5
                issues.append(T("The typed sentence didn't match", "جمله تایپ‌شده مطابقت نداشت"))
            }
            let trackpadMisses = 3 - keyboard.trackpadChecksPassed
            if trackpadMisses > 0 {
                score -= Double(trackpadMisses) * 15
                issues.append(T("Trackpad: \(keyboard.trackpadChecksPassed) of 3 checks detected",
                                "ترک‌پد: \(keyboard.trackpadChecksPassed) از ۳ بررسی تشخیص داده شد"))
            }
            components.append(HealthComponent(kind: .keyboard, score: score, issues: issues))
        }

        if let display = report.display {
            let score = 100 - display.issues.reduce(0) { $0 + $1.deduction }
            components.append(HealthComponent(kind: .display, score: score, issues: display.issues.map { $0.shortText }))
        }

        if let audio = report.audio {
            var score = 100.0
            var issues: [String] = []
            if !audio.leftHeard {
                score -= 50
                issues.append(T("Left speaker not heard", "بلندگوی چپ شنیده نشد"))
            }
            if !audio.rightHeard {
                score -= 50
                issues.append(T("Right speaker not heard", "بلندگوی راست شنیده نشد"))
            }
            if let note = audio.reportedIssue, !note.isEmpty {
                score -= 20
                issues.append(note)
            }
            components.append(HealthComponent(kind: .speakers, score: score, issues: issues))
        }

        if let mc = report.micCamera {
            var parts: [Double] = []
            var issues: [String] = []
            if mc.cameraPresent {
                parts.append(mc.cameraWorks ? 100 : 0)
                if !mc.cameraWorks { issues.append(T("Camera not working", "دوربین کار نمی‌کند")) }
            }
            if mc.microphonePresent {
                parts.append(mc.microphoneWorks ? 100 : 0)
                if !mc.microphoneWorks { issues.append(T("Microphone not working", "میکروفون کار نمی‌کند")) }
            } else {
                parts.append(0)
                issues.append(T("No microphone was found", "میکروفونی پیدا نشد"))
            }
            if let note = mc.reportedIssue, !note.isEmpty { issues.append(note) }
            let score = parts.isEmpty ? 100 : parts.reduce(0, +) / Double(parts.count)
            components.append(HealthComponent(kind: .micCamera, score: score, issues: issues))
        }

        var batteryPenalty = 0
        if let battery = report.battery, battery.batteryPresent {
            let health = battery.healthPercent
            var score: Double
            if health >= 95 {
                score = 100
            } else if health >= 80 {
                score = 60 + (health - 80) / 15 * 40
            } else {
                score = max(0, (health - 50) / 30 * 60)
            }
            for issue in battery.issues {
                switch issue {
                case .highCycleCount: score -= 10
                case .notCharging: score -= 15
                case .healthBelow90, .healthCritical: break
                }
            }
            if health > 0 && health < 70 {
                batteryPenalty = 2
            } else if health > 0 && health < 80 {
                batteryPenalty = 1
            }
            components.append(HealthComponent(kind: .battery, score: score, issues: battery.issues.map { $0.text }))
        }

        var caps: [(HealthComponent.Kind, DeviceGrade)] = []

        if let memory = report.memory {
            let issues = memory.errorsDetected > 0
                ? [T("\(memory.errorsDetected) memory errors detected", "\(memory.errorsDetected) خطای حافظه پیدا شد")]
                : []
            components.append(HealthComponent(kind: .memory, score: memory.errorsDetected > 0 ? 0 : 100, issues: issues))
            if memory.errorsDetected > 0 { caps.append((.memory, .d)) }
        }

        if let cpu = report.cpu {
            if cpu.wrongResults > 0 {
                components.append(HealthComponent(kind: .cpuStability, score: 0, issues: [
                    T("The CPU returned wrong results in \(cpu.wrongResults) calculation(s)",
                      "پردازنده در \(cpu.wrongResults) محاسبه نتیجه اشتباه داد")
                ]))
                caps.append((.cpuStability, .d))
            } else {
                components.append(HealthComponent(kind: .cpuStability, score: 100, issues: []))
            }
        }

        if let drives = report.drives, let worst = drives.worstScore {
            let issues = drives.drives.flatMap { drive in drive.issues.map { "\(drive.model): \($0.text)" } }
            components.append(HealthComponent(kind: .drives, score: worst, issues: issues))
            if worst < 20 { caps.append((.drives, .f)) }
        }

        guard !components.isEmpty else {
            return HealthEvaluation(score: nil, grade: nil, gradeFromScore: nil, components: [],
                                    batteryPenaltySteps: 0, cappedBy: [])
        }

        let totalWeight = components.reduce(0) { $0 + $1.kind.weight }
        let weighted = components.reduce(0) { $0 + $1.score * $1.kind.weight }
        // A single failing part pulls the average down hard, so a broken keyboard can't hide
        // behind seven perfect scores.
        let worst = components.map(\.score).min() ?? 100
        var score = weighted / totalWeight
        if worst < 60 {
            score = min(score, (score + worst) / 2 + 20)
        }
        score = max(0, min(100, score))

        let baseGrade = DeviceGrade(score: score)
        var grade = baseGrade.lowered(by: batteryPenalty)
        var cappedBy: [HealthComponent.Kind] = []
        for (kind, cap) in caps {
            grade = grade.cappedAt(cap)
            cappedBy.append(kind)
        }

        return HealthEvaluation(score: score, grade: grade, gradeFromScore: baseGrade, components: components,
                                batteryPenaltySteps: batteryPenalty, cappedBy: cappedBy)
    }
}
