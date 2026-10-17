#if os(macOS)
import AppKit
import KavoshCore
import SwiftUI
import UniformTypeIdentifiers

/// App-wide state: settings, the Mac's specs, saved results and which screen is showing.
@MainActor
final class AppModel: ObservableObject {
    enum Screen: Equatable {
        case dashboard
        case suite
        case results
        case storage
        case washerSetup
    }

    @Published var screen: Screen = .dashboard
    @Published var settings: AppSettings {
        didSet {
            guard settings != oldValue else { return }
            Loc.language = settings.language
            ResultStore.saveSettings(settings)
        }
    }

    @Published private(set) var specs: SystemSpecs?
    @Published private(set) var specsComplete = false
    @Published private(set) var driveHealth: DriveHealthReport?
    @Published private(set) var driveHealthLoading = false
    @Published private(set) var lastReport: FinalReport?
    @Published private(set) var lastStorage: StorageBenchmarkResult?
    @Published private(set) var unfinishedRun: FinalReport?
    @Published var shownReport: FinalReport?
    @Published private(set) var runner: TestSuiteRunner?
    @Published var notice: String?

    let telemetry = LiveTelemetry()

    init() {
        let loaded = ResultStore.loadSettings()
        settings = loaded
        Loc.language = loaded.language
        lastReport = ResultStore.load(FinalReport.self, from: AppPaths.lastReportFile)
        lastStorage = ResultStore.load(StorageBenchmarkResult.self, from: AppPaths.lastStorageFile)
        if let unfinished = ResultStore.load(FinalReport.self, from: AppPaths.unfinishedRunFile), !unfinished.isFinished {
            unfinishedRun = unfinished
        }
        telemetry.start()
        loadSpecs()
        refreshDriveHealth()
    }

    // MARK: Specs

    func loadSpecs() {
        let refresh = NSScreen.main?.maximumFramesPerSecond
        DispatchQueue.global(qos: .userInitiated).async {
            var specs = SystemProbe.quickSpecs()
            if let refresh, refresh > 0 { specs.displayRefreshRateHz = refresh }
            let quick = specs
            onMain { [weak self] in self?.specs = quick }
            SystemProbe.addSlowDetails(to: &specs)
            if let refresh, refresh > 0 { specs.displayRefreshRateHz = refresh }
            let full = specs
            onMain { [weak self] in
                self?.specs = full
                self?.specsComplete = true
            }
        }
    }

    func refreshDriveHealth() {
        guard !driveHealthLoading else { return }
        driveHealthLoading = true
        DispatchQueue.global(qos: .utility).async {
            let report = DriveReader.readHealth()
            onMain { [weak self] in
                self?.driveHealth = report
                self?.driveHealthLoading = false
            }
        }
    }

    // MARK: Language

    func setLanguage(_ language: AppLanguage) {
        Loc.language = language
        settings.language = language
    }

    // MARK: Test runs

    func startRun(_ plan: [TestModule]) {
        // On desktop Macs (no built-in keyboard/trackpad/camera) those checks are bypassed,
        // not failed; the stations read `hasNoBuiltInInputs`.
        let report = FinalReport(plan: plan, specs: specs, hasNoBuiltInInputs: settings.noBuiltInInputs)
        runner = TestSuiteRunner(report: report, model: self, resumed: false)
        screen = .suite
    }

    func resumeUnfinished() {
        guard var saved = unfinishedRun else { return }
        if saved.specs == nil { saved.specs = specs }
        runner = TestSuiteRunner(report: saved, model: self, resumed: true)
        unfinishedRun = nil
        screen = .suite
    }

    func discardUnfinished() {
        unfinishedRun = nil
        ResultStore.remove(AppPaths.unfinishedRunFile)
    }

    func saveProgress(_ report: FinalReport) {
        guard !report.completed.isEmpty || !report.failures.isEmpty || !report.skipped.isEmpty else { return }
        try? ResultStore.save(report, to: AppPaths.unfinishedRunFile)
    }

    func runFinished(_ report: FinalReport) {
        var finished = report
        if finished.finishedAt == nil { finished.finishedAt = Date() }
        if finished.specs == nil { finished.specs = specs }
        let evaluation = BenchmarkScoring.evaluate(finished)
        try? ResultStore.save(finished, to: AppPaths.lastReportFile)
        try? ResultStore.appendToHistory(report: finished, evaluation: evaluation, storage: lastStorage)
        ResultStore.remove(AppPaths.unfinishedRunFile)
        unfinishedRun = nil
        lastReport = finished
        if let drives = finished.drives { driveHealth = drives }
        shownReport = finished
        runner = nil
        screen = .results
    }

    func runStopped(_ report: FinalReport) {
        runner = nil
        if !report.completed.isEmpty || !report.failures.isEmpty || !report.skipped.isEmpty {
            saveProgress(report)
            unfinishedRun = report
        }
        screen = .dashboard
    }

    func showReport(_ report: FinalReport) {
        shownReport = report
        screen = .results
    }

    func goToDashboard() {
        screen = .dashboard
    }

    // MARK: SSD test

    func storageFinished(_ result: StorageBenchmarkResult) {
        lastStorage = result
        try? ResultStore.save(result, to: AppPaths.lastStorageFile)
    }

    // MARK: Export

    func exportCSV(_ report: FinalReport) {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.commaSeparatedText]
        let model = (report.specs?.modelIdentifier ?? "Mac").replacingOccurrences(of: ",", with: "-")
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd-HHmm"
        formatter.locale = Locale(identifier: "en_US_POSIX")
        panel.nameFieldStringValue = "Kavosh-\(model)-\(formatter.string(from: report.finishedAt ?? report.startedAt)).csv"
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let evaluation = BenchmarkScoring.evaluate(report)
        let text = CSVExport.document(report: report, evaluation: evaluation, storage: lastStorage)
        do {
            try Data(text.utf8).write(to: url, options: .atomic)
            NSWorkspace.shared.activateFileViewerSelecting([url])
        } catch {
            notice = T("Couldn't save the CSV file: \(error.localizedDescription)",
                       "ذخیره فایل CSV ممکن نشد: \(error.localizedDescription)")
        }
    }

    func openHistoryCSV() {
        let url = AppPaths.historyCSV
        if FileManager.default.fileExists(atPath: url.path) {
            NSWorkspace.shared.open(url)
        } else {
            notice = T("No CSV has been generated yet — finish a test run first.",
                       "هنوز فایل CSV ساخته نشده است — ابتدا یک اجرای تست را کامل کنید.")
        }
    }
}
#endif
