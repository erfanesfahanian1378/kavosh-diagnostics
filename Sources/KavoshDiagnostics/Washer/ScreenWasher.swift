#if os(macOS)
import AppKit
import KavoshCore
import SwiftUI

// MARK: - Controller

@MainActor
final class ScreenWasherController: ObservableObject {
    struct InspectionSlide {
        var name: String
        var color: SIMD4<Float>
    }

    @Published private(set) var pattern: ScreenWasherPattern
    @Published private(set) var elapsed: Double = 0
    @Published private(set) var controlsVisible = true
    @Published private(set) var inspectionIndex: Int?

    let duration: ScreenWasherDuration
    let rotate: Bool
    let renderState = WasherRenderState()
    let slides: [InspectionSlide] = [
        InspectionSlide(name: T("5% gray", "خاکستری ۵٪"), color: SIMD4<Float>(0.05, 0.05, 0.05, 1)),
        InspectionSlide(name: T("10% gray", "خاکستری ۱۰٪"), color: SIMD4<Float>(0.10, 0.10, 0.10, 1)),
        InspectionSlide(name: T("20% gray", "خاکستری ۲۰٪"), color: SIMD4<Float>(0.20, 0.20, 0.20, 1)),
        InspectionSlide(name: T("50% gray", "خاکستری ۵۰٪"), color: SIMD4<Float>(0.50, 0.50, 0.50, 1)),
        InspectionSlide(name: T("White", "سفید"), color: SIMD4<Float>(1, 1, 1, 1)),
        InspectionSlide(name: T("Red", "قرمز"), color: SIMD4<Float>(1, 0, 0, 1)),
        InspectionSlide(name: T("Green", "سبز"), color: SIMD4<Float>(0, 1, 0, 1)),
        InspectionSlide(name: T("Blue", "آبی"), color: SIMD4<Float>(0, 0, 1, 1))
    ]

    private let sleepBlocker = DisplaySleepBlocker()
    private var ticker: Task<Void, Never>?
    private var hideTask: Task<Void, Never>?
    private var started = Date()
    private var lastRotation = Date()
    private var onDone: ((Bool) -> Void)?

    /// - Parameter inspectOnly: open straight into the inspection slides, without washing.
    init(duration: ScreenWasherDuration, pattern: ScreenWasherPattern, rotate: Bool, inspectOnly: Bool,
         onDone: @escaping (Bool) -> Void) {
        self.duration = duration
        self.pattern = pattern
        self.rotate = rotate
        self.onDone = onDone
        if inspectOnly { inspectionIndex = 0 }
        applyToRenderer()
    }

    var remaining: Double? {
        guard let total = duration.seconds else { return nil }
        return max(0, total - elapsed)
    }

    var isInspecting: Bool { inspectionIndex != nil }

    func begin() {
        started = Date()
        lastRotation = Date()
        sleepBlocker.begin(reason: "Kavosh Screen Washer")
        ticker = Task { @MainActor [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 250_000_000)
                self?.tick()
            }
        }
        scheduleHide()
    }

    private func tick() {
        guard !isInspecting else { return }
        elapsed = Date().timeIntervalSince(started)
        if rotate && Date().timeIntervalSince(lastRotation) >= 60 {
            lastRotation = Date()
            let all = ScreenWasherPattern.allCases
            let next = all[((all.firstIndex(of: pattern) ?? 0) + 1) % all.count]
            setPattern(next)
        }
        if let total = duration.seconds, elapsed >= total {
            stop(completed: true)
        }
    }

    func setPattern(_ newPattern: ScreenWasherPattern) {
        pattern = newPattern
        inspectionIndex = nil
        applyToRenderer()
    }

    private func applyToRenderer() {
        if let index = inspectionIndex {
            renderState.mode = .solid
            renderState.solidColor = slides[index].color
            return
        }
        switch pattern {
        case .noise: renderState.mode = .noise
        case .rgbw: renderState.mode = .rgbw
        case .gradient: renderState.mode = .gradient
        }
    }

    func pointerMoved() {
        if !controlsVisible { controlsVisible = true }
        scheduleHide()
    }

    private func scheduleHide() {
        hideTask?.cancel()
        hideTask = Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 3_000_000_000)
            if Task.isCancelled { return }
            self?.controlsVisible = false
            NSCursor.setHiddenUntilMouseMoves(true)
        }
    }

    func nextSlide() {
        if let index = inspectionIndex {
            inspectionIndex = min(slides.count - 1, index + 1)
        } else {
            inspectionIndex = 0
        }
        applyToRenderer()
    }

    func previousSlide() {
        guard let index = inspectionIndex else { return }
        if index == 0 {
            inspectionIndex = nil
            started = Date().addingTimeInterval(-elapsed)
        } else {
            inspectionIndex = index - 1
        }
        applyToRenderer()
    }

    func stop(completed: Bool) {
        ticker?.cancel()
        hideTask?.cancel()
        sleepBlocker.end()
        guard let done = onDone else { return }
        onDone = nil
        onMain {
            FullscreenPresenter.shared.dismiss()
            done(completed)
        }
    }

    func handleKey(_ event: NSEvent) -> Bool {
        switch event.keyCode {
        case 53:
            stop(completed: false)
        case 124:
            nextSlide()
        case 123:
            previousSlide()
        default:
            break
        }
        pointerMoved()
        return true
    }
}

