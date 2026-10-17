#if os(macOS)
import AppKit
import KavoshCore
import QuartzCore
import SwiftUI

// MARK: - Controller

@MainActor
final class DisplayTestController: ObservableObject {
    enum Slide: Equatable {
        case intro
        case solid(name: String, color: Color, hint: String)
        case gradient
        case refresh
        case checklist
    }

    @Published private(set) var index = 0
    @Published var issues: Set<DisplayIssue> = []
    @Published var measuredHz: Double?
    @Published private(set) var showLabel = true

    let slides: [Slide]
    private var onDone: ((DisplayTestResult?) -> Void)?
    private var labelTask: Task<Void, Never>?

    init(onDone: @escaping (DisplayTestResult?) -> Void) {
        self.onDone = onDone
        let lookFor = T("look for dots that don't match", "دنبال نقطه‌هایی بگردید که هم‌رنگ نیستند")
        slides = [
            .intro,
            .solid(name: T("Red", "قرمز"), color: Color(red: 1, green: 0, blue: 0), hint: lookFor),
            .solid(name: T("Green", "سبز"), color: Color(red: 0, green: 1, blue: 0), hint: lookFor),
            .solid(name: T("Blue", "آبی"), color: Color(red: 0, green: 0, blue: 1), hint: lookFor),
            .solid(name: T("White", "سفید"), color: .white, hint: T("look for dark dots and uneven brightness", "دنبال نقطه تیره و روشنایی ناهموار بگردید")),
            .solid(name: T("Black", "سیاه"), color: .black, hint: T("look for bright dots and light bleeding in from the edges", "دنبال نقطه روشن و نشت نور از لبه‌ها بگردید")),
            .solid(name: T("50% gray", "خاکستری ۵۰٪"), color: Color(white: 0.5), hint: T("look for a faint image that stays (burn-in)", "دنبال تصویر کم‌رنگ ماندگار (سوختگی) بگردید")),
            .solid(name: T("20% gray", "خاکستری ۲۰٪"), color: Color(white: 0.2), hint: T("look for blotches and uneven areas", "دنبال لکه و ناحیه ناهموار بگردید")),
            .solid(name: T("10% gray", "خاکستری ۱۰٪"), color: Color(white: 0.1), hint: T("look for blotches and uneven areas", "دنبال لکه و ناحیه ناهموار بگردید")),
            .solid(name: T("5% gray", "خاکستری ۵٪"), color: Color(white: 0.05), hint: T("look for blotches and uneven areas", "دنبال لکه و ناحیه ناهموار بگردید")),
            .gradient,
            .refresh,
            .checklist
        ]
    }

    var slide: Slide { slides[index] }
    var stepText: String { T("Step \(index + 1) of \(slides.count)", "مرحله \(index + 1) از \(slides.count)") }

    func next() {
        guard index < slides.count - 1 else { return }
        index += 1
        flashLabel()
        NSCursor.setHiddenUntilMouseMoves(slide != .checklist)
    }

    func previous() {
        guard index > 0 else { return }
        index -= 1
        flashLabel()
    }

    private func flashLabel() {
        showLabel = true
        labelTask?.cancel()
        labelTask = Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 1_800_000_000)
            if Task.isCancelled { return }
            withAnimation(.easeOut(duration: 0.4)) { self?.showLabel = false }
        }
    }

    func finish() {
        let result = DisplayTestResult(issues: DisplayIssue.allCases.filter { issues.contains($0) }, measuredRefreshRateHz: measuredHz)
        close(result)
    }

    func exitEarly() {
        close(nil)
    }

    private func close(_ result: DisplayTestResult?) {
        labelTask?.cancel()
        guard let done = onDone else { return }
        onDone = nil
        // Close on the next pass of the run loop, not from inside the window's own event.
        onMain {
            FullscreenPresenter.shared.dismiss()
            done(result)
        }
    }

    /// Space / arrows / Esc while the fullscreen test is up.
    func handleKey(_ event: NSEvent) -> Bool {
        switch event.keyCode {
        case 53: // esc
            exitEarly()
            return true
        case 49, 124, 36: // space, right arrow, return
            if slide == .checklist { return false }
            next()
            return true
        case 123: // left arrow
            previous()
            return true
        default:
            return slide != .checklist
        }
    }
}

