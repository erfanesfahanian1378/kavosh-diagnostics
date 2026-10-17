#if os(macOS)
import KavoshCore
import SwiftUI

@MainActor
final class CPUStationModel: ObservableObject {
    @Published private(set) var progress: CpuBenchmarkProgress?
    @Published private(set) var result: CpuBenchmarkResult?
    @Published private(set) var errorMessage: String?

    private let flag = CancellationFlag()
    private var started = false
    private var recording: UUID?
    private weak var telemetry: LiveTelemetry?

    func start(durationSeconds: Int, telemetry: LiveTelemetry?) {
        guard !started else { return }
        started = true
        self.telemetry = telemetry
        recording = telemetry?.beginRecording()
        let engine = CPUBenchmarkEngine(configuration: .init(durationSeconds: Double(durationSeconds)))
        let flag = self.flag
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                let result = try engine.run(isCancelled: { flag.isCancelled }) { progress in
                    onMain { [weak self] in self?.progress = progress }
                }
                onMain { [weak self] in self?.finish(result) }
            } catch {
                if !flag.isCancelled {
                    onMain { [weak self] in self?.errorMessage = String(describing: error) }
                }
            }
        }
    }

    private func finish(_ raw: CpuBenchmarkResult) {
        var result = raw
        if let recording, let telemetry {
            let summary = telemetry.endRecording(recording)
            result.peakTemperatureCelsius = summary.peakCPUTemperature
            result.averagePowerWatts = summary.averagePower
            if summary.worstThermal >= .serious { result.thermalThrottleDetected = true }
        }
        recording = nil
        self.result = result
    }

    func cancel() {
        flag.cancel()
        if let recording { _ = telemetry?.endRecording(recording) }
        recording = nil
    }
}

@MainActor
struct CPUStation: View {
    @ObservedObject var runner: TestSuiteRunner
    @StateObject private var model = CPUStationModel()
    @EnvironmentObject private var telemetry: LiveTelemetry

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            if let error = model.errorMessage {
                StationErrorView(title: T("The CPU test stopped", "تست پردازنده متوقف شد"), message: error) {
                    runner.fail(.cpu, message: error)
                }
            } else if let result = model.result {
                CPUResultCard(result: result)
                ContinueBar(isLast: runner.report.remaining.count <= 1, autoAdvanceSeconds: runner.isSingleTest ? nil : 4) {
                    runner.complete(.cpu) { $0.cpu = result }
                }
            } else {
                running
            }
        }
        .onAppear {
            model.start(durationSeconds: runner.settings.performanceTestSeconds, telemetry: runner.telemetry)
        }
        .onDisappear { model.cancel() }
    }

    private var running: some View {
        let p = model.progress
        return VStack(alignment: .leading, spacing: 18) {
            RetroCard {
                VStack(alignment: .leading, spacing: 14) {
                    SectionLabel(stageTitle(p?.phase), symbol: "cpu")
                    Text(stageDetail(p?.phase))
                        .font(Theme.font(13, .medium))
                        .foregroundStyle(Theme.muted)
                        .fixedSize(horizontal: false, vertical: true)
                    HStack(alignment: .lastTextBaseline, spacing: 10) {
                        HardwareText(p?.liveScore.map { Fmt.number($0, decimals: 0) } ?? "…", size: 54, weight: .black, color: Theme.accent)
                        Text(T("live score", "امتیاز زنده"))
                            .font(Theme.font(13, .bold))
                            .foregroundStyle(Theme.muted)
                    }
                    RetroProgressBar(value: p?.phaseFraction ?? 0, color: Theme.accent)
                    HStack {
                        Text(T("Overall", "کل"))
                            .font(Theme.font(11, .bold))
                            .foregroundStyle(Theme.muted)
                        RetroProgressBar(value: p?.overallFraction ?? 0, color: Theme.yellow, height: 8)
                        HardwareText(Fmt.duration(p?.elapsed ?? 0), size: 12, weight: .bold, color: Theme.muted)
                    }
                }
            }
            HStack(spacing: 14) {
                StatTile(title: T("Multi-core score", "امتیاز چندهسته‌ای"),
                         value: p?.multiCoreScore.map { Fmt.number($0, decimals: 0) } ?? "—", symbol: "square.grid.3x3.fill")
                StatTile(title: T("Single-core score", "امتیاز تک‌هسته‌ای"),
                         value: p?.singleCoreScore.map { Fmt.number($0, decimals: 0) } ?? "—", symbol: "square.fill")
                StatTile(title: T("CPU temperature", "دمای پردازنده"),
                         value: Fmt.celsius(telemetry.latest?.cpuTemperatureCelsius),
                         color: Theme.temperatureColor(telemetry.latest?.cpuTemperatureCelsius), symbol: "thermometer.medium")
                StatTile(title: T("System power", "توان سیستم"),
                         value: Fmt.watts(telemetry.latest?.systemPowerWatts), symbol: "bolt.fill")
            }
            if let samples = p?.sustainedSamples, !samples.isEmpty {
                RetroCard {
                    VStack(alignment: .leading, spacing: 8) {
                        SectionLabel(T("Sustained score over time", "امتیاز پایدار در طول زمان"), symbol: "chart.xyaxis.line")
                        HistoryChart(values: samples, maxValue: (p?.multiCoreScore ?? 1000) * 1.15,
                                     capacity: max(12, samples.count), color: Theme.accent)
                            .frame(height: 90)
                    }
                }
            }
            if let wrong = p?.wrongResults, wrong > 0 {
                Label(T("The CPU returned \(wrong) wrong result(s) so far", "پردازنده تاکنون \(wrong) نتیجه اشتباه داده است"),
                      systemImage: "exclamationmark.triangle.fill")
                    .font(Theme.font(13, .bold))
                    .foregroundStyle(Theme.red)
            }
        }
    }

    private func stageTitle(_ phase: CpuBenchmarkProgress.Phase?) -> String {
        switch phase {
        case .multiCore: return T("Stage 1 of 3 — multi-core", "مرحله ۱ از ۳ — چندهسته‌ای")
        case .singleCore: return T("Stage 2 of 3 — single-core", "مرحله ۲ از ۳ — تک‌هسته‌ای")
        case .sustained: return T("Stage 3 of 3 — sustained load", "مرحله ۳ از ۳ — بار پایدار")
        case .finished: return T("Finishing", "در حال پایان")
        case .preparing, .none: return T("Preparing", "در حال آماده‌سازی")
        }
    }

    private func stageDetail(_ phase: CpuBenchmarkProgress.Phase?) -> String {
        switch phase {
        case .multiCore:
            return T("A fixed amount of work spread over every core. Takes the same time whatever duration you picked.",
                     "مقدار ثابتی کار روی همه هسته‌ها. مدت آن به مدت انتخابی شما بستگی ندارد.")
        case .singleCore:
            return T("A fixed amount of work on one core.", "مقدار ثابتی کار روی یک هسته.")
        case .sustained:
            return T("Sustained load for the rest of the chosen duration, to see whether the CPU holds its speed as it heats up.",
                     "بار پایدار تا پایان مدت انتخابی، برای دیدن این‌که آیا پردازنده با گرم شدن سرعتش را حفظ می‌کند.")
        default:
            return T("Getting the workload ready…", "آماده‌سازی بار کاری…")
        }
    }
}

