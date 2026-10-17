#if os(macOS)
import KavoshCore
import SwiftUI

// MARK: - Memory

@MainActor
final class RAMStationModel: ObservableObject {
    @Published private(set) var progress: MemoryTestProgress?
    @Published private(set) var result: MemoryTestResult?
    @Published private(set) var errorMessage: String?

    private let flag = CancellationFlag()
    private var started = false

    func start(underPressure: Bool) {
        guard !started else { return }
        started = true
        let size = MemoryTestEngine.recommendedSize(underPressure: underPressure)
        let engine = MemoryTestEngine(sizeBytes: size)
        let flag = self.flag
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                let result = try engine.run(isCancelled: { flag.isCancelled }) { progress in
                    onMain { [weak self] in self?.progress = progress }
                }
                onMain { [weak self] in self?.result = result }
            } catch MemoryTestError.allocationFailed {
                onMain { [weak self] in
                    self?.errorMessage = T("macOS couldn't give the test enough memory. Close some apps and try again.",
                                           "macOS حافظه کافی به تست نداد. چند برنامه را ببندید و دوباره امتحان کنید.")
                }
            } catch {
                // Cancelled: nothing to report.
            }
        }
    }

    func cancel() { flag.cancel() }
}

@MainActor
struct RAMStation: View {
    @ObservedObject var runner: TestSuiteRunner
    @StateObject private var model = RAMStationModel()

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            if let error = model.errorMessage {
                StationErrorView(title: T("The memory test couldn't run", "تست حافظه اجرا نشد"), message: error) {
                    runner.fail(.memory, message: error)
                }
            } else if let result = model.result {
                MemoryResultCard(result: result)
                ContinueBar(isLast: runner.report.remaining.count <= 1, autoAdvanceSeconds: runner.isSingleTest ? nil : 4) {
                    runner.complete(.memory) { $0.memory = result }
                }
            } else {
                running
            }
        }
        .onAppear {
            model.start(underPressure: (runner.telemetry?.latest?.memoryPressure ?? .normal) != .normal)
        }
        .onDisappear { model.cancel() }
    }

    private var running: some View {
        let p = model.progress
        return RetroCard {
            VStack(alignment: .leading, spacing: 14) {
                SectionLabel(T("Pattern write / verify", "نوشتن / بررسی الگو"), symbol: "memorychip")
                Text(phaseText(p))
                    .font(Theme.font(18, .bold))
                RetroProgressBar(value: p?.fraction ?? 0, color: Theme.purple)
                HStack(spacing: 14) {
                    StatTile(title: T("Tested", "حجم تست"), value: p.map { "\($0.testedBytes / 1_048_576) MiB" } ?? "—")
                    StatTile(title: T("Write throughput", "سرعت نوشتن"), value: p?.writeMBps.map { Fmt.mbps($0) } ?? "—")
                    StatTile(title: T("Read throughput", "سرعت خواندن"), value: p?.readMBps.map { Fmt.mbps($0) } ?? "—")
                    StatTile(title: T("Errors detected", "خطاهای پیدا‌شده"), value: "\(p?.errors ?? 0)",
                             color: (p?.errors ?? 0) > 0 ? Theme.red : Theme.green)
                }
            }
        }
    }

    private func phaseText(_ p: MemoryTestProgress?) -> String {
        guard let p else { return T("Allocating memory…", "در حال گرفتن حافظه…") }
        let hex = String(format: "%016llX", p.pattern)
        switch p.phase {
        case .allocating: return T("Allocating memory…", "در حال گرفتن حافظه…")
        case .writing: return T("Writing pass \(p.pass)/\(p.passes) (0x\(hex))", "نوشتن دور \(p.pass)/\(p.passes) (0x\(hex))")
        case .verifying: return T("Verifying pass \(p.pass)/\(p.passes) (0x\(hex))", "بررسی دور \(p.pass)/\(p.passes) (0x\(hex))")
        case .finished: return T("Done", "تمام شد")
        }
    }
}

