#if os(macOS)
import AppKit
import KavoshCore
import SwiftUI

@MainActor
final class InputStationState: ObservableObject {
    enum Step { case typing, keys, result }

    @Published var step: Step = .typing
    @Published var typed = ""
    @Published private(set) var tracker = KeyPressTracker()
    @Published private(set) var lastKey: String?
    @Published var trackpadMoved = false
    @Published var trackpadClicked = false
    @Published var trackpadScrolled = false

    private var monitor: Any?

    var sentenceCorrect: Bool { TypingPrompt.matches(typed) }

    func beginKeyCapture() {
        step = .keys
        endKeyCapture()
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .flagsChanged]) { [weak self] event in
            MainActor.assumeIsolated {
                self?.handle(event)
            }
            return nil // swallow keys so shortcuts don't fire during the test
        }
    }

    func endKeyCapture() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
    }

    func finishKeys() {
        endKeyCapture()
        step = .result
    }

    private func handle(_ event: NSEvent) {
        let code = event.keyCode
        if event.type == .keyDown {
            tracker.keyDown(code: code, timestamp: event.timestamp, isRepeat: event.isARepeat)
        } else if event.type == .flagsChanged {
            if code == 57 {
                // Caps lock toggles; every change is one press.
                tracker.keyDown(code: code, timestamp: event.timestamp, isRepeat: false)
            } else if let flag = MacKeyboardLayout.modifierFlag(for: code),
                      event.modifierFlags.rawValue & flag != 0 {
                tracker.keyDown(code: code, timestamp: event.timestamp, isRepeat: false)
            } else {
                return
            }
        }
        lastKey = tracker.lastKeyLabel
    }

    func result() -> KeyboardTestResult {
        tracker.result(sentenceCorrect: sentenceCorrect, trackpadMoved: trackpadMoved,
                       trackpadClicked: trackpadClicked, trackpadScrolled: trackpadScrolled)
    }
}

@MainActor
struct InputStation: View {
    @ObservedObject var runner: TestSuiteRunner
    @StateObject private var state = InputStationState()

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            if runner.report.hasNoBuiltInInputs {
                RetroCard {
                    VStack(alignment: .leading, spacing: 8) {
                        SectionLabel(T("Keyboard / trackpad", "کیبورد / ترک‌پد"), symbol: "keyboard")
                        Text(T("Skipped on a Mac without a built-in keyboard/trackpad — not counted as a failure and not scored.",
                               "روی مکی بدون کیبورد/ترک‌پد داخلی رد شد — خرابی حساب نمی‌شود و امتیازی ندارد."))
                            .font(Theme.font(14, .medium))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
                ContinueBar(isLast: runner.report.remaining.count <= 1, autoAdvanceSeconds: 3) {
                    runner.complete(.input) { $0.keyboard = KeyboardTestResult(bypassed: true) }
                }
            } else {
                switch state.step {
                case .typing: typingStep
                case .keys: keysStep
                case .result: resultStep
                }
            }
        }
        .onDisappear { state.endKeyCapture() }
    }

    // MARK: Step 1

