#if os(macOS)
import KavoshCore
import SwiftUI

@MainActor
final class GPUStationModel: ObservableObject {
    @Published private(set) var progress: GPUBenchmarkProgress?
    @Published private(set) var preview: CGImage?
    @Published private(set) var result: GpuBenchmarkResult?
    @Published private(set) var errorMessage: String?
    @Published private(set) var adapterName: String?

    private let flag = CancellationFlag()
    private var started = false
    private var recording: UUID?
    private weak var telemetry: LiveTelemetry?

    func start(durationSeconds: Int, telemetry: LiveTelemetry?) {
        guard !started else { return }
        started = true
        self.telemetry = telemetry
        recording = telemetry?.beginRecording()
        let flag = self.flag
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                var configuration = GPURaymarchBenchmark.Configuration()
                configuration.totalSeconds = Double(durationSeconds)
                let benchmark = try GPURaymarchBenchmark(configuration: configuration)
                let name = benchmark.device.name
                onMain { [weak self] in self?.adapterName = name }
                let result = try benchmark.run(isCancelled: { flag.isCancelled }) { progress in
                    onMain { [weak self] in
                        self?.progress = progress
                        if let image = progress.preview { self?.preview = image }
                    }
                }
                onMain { [weak self] in self?.finish(result) }
            } catch {
                if !flag.isCancelled {
                    let message = (error as? GPUBenchmarkError)?.description ?? error.localizedDescription
                    onMain { [weak self] in self?.errorMessage = message }
                }
            }
        }
    }

    private func finish(_ raw: GpuBenchmarkResult) {
        var result = raw
        if let recording, let telemetry {
            result.peakTemperatureCelsius = telemetry.endRecording(recording).peakGPUTemperature
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
struct GPUStation: View {
    @ObservedObject var runner: TestSuiteRunner
    @StateObject private var model = GPUStationModel()
    @EnvironmentObject private var telemetry: LiveTelemetry

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            if let error = model.errorMessage {
                StationErrorView(title: T("The GPU test couldn't run", "تست گرافیک اجرا نشد"), message: error) {
                    runner.fail(.gpu, message: error)
                }
            } else {
                previewCard
                if let result = model.result {
                    GPUResultCard(result: result)
                    ContinueBar(isLast: runner.report.remaining.count <= 1, autoAdvanceSeconds: runner.isSingleTest ? nil : 4) {
                        runner.complete(.gpu) { $0.gpu = result }
                    }
                } else {
                    liveTiles
                }
            }
        }
        .onAppear {
            model.start(durationSeconds: runner.settings.performanceTestSeconds, telemetry: runner.telemetry)
        }
        .onDisappear { model.cancel() }
    }

    private var previewCard: some View {
        RetroCard(padding: 12) {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    SectionLabel(T("Live render", "رندر زنده"), symbol: "sparkles")
                    Spacer()
                    if let name = model.adapterName {
                        RetroBadge(T("GPU under test: \(name)", "گرافیک در حال تست: \(name)"), color: Theme.surfaceAlt)
                    }
                }
                ZStack {
                    RoundedRectangle(cornerRadius: 8).fill(Color.black)
                    if let preview = model.preview {
                        Image(decorative: preview, scale: 1)
                            .resizable()
                            .interpolation(.high)
                            .aspectRatio(16.0 / 9.0, contentMode: .fit)
                    } else {
                        VStack(spacing: 8) {
                            ProgressView().controlSize(.small).tint(.white)
                            Text(T("The first image appears once the GPU finishes a frame.",
                                   "اولین تصویر وقتی ظاهر می‌شود که پردازنده گرافیکی یک فریم را تمام کند."))
                                .font(Theme.font(12, .medium))
                                .foregroundStyle(.white.opacity(0.8))
                        }
                    }
                }
                .aspectRatio(16.0 / 9.0, contentMode: .fit)
                .clipShape(RoundedRectangle(cornerRadius: 8))
                .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.ink, lineWidth: Theme.border))
                .frame(maxWidth: 760)
                Text(stageText)
                    .font(Theme.font(13, .semibold))
                RetroProgressBar(value: model.progress?.fraction ?? 0, color: Theme.teal)
            }
        }
    }

    private var liveTiles: some View {
        HStack(spacing: 14) {
            StatTile(title: T("Live score", "امتیاز زنده"),
                     value: model.progress?.liveScore.map { Fmt.number($0, decimals: 0) } ?? "…",
                     color: Theme.teal, symbol: "gauge.with.dots.needle.67percent")
            StatTile(title: T("ms per frame", "میلی‌ثانیه در هر فریم"),
                     value: model.progress?.lastFrameMs.map { Fmt.fixed($0, 1) } ?? "—", symbol: "timer")
            StatTile(title: T("GPU temperature", "دمای گرافیک"),
                     value: Fmt.celsius(telemetry.latest?.gpuTemperatureCelsius),
                     color: Theme.temperatureColor(telemetry.latest?.gpuTemperatureCelsius), symbol: "thermometer.medium")
            StatTile(title: T("GPU usage", "مصرف گرافیک"),
                     value: telemetry.latest?.gpuLoadPercent.map { Fmt.percent($0) } ?? "—", symbol: "chart.bar.fill")
        }
    }

    private var stageText: String {
        switch model.progress?.stage {
        case .none, .some(.preparing):
            return T("Preparing the GPU (compiling the shader, allocating a 5120×2880 render target)…",
                     "آماده‌سازی گرافیک (کامپایل شیدر و ساخت تصویر ۵۱۲۰×۲۸۸۰)…")
        case .some(.warmingUp(let frame)):
            return T("Warming up until clocks settle — \(frame) frames", "گرم کردن تا ثابت شدن فرکانس — \(frame) فریم")
        case .some(.scored(let frame, let total)):
            return T("Scored run — frame \(frame) of \(total)", "اجرای امتیازدار — فریم \(frame) از \(total)")
        case .some(.sustained(let frames)):
            return T("Sustained load — \(frames) frames so far", "بار پایدار — تاکنون \(frames) فریم")
        case .some(.finished):
            return T("Done", "تمام شد")
        }
    }
}