@MainActor
struct MemoryResultCard: View {
    var result: MemoryTestResult

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 14) {
                SectionLabel(T("Memory test results", "نتایج تست حافظه"), symbol: "memorychip")
                if result.passed {
                    Label(T("No issues found.", "مشکلی پیدا نشد."), systemImage: "checkmark.seal.fill")
                        .font(Theme.font(16, .bold))
                        .foregroundStyle(Theme.green)
                } else {
                    Label(T("Memory errors detected", "خطای حافظه پیدا شد"), systemImage: "xmark.octagon.fill")
                        .font(Theme.font(16, .bold))
                        .foregroundStyle(Theme.red)
                }
                HStack(spacing: 14) {
                    StatTile(title: T("Tested", "حجم تست"), value: "\(result.testedMiB) MiB",
                             detail: T("\(result.passes) patterns", "\(result.passes) الگو"))
                    StatTile(title: T("Write throughput", "سرعت نوشتن"), value: Fmt.mbps(result.averageWriteThroughputMBps))
                    StatTile(title: T("Read throughput", "سرعت خواندن"), value: Fmt.mbps(result.averageReadThroughputMBps))
                    StatTile(title: T("Errors detected", "خطاهای پیدا‌شده"), value: "\(result.errorsDetected)",
                             color: result.passed ? Theme.green : Theme.red)
                }
            }
        }
    }
}

// MARK: - Battery

@MainActor
final class BatteryStationModel: ObservableObject {
    enum Phase { case reading, noBattery, sampling, done }

    @Published private(set) var phase: Phase = .reading
    @Published private(set) var reading: BatteryReader.Reading?
    @Published private(set) var dischargeSamples: [Double] = []
    @Published private(set) var elapsed: Double = 0
    @Published private(set) var result: BatteryHealthResult?

    static let loadSeconds = 20.0
    private let flag = CancellationFlag()
    private var started = false

    func start() {
        guard !started else { return }
        started = true
        let flag = self.flag
        let loadSeconds = BatteryStationModel.loadSeconds
        DispatchQueue.global(qos: .userInitiated).async {
            guard let first = BatteryReader.read(), first.designCapacityMah > 0 else {
                onMain { [weak self] in self?.phase = .noBattery }
                return
            }
            onMain { [weak self] in
                self?.reading = first
                self?.phase = .sampling
            }
            let load = CPULoadGenerator()
            load.start()
            let start = Date()
            var samples: [Double] = []
            var latest = first
            while Date().timeIntervalSince(start) < loadSeconds && !flag.isCancelled {
                Thread.sleep(forTimeInterval: 1)
                if let r = BatteryReader.read() {
                    latest = r
                    if let watts = r.dischargeWatts { samples.append(watts) }
                }
                let snapshot = samples
                let elapsed = Date().timeIntervalSince(start)
                let current = latest
                onMain { [weak self] in
                    self?.dischargeSamples = snapshot
                    self?.elapsed = elapsed
                    self?.reading = current
                }
            }
            load.stop()
            guard !flag.isCancelled else { return }
            // Ignore the first couple of seconds while the load ramps up.
            let settled = samples.count > 4 ? Array(samples.dropFirst(2)) : samples
            let average = settled.isEmpty ? nil : settled.reduce(0, +) / Double(settled.count)
            let result = BatteryReader.result(from: latest, dischargeUnderLoad: average)
            onMain { [weak self] in
                self?.result = result
                self?.phase = .done
            }
        }
    }

    func cancel() { flag.cancel() }
}