    private var typingStep: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 14) {
                SectionLabel(T("Step 1 of 3 — type this sentence", "مرحله ۱ از ۳ — این جمله را تایپ کنید"), symbol: "character.cursor.ibeam")
                PromptText(typed: state.typed)
                TypingField(text: $state.typed)
                HStack {
                    if !state.typed.isEmpty {
                        if state.sentenceCorrect {
                            Label(T("Matches", "مطابقت دارد"), systemImage: "checkmark.circle.fill")
                                .foregroundStyle(Theme.green)
                        } else {
                            Label(T("\(state.typed.count) of \(TypingPrompt.sentence.count) characters", "\(state.typed.count) از \(TypingPrompt.sentence.count) نویسه"),
                                  systemImage: "keyboard")
                                .foregroundStyle(Theme.muted)
                        }
                    }
                    Spacer()
                    Button {
                        state.beginKeyCapture()
                    } label: {
                        Label(T("Next — check every key", "بعدی — بررسی همه کلیدها"), systemImage: "arrow.right")
                    }
                    .retroButton(.primary)
                }
                .font(Theme.font(13, .semibold))
            }
        }
    }

    // MARK: Step 2

    private var keysStep: some View {
        let progress = state.tracker.requiredProgress
        return RetroCard {
            VStack(alignment: .leading, spacing: 14) {
                SectionLabel(T("Step 2 of 3 — press every key on your keyboard", "مرحله ۲ از ۳ — همه کلیدهای کیبورد را بزنید"), symbol: "keyboard")
                Text(T("Press each key once. It lights up when it registers. Function keys and fn are optional — hold fn if the top row controls brightness and volume.",
                       "هر کلید را یک بار بزنید. وقتی ثبت شود روشن می‌شود. کلیدهای تابعی و fn اختیاری‌اند — اگر ردیف بالا روشنایی و صدا را کنترل می‌کند fn را نگه دارید."))
                    .font(Theme.font(13, .medium))
                    .fixedSize(horizontal: false, vertical: true)
                KeyboardGrid(tracker: state.tracker)
                HStack(spacing: 12) {
                    RetroProgressBar(value: Double(progress.pressed) / Double(max(1, progress.total)), color: Theme.green)
                    HardwareText("\(progress.pressed) / \(progress.total)", size: 14, weight: .heavy)
                }
                HStack {
                    Text(T("Last key: \(state.lastKey ?? "—")", "آخرین کلید: \(state.lastKey ?? "—")"))
                        .font(Theme.font(13, .semibold))
                        .environment(\.layoutDirection, .leftToRight)
                    Spacer()
                    Button {
                        state.finishKeys()
                    } label: {
                        Label(T("Finish key check", "پایان بررسی کلیدها"), systemImage: "checkmark")
                    }
                    .retroButton(.primary)
                }
            }
        }
    }

    // MARK: Step 3

    private var resultStep: some View {
        let result = state.result()
        return VStack(alignment: .leading, spacing: 18) {
            RetroCard {
                VStack(alignment: .leading, spacing: 12) {
                    SectionLabel(T("Step 3 of 3 — keyboard result", "مرحله ۳ از ۳ — نتیجه کیبورد"), symbol: "checklist")
                    if result.missingKeys.isEmpty && result.doublePressedKeys.isEmpty {
                        Label(T("No missing or double-pressed keys.", "هیچ کلید ازکارافتاده یا دوبارثبت‌شده‌ای نیست."), systemImage: "checkmark.seal.fill")
                            .font(Theme.font(15, .bold))
                            .foregroundStyle(Theme.green)
                    }
                    if !result.missingKeys.isEmpty {
                        Text(T("Keys that didn't register (\(result.missingKeys.count)):", "کلیدهایی که ثبت نشدند (\(result.missingKeys.count)):"))
                            .font(Theme.font(13, .bold))
                        FlowLayout(spacing: 6) {
                            ForEach(result.missingKeys, id: \.self) { RetroBadge($0, color: Theme.red, textColor: .white) }
                        }
                    }
                    if !result.doublePressedKeys.isEmpty {
                        Text(T("Keys that registered twice for one press:", "کلیدهایی که با یک فشار دوبار ثبت شدند:"))
                            .font(Theme.font(13, .bold))
                        FlowLayout(spacing: 6) {
                            ForEach(result.doublePressedKeys, id: \.self) { RetroBadge($0, color: Theme.orange, textColor: Theme.onYellow) }
                        }
                    }
                    if !result.sentenceCorrect {
                        Label(T("The typed sentence didn't match the prompt.", "جمله تایپ‌شده با متن مطابقت نداشت."), systemImage: "exclamationmark.triangle.fill")
                            .font(Theme.font(13, .semibold))
                            .foregroundStyle(Theme.orange)
                    }
                    HStack(spacing: 10) {
                        Button(T("Check keys again", "بررسی دوباره کلیدها")) { state.beginKeyCapture() }
                            .retroButton(.ghost, size: .small)
                    }
                }
            }
            RetroCard {
                VStack(alignment: .leading, spacing: 12) {
                    SectionLabel(T("Trackpad — move, click and scroll inside this area", "ترک‌پد — داخل این ناحیه حرکت، کلیک و اسکرول کنید"), symbol: "rectangle.and.hand.point.up.left")
                    TrackpadArea(moved: $state.trackpadMoved, clicked: $state.trackpadClicked, scrolled: $state.trackpadScrolled)
                        .frame(height: 170)
                    HStack(spacing: 8) {
                        RetroBadge(T("Move", "حرکت"), color: state.trackpadMoved ? Theme.green : Theme.surfaceAlt,
                                   symbol: state.trackpadMoved ? "checkmark" : "circle")
                        RetroBadge(T("Click", "کلیک"), color: state.trackpadClicked ? Theme.green : Theme.surfaceAlt,
                                   symbol: state.trackpadClicked ? "checkmark" : "circle")
                        RetroBadge(T("Scroll", "اسکرول"), color: state.trackpadScrolled ? Theme.green : Theme.surfaceAlt,
                                   symbol: state.trackpadScrolled ? "checkmark" : "circle")
                        Spacer()
                        Text(T("Movement, click and scroll detected: \(result.trackpadChecksPassed) of 3",
                               "حرکت، کلیک و اسکرول تشخیص داده شد: \(result.trackpadChecksPassed) از ۳"))
                            .font(Theme.font(12, .semibold))
                    }
                }
            }
            HStack {
                Spacer()
                Button {
                    let finalResult = state.result()
                    runner.complete(.input) { $0.keyboard = finalResult }
                } label: {
                    Label(runner.report.remaining.count <= 1 ? T("Finish", "پایان") : T("Next test", "تست بعدی"),
                          systemImage: "arrow.right")
                }
                .retroButton(.primary, size: .large)
            }
        }
    }
}

