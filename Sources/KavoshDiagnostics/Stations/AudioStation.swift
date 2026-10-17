#if os(macOS)
import KavoshCore
import SwiftUI

@MainActor
final class AudioStationModel: ObservableObject {
    @Published private(set) var playing: SpeakerSound = .silence
    @Published var leftHeard = false
    @Published var rightHeard = false
    @Published var muted = false {
        didSet { player?.isMuted = muted }
    }
    @Published private(set) var errorMessage: String?

    private var player: SpeakerTestPlayer?
    private var stopTask: Task<Void, Never>?

    func play(_ sound: SpeakerSound) {
        if player == nil { player = SpeakerTestPlayer() }
        do {
            try player?.play(sound)
            playing = sound
            errorMessage = nil
            stopTask?.cancel()
            let seconds = SpeakerTestPlayer.duration(of: sound)
            stopTask = Task { @MainActor [weak self] in
                try? await Task.sleep(nanoseconds: UInt64((seconds + 0.2) * 1_000_000_000))
                if Task.isCancelled { return }
                if self?.playing == sound { self?.playing = .silence }
            }
        } catch {
            errorMessage = T("The sound couldn't be played on this Mac's sound output. Check the output in System Settings — Sound and try again, or report an issue to move on.",
                             "صدا روی خروجی صدای این مک پخش نشد. خروجی را در تنظیمات سیستم — صدا بررسی کنید و دوباره امتحان کنید، یا برای ادامه ایراد را گزارش دهید.")
        }
    }

    func stopPlayback() {
        stopTask?.cancel()
        player?.stop()
        playing = .silence
    }
}

@MainActor
struct AudioStation: View {
    @ObservedObject var runner: TestSuiteRunner
    @StateObject private var model = AudioStationModel()

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            RetroCard {
                VStack(alignment: .leading, spacing: 8) {
                    SectionLabel(T("How it works", "روش کار"), symbol: "speaker.wave.2")
                    InstructionText(T("Play each tone and confirm you heard it from the correct side. Turn the volume up first.",
                                      "هر صدا را پخش کنید و تأیید کنید که از سمت درست شنیدید. اول صدا را زیاد کنید."))
                }
            }
            HStack(alignment: .top, spacing: 18) {
                ChannelCard(title: T("Left channel", "کانال چپ"), symbol: "speaker.wave.2.fill",
                            playing: model.playing == .toneLeft, heard: $model.leftHeard,
                            confirmText: T("I heard it on the left speaker", "از بلندگوی چپ شنیدم")) {
                    model.play(.toneLeft)
                }
                ChannelCard(title: T("Right channel", "کانال راست"), symbol: "speaker.wave.2.fill",
                            playing: model.playing == .toneRight, heard: $model.rightHeard,
                            confirmText: T("I heard it on the right speaker", "از بلندگوی راست شنیدم")) {
                    model.play(.toneRight)
                }
            }
            .environment(\.layoutDirection, .leftToRight)

            RetroCard {
                VStack(alignment: .leading, spacing: 10) {
                    SectionLabel(T("Music track", "قطعه موسیقی"), symbol: "music.note")
                    Text(T("Starts at half volume and ramps up to full over a few seconds — a more realistic listening check than the plain tone above. Listen for crackling, buzzing or distortion.",
                           "با نصف صدا شروع می‌شود و طی چند ثانیه به صدای کامل می‌رسد — بررسی واقعی‌تری از صدای ساده بالا. به خش‌خش، وزوز یا اعوجاج دقت کنید."))
                        .font(Theme.font(13, .medium))
                        .fixedSize(horizontal: false, vertical: true)
                    HStack(spacing: 10) {
                        Button {
                            if model.playing == .music {
                                model.stopPlayback()
                            } else {
                                model.play(.music)
                            }
                        } label: {
                            Label(model.playing == .music ? T("Stop", "توقف") : T("Play track", "پخش قطعه"),
                                  systemImage: model.playing == .music ? "stop.fill" : "play.fill")
                        }
                        .retroButton(.secondary)
                        Button {
                            model.muted.toggle()
                        } label: {
                            Image(systemName: model.muted ? "speaker.slash.fill" : "speaker.wave.3.fill")
                        }
                        .retroButton(.ghost)
                        .help(T("Mute / unmute", "بی‌صدا / باصدا"))
                    }
                }
            }

            if let error = model.errorMessage {
                Label(error, systemImage: "exclamationmark.triangle.fill")
                    .font(Theme.font(13, .semibold))
                    .foregroundStyle(Theme.red)
                    .fixedSize(horizontal: false, vertical: true)
            }

            HStack(spacing: 12) {
                ReportIssueMenu(options: [
                    T("No sound at all", "اصلاً صدایی نیست"),
                    T("Crackling or distorted sound", "صدای خش‌دار یا معوج"),
                    T("Sound came from the wrong side", "صدا از سمت اشتباه آمد"),
                    T("One side is much quieter", "یک سمت خیلی آرام‌تر است")
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
        .onDisappear { model.stopPlayback() }
    }

    private func finish(issue: String?) {
        model.stopPlayback()
        let result = AudioTestResult(leftHeard: model.leftHeard, rightHeard: model.rightHeard, reportedIssue: issue)
        runner.complete(.speakers) { $0.audio = result }
    }
}

@MainActor
struct ChannelCard: View {
    var title: String
    var symbol: String
    var playing: Bool
    @Binding var heard: Bool
    var confirmText: String
    var onPlay: () -> Void

    var body: some View {
        RetroCard(fill: playing ? Theme.yellow.opacity(0.35) : Theme.surface) {
            VStack(alignment: .leading, spacing: 12) {
                SectionLabel(title, symbol: symbol)
                Button(action: onPlay) {
                    Label(playing ? T("Playing…", "در حال پخش…") : T("Play tone", "پخش صدا"),
                          systemImage: playing ? "waveform" : "play.fill")
                }
                .retroButton(.primary)
                Toggle(isOn: $heard) {
                    Text(confirmText)
                }
                .toggleStyle(RetroCheckboxStyle())
            }
        }
    }
}

/// "Report Issue" button with a list of common problems.
@MainActor
struct ReportIssueMenu: View {
    var options: [String]
    var onPick: (String) -> Void

    var body: some View {
        Menu {
            ForEach(options, id: \.self) { option in
                Button(option) { onPick(option) }
            }
            Divider()
            Button(T("Other problem", "مشکل دیگر")) { onPick(T("Other problem", "مشکل دیگر")) }
        } label: {
            Label(T("Report Issue", "گزارش ایراد"), systemImage: "exclamationmark.bubble")
                .font(Theme.font(14, .bold))
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .fixedSize()
        .padding(.horizontal, 14)
        .padding(.vertical, 8)
        .retroSurface(fill: Theme.surface, corner: 10, shadow: 3)
    }
}
#endif