@MainActor
struct BatteryStation: View {
    @ObservedObject var runner: TestSuiteRunner
    @StateObject private var model = BatteryStationModel()

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            switch model.phase {
            case .reading:
                RetroCard {
                    HStack(spacing: 10) {
                        ProgressView().controlSize(.small)
                        Text(T("Reading the battery…", "در حال خواندن باتری…")).font(Theme.font(14, .semibold))
                    }
                }
            case .noBattery:
                RetroCard {
                    VStack(alignment: .leading, spacing: 8) {
                        SectionLabel(T("Battery", "باتری"), symbol: "battery.0percent")
                        Text(T("No battery detected on this system. This Mac has no battery, so nothing is scored for it.",
                               "باتری‌ای روی این سیستم پیدا نشد. این مک باتری ندارد، پس امتیازی برای آن حساب نمی‌شود."))
                            .font(Theme.font(14, .medium))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                ContinueBar(isLast: runner.report.remaining.count <= 1, autoAdvanceSeconds: runner.isSingleTest ? nil : 3) {
                    runner.complete(.battery) { $0.battery = BatteryHealthResult(batteryPresent: false) }
                }
            case .sampling:
                if let reading = model.reading {
                    BatteryInfoCard(reading: reading)
                }
                RetroCard {
                    VStack(alignment: .leading, spacing: 12) {
                        SectionLabel(T("Discharge (under load)", "تخلیه (زیر بار)"), symbol: "bolt.fill")
                        Text(T("Sampling battery + running a short load…", "در حال نمونه‌برداری از باتری و اجرای یک بار کوتاه…"))
                            .font(Theme.font(14, .semibold))
                        RetroProgressBar(value: model.elapsed / BatteryStationModel.loadSeconds, color: Theme.green)
                        if model.reading?.isPluggedIn == true {
                            Text(T("The Mac is plugged in, so the discharge rate can't be measured. Unplug it for this step to measure it.",
                                   "مک به برق وصل است، پس نرخ تخلیه اندازه‌گیری نمی‌شود. برای اندازه‌گیری، در این مرحله آن را از برق جدا کنید."))
                                .font(Theme.font(12, .medium))
                                .foregroundStyle(Theme.muted)
                                .fixedSize(horizontal: false, vertical: true)
                        } else if let last = model.dischargeSamples.last {
                            HardwareText("\(Fmt.fixed(last, 1)) W", size: 28, weight: .heavy, color: Theme.green)
                        }
                    }
                }
            case .done:
                if let result = model.result {
                    BatteryResultCard(result: result)
                    ContinueBar(isLast: runner.report.remaining.count <= 1, autoAdvanceSeconds: runner.isSingleTest ? nil : 5) {
                        runner.complete(.battery) { $0.battery = result }
                    }
                }
            }
        }
        .onAppear { model.start() }
        .onDisappear { model.cancel() }
    }
}

@MainActor
struct BatteryInfoCard: View {
    var reading: BatteryReader.Reading

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 12) {
                SectionLabel(T("General info", "اطلاعات کلی"), symbol: "battery.75percent")
                HStack(spacing: 14) {
                    StatTile(title: T("Health", "سلامت"), value: Fmt.percent(reading.healthPercent, decimals: 1),
                             detail: T("\(Fmt.number(reading.fullChargeCapacityMah)) mAh of \(Fmt.number(reading.designCapacityMah)) mAh design",
                                       "\(Fmt.number(reading.fullChargeCapacityMah)) از \(Fmt.number(reading.designCapacityMah)) میلی‌آمپرساعت طراحی"),
                             color: Theme.scoreColor(reading.healthPercent))
                    StatTile(title: T("Cycles", "سیکل‌ها"), value: "\(reading.cycleCount)")
                    StatTile(title: T("Charge", "شارژ"), value: "\(reading.chargePercent)%",
                             detail: reading.isCharging ? T("charging", "در حال شارژ")
                                : (reading.isPluggedIn ? T("plugged in, not charging", "به برق وصل، بدون شارژ") : T("on battery", "روی باتری")))
                    StatTile(title: T("Temperature", "دما"), value: Fmt.celsius(reading.temperatureCelsius))
                }
            }
        }
    }
}