// MARK: - Fullscreen view

@MainActor
struct ScreenWasherView: View {
    @ObservedObject var controller: ScreenWasherController

    var body: some View {
        ZStack {
            WasherMetalView(state: controller.renderState)
                .ignoresSafeArea()
            if controller.controlsVisible {
                controls
                    .transition(.opacity)
            }
        }
        .onContinuousHover { _ in controller.pointerMoved() }
        .onTapGesture { controller.pointerMoved() }
        .animation(.easeOut(duration: 0.25), value: controller.controlsVisible)
    }

    private var controls: some View {
        VStack {
            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(T("SCREEN WASHER", "صفحه‌شوی"))
                        .font(Theme.font(12, .heavy))
                        .foregroundStyle(Theme.yellow)
                    if let index = controller.inspectionIndex {
                        Text(T("Inspect: \(controller.slides[index].name) (\(index + 1) of \(controller.slides.count))",
                               "بررسی: \(controller.slides[index].name) (\(index + 1) از \(controller.slides.count))"))
                            .font(Theme.font(16, .bold))
                    } else {
                        Text(controller.pattern.title)
                            .font(Theme.font(16, .bold))
                    }
                }
                Spacer()
                VStack(alignment: .trailing, spacing: 2) {
                    HardwareText(T("\(Fmt.duration(controller.elapsed)) elapsed", "\(Fmt.duration(controller.elapsed)) گذشته"),
                                 size: 14, weight: .bold, color: .white)
                    if let remaining = controller.remaining {
                        HardwareText(T("\(Fmt.duration(remaining)) left", "\(Fmt.duration(remaining)) مانده"),
                                     size: 12, weight: .medium, color: .white.opacity(0.75))
                    }
                }
            }
            .foregroundStyle(.white)
            .padding(16)
            .background(RoundedRectangle(cornerRadius: 14).fill(Color.black.opacity(0.72)))

            Spacer()

            HStack(spacing: 10) {
                ForEach(ScreenWasherPattern.allCases, id: \.self) { pattern in
                    Button(pattern.title) { controller.setPattern(pattern) }
                        .retroButton(controller.pattern == pattern && !controller.isInspecting ? .warning : .secondary, size: .small)
                }
                Button {
                    controller.nextSlide()
                } label: {
                    Label(T("Inspect", "بررسی"), systemImage: "eye")
                }
                .retroButton(controller.isInspecting ? .warning : .secondary, size: .small)
                Spacer()
                Text(T("← → to change slide", "← → برای تغییر اسلاید"))
                    .font(Theme.font(12, .medium))
                    .foregroundStyle(.white.opacity(0.8))
                Button {
                    controller.stop(completed: false)
                } label: {
                    Label(T("STOP  (Esc)", "توقف (Esc)"), systemImage: "stop.fill")
                }
                .retroButton(.danger)
            }
            .padding(14)
            .background(RoundedRectangle(cornerRadius: 14).fill(Color.black.opacity(0.72)))
        }
        .padding(28)
    }
}

// MARK: - Setup screen

@MainActor
struct ScreenWasherSetupView: View {
    @EnvironmentObject private var model: AppModel
    @State private var acknowledged = false
    @State private var lastRunFinished: Bool?
    @State private var controller: ScreenWasherController?