// MARK: - Fullscreen view

@MainActor
struct DisplayTestView: View {
    @ObservedObject var controller: DisplayTestController

    var body: some View {
        ZStack {
            slideContent
                .contentShape(Rectangle())
                .onTapGesture {
                    if controller.slide != .checklist { controller.next() }
                }
            if controller.slide != .intro && controller.slide != .checklist {
                VStack {
                    HStack {
                        Spacer()
                        Button {
                            controller.exitEarly()
                        } label: {
                            Image(systemName: "xmark")
                                .font(.system(size: 13, weight: .black))
                                .padding(9)
                                .background(Circle().fill(Color.black.opacity(0.55)))
                                .foregroundStyle(.white)
                        }
                        .buttonStyle(.plain)
                        .help(T("Exit the display test", "خروج از تست نمایشگر"))
                        .padding(18)
                    }
                    Spacer()
                }
            }
        }
        .background(Color.black)
    }

    @ViewBuilder
    private var slideContent: some View {
        switch controller.slide {
        case .intro:
            DisplayIntroSlide(stepText: controller.stepText)
        case .solid(let name, let color, let hint):
            ZStack {
                color
                if controller.showLabel {
                    SlideLabel(title: name, subtitle: "\(hint) · \(controller.stepText)", dark: isLight(name: name))
                        .transition(.opacity)
                }
            }
        case .gradient:
            ZStack {
                VStack(spacing: 0) {
                    LinearGradient(colors: [.black, .white], startPoint: .leading, endPoint: .trailing)
                    LinearGradient(colors: [.black, Color(red: 1, green: 0, blue: 0)], startPoint: .leading, endPoint: .trailing)
                    LinearGradient(colors: [.black, Color(red: 0, green: 1, blue: 0)], startPoint: .leading, endPoint: .trailing)
                    LinearGradient(colors: [.black, Color(red: 0, green: 0, blue: 1)], startPoint: .leading, endPoint: .trailing)
                }
                if controller.showLabel {
                    SlideLabel(title: T("Gradients", "گرادیان‌ها"),
                               subtitle: T("smooth ramps — visible steps or bands mean poor color depth · \(controller.stepText)",
                                           "شیب نرم — پله یا نوار قابل‌دیدن یعنی عمق رنگ ضعیف · \(controller.stepText)"),
                               dark: false)
                        .transition(.opacity)
                }
            }
            .environment(\.layoutDirection, .leftToRight)
        case .refresh:
            RefreshRateSlide(controller: controller)
        case .checklist:
            DisplayChecklistSlide(controller: controller)
        }
    }

    private func isLight(name: String) -> Bool {
        name == T("White", "سفید") || name == T("Green", "سبز") || name == T("50% gray", "خاکستری ۵۰٪")
    }
}

@MainActor
struct SlideLabel: View {
    var title: String
    var subtitle: String
    var dark: Bool

    var body: some View {
        VStack(spacing: 6) {
            Text(title).font(Theme.font(34, .heavy))
            Text(subtitle).font(Theme.font(15, .medium))
        }
        .foregroundStyle(dark ? Color.black : Color.white)
        .padding(.horizontal, 26)
        .padding(.vertical, 16)
        .background(RoundedRectangle(cornerRadius: 14).fill((dark ? Color.white : Color.black).opacity(0.35)))
    }
}

@MainActor
struct DisplayIntroSlide: View {
    var stepText: String

