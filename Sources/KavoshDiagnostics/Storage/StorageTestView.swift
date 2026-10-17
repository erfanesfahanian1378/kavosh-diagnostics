#if os(macOS)
import AppKit
import KavoshCore
import SwiftUI

@MainActor
final class StorageTestModel: ObservableObject {
    @Published var folder: URL = FileManager.default.temporaryDirectory
    @Published private(set) var running = false
    @Published private(set) var progress: StorageBenchmarkProgress?
    @Published private(set) var liveHistory: [Double] = []
    @Published private(set) var result: StorageBenchmarkResult?
    @Published private(set) var errorMessage: String?

    private var flag = CancellationFlag()

    var folderDisplayName: String {
        let volume = StorageBenchmarkEngine.volumeName(for: folder) ?? "—"
        if folder.standardizedFileURL == FileManager.default.temporaryDirectory.standardizedFileURL {
            return T("\(volume) (startup drive)", "\(volume) (درایو راه‌اندازی)")
        }
        return "\(volume) — \(folder.path)"
    }

    func chooseFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.canCreateDirectories = true
        panel.prompt = T("Test this folder", "تست این پوشه")
        panel.message = T("Select a drive or folder to test (SSD Speed Test)", "یک درایو یا پوشه برای تست انتخاب کنید (تست سرعت SSD)")
        if panel.runModal() == .OK, let url = panel.url {
            folder = url
        }
    }

    func start(profile: StorageTestProfile, onFinish: @escaping (StorageBenchmarkResult) -> Void) {
        guard !running else { return }
        running = true
        errorMessage = nil
        result = nil
        liveHistory = []
        flag = CancellationFlag()
        let flag = self.flag
        let folder = self.folder
        let engine = StorageBenchmarkEngine(profile: profile)
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                let result = try engine.run(in: folder, isCancelled: { flag.isCancelled }) { progress in
                    onMain { [weak self] in self?.apply(progress) }
                }
                onMain { [weak self] in
                    self?.running = false
                    self?.result = result
                    onFinish(result)
                }
            } catch {
                let message = (error as? StorageBenchmarkFailure)?.message ?? error.localizedDescription
                onMain { [weak self] in
                    self?.running = false
                    self?.errorMessage = message
                }
            }
        }
    }

    private func apply(_ progress: StorageBenchmarkProgress) {
        self.progress = progress
        if case .running = progress.stage, let live = progress.liveMBps {
            liveHistory.append(live)
            if liveHistory.count > 160 { liveHistory.removeFirst(liveHistory.count - 160) }
        }
    }

    func stop() {
        flag.cancel()
    }
}

@MainActor
struct StorageTestView: View {
    @EnvironmentObject private var app: AppModel
    @StateObject private var model = StorageTestModel()

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                HStack(spacing: 14) {
                    Button {
                        if !model.running { app.goToDashboard() }
                    } label: {
                        Label(T("Back to Dashboard", "بازگشت به داشبورد"), systemImage: "chevron.backward")
                    }
                    .retroButton(.secondary, size: .small)
                    .disabled(model.running)
                    VStack(alignment: .leading, spacing: 2) {
                        SectionLabel(T("SSD speed test", "تست سرعت SSD"), symbol: "speedometer")
                        Text(T("Drive speed", "سرعت درایو")).font(Theme.font(26, .heavy))
                    }
                    Spacer()
                }

                setupCard