@MainActor
struct GPUResultCard: View {
    var result: GpuBenchmarkResult

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 16) {
                SectionLabel(T("GPU benchmark results", "نتایج بنچمارک گرافیک"), symbol: "cube.transparent")
                HStack(spacing: 14) {
                    StatTile(title: T("GPU score", "امتیاز گرافیک"), value: Fmt.number(result.score, decimals: 0),
                             detail: result.adapterName, color: Theme.teal)
                    StatTile(title: T("Average frame", "میانگین فریم"),
                             value: "\(Fmt.fixed(result.averageFrameTimeMs, 1)) ms",
                             detail: "\(Fmt.fixed(result.averageFps, 2)) fps")
                    StatTile(title: T("Sustained score", "امتیاز پایدار"),
                             value: result.sustainedScore.map { Fmt.number($0, decimals: 0) } ?? "—",
                             detail: result.sustainedScore == nil
                                ? T("No sustained phase — raise the test duration to measure throttling.",
                                    "مرحله پایدار نداشت — برای سنجش افت سرعت، مدت تست را بیشتر کنید.") : nil)
                    StatTile(title: T("Peak GPU temperature", "بیشترین دمای گرافیک"),
                             value: Fmt.celsius(result.peakTemperatureCelsius),
                             color: Theme.temperatureColor(result.peakTemperatureCelsius))
                }
                HStack(spacing: 18) {
                    KeyValueRow(key: T("Slowest / fastest", "کندترین / سریع‌ترین"),
                                value: "\(Fmt.fixed(result.minFps, 2)) / \(Fmt.fixed(result.maxFps, 2)) fps")
                    KeyValueRow(key: T("1% low", "۱٪ پایین"), value: "\(Fmt.fixed(result.onePercentLowFps, 2)) fps")
                    KeyValueRow(key: T("Frames", "فریم‌ها"), value: Fmt.number(result.totalFramesRendered))
                }
                Text(T("Off-screen Metal raymarch — \(result.outputWidth)×\(result.outputHeight), \(result.samplesPerPixel) samples per pixel. Reference \(GPURaymarchBenchmark.referenceName) = 1,000.",
                       "رِی‌مارچ Metal خارج از صفحه — \(result.outputWidth)×\(result.outputHeight)، \(result.samplesPerPixel) نمونه در هر پیکسل. مرجع \(GPURaymarchBenchmark.referenceName) = ۱۰۰۰."))
                    .font(Theme.font(11, .medium))
                    .foregroundStyle(Theme.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}
#endif