@MainActor
struct CPUResultCard: View {
    var result: CpuBenchmarkResult

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 16) {
                SectionLabel(T("CPU benchmark results", "نتایج بنچمارک پردازنده"), symbol: "cpu")
                HStack(spacing: 14) {
                    StatTile(title: T("Multi-core score", "امتیاز چندهسته‌ای"), value: Fmt.number(result.score, decimals: 0),
                             detail: T("\(result.threadsUsed) threads", "\(result.threadsUsed) رشته"), color: Theme.accent)
                    StatTile(title: T("Single-core score", "امتیاز تک‌هسته‌ای"), value: Fmt.number(result.singleCoreScore, decimals: 0),
                             color: Theme.accent)
                    StatTile(title: T("Sustained", "پایدار"),
                             value: result.sustainedScore.map { Fmt.number($0, decimals: 0) } ?? "—",
                             detail: result.sustainedScore == nil
                                ? T("Raise the duration to measure it", "برای اندازه‌گیری، مدت را بیشتر کنید") : nil)
                    StatTile(title: T("Peak CPU temperature", "بیشترین دمای پردازنده"),
                             value: Fmt.celsius(result.peakTemperatureCelsius),
                             color: Theme.temperatureColor(result.peakTemperatureCelsius))
                }
                if result.wrongResults == 0 {
                    Label(T("Every calculation came back correct.", "همه محاسبات درست برگشتند."), systemImage: "checkmark.seal.fill")
                        .font(Theme.font(13, .bold))
                        .foregroundStyle(Theme.green)
                } else {
                    Label(T("The CPU returned wrong results in \(result.wrongResults) calculation(s). This points to instability (an overclock or undervolt) or a hardware fault — the score isn't valid.",
                            "پردازنده در \(result.wrongResults) محاسبه نتیجه اشتباه داد. این نشانه ناپایداری (اورکلاک یا آندرولت) یا ایراد سخت‌افزاری است — امتیاز معتبر نیست."),
                          systemImage: "xmark.octagon.fill")
                        .font(Theme.font(13, .bold))
                        .foregroundStyle(Theme.red)
                        .fixedSize(horizontal: false, vertical: true)
                }
                if result.thermalThrottleDetected {
                    Label(T("Thermal throttle detected — this Mac did not hold its speed under sustained load.",
                            "کاهش سرعت حرارتی تشخیص داده شد — این مک زیر بار پایدار سرعتش را حفظ نکرد."),
                          systemImage: "flame.fill")
                        .font(Theme.font(13, .bold))
                        .foregroundStyle(Theme.orange)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Text(T("Reference \(CPUBenchmarkEngine.referenceName): 1,000 multi-core, 100 single-core. A CPU that finishes the same work in half the time scores double. Sustained = the multi-core rate at the end of the soak; it depends on the duration you chose.",
                       "مرجع \(CPUBenchmarkEngine.referenceName): ۱۰۰۰ چندهسته‌ای و ۱۰۰ تک‌هسته‌ای. پردازنده‌ای که همین کار را در نصف زمان انجام دهد دو برابر امتیاز می‌گیرد. امتیاز پایدار = سرعت چندهسته‌ای در پایان بار پایدار؛ به مدت انتخابی شما بستگی دارد."))
                    .font(Theme.font(11, .medium))
                    .foregroundStyle(Theme.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}
#endif
