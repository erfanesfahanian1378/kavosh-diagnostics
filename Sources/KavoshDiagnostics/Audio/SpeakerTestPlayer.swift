#if os(macOS)
import AVFoundation
import Foundation

/// What the synthesizer is playing.
enum SpeakerSound: Int {
    case silence = 0
    case toneLeft = 1
    case toneRight = 2
    case music = 3
}

/// Shared between the main thread and the audio render thread. The values are plain words,
/// written by one side and read by the other.
final class SynthState: @unchecked Sendable {
    var sound: SpeakerSound = .silence
    var startSample: Int64 = 0
    var sampleCounter: Int64 = 0
    var muted = false
    var noise: UInt32 = 0x1234_5678

    static let toneSeconds = 3.0
    static let musicSeconds = 17.5

    func render(frames: Int, left: UnsafeMutablePointer<Float>, right: UnsafeMutablePointer<Float>, sampleRate: Double) {
        let sound = self.sound
        let muted = self.muted
        for i in 0..<frames {
            let n = sampleCounter - startSample
            sampleCounter += 1
            let t = Double(n) / sampleRate
            var l: Double = 0
            var r: Double = 0
            switch sound {
            case .silence:
                break
            case .toneLeft, .toneRight:
                if t < SynthState.toneSeconds {
                    let value = SynthState.beep(t)
                    if sound == .toneLeft { l = value } else { r = value }
                }
            case .music:
                if t < SynthState.musicSeconds {
                    (l, r) = music(t)
                }
            }
            if muted {
                l = 0
                r = 0
            }
            left[i] = Float(l)
            right[i] = Float(r)
        }
    }

    /// 660 Hz beeps: 0.35 s on, 0.15 s off, with short fades so there are no clicks.
    static func beep(_ t: Double) -> Double {
        let period = 0.5
        let local = t.truncatingRemainder(dividingBy: period)
        guard local < 0.35 else { return 0 }
        let fade = min(1, local / 0.01, (0.35 - local) / 0.01)
        return 0.35 * fade * sin(2 * .pi * 660 * local)
    }

    private static func frequency(_ midi: Double) -> Double {
        440 * pow(2, (midi - 69) / 12)
    }

    // C – G – Am – F, 110 bpm, arpeggio + bass + hi-hat. Volume ramps from half to full.
    private static let chords: [[Double]] = [[60, 64, 67], [55, 59, 62], [57, 60, 64], [53, 57, 60]]
    private static let arpeggio = [0, 1, 2, 1, 0, 2, 1, 2]

    private func music(_ t: Double) -> (Double, Double) {
        let beat = 60.0 / 110.0
        let eighth = beat / 2
        let barLength = beat * 4
        let bar = Int(t / barLength) % SynthState.chords.count
        let chord = SynthState.chords[bar]

        // Arpeggio note, alternating between the left and right side.
        let eighthIndex = Int(t / eighth)
        let noteTime = t - Double(eighthIndex) * eighth
        let toneIndex = SynthState.arpeggio[eighthIndex % SynthState.arpeggio.count]
        let octave = (eighthIndex / 4) % 2 == 0 ? 12.0 : 0.0
        let f = SynthState.frequency(chord[toneIndex] + octave)
        let pluckEnvelope = min(1, noteTime / 0.004) * exp(-noteTime * 5.5)
        let pluck = 0.20 * pluckEnvelope * (sin(2 * .pi * f * noteTime) + 0.35 * sin(4 * .pi * f * noteTime))
        let pan = 0.5 + 0.42 * sin(Double(eighthIndex) * .pi / 2)

        // Bass on every beat.
        let beatIndex = Int(t / beat)
        let beatTime = t - Double(beatIndex) * beat
        let bassFrequency = SynthState.frequency(chord[0] - 24)
        let bassEnvelope = min(1, beatTime / 0.006) * exp(-beatTime * 3.2)
        let bass = 0.26 * bassEnvelope * (sin(2 * .pi * bassFrequency * beatTime) + 0.25 * sin(6 * .pi * bassFrequency * beatTime))

        // Hi-hat on the off-beats.
        var hat = 0.0
        if eighthIndex % 2 == 1 {
            noise = noise &* 1_664_525 &+ 1_013_904_223
            let white = Double(Int32(bitPattern: noise)) / Double(Int32.max)
            hat = 0.05 * white * exp(-noteTime * 60)
        }

        let ramp = 0.5 + 0.5 * min(1, t / 4)
        let fadeOut = min(1, (SynthState.musicSeconds - t) / 0.5)
        let gain = ramp * max(0, fadeOut)
        let left = gain * (pluck * (1 - pan) * 1.4 + bass + hat)
        let right = gain * (pluck * pan * 1.4 + bass + hat)
        return (left, right)
    }
}

/// Plays the left/right tones and the music track through the default output.
final class SpeakerTestPlayer {
    private let engine = AVAudioEngine()
    private let state = SynthState()
    private var source: AVAudioSourceNode?
    private(set) var sampleRate: Double = 48_000

    init() {
        let hardware = engine.outputNode.outputFormat(forBus: 0)
        if hardware.sampleRate > 0 { sampleRate = hardware.sampleRate }
        guard let format = AVAudioFormat(standardFormatWithSampleRate: sampleRate, channels: 2) else { return }
        let state = self.state
        let rate = sampleRate
        let node = AVAudioSourceNode(format: format) { _, _, frameCount, audioBufferList -> OSStatus in
            let buffers = UnsafeMutableAudioBufferListPointer(audioBufferList)
            guard buffers.count >= 2,
                  let left = buffers[0].mData?.assumingMemoryBound(to: Float.self),
                  let right = buffers[1].mData?.assumingMemoryBound(to: Float.self) else {
                return noErr
            }
            state.render(frames: Int(frameCount), left: left, right: right, sampleRate: rate)
            return noErr
        }
        engine.attach(node)
        engine.connect(node, to: engine.mainMixerNode, format: format)
        source = node
    }

    var isMuted: Bool {
        get { state.muted }
        set { state.muted = newValue }
    }

    func play(_ sound: SpeakerSound) throws {
        guard source != nil else { throw NSError(domain: "Kavosh", code: 1) }
        state.startSample = state.sampleCounter
        state.sound = sound
        if !engine.isRunning {
            engine.prepare()
            try engine.start()
        }
    }

    func stop() {
        state.sound = .silence
        if engine.isRunning { engine.stop() }
    }

    static func duration(of sound: SpeakerSound) -> Double {
        switch sound {
        case .silence: return 0
        case .toneLeft, .toneRight: return SynthState.toneSeconds
        case .music: return SynthState.musicSeconds
        }
    }
}
#endif
