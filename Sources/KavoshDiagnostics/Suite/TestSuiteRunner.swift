#if os(macOS)
import Foundation
import Combine
import KavoshCore

/// Walks through the stations of a run, collecting each one's result.
@MainActor
final class TestSuiteRunner: ObservableObject {
    @Published private(set) var report: FinalReport
    @Published private(set) var current: TestModule?
    @Published var confirmingStop = false

    let resumed: Bool
    private weak var model: AppModel?

    init(report: FinalReport, model: AppModel, resumed: Bool) {
        self.report = report
        self.model = model
        self.resumed = resumed
        self.current = report.remaining.first
    }

    var plan: [TestModule] { report.plan }

    /// 1-based number of the station that's running.
    var stepNumber: Int {
        guard let current, let index = plan.firstIndex(of: current) else { return plan.count }
        return index + 1
    }

    var settings: AppSettings { model?.settings ?? AppSettings() }
    var telemetry: LiveTelemetry? { model?.telemetry }
    var isSingleTest: Bool { plan.count == 1 }

    enum StationState {
        case done, failed, skipped, running, waiting
    }

    func state(of module: TestModule) -> StationState {
        if report.failure(for: module) != nil { return .failed }
        if report.skipped.contains(module) { return .skipped }
        if report.completed.contains(module) { return .done }
        if module == current { return .running }
        return .waiting
    }

    // MARK: Station results

    func complete(_ module: TestModule, _ update: (inout FinalReport) -> Void) {
        guard current == module else { return }
        update(&report)
        if !report.completed.contains(module) { report.completed.append(module) }
        advance()
    }

    func skip(_ module: TestModule) {
        guard current == module else { return }
        if !report.skipped.contains(module) { report.skipped.append(module) }
        advance()
    }

    func fail(_ module: TestModule, message: String) {
        guard current == module else { return }
        report.failures.removeAll { $0.module == module }
        report.failures.append(ModuleFailure(module: module, message: message))
        advance()
    }

    private func advance() {
        current = report.remaining.first
        if current == nil {
            report.finishedAt = Date()
            model?.runFinished(report)
        } else {
            model?.saveProgress(report)
        }
    }

    // MARK: Stopping

    func requestStop() {
        confirmingStop = true
    }

    func stopNow() {
        confirmingStop = false
        current = nil
        model?.runStopped(report)
    }
}
#endif
