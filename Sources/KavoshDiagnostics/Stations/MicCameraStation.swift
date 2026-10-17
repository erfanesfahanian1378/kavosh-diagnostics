#if os(macOS)
import AppKit
import AVFoundation
import KavoshCore
import SwiftUI

@MainActor
final class MicCameraModel: ObservableObject {
    enum DeviceState: Equatable {
        case checking, missing, denied, running, failed
    }

    @Published private(set) var cameraState: DeviceState = .checking
    @Published private(set) var micState: DeviceState = .checking
    @Published private(set) var cameraName: String?
    @Published private(set) var micName: String?
    @Published private(set) var levelDb: Double = -160
    @Published private(set) var peakDb: Double = -160
    @Published private(set) var pitch: Double?
    @Published private(set) var levelHistory: [Double] = []
    @Published var cameraWorks = false
    @Published var micWorks = false

    private(set) var session: AVCaptureSession?
    private var micEngine: AVAudioEngine?
    private var started = false
    private var lastUpdate = Date.distantPast

    func start() {
        guard !started else { return }
        started = true
        startCamera()
        startMicrophone()
    }

    // MARK: Camera

    private func startCamera() {
        guard let device = AVCaptureDevice.default(for: .video) else {
            cameraState = .missing
            return
        }
        cameraName = device.localizedName
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            configureCamera(device)
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .video) { granted in
                onMain { [weak self] in
                    if granted { self?.configureCamera(device) } else { self?.cameraState = .denied }
                }
            }
        default:
            cameraState = .denied
        }
    }

    private func configureCamera(_ device: AVCaptureDevice) {
        do {
            let input = try AVCaptureDeviceInput(device: device)
            let session = AVCaptureSession()
            session.beginConfiguration()
            if session.canSetSessionPreset(.high) { session.sessionPreset = .high }
            guard session.canAddInput(input) else {
                session.commitConfiguration()
                cameraState = .failed
                return
            }
            session.addInput(input)
            session.commitConfiguration()
            self.session = session
            cameraState = .running
            DispatchQueue.global(qos: .userInitiated).async {
                session.startRunning()
            }
        } catch {
            cameraState = .failed
        }
    }

    // MARK: Microphone

    private func startMicrophone() {
        guard let device = AVCaptureDevice.default(for: .audio) else {
            micState = .missing
            return
        }
        micName = device.localizedName
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized:
            configureMicrophone()
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .audio) { granted in
                onMain { [weak self] in
                    if granted { self?.configureMicrophone() } else { self?.micState = .denied }
                }
            }
        default:
            micState = .denied
        }
    }

    private func configureMicrophone() {
        let engine = AVAudioEngine()
        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        guard format.channelCount > 0, format.sampleRate > 0 else {
            micState = .missing
            return
        }
        let sampleRate = format.sampleRate
        input.installTap(onBus: 0, bufferSize: 4096, format: format) { @Sendable [weak self] buffer, _ in
            guard let channel = buffer.floatChannelData?[0] else { return }
            let samples = UnsafeBufferPointer(start: channel, count: Int(buffer.frameLength))
            let db = AudioAnalysis.rmsDecibels(samples)
            let pitch = AudioAnalysis.estimatePitch(samples, sampleRate: sampleRate)
            onMain { self?.update(db: db, pitch: pitch) }
        }
        do {
            engine.prepare()
            try engine.start()
            micEngine = engine
            micState = .running
        } catch {
            input.removeTap(onBus: 0)
            micState = .failed
        }
    }

    private func update(db: Double, pitch: Double?) {
        levelDb = db
        peakDb = max(peakDb, db)
        if let pitch { self.pitch = pitch }
        // Loud enough sound means the microphone clearly works.
        if db > -38 && !micWorks { micWorks = true }
        let now = Date()
        if now.timeIntervalSince(lastUpdate) > 0.08 {
            lastUpdate = now
            levelHistory.append(AudioAnalysis.meterLevel(decibels: db) * 100)
            if levelHistory.count > 120 { levelHistory.removeFirst(levelHistory.count - 120) }
        }
    }

    func stop() {
        if let session {
            DispatchQueue.global(qos: .userInitiated).async { session.stopRunning() }
        }
        session = nil
        if let micEngine {
            micEngine.inputNode.removeTap(onBus: 0)
            micEngine.stop()
        }
        micEngine = nil
    }

    func result(issue: String?) -> MicCameraTestResult {
        MicCameraTestResult(
            cameraPresent: cameraState != .missing,
            cameraWorks: cameraState != .missing && cameraWorks,
            microphonePresent: micState != .missing,
            microphoneWorks: micState != .missing && micWorks,
            peakLevelDb: peakDb > -160 ? peakDb : nil,
            reportedIssue: issue
        )
    }
}