@MainActor
struct BatteryResultCard: View {
    var result: BatteryHealthResult

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    SectionLabel(T("Battery score", "امتیاز باتری"), symbol: "battery.100percent.bolt")
                    Spacer()
                    if result.needsService {
                        RetroBadge(T("Need service", "نیاز به سرویس"), color: Theme.red, textColor: .white, symbol: "wrench.fill")
                    }
                }
                HStack(spacing: 14) {
                    StatTile(title: T("Health", "سلامت"), value: Fmt.percent(result.healthPercent, decimals: 1),
                             detail: T("\(Fmt.number(result.fullChargeCapacityMah)) mAh of \(Fmt.number(result.designCapacityMah)) mAh design",
                                       "\(Fmt.number(result.fullChargeCapacityMah)) از \(Fmt.number(result.designCapacityMah)) میلی‌آمپرساعت طراحی"),
                             color: Theme.scoreColor(result.healthPercent))
                    StatTile(title: T("Cycles", "سیکل‌ها"), value: "\(result.cycleCount)")
                    StatTile(title: T("Discharge (under load)", "تخلیه (زیر بار)"),
                             value: result.dischargeRateWatts.map { "\(Fmt.fixed($0, 1)) W" } ?? "—",
                             detail: result.dischargeRateWatts == nil ? T("Not measured (plugged in)", "اندازه‌گیری نشد (به برق وصل)") : nil)
                    StatTile(title: T("Condition", "وضعیت"), value: result.condition ?? "—")
                }
                Text(result.isCharging
                     ? T("Currently \(result.currentChargePercent)% charged and charging. Charged/discharged roughly \(result.cycleCount) cycles so far.",
                         "اکنون \(result.currentChargePercent)٪ شارژ دارد و در حال شارژ است. تاکنون حدود \(result.cycleCount) سیکل شارژ/تخلیه شده است.")
                     : T("Currently \(result.currentChargePercent)% charged, not charging. Charged/discharged roughly \(result.cycleCount) cycles so far.",
                         "اکنون \(result.currentChargePercent)٪ شارژ دارد و شارژ نمی‌شود. تاکنون حدود \(result.cycleCount) سیکل شارژ/تخلیه شده است."))
                    .font(Theme.font(13, .medium))
                    .fixedSize(horizontal: false, vertical: true)
                Text(T("A cycle counts as one full charge worth of use, spread across however many partial charges it took — recent MacBook batteries are rated for about 1,000 cycles.",
                       "هر سیکل برابر یک شارژ کامل مصرف است، حتی اگر در چند شارژ جزئی پخش شده باشد — باتری مک‌بوک‌های جدید برای حدود ۱۰۰۰ سیکل طراحی شده‌اند."))
                    .font(Theme.font(11, .medium))
                    .foregroundStyle(Theme.muted)
                    .fixedSize(horizontal: false, vertical: true)
                ForEach(result.issues, id: \.self) { issue in
                    Label(issue.text, systemImage: "exclamationmark.triangle.fill")
                        .font(Theme.font(13, .bold))
                        .foregroundStyle(Theme.orange)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }
}

// MARK: - Drives

@MainActor
final class DriveStationModel: ObservableObject {
    @Published private(set) var report: DriveHealthReport?
    private var started = false

    func start() {
        guard !started else { return }
        started = true
        DispatchQueue.global(qos: .userInitiated).async {
            let report = DriveReader.readHealth()
            onMain { [weak self] in self?.report = report }
        }
    }
}

@MainActor
struct DriveStation: View {
    @ObservedObject var runner: TestSuiteRunner
    @StateObject private var model = DriveStationModel()

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            RetroCard {
                VStack(alignment: .leading, spacing: 12) {
                    SectionLabel(T("SSD / Drive health (SMART)", "سلامت SSD / درایو (SMART)"), symbol: "internaldrive")
                    if let report = model.report {
                        if report.drives.isEmpty {
                            Text(T("No drives found.", "درایوی پیدا نشد.")).font(Theme.font(14, .medium))
                        }
                        ForEach(report.drives) { drive in
                            DriveHealthRow(drive: drive)
                        }
                    } else {
                        HStack(spacing: 8) {
                            ProgressView().controlSize(.small)
                            Text(T("Reading drive health (SMART)…", "در حال خواندن سلامت درایو (SMART)…"))
                                .font(Theme.font(14, .semibold))
                        }
                    }
                }
            }
            if let report = model.report {
                ContinueBar(isLast: runner.report.remaining.count <= 1, autoAdvanceSeconds: runner.isSingleTest ? nil : 4) {
                    runner.complete(.drives) { $0.drives = report }
                }
            }
        }
        .onAppear { model.start() }
    }
}
#endif
