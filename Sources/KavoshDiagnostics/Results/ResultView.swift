#if os(macOS)
import KavoshCore
import SwiftUI

@MainActor
struct ResultView: View {
    @EnvironmentObject private var model: AppModel
    var report: FinalReport

    var body: some View {
        let evaluation = BenchmarkScoring.evaluate(report)
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                VerdictCard(report: report, evaluation: evaluation)
                HStack(alignment: .top, spacing: 20) {
                    VStack(spacing: 20) {
                        if !evaluation.components.isEmpty {
                            HealthBreakdownCard(evaluation: evaluation)
                        }
                        if !report.failures.isEmpty || !report.skipped.isEmpty {
                            NotRunCard(report: report)
                        }
                        DetailsCard(report: report)
                    }
                    .frame(maxWidth: .infinity)
                    VStack(spacing: 20) {
                        PerformanceCard(report: report)
                        if let drives = report.drives, !drives.drives.isEmpty {
                            RetroCard {
                                VStack(alignment: .leading, spacing: 10) {
                                    SectionLabel(T("Drives (SMART)", "درایوها (SMART)"), symbol: "internaldrive")
                                    ForEach(drives.drives) { DriveHealthRow(drive: $0) }
                                }
                            }
                        }
                    }
                    .frame(maxWidth: .infinity)
                }
            }
            .padding(24)
        }
    }
}

@MainActor
struct VerdictCard: View {
    @EnvironmentObject private var model: AppModel
    var report: FinalReport
    var evaluation: HealthEvaluation

    private var batteryPenaltyText: String {
        evaluation.batteryPenaltySteps == 1
            ? T("The grade is one step below the health score because the battery has worn below 80% of its design capacity.",
                "نمره یک پله پایین‌تر از نمره سلامت است چون باتری زیر ۸۰٪ ظرفیت طراحی فرسوده شده است.")
            : T("The grade is two steps below the health score because the battery has worn below 70% of its design capacity.",
                "نمره دو پله پایین‌تر از نمره سلامت است چون باتری زیر ۷۰٪ ظرفیت طراحی فرسوده شده است.")
    }

    var body: some View {
        RetroCard(padding: 22) {
            HStack(alignment: .center, spacing: 24) {
                GradeBadge(grade: evaluation.grade, size: 120)
                VStack(alignment: .leading, spacing: 8) {
                    SectionLabel(T("Device health", "سلامت دستگاه"), symbol: "heart.text.square")
                    if let score = evaluation.score, let grade = evaluation.grade {
                        Text(grade.verdict)
                            .font(Theme.font(28, .heavy))
                        HardwareText(T("Health score \(Fmt.fixed(score, 1)) of 100", "نمره سلامت \(Fmt.fixed(score, 1)) از ۱۰۰"), size: 16, weight: .bold,
                                     color: Theme.muted)
                        Text(T("Based only on the hardware checks — keyboard, display, speakers, mic & camera, battery, memory errors and drive (SMART) health. CPU and GPU speed don't affect it.",
                               "فقط بر اساس بررسی‌های سخت‌افزاری — کیبورد، نمایشگر، بلندگو، میکروفون و دوربین، باتری، خطای حافظه و سلامت درایو (SMART). سرعت پردازنده و گرافیک در آن اثری ندارد."))
                            .font(Theme.font(12, .medium))
                            .foregroundStyle(Theme.muted)
                            .fixedSize(horizontal: false, vertical: true)
                        if evaluation.batteryPenaltySteps > 0 {
                            Label(batteryPenaltyText, systemImage: "battery.25percent")
                                .font(Theme.font(12, .semibold))
                                .foregroundStyle(Theme.orange)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        ForEach(evaluation.cappedBy, id: \.self) { kind in
                            Label(T("\(kind.title) limit the grade.", "\(kind.title) نمره را محدود می‌کند."), systemImage: "exclamationmark.octagon.fill")
                                .font(Theme.font(12, .semibold))
                                .foregroundStyle(Theme.red)
                        }
                    } else {
                        Text(T("Performance test only", "فقط تست کارایی"))
                            .font(Theme.font(28, .heavy))
                        Text(T("No hardware checks were run, so there's no health verdict. The CPU and GPU scores are below.",
                               "هیچ بررسی سخت‌افزاری اجرا نشد، پس نتیجه سلامتی وجود ندارد. امتیاز پردازنده و گرافیک در پایین آمده است."))
                            .font(Theme.font(13, .medium))
                            .foregroundStyle(Theme.muted)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    HStack(spacing: 6) {
                        if let specs = report.specs {
                            RetroBadge(specs.modelName, symbol: "laptopcomputer")
                            RetroBadge(specs.cpuName, symbol: "cpu")
                        }
                        RetroBadge(DateText.string(report.finishedAt ?? report.startedAt), symbol: "calendar")
                    }
                }
                Spacer(minLength: 0)
                VStack(alignment: .trailing, spacing: 10) {
                    Button {
                        model.exportCSV(report)
                    } label: {
                        Label(T("Export CSV", "خروجی CSV"), systemImage: "square.and.arrow.down")
                    }
                    .retroButton(.secondary)
                    Button {
                        model.startRun(report.plan)
                    } label: {
                        Label(T("Run again", "اجرای دوباره"), systemImage: "arrow.clockwise")
                    }
                    .retroButton(.secondary)
                    Button {
                        model.goToDashboard()
                    } label: {
                        Label(T("Back to Dashboard", "بازگشت به داشبورد"), systemImage: "house.fill")
                    }
                    .retroButton(.primary)
                }
            }
        }
    }
}

@MainActor
struct HealthBreakdownCard: View {
    var evaluation: HealthEvaluation

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 12) {
                SectionLabel(T("See the full results behind this grade", "نتایج کامل پشت این نمره"), symbol: "list.bullet.clipboard")
                ForEach(evaluation.components) { component in
                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            StatusIcon(state: component.score >= 90 ? .pass : (component.score >= 60 ? .warn : .fail))
                            Text(component.kind.title)
                                .font(Theme.font(14, .bold))
                            Spacer()
                            ScoreBadge(score: component.score)
                        }
                        if component.issues.isEmpty {
                            Text(T("No issues found.", "مشکلی پیدا نشد."))
                                .font(Theme.font(12, .medium))
                                .foregroundStyle(Theme.muted)
                                .padding(.leading, 32)
                        } else {
                            ForEach(component.issues, id: \.self) { issue in
                                Text("• \(issue)")
                                    .font(Theme.font(12, .medium))
                                    .fixedSize(horizontal: false, vertical: true)
                                    .padding(.leading, 32)
                            }
                        }
                    }
                    if component.id != evaluation.components.last?.id {
                        Rectangle().fill(Theme.ink.opacity(0.1)).frame(height: 1)
                    }
                }
            }
        }
    }
}