    var body: some View {
        VStack(spacing: 22) {
            Image(systemName: "display")
                .font(.system(size: 56, weight: .bold))
            Text(T("Display test", "تست نمایشگر"))
                .font(Theme.font(40, .heavy))
            Text(T("This screen will fill your entire display with solid colors, one at a time, so you can spot dead or stuck pixels, ghosting, brightness issues and light bleeding across the whole panel.",
                   "این صفحه کل نمایشگر را یکی‌یکی با رنگ‌های یکدست پر می‌کند تا پیکسل سوخته یا گیرکرده، سایه‌افتادن، مشکل روشنایی و نشت نور را در کل پنل ببینید."))
                .font(Theme.font(18, .medium))
                .multilineTextAlignment(.center)
                .frame(maxWidth: 760)
            Text(T("Press SPACE, click, or tap anywhere to move to each next screen, all the way to the end — on the last one you'll be asked which issues, if any, you noticed. Press ESC to exit early.",
                   "برای رفتن به صفحه بعد SPACE را بزنید یا هرجا کلیک کنید تا آخر — در صفحه آخر می‌پرسیم چه ایرادهایی دیدید. برای خروج زودهنگام ESC را بزنید."))
                .font(Theme.font(15, .medium))
                .foregroundStyle(.white.opacity(0.75))
                .multilineTextAlignment(.center)
                .frame(maxWidth: 760)
            Text(T("PRESS SPACE, CLICK OR TAP TO BEGIN", "برای شروع SPACE را بزنید یا کلیک کنید"))
                .font(Theme.font(14, .heavy))
                .padding(.horizontal, 18)
                .padding(.vertical, 10)
                .background(Capsule().fill(Theme.yellow))
                .foregroundStyle(Theme.onYellow)
            Text(stepText).font(Theme.font(12, .medium)).foregroundStyle(.white.opacity(0.5))
        }
        .foregroundStyle(.white)
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(hex: 0x17141C))
    }
}

@MainActor
struct RefreshRateSlide: View {
    @ObservedObject var controller: DisplayTestController

    var body: some View {
        ZStack {
            RefreshRateView { hz in controller.measuredHz = hz }
            VStack(spacing: 10) {
                Text(T("REFRESH RATE CHECK", "بررسی نرخ تازه‌سازی"))
                    .font(Theme.font(15, .heavy))
                    .foregroundStyle(Theme.yellow)
                Text(T("Watch the square move — smoother, less blurry motion means a higher effective refresh rate. The number below is the frame rate actually being measured right now, not a value read from macOS settings.",
                       "حرکت مربع را تماشا کنید — حرکت نرم‌تر و کم‌تارتر یعنی نرخ تازه‌سازی مؤثر بالاتر. عدد زیر نرخ فریمی است که همین حالا اندازه‌گیری می‌شود، نه مقداری از تنظیمات macOS."))
                    .font(Theme.font(15, .medium))
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: 720)
                HardwareText(controller.measuredHz.map { "\(Fmt.fixed($0, 0)) Hz" } ?? "… Hz", size: 64, weight: .black, color: .white)
                Text(T("PRESS SPACE, CLICK OR TAP WHEN YOU'RE DONE", "وقتی تمام شد SPACE را بزنید یا کلیک کنید"))
                    .font(Theme.font(13, .heavy))
                    .foregroundStyle(.white.opacity(0.7))
                Text(controller.stepText).font(Theme.font(12, .medium)).foregroundStyle(.white.opacity(0.5))
            }
            .foregroundStyle(.white)
            .padding(.top, 60)
            .frame(maxHeight: .infinity, alignment: .top)
        }
    }
}

@MainActor
struct DisplayChecklistSlide: View {
    @ObservedObject var controller: DisplayTestController