                if let error = model.errorMessage {
                    RetroCard(fill: Theme.red.opacity(0.12)) {
                        VStack(alignment: .leading, spacing: 8) {
                            SectionLabel(T("The test couldn't run", "تست اجرا نشد"), symbol: "exclamationmark.triangle.fill", color: Theme.red)
                            Text(error)
                                .font(Theme.font(14, .medium))
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                }

                if model.running {
                    liveCard
                }

                if let result = model.result ?? (model.running ? nil : app.lastStorage) {
                    StorageResultsCard(result: result, live: model.running ? model.progress : nil)
                } else if model.running, let progress = model.progress {
                    StorageResultsCard(result: nil, live: progress)
                }

                Text(T("Measured the way CrystalDiskMark measures: a pre-filled test file, uncached direct I/O, incompressible random data and several timed runs per test — the best run is reported. 1 MB/s = 1,000,000 bytes per second (CrystalDiskMark's default unit); 1 MiB/s = 1,048,576. The time in ms is the average wait per request.",
                       "به روش CrystalDiskMark اندازه‌گیری می‌شود: یک فایل تست ازپیش‌پرشده، ورودی/خروجی مستقیم بدون کش، داده تصادفی غیرقابل فشرده‌سازی و چند اجرای زمان‌دار برای هر تست — بهترین اجرا گزارش می‌شود. ۱ MB/s = ۱٬۰۰۰٬۰۰۰ بایت در ثانیه؛ ۱ MiB/s = ۱٬۰۴۸٬۵۷۶. زمان بر حسب ms میانگین انتظار برای هر درخواست است."))
                    .font(Theme.font(11, .medium))
                    .foregroundStyle(Theme.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(24)
        }
        .onDisappear { model.stop() }
    }

    private var setupCard: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 14) {
                HStack(alignment: .center, spacing: 12) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(T("Tested folder", "پوشه تست"))
                            .font(Theme.font(11, .bold))
                            .foregroundStyle(Theme.muted)
                        HardwareText(model.folderDisplayName, size: 14, weight: .bold)
                            .lineLimit(1)
                            .truncationMode(.middle)
                    }
                    Spacer()
                    Button(T("Choose…", "انتخاب…")) { model.chooseFolder() }
                        .retroButton(.secondary, size: .small)
                        .disabled(model.running)
                }
                HStack(spacing: 12) {
                    Text(T("Profile", "حالت"))
                        .font(Theme.font(11, .bold))
                        .foregroundStyle(Theme.muted)
                    RetroSegmented(options: [("standard", StorageTestProfile.standard.title + " · 1 GiB · 5×5 s"),
                                             ("quick", StorageTestProfile.quick.title + " · 512 MiB · 2×3 s")],
                                   selection: $app.settings.storageProfile)
                        .disabled(model.running)
                    Spacer()
                    if model.running {
                        Button {
                            model.stop()
                        } label: {
                            Label(T("Stop", "توقف"), systemImage: "stop.fill")
                        }
                        .retroButton(.danger)
                    } else {
                        Button {
                            model.start(profile: app.settings.storageTestProfile) { result in
                                app.storageFinished(result)
                            }
                        } label: {
                            Label(T("Start", "شروع"), systemImage: "play.fill")
                        }
                        .retroButton(.primary, size: .large)
                    }
                }
                Text(T("The test writes a temporary file (about 1 GiB) and deletes it afterwards. It needs about 1.3 GB of free space.",
                       "تست یک فایل موقت (حدود ۱ گیگابایت) می‌نویسد و بعد پاکش می‌کند. حدود ۱٫۳ گیگابایت فضای خالی لازم دارد."))
                    .font(Theme.font(11, .medium))
                    .foregroundStyle(Theme.muted)
            }
        }
    }

    private var liveCard: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    SectionLabel(T("Throughput — live", "سرعت — زنده"), symbol: "waveform.path.ecg")
                    Spacer()
                    HardwareText(model.progress?.liveMBps.map { Fmt.mbps($0) } ?? "—", size: 18, weight: .heavy, color: Theme.accent)
                }
                Text(stageText(model.progress?.stage))
                    .font(Theme.font(14, .bold))
                HistoryChart(values: model.liveHistory, maxValue: max(100, (model.liveHistory.max() ?? 0) * 1.1),
                             capacity: 160, color: Theme.accent)
                    .frame(height: 110)
                RetroProgressBar(value: model.progress?.overallFraction ?? 0, color: Theme.accent)
            }
        }
    }

    private func stageText(_ stage: StorageBenchmarkProgress.Stage?) -> String {
        switch stage {
        case .none, .some(.preparing):
            return T("Preparing a test file", "آماده‌سازی فایل تست")
        case .some(.settling):
            return T("Letting the drive settle before the next test", "استراحت درایو پیش از تست بعدی")
        case .some(.running(let kind, let op, let run, let of)):
            return "\(kind.title) \(op.title) — \(kind.label) — " + T("run \(run) of \(of)", "اجرای \(run) از \(of)")
        case .some(.cleaningUp):
            return T("Removing the test file", "حذف فایل تست")
        case .some(.finished):
            return T("Done", "تمام شد")
        }
    }
}

