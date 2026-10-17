#if os(macOS)
import KavoshCore
import SwiftUI

@MainActor
struct TestSuiteView: View {
    @ObservedObject var runner: TestSuiteRunner

    var body: some View {
        HStack(alignment: .top, spacing: 22) {
            VStack(alignment: .leading, spacing: 18) {
                SuiteHeader(runner: runner)
                ScrollView {
                    station
                        .id(runner.current)
                        .padding(.trailing, 6)
                        .padding(.bottom, 8)
                }
            }
            .frame(maxWidth: .infinity)

            ScrollView {
                VStack(spacing: 16) {
                    StationListCard(runner: runner)
                    TelemetryPanel(compact: true)
                }
                .padding(.trailing, 6)
                .padding(.bottom, 8)
            }
            .frame(width: 300)
        }
        .padding(24)
        .alert(T("Stop the test now?", "تست الان متوقف شود؟"), isPresented: $runner.confirmingStop) {
            Button(T("Stop Test", "توقف تست"), role: .destructive) { runner.stopNow() }
            Button(T("Keep testing", "ادامه تست"), role: .cancel) {}
        } message: {
            Text(T("The current station's resources (camera, mic, memory buffer, CPU load) are released immediately. Stations that already finished are kept, and you can resume the rest later from the Dashboard.",
                   "منابع ایستگاه فعلی (دوربین، میکروفون، حافظه، بار پردازنده) فوراً آزاد می‌شوند. ایستگاه‌های تمام‌شده حفظ می‌شوند و می‌توانید بقیه را بعداً از داشبورد ادامه دهید."))
        }
    }

    @ViewBuilder
    private var station: some View {
        switch runner.current {
        case .cpu: CPUStation(runner: runner)
        case .gpu: GPUStation(runner: runner)
        case .memory: RAMStation(runner: runner)
        case .battery: BatteryStation(runner: runner)
        case .display: DisplayStation(runner: runner)
        case .speakers: AudioStation(runner: runner)
        case .micCamera: MicCameraStation(runner: runner)
        case .input: InputStation(runner: runner)
        case .drives: DriveStation(runner: runner)
        case .none:
            ProgressView()
                .frame(maxWidth: .infinity, minHeight: 200)
        }
    }
}

@MainActor
struct SuiteHeader: View {
    @ObservedObject var runner: TestSuiteRunner

    var body: some View {
        HStack(alignment: .center, spacing: 16) {
            if let module = runner.current {
                Image(systemName: module.symbol)
                    .font(.system(size: 24, weight: .bold))
                    .frame(width: 54, height: 54)
                    .retroSurface(fill: Theme.yellow, corner: 12, shadow: 3)
                    .foregroundStyle(Theme.onYellow)
                VStack(alignment: .leading, spacing: 4) {
                    SectionLabel(runner.isSingleTest
                                 ? T("Individual test", "تست جداگانه")
                                 : T("Test suite · step \(runner.stepNumber) of \(runner.plan.count)",
                                     "مجموعه تست · مرحله \(runner.stepNumber) از \(runner.plan.count)"))
                    Text(module.title)
                        .font(Theme.font(26, .heavy))
                    Text(module.subtitle)
                        .font(Theme.font(13, .medium))
                        .foregroundStyle(Theme.muted)
                }
            }
            Spacer()
            Button {
                runner.requestStop()
            } label: {
                Label(T("Stop Test", "توقف تست"), systemImage: "stop.fill")
            }
            .retroButton(.danger)
        }
    }
}

@MainActor
struct StationListCard: View {
    @ObservedObject var runner: TestSuiteRunner

    var body: some View {
        RetroCard(padding: 14) {
            VStack(alignment: .leading, spacing: 10) {
                SectionLabel(T("Stations", "ایستگاه‌ها"), symbol: "list.number")
                ForEach(runner.plan) { module in
                    HStack(spacing: 10) {
                        StatusIcon(state: iconState(runner.state(of: module)))
                        Text(module.title)
                            .font(Theme.font(12, module == runner.current ? .heavy : .medium))
                            .foregroundStyle(module == runner.current ? Theme.ink : Theme.muted)
                        Spacer()
                        if module == runner.current {
                            ProgressView().controlSize(.mini)
                        }
                    }
                }
                if runner.resumed {
                    Text(T("Resumed — stations finished in the interrupted run are kept.",
                           "ادامه داده شد — ایستگاه‌های تمام‌شده در اجرای قبلی حفظ شده‌اند."))
                        .font(Theme.font(11, .medium))
                        .foregroundStyle(Theme.muted)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private func iconState(_ state: TestSuiteRunner.StationState) -> StatusIcon.State {
        switch state {
        case .done: return .pass
        case .failed: return .fail
        case .skipped: return .skipped
        case .running, .waiting: return .pending
        }
    }
}

/// Shown when a station is done: a "Next" button, and for automatic stations a short
/// countdown that continues by itself.
@MainActor
struct ContinueBar: View {
    var isLast: Bool
    var autoAdvanceSeconds: Int?
    var action: () -> Void

    @State private var remaining = 0
    @State private var countdown: Task<Void, Never>?

    var body: some View {
        HStack(spacing: 12) {
            if remaining > 0 {
                Text(T("Continuing in \(remaining) s…", "ادامه در \(remaining) ثانیه…"))
                    .font(Theme.font(12, .medium))
                    .foregroundStyle(Theme.muted)
                Button(T("Wait", "صبر کن")) { stopCountdown() }
                    .retroButton(.ghost, size: .small)
            }
            Spacer()
            Button {
                stopCountdown()
                action()
            } label: {
                Label(isLast ? T("See results", "مشاهده نتایج") : T("Next test", "تست بعدی"),
                      systemImage: isLast ? "checkmark.seal.fill" : "arrow.right")
            }
            .retroButton(.primary, size: .large)
        }
        .onAppear(perform: startCountdown)
        .onDisappear(perform: stopCountdown)
    }

    private func startCountdown() {
        guard let seconds = autoAdvanceSeconds, seconds > 0 else { return }
        remaining = seconds
        countdown = Task { @MainActor in
            while remaining > 0 {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                if Task.isCancelled { return }
                remaining -= 1
            }
            if !Task.isCancelled { action() }
        }
    }

    private func stopCountdown() {
        countdown?.cancel()
        countdown = nil
        remaining = 0
    }
}

/// Error box with a way to move on.
@MainActor
struct StationErrorView: View {
    var title: String
    var message: String
    var onContinue: () -> Void

    var body: some View {
        RetroCard(fill: Theme.red.opacity(0.12)) {
            VStack(alignment: .leading, spacing: 12) {
                SectionLabel(T("The test couldn't run", "تست اجرا نشد"), symbol: "exclamationmark.triangle.fill", color: Theme.red)
                Text(title).font(Theme.font(16, .bold))
                Text(message)
                    .font(Theme.font(13, .medium))
                    .fixedSize(horizontal: false, vertical: true)
                HStack {
                    Spacer()
                    Button(T("Continue", "ادامه"), action: onContinue)
                        .retroButton(.primary)
                }
            }
        }
    }
}

/// Explanatory text block used at the top of interactive stations.
@MainActor
struct InstructionText: View {
    var text: String

    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(Theme.font(14, .medium))
            .fixedSize(horizontal: false, vertical: true)
    }
}
#endif