    var body: some View {
        VStack {
            RetroCard(padding: 26) {
                VStack(alignment: .leading, spacing: 16) {
                    SectionLabel(T("Did you notice any of these?", "هیچ‌کدام از این‌ها را دیدید؟"), symbol: "checklist")
                    ForEach(DisplayIssue.allCases, id: \.self) { issue in
                        Toggle(isOn: binding(for: issue)) {
                            Text(issue.text)
                        }
                        .toggleStyle(RetroCheckboxStyle())
                    }
                    if let hz = controller.measuredHz {
                        KeyValueRow(key: T("Measured refresh rate", "نرخ تازه‌سازی اندازه‌گیری‌شده"), value: "\(Fmt.fixed(hz, 0)) Hz")
                    }
                    HStack {
                        Button(T("Back", "قبلی")) { controller.previous() }
                            .retroButton(.secondary)
                        Spacer()
                        Button {
                            controller.finish()
                        } label: {
                            Label(controller.issues.isEmpty ? T("No issues — Finish Display Test", "بدون ایراد — پایان تست نمایشگر")
                                                            : T("Finish Display Test", "پایان تست نمایشگر"),
                                  systemImage: "checkmark")
                        }
                        .retroButton(.primary, size: .large)
                    }
                }
            }
            .frame(maxWidth: 720)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Theme.background)
        .foregroundStyle(Theme.ink)
    }

    private func binding(for issue: DisplayIssue) -> Binding<Bool> {
        Binding(
            get: { controller.issues.contains(issue) },
            set: { on in
                if on { controller.issues.insert(issue) } else { controller.issues.remove(issue) }
            }
        )
    }
}

// MARK: - Refresh rate measurement

/// Moves a white square with a display link and reports the measured frame rate.
@MainActor
struct RefreshRateView: NSViewRepresentable {
    var onMeasure: (Double) -> Void

    func makeNSView(context: Context) -> RefreshRateNSView {
        let view = RefreshRateNSView()
        view.onMeasure = onMeasure
        return view
    }

    func updateNSView(_ nsView: RefreshRateNSView, context: Context) {
        nsView.onMeasure = onMeasure
    }

    static func dismantleNSView(_ nsView: RefreshRateNSView, coordinator: ()) {
        nsView.stop()
    }
}

final class RefreshRateNSView: NSView {
    var onMeasure: ((Double) -> Void)?

    private var link: CADisplayLink?
    private let square = CALayer()
    private var lastTimestamp: CFTimeInterval = 0
    private var intervals: [CFTimeInterval] = []
    private var lastReport: CFTimeInterval = 0
    private var position: CGFloat = 0
    private var direction: CGFloat = 1

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor(white: 0.07, alpha: 1).cgColor
        square.backgroundColor = NSColor.white.cgColor
        layer?.addSublayer(square)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not used")
    }

    // Let clicks fall through to SwiftUI's tap handler.
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        stop()
        guard window != nil else { return }
        let link = displayLink(target: self, selector: #selector(step(_:)))
        link.preferredFrameRateRange = CAFrameRateRange(minimum: 24, maximum: 240, preferred: 240)
        link.add(to: .main, forMode: .common)
        self.link = link
    }

    func stop() {
        link?.invalidate()
        link = nil
    }

    @objc private func step(_ link: CADisplayLink) {
        let now = link.timestamp
        var dt: CFTimeInterval = 1.0 / 60.0
        if lastTimestamp > 0 {
            dt = now - lastTimestamp
            if dt > 0 && dt < 0.25 {
                intervals.append(dt)
                if intervals.count > 120 { intervals.removeFirst(intervals.count - 120) }
            }
        }
        lastTimestamp = now

        let size = max(40, min(bounds.width, bounds.height) * 0.12)
        let travel = max(1, bounds.width - size)
        // Cross the screen in about 1.4 seconds.
        position += direction * travel / 1.4 * CGFloat(min(dt, 0.1))
        if position >= travel { position = travel; direction = -1 }
        if position <= 0 { position = 0; direction = 1 }
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        square.frame = CGRect(x: position, y: bounds.midY - size / 2 - bounds.height * 0.12, width: size, height: size)
        CATransaction.commit()

        if now - lastReport > 0.5 && intervals.count >= 20 {
            lastReport = now
            let average = intervals.reduce(0, +) / Double(intervals.count)
            if average > 0 { onMeasure?(1 / average) }
        }
    }
}