@MainActor
struct NotRunCard: View {
    var report: FinalReport

    var body: some View {
        RetroCard(fill: Theme.yellow.opacity(0.2)) {
            VStack(alignment: .leading, spacing: 10) {
                SectionLabel(T("These tests couldn't run", "این تست‌ها اجرا نشدند"), symbol: "exclamationmark.triangle")
                ForEach(report.failures, id: \.module) { failure in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(failure.module.title).font(Theme.font(13, .bold))
                        Text(failure.message)
                            .font(Theme.font(12, .medium))
                            .foregroundStyle(Theme.muted)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                ForEach(report.skipped) { module in
                    HStack {
                        Text(module.title).font(Theme.font(13, .bold))
                        Spacer()
                        RetroBadge(T("Skipped", "رد شد"))
                    }
                }
            }
        }
    }
}

@MainActor
struct PerformanceCard: View {
    var report: FinalReport

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 14) {
                SectionLabel(T("Performance", "کارایی"), symbol: "speedometer")
                if let cpu = report.cpu {
                    performanceBlock(title: T("CPU", "پردازنده"), symbol: "cpu", rows: [
                        (T("Multi-core score", "امتیاز چندهسته‌ای"), Fmt.number(cpu.score, decimals: 0)),
                        (T("Single-core score", "امتیاز تک‌هسته‌ای"), Fmt.number(cpu.singleCoreScore, decimals: 0)),
                        (T("Sustained", "پایدار"), cpu.sustainedScore.map { Fmt.number($0, decimals: 0) } ?? "—"),
                        (T("Peak temperature", "بیشترین دما"), Fmt.celsius(cpu.peakTemperatureCelsius)),
                        (T("Calculation errors", "خطای محاسبه"), cpu.wrongResults == 0 ? T("None", "هیچ") : "\(cpu.wrongResults)"),
                        (T("Thermal throttling", "کاهش سرعت حرارتی"), cpu.thermalThrottleDetected ? T("Detected", "تشخیص داده شد") : T("Not detected", "تشخیص داده نشد"))
                    ])
                }
                if let gpu = report.gpu {
                    performanceBlock(title: T("GPU", "گرافیک"), symbol: "cube.transparent", rows: [
                        (T("GPU score", "امتیاز گرافیک"), Fmt.number(gpu.score, decimals: 0)),
                        (T("Average", "میانگین"), "\(Fmt.fixed(gpu.averageFps, 2)) fps · \(Fmt.fixed(gpu.averageFrameTimeMs, 1)) ms"),
                        (T("Sustained", "پایدار"), gpu.sustainedScore.map { Fmt.number($0, decimals: 0) } ?? "—"),
                        (T("Peak temperature", "بیشترین دما"), Fmt.celsius(gpu.peakTemperatureCelsius))
                    ])
                }
                if let memory = report.memory {
                    performanceBlock(title: T("Memory", "حافظه"), symbol: "memorychip", rows: [
                        (T("Write", "نوشتن"), Fmt.mbps(memory.averageWriteThroughputMBps)),
                        (T("Read", "خواندن"), Fmt.mbps(memory.averageReadThroughputMBps)),
                        (T("Errors detected", "خطاهای پیدا‌شده"), "\(memory.errorsDetected)")
                    ])
                }
                if report.cpu == nil && report.gpu == nil && report.memory == nil {
                    Text(T("No performance tests were run.", "هیچ تست کارایی اجرا نشد."))
                        .font(Theme.font(12, .medium))
                        .foregroundStyle(Theme.muted)
                }
            }
        }
    }

    private func performanceBlock(title: String, symbol: String, rows: [(String, String)]) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Label(title, systemImage: symbol)
                .font(Theme.font(14, .heavy))
            ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                KeyValueRow(key: row.0, value: row.1)
            }
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 9).fill(Theme.surfaceAlt.opacity(0.5)))
    }
}