/// The sentence with typos highlighted as you type.
@MainActor
struct PromptText: View {
    var typed: String

    var body: some View {
        let wrong = TypingPrompt.mismatchedIndices(typed)
        let typedCount = typed.count
        let characters = Array(TypingPrompt.sentence)
        var text = Text("")
        for (index, character) in characters.enumerated() {
            let color: Color = wrong.contains(index) ? Theme.red : (index < typedCount ? Theme.green : Theme.ink)
            text = text + Text(String(character)).foregroundStyle(color)
        }
        return text
            .font(.system(size: 22, weight: .bold, design: .monospaced))
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 8).fill(Theme.surfaceAlt))
            .environment(\.layoutDirection, .leftToRight)
    }
}

/// Text field that grabs focus as soon as it appears.
@MainActor
struct TypingField: View {
    @Binding var text: String
    @FocusState private var focused: Bool

    var body: some View {
        TextField(T("Type the sentence here", "جمله را اینجا تایپ کنید"), text: $text)
            .textFieldStyle(.plain)
            .font(.system(size: 20, weight: .medium, design: .monospaced))
            .padding(12)
            .retroSurface(fill: Theme.surface, corner: 8, shadow: 3)
            .focused($focused)
            .autocorrectionDisabled(true)
            .environment(\.layoutDirection, .leftToRight)
            .task {
                try? await Task.sleep(nanoseconds: 200_000_000)
                focused = true
            }
    }
}

/// On-screen Mac keyboard; keys light up once pressed.
@MainActor
struct KeyboardGrid: View {
    var tracker: KeyPressTracker
    private let unit: CGFloat = 40
    private let gap: CGFloat = 5