// MARK: - Station

@MainActor
struct DisplayStation: View {
    @ObservedObject var runner: TestSuiteRunner
    @State private var result: DisplayTestResult?
    @State private var exitedEarly = false
    @State private var controller: DisplayTestController?

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            if let result {
                RetroCard {
                    VStack(alignment: .leading, spacing: 12) {
                        SectionLabel(T("Display test result", "نتیجه تست نمایشگر"), symbol: "display")
                        if result.issues.isEmpty {
                            Label(T("No issues reported", "ایرادی گزارش نشد"), systemImage: "checkmark.seal.fill")
                                .font(Theme.font(16, .bold))
                                .foregroundStyle(Theme.green)
                        } else {
                            Text(T("\(result.issues.count) issue(s) reported", "\(result.issues.count) ایراد گزارش شد"))
                                .font(Theme.font(16, .bold))
                                .foregroundStyle(Theme.orange)
                            ForEach(result.issues, id: \.self) { issue in
                                Label(issue.shortText, systemImage: "exclamationmark.triangle.fill")
                                    .font(Theme.font(13, .semibold))
                            }
                        }
                        if let hz = result.measuredRefreshRateHz {
                            KeyValueRow(key: T("Measured refresh rate", "نرخ تازه‌سازی اندازه‌گیری‌شده"), value: "\(Fmt.fixed(hz, 0)) Hz")
                        }
                        Button(T("Run the display test again", "اجرای دوباره تست نمایشگر")) { start() }
                            .retroButton(.ghost, size: .small)
                    }
                }
                ContinueBar(isLast: runner.report.remaining.count <= 1, autoAdvanceSeconds: nil) {
                    runner.complete(.display) { $0.display = result }
                }
            } else {
                RetroCard {
                    VStack(alignment: .leading, spacing: 14) {
                        SectionLabel(T("Fullscreen display test", "تست تمام‌صفحه نمایشگر"), symbol: "display")
                        InstructionText(T("The display test fills the whole screen with solid colors, gradients and a moving square. Look closely at each screen; at the end you'll tick any problems you noticed.",
                                          "تست نمایشگر کل صفحه را با رنگ‌های یکدست، گرادیان‌ها و یک مربع متحرک پر می‌کند. هر صفحه را دقیق نگاه کنید؛ در پایان هر ایرادی دیدید را علامت می‌زنید."))
                        if exitedEarly {
                            Text(T("The display test was closed early. Start it again to finish it.",
                                   "تست نمایشگر زودتر بسته شد. برای کامل کردن، دوباره شروعش کنید."))
                                .font(Theme.font(13, .semibold))
                                .foregroundStyle(Theme.orange)
                        }
                        HStack(spacing: 12) {
                            Button {
                                start()
                            } label: {
                                Label(T("Start the fullscreen display test", "شروع تست تمام‌صفحه نمایشگر"), systemImage: "arrow.up.left.and.arrow.down.right")
                            }
                            .retroButton(.primary, size: .large)
                            Button(T("Skip", "رد شدن")) { runner.skip(.display) }
                                .retroButton(.ghost)
                        }
                    }
                }
            }
        }
        .onDisappear {
            if FullscreenPresenter.shared.isPresenting { FullscreenPresenter.shared.dismiss() }
        }
    }

    private func start() {
        let controller = DisplayTestController { outcome in
            if let outcome {
                result = outcome
                exitedEarly = false
            } else {
                exitedEarly = true
            }
        }
        self.controller = controller
        FullscreenPresenter.shared.present(DisplayTestView(controller: controller)) { event in
            controller.handleKey(event)
        }
    }
}
#endif