@MainActor
struct DetailsCard: View {
    var report: FinalReport

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 12) {
                SectionLabel(T("Hardware checks", "بررسی‌های سخت‌افزاری"), symbol: "checklist")
                ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                    detail(row.0, row.1)
                }
                if rows.isEmpty {
                    Text(T("No hardware checks in this run.", "در این اجرا بررسی سخت‌افزاری نبود."))
                        .font(Theme.font(12, .medium))
                        .foregroundStyle(Theme.muted)
                }
            }
        }
    }

    private var rows: [(String, String)] {
        var result: [(String, String)] = []
        if let battery = report.battery { result.append((T("Battery", "باتری"), batteryText(battery))) }
        if let display = report.display { result.append((T("Display", "نمایشگر"), displayText(display))) }
        if let audio = report.audio { result.append((T("Speakers", "بلندگوها"), audioText(audio))) }
        if let mc = report.micCamera { result.append((T("Mic & Camera", "میکروفون و دوربین"), micCameraText(mc))) }
        if let keyboard = report.keyboard { result.append((T("Keyboard & Trackpad", "کیبورد و ترک‌پد"), keyboardText(keyboard))) }
        return result
    }

    private func batteryText(_ battery: BatteryHealthResult) -> String {
        guard battery.batteryPresent else { return T("No battery", "بدون باتری") }
        var parts = [
            T("\(Fmt.fixed(battery.healthPercent, 1))% health", "سلامت \(Fmt.fixed(battery.healthPercent, 1))٪"),
            T("\(battery.cycleCount) cycles", "\(battery.cycleCount) سیکل")
        ]
        if let watts = battery.dischargeRateWatts {
            parts.append(T("\(Fmt.fixed(watts, 1)) W discharge", "تخلیه \(Fmt.fixed(watts, 1)) وات"))
        }
        return parts.joined(separator: " · ")
    }

    private func displayText(_ display: DisplayTestResult) -> String {
        if display.issues.isEmpty {
            var text = T("No issues reported", "ایرادی گزارش نشد")
            if let hz = display.measuredRefreshRateHz { text += " · \(Fmt.fixed(hz, 0)) Hz" }
            return text
        }
        return display.issues.map { $0.shortText }.joined(separator: ", ")
    }

    private func mark(_ ok: Bool) -> String { ok ? "✓" : "✗" }

    private func audioText(_ audio: AudioTestResult) -> String {
        var text = "\(T("Left", "چپ")) \(mark(audio.leftHeard)) · \(T("Right", "راست")) \(mark(audio.rightHeard))"
        if let issue = audio.reportedIssue { text += " · \(issue)" }
        return text
    }

    private func micCameraText(_ mc: MicCameraTestResult) -> String {
        let notAvailable = T("N/A", "ندارد")
        let camera = mc.cameraPresent ? mark(mc.cameraWorks) : notAvailable
        let mic = mc.microphonePresent ? mark(mc.microphoneWorks) : notAvailable
        var text = "\(T("Camera", "دوربین")) \(camera) · \(T("Microphone", "میکروفون")) \(mic)"
        if let issue = mc.reportedIssue { text += " · \(issue)" }
        return text
    }

    private func keyboardText(_ keyboard: KeyboardTestResult) -> String {
        if keyboard.bypassed { return T("N/A (bypassed by user)", "ندارد (رد شده توسط کاربر)") }
        return T("\(keyboard.pressedKeys) of \(keyboard.totalKeys) keys · trackpad \(keyboard.trackpadChecksPassed)/3",
                 "\(keyboard.pressedKeys) از \(keyboard.totalKeys) کلید · ترک‌پد \(keyboard.trackpadChecksPassed)/۳")
    }

    private func detail(_ title: String, _ value: String) -> some View {
        HStack(alignment: .firstTextBaseline) {
            Text(title)
                .font(Theme.font(13, .bold))
                .frame(width: 150, alignment: .leading)
            Text(value)
                .font(Theme.font(13, .medium))
                .foregroundStyle(Theme.muted)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
        }
    }
}
#endif