    private var willFlash: Bool {
        model.settings.washerPattern.isFlashing || model.settings.washerRotate
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                HStack(spacing: 14) {
                    Button {
                        model.goToDashboard()
                    } label: {
                        Label(T("Back to Dashboard", "بازگشت به داشبورد"), systemImage: "chevron.backward")
                    }
                    .retroButton(.secondary, size: .small)
                    VStack(alignment: .leading, spacing: 2) {
                        SectionLabel(T("Maintenance tools", "ابزارهای نگهداری"), symbol: "sparkles.tv")
                        Text(T("Screen Washer & Burn-in Fixer", "صفحه‌شوی و رفع سوختگی")).font(Theme.font(26, .heavy))
                    }
                    Spacer()
                }

                Text(T("Rapidly cycles every pixel through its full range to help clear temporary image retention, and exercises the subpixel drivers on an OLED or LCD panel.",
                       "هر پیکسل را به‌سرعت در کل محدوده‌اش می‌چرخاند تا اثر تصویر موقت پاک شود و زیرپیکسل‌های پنل OLED یا LCD را فعال می‌کند."))
                    .font(Theme.font(14, .medium))
                    .fixedSize(horizontal: false, vertical: true)

                if let finished = lastRunFinished {
                    RetroCard(fill: finished ? Theme.green.opacity(0.2) : Theme.surface) {
                        Label(finished ? T("Wash finished. Look at the inspection slides to check whether the retention faded.",
                                           "شستشو تمام شد. اسلایدهای بررسی را ببینید تا مطمئن شوید اثر کم شده است.")
                                       : T("The wash was stopped.", "شستشو متوقف شد."),
                              systemImage: finished ? "checkmark.seal.fill" : "stop.circle")
                            .font(Theme.font(14, .bold))
                    }
                }

                HStack(alignment: .top, spacing: 20) {
                    warningCard
                    timeCard
                }

                HStack(alignment: .top, spacing: 20) {
                    durationCard
                    patternCard
                }

                RetroCard {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(T("While it's running: move the mouse to bring back the controls — press Esc or click Stop to end it at any time — press ← → to step through the inspection slides.",
                               "در حین اجرا: موس را حرکت دهید تا کنترل‌ها برگردند — هر زمان با Esc یا دکمه توقف تمامش کنید — با ← → بین اسلایدهای بررسی جابه‌جا شوید."))
                            .font(Theme.font(13, .medium))
                            .fixedSize(horizontal: false, vertical: true)
                        Text(T("Tip: look at the inspection slides before and after the wash — that's the only reliable way to tell whether the retention actually faded.",
                               "نکته: اسلایدهای بررسی را قبل و بعد از شستشو ببینید — تنها راه مطمئن برای فهمیدن این‌که اثر واقعاً کم شده است."))
                            .font(Theme.font(12, .medium))
                            .foregroundStyle(Theme.muted)
                            .fixedSize(horizontal: false, vertical: true)
                        if willFlash {
                            Toggle(isOn: $acknowledged) {
                                Text(T("I've read the warning above and I understand this will flash the screen.",
                                       "هشدار بالا را خواندم و می‌دانم که صفحه چشمک خواهد زد."))
                            }
                            .toggleStyle(RetroCheckboxStyle())
                        }
                        HStack(spacing: 12) {
                            Button {
                                start(inspectOnly: false)
                            } label: {
                                Label(T("Start Screen Washer", "شروع صفحه‌شوی"), systemImage: "play.fill")
                            }
                            .retroButton(.primary, size: .large)
                            .disabled(willFlash && !acknowledged)
                            Button {
                                start(inspectOnly: true)
                            } label: {
                                Label(T("Uniformity slides", "اسلایدهای یکنواختی"), systemImage: "eye")
                            }
                            .retroButton(.secondary)
                        }
                    }
                }
            }
            .padding(24)
        }
    }

    private var warningCard: some View {
        RetroCard(fill: Theme.red.opacity(0.14)) {
            VStack(alignment: .leading, spacing: 8) {
                SectionLabel(T("Photosensitivity warning", "هشدار حساسیت به نور"), symbol: "exclamationmark.triangle.fill", color: Theme.red)
                Text(T("Two of the washing patterns fill the entire screen with rapidly changing colour and high-frequency flashing noise. Do not use them if you or anyone who can see this screen has photosensitive epilepsy or is sensitive to flashing light.",
                       "دو الگوی شستشو کل صفحه را با رنگ‌های سریع‌تغییر و نویز چشمک‌زن پر می‌کنند. اگر شما یا هر کسی که این صفحه را می‌بیند صرع حساس به نور دارد یا به نور چشمک‌زن حساس است، از آن‌ها استفاده نکنید."))
                    .font(Theme.font(13, .medium))
                    .fixedSize(horizontal: false, vertical: true)
                Text(T("If you are unsure, choose \"Gradient sweep\" as the starting pattern and turn off \"Rotate through all patterns\" below — that pattern moves smoothly and never flashes.",
                       "اگر مطمئن نیستید، «گرادیان متحرک» را به‌عنوان الگوی شروع انتخاب کنید و «چرخش بین همه الگوها» را خاموش کنید — آن الگو نرم حرکت می‌کند و هرگز چشمک نمی‌زند."))
                    .font(Theme.font(12, .semibold))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var timeCard: some View {
        RetroCard(fill: Theme.yellow.opacity(0.25)) {
            VStack(alignment: .leading, spacing: 8) {
                SectionLabel(T("This takes time to work", "این کار زمان می‌برد"), symbol: "hourglass", color: Theme.ink)
                Text(T("Clearing image retention is gradual — plan on leaving this running for at least 5 minutes, and up to 30 for a stubborn ghost. Your screen will be completely covered for the whole run and the machine will be kept awake, so start it when you don't need the computer for anything else.",
                       "پاک شدن اثر تصویر تدریجی است — دست‌کم ۵ دقیقه و برای اثرهای سمج تا ۳۰ دقیقه اجرا کنید. در تمام مدت صفحه کاملاً پوشیده می‌ماند و دستگاه بیدار نگه داشته می‌شود، پس وقتی به کامپیوتر نیاز ندارید شروعش کنید."))
                    .font(Theme.font(13, .medium))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var durationCard: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 10) {
                SectionLabel(T("How long", "چه مدت"), symbol: "timer")
                ForEach(ScreenWasherDuration.allCases, id: \.self) { option in
                    OptionRow(title: option.title, detail: option.detail, selected: model.settings.washerDuration == option) {
                        model.settings.washerDuration = option
                    }
                }
            }
        }
    }

    private var patternCard: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 10) {
                SectionLabel(T("Starting pattern", "الگوی شروع"), symbol: "paintpalette")
                ForEach(ScreenWasherPattern.allCases, id: \.self) { option in
                    OptionRow(title: option.title, detail: option.detail, selected: model.settings.washerPattern == option,
                              flashing: option.isFlashing) {
                        model.settings.washerPattern = option
                    }
                }
                Toggle(isOn: $model.settings.washerRotate) {
                    Text(T("Rotate through all patterns while running (recommended)", "چرخش بین همه الگوها در حین اجرا (توصیه‌شده)"))
                }
                .toggleStyle(RetroCheckboxStyle())
            }
        }
    }

    private func start(inspectOnly: Bool) {
        let controller = ScreenWasherController(
            duration: model.settings.washerDuration,
            pattern: model.settings.washerPattern,
            rotate: model.settings.washerRotate && !inspectOnly,
            inspectOnly: inspectOnly
        ) { completed in
            lastRunFinished = inspectOnly ? nil : completed
        }
        self.controller = controller
        FullscreenPresenter.shared.present(ScreenWasherView(controller: controller)) { event in
            controller.handleKey(event)
        }
        controller.begin()
    }
}

@MainActor
struct OptionRow: View {
    var title: String
    var detail: String
    var selected: Bool
    var flashing = false
    var action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(alignment: .top, spacing: 10) {
                Circle()
                    .fill(selected ? Theme.accent : Theme.surface)
                    .frame(width: 18, height: 18)
                    .overlay(Circle().strokeBorder(Theme.ink, lineWidth: Theme.border))
                    .overlay(Circle().fill(Color.white).frame(width: 6, height: 6).opacity(selected ? 1 : 0))
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 6) {
                        Text(title).font(Theme.font(13, .bold))
                        if flashing {
                            RetroBadge(T("Flashing", "چشمک‌زن"), color: Theme.red, textColor: .white, symbol: "bolt.fill")
                        }
                    }
                    Text(detail)
                        .font(Theme.font(11, .medium))
                        .foregroundStyle(Theme.muted)
                        .fixedSize(horizontal: false, vertical: true)
                        .multilineTextAlignment(.leading)
                }
                Spacer(minLength: 0)
            }
            .padding(10)
            .background(RoundedRectangle(cornerRadius: 9).fill(selected ? Theme.accent.opacity(0.12) : Color.clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}
#endif