/// CrystalDiskMark-style grid: three tests × read/write.
@MainActor
struct StorageResultsCard: View {
    var result: StorageBenchmarkResult?
    var live: StorageBenchmarkProgress?

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    SectionLabel(T("SSD speed test — results", "تست سرعت SSD — نتایج"), symbol: "tablecells")
                    Spacer()
                    if let result {
                        Text(DateText.string(result.date))
                            .font(Theme.font(11, .medium))
                            .foregroundStyle(Theme.muted)
                    }
                }
                Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 12) {
                    GridRow {
                        Text("")
                        Text(T("Read", "خواندن")).font(Theme.font(12, .heavy)).foregroundStyle(Theme.muted)
                        Text(T("Write", "نوشتن")).font(Theme.font(12, .heavy)).foregroundStyle(Theme.muted)
                    }
                    ForEach(StorageBenchmarkEngine.tests, id: \.self) { kind in
                        GridRow {
                            VStack(alignment: .leading, spacing: 2) {
                                HardwareText(kind.label, size: 14, weight: .heavy)
                                Text(kind.title)
                                    .font(Theme.font(11, .medium))
                                    .foregroundStyle(Theme.muted)
                            }
                            .frame(width: 150, alignment: .leading)
                            SpeedCell(measurement: measurement(kind, .read), active: isActive(kind, .read))
                            SpeedCell(measurement: measurement(kind, .write), active: isActive(kind, .write))
                        }
                    }
                }
                if let result, let rnd = result.measurement(.rnd4kQ1T1, .read) {
                    Text(T("Random, one request at a time (RND4K Q1T1) is the closest of these numbers to how everyday use feels: opening files, launching apps. Read: \(Fmt.mbps(rnd.mbPerSecond)).",
                           "تصادفی، یک درخواست در هر زمان (RND4K Q1T1) نزدیک‌ترین عدد به حس استفاده روزمره است: باز کردن فایل‌ها و اجرای برنامه‌ها. خواندن: \(Fmt.mbps(rnd.mbPerSecond))."))
                        .font(Theme.font(12, .medium))
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private func measurement(_ kind: StorageTestKind, _ op: StorageOperation) -> StorageTestMeasurement? {
        if let live { return live.completed.first { $0.kind == kind && $0.operation == op } }
        return result?.measurement(kind, op)
    }

    private func isActive(_ kind: StorageTestKind, _ op: StorageOperation) -> Bool {
        if case .running(let k, let o, _, _) = live?.stage { return k == kind && o == op }
        return false
    }
}

@MainActor
struct SpeedCell: View {
    var measurement: StorageTestMeasurement?
    var active: Bool

    var body: some View {
        VStack(alignment: .trailing, spacing: 3) {
            HardwareText(measurement.map { Fmt.number($0.mbPerSecond, decimals: $0.mbPerSecond < 100 ? 2 : 0) } ?? (active ? "…" : "—"),
                         size: 26, weight: .black, color: measurement == nil ? Theme.muted : Theme.ink)
            if let m = measurement {
                HardwareText("\(Fmt.number(m.mibPerSecond, decimals: 0)) MiB/s · \(Fmt.number(m.iops, decimals: 0)) IOPS · \(Fmt.fixed(m.averageLatencyMs, 3)) ms",
                             size: 10, weight: .medium, color: Theme.muted)
            } else {
                Text("MB/s").font(Theme.number(10, .medium)).foregroundStyle(Theme.muted)
            }
        }
        .padding(12)
        .frame(minWidth: 220, alignment: .trailing)
        .retroSurface(fill: active ? Theme.yellow.opacity(0.5) : (measurement == nil ? Theme.surfaceAlt : Theme.teal.opacity(0.25)),
                      corner: 10, shadow: 3)
    }
}
#endif