@MainActor
struct MicCameraStation: View {
    @ObservedObject var runner: TestSuiteRunner
    @StateObject private var model = MicCameraModel()

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            RetroCard {
                VStack(alignment: .leading, spacing: 8) {
                    SectionLabel(T("How it works", "روش کار"), symbol: "web.camera")
                    InstructionText(T("Check that you can see yourself, then speak: the level should move with your voice.",
                                      "بررسی کنید که خودتان را می‌بینید، سپس حرف بزنید: سطح صدا باید با صدای شما حرکت کند."))
                }
            }
            HStack(alignment: .top, spacing: 18) {
                cameraCard
                microphoneCard
            }
            if model.cameraState == .denied || model.micState == .denied {
                Label(T("Access to the camera or microphone was denied. Allow it in System Settings — Privacy & Security, then run this test again.",
                        "دسترسی به دوربین یا میکروفون رد شد. آن را در تنظیمات سیستم — حریم خصوصی و امنیت مجاز کنید و دوباره این تست را اجرا کنید."),
                      systemImage: "lock.fill")
                    .font(Theme.font(13, .semibold))
                    .foregroundStyle(Theme.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack(spacing: 12) {
                ReportIssueMenu(options: [
                    T("The camera image is black or frozen", "تصویر دوربین سیاه یا ثابت است"),
                    T("The camera image is blurry or discolored", "تصویر دوربین تار یا بی‌رنگ است"),
                    T("The microphone doesn't react", "میکروفون واکنش نشان نمی‌دهد"),
                    T("The microphone sounds noisy", "میکروفون نویز دارد")
                ]) { issue in
                    finish(issue: issue)
                }
                Spacer()
                Button {
                    finish(issue: nil)
                } label: {
                    Label(runner.report.remaining.count <= 1 ? T("Finish", "پایان") : T("Next test", "تست بعدی"),
                          systemImage: "arrow.right")
                }
                .retroButton(.primary, size: .large)
            }
        }
        .onAppear { model.start() }
        .onDisappear { model.stop() }
    }

    private func finish(issue: String?) {
        let result = model.result(issue: issue)
        model.stop()
        runner.complete(.micCamera) { $0.micCamera = result }
    }

    private var cameraCard: some View {
        RetroCard(padding: 12) {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    SectionLabel(T("Camera", "دوربین"), symbol: "camera.fill")
                    Spacer()
                    if let name = model.cameraName { RetroBadge(name) }
                }
                ZStack {
                    RoundedRectangle(cornerRadius: 8).fill(Color.black)
                    switch model.cameraState {
                    case .running:
                        if let session = model.session {
                            CameraPreview(session: session)
                        }
                    case .missing:
                        Text(T("No webcam on this device — the camera check is skipped and nothing is deducted for it.",
                               "این دستگاه وب‌کم ندارد — بررسی دوربین رد می‌شود و امتیازی کم نمی‌شود."))
                            .font(Theme.font(13, .medium))
                            .foregroundStyle(.white)
                            .multilineTextAlignment(.center)
                            .padding()
                    case .denied:
                        Image(systemName: "lock.fill").font(.system(size: 28)).foregroundStyle(.white)
                    case .failed:
                        Text(T("The camera couldn't be started, so there is no preview. Report an issue to move on.",
                               "دوربین شروع نشد، پس پیش‌نمایشی نیست. برای ادامه ایراد را گزارش دهید."))
                            .font(Theme.font(13, .medium))
                            .foregroundStyle(.white)
                            .multilineTextAlignment(.center)
                            .padding()
                    case .checking:
                        ProgressView().tint(.white)
                    }
                }
                .aspectRatio(16.0 / 9.0, contentMode: .fit)
                .clipShape(RoundedRectangle(cornerRadius: 8))
                .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.ink, lineWidth: Theme.border))
                if model.cameraState != .missing {
                    Toggle(isOn: $model.cameraWorks) {
                        Text(T("Camera works — I can see myself clearly", "دوربین کار می‌کند — خودم را واضح می‌بینم"))
                    }
                    .toggleStyle(RetroCheckboxStyle())
                }
            }
        }
    }

    private var microphoneCard: some View {
        RetroCard(padding: 12) {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    SectionLabel(T("Microphone", "میکروفون"), symbol: "mic.fill")
                    Spacer()
                    if let name = model.micName { RetroBadge(name) }
                }
                switch model.micState {
                case .missing:
                    Text(T("No microphone was found on this Mac.", "میکروفونی روی این مک پیدا نشد."))
                        .font(Theme.font(13, .medium))
                case .failed:
                    Text(T("The microphone couldn't be started, so its level can't be shown. Report an issue to move on.",
                           "میکروفون شروع نشد، پس سطح صدا نشان داده نمی‌شود. برای ادامه ایراد را گزارش دهید."))
                        .font(Theme.font(13, .medium))
                        .fixedSize(horizontal: false, vertical: true)
                default:
                    VStack(alignment: .leading, spacing: 10) {
                        RetroProgressBar(value: AudioAnalysis.meterLevel(decibels: model.levelDb),
                                         color: model.levelDb > -12 ? Theme.red : Theme.green, height: 22)
                        HStack {
                            HardwareText(model.levelDb > -159 ? "\(Fmt.fixed(model.levelDb, 0)) dB" : "— dB", size: 20, weight: .heavy)
                            Spacer()
                            VStack(alignment: .trailing, spacing: 1) {
                                Text(T("Estimated pitch — speak or make noise", "زیر و بمی تخمینی — حرف بزنید یا صدا ایجاد کنید"))
                                    .font(Theme.font(10, .bold))
                                    .foregroundStyle(Theme.muted)
                                HardwareText(model.pitch.map { "\(Fmt.fixed($0, 0)) Hz · \(AudioAnalysis.noteName(for: $0))" } ?? "—",
                                             size: 16, weight: .bold, color: Theme.purple)
                            }
                        }
                        HistoryChart(values: model.levelHistory, maxValue: 100, capacity: 120, color: Theme.green)
                            .frame(height: 70)
                    }
                }
                if model.micState != .missing {
                    Toggle(isOn: $model.micWorks) {
                        Text(T("Microphone works — the level moves with my voice", "میکروفون کار می‌کند — سطح با صدایم حرکت می‌کند"))
                    }
                    .toggleStyle(RetroCheckboxStyle())
                }
            }
        }
    }
}

/// Live camera picture.
@MainActor
struct CameraPreview: NSViewRepresentable {
    let session: AVCaptureSession

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        let layer = AVCaptureVideoPreviewLayer(session: session)
        layer.videoGravity = .resizeAspectFill
        view.layer = layer
        view.wantsLayer = true
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}
#endif