    var body: some View {
        VStack(alignment: .leading, spacing: gap) {
            ForEach(Array(MacKeyboardLayout.rows.enumerated()), id: \.offset) { _, row in
                HStack(spacing: gap) {
                    ForEach(row) { key in
                        keyCap(key)
                    }
                }
            }
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 12).fill(Theme.surfaceAlt))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Theme.ink, lineWidth: Theme.border))
        .environment(\.layoutDirection, .leftToRight)
    }

    private func keyCap(_ key: KeyboardKey) -> some View {
        let pressed = tracker.isPressed(key.code)
        let doubled = tracker.doublePressed.contains(key.code)
        let fill: Color = doubled ? Theme.orange : (pressed ? Theme.green : (key.optional ? Theme.surfaceAlt : Theme.surface))
        return Text(key.label)
            .font(.system(size: key.label.count > 2 ? 10 : 13, weight: .bold, design: .rounded))
            .foregroundStyle(pressed ? Color.white : Theme.ink)
            .frame(width: unit * key.width + gap * (key.width - 1), height: unit)
            .background(RoundedRectangle(cornerRadius: 7).fill(fill))
            .overlay(RoundedRectangle(cornerRadius: 7).strokeBorder(Theme.ink.opacity(key.optional ? 0.4 : 1),
                                                                     style: StrokeStyle(lineWidth: 1.5, dash: key.optional ? [3, 2] : [])))
    }
}

/// Area that notices pointer movement, clicks and scrolling.
@MainActor
struct TrackpadArea: NSViewRepresentable {
    @Binding var moved: Bool
    @Binding var clicked: Bool
    @Binding var scrolled: Bool

    func makeNSView(context: Context) -> TrackpadNSView {
        let view = TrackpadNSView()
        view.onMove = { if !moved { moved = true } }
        view.onClick = { if !clicked { clicked = true } }
        view.onScroll = { if !scrolled { scrolled = true } }
        return view
    }

    func updateNSView(_ nsView: TrackpadNSView, context: Context) {
        nsView.onMove = { if !moved { moved = true } }
        nsView.onClick = { if !clicked { clicked = true } }
        nsView.onScroll = { if !scrolled { scrolled = true } }
        nsView.needsDisplay = true
    }
}

final class TrackpadNSView: NSView {
    var onMove: (() -> Void)?
    var onClick: (() -> Void)?
    var onScroll: (() -> Void)?
    private var trackingArea: NSTrackingArea?
    private var pointer: NSPoint?

    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let trackingArea { removeTrackingArea(trackingArea) }
        let area = NSTrackingArea(rect: bounds, options: [.mouseMoved, .activeInKeyWindow, .inVisibleRect, .mouseEnteredAndExited],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        trackingArea = area
    }

    override func mouseMoved(with event: NSEvent) {
        pointer = convert(event.locationInWindow, from: nil)
        onMove?()
        needsDisplay = true
    }

    override func mouseDown(with event: NSEvent) {
        onClick?()
    }

    override func rightMouseDown(with event: NSEvent) {
        onClick?()
    }

    override func scrollWheel(with event: NSEvent) {
        if abs(event.scrollingDeltaY) > 0.5 || abs(event.scrollingDeltaX) > 0.5 { onScroll?() }
    }

    override func draw(_ dirtyRect: NSRect) {
        let path = NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 1), xRadius: 10, yRadius: 10)
        NSColor.controlBackgroundColor.setFill()
        path.fill()
        NSColor.labelColor.withAlphaComponent(0.6).setStroke()
        path.setLineDash([6, 4], count: 2, phase: 0)
        path.lineWidth = 2
        path.stroke()
        if let pointer {
            NSColor.systemBlue.setFill()
            NSBezierPath(ovalIn: NSRect(x: pointer.x - 7, y: pointer.y - 7, width: 14, height: 14)).fill()
        }
    }
}
#endif
