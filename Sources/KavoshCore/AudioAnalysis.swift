import Foundation

/// Microphone level and pitch estimation.
public enum AudioAnalysis {
    /// Root-mean-square level in dBFS (0 = full scale, -160 for silence).
    public static func rmsDecibels(_ samples: UnsafeBufferPointer<Float>) -> Double {
        guard !samples.isEmpty else { return -160 }
        var sum: Double = 0
        for s in samples { sum += Double(s) * Double(s) }
        let rms = (sum / Double(samples.count)).squareRoot()
        return rms > 0 ? max(-160, 20 * log10(rms)) : -160
    }

    public static func rmsDecibels(_ samples: [Float]) -> Double {
        samples.withUnsafeBufferPointer { rmsDecibels($0) }
    }

    /// Maps dBFS to 0...1 for a level meter (-60 dB → 0, 0 dB → 1).
    public static func meterLevel(decibels: Double, floor: Double = -60) -> Double {
        guard decibels > floor else { return 0 }
        return min(1, (decibels - floor) / -floor)
    }

    /// Estimates the fundamental frequency with a normalized autocorrelation (YIN-style
    /// difference function). Returns nil for silence or noise without a clear pitch.
    public static func estimatePitch(_ samples: UnsafeBufferPointer<Float>, sampleRate: Double,
                                     minFrequency: Double = 70, maxFrequency: Double = 1200) -> Double? {
        let n = samples.count
        guard n >= 256, sampleRate > 0 else { return nil }
        guard rmsDecibels(samples) > -50 else { return nil }

        let minLag = max(2, Int(sampleRate / maxFrequency))
        let maxLag = min(n / 2, Int(sampleRate / minFrequency))
        guard maxLag > minLag + 2 else { return nil }

        let window = n - maxLag
        var difference = [Double](repeating: 0, count: maxLag + 1)
        for lag in 1...maxLag {
            var sum: Double = 0
            for i in 0..<window {
                let d = Double(samples[i]) - Double(samples[i + lag])
                sum += d * d
            }
            difference[lag] = sum
        }

        // Cumulative mean normalized difference.
        var normalized = [Double](repeating: 1, count: maxLag + 1)
        var running: Double = 0
        for lag in 1...maxLag {
            running += difference[lag]
            normalized[lag] = running > 0 ? difference[lag] * Double(lag) / running : 1
        }

        let threshold = 0.15
        var bestLag: Int?
        var lag = minLag
        while lag <= maxLag {
            if normalized[lag] < threshold {
                var l = lag
                while l + 1 <= maxLag && normalized[l + 1] < normalized[l] { l += 1 }
                bestLag = l
                break
            }
            lag += 1
        }
        if bestLag == nil {
            // No confident dip; take the global minimum only when it is reasonably clear.
            var minValue = Double.infinity
            var minIndex = minLag
            for l in minLag...maxLag where normalized[l] < minValue {
                minValue = normalized[l]
                minIndex = l
            }
            if minValue < 0.35 { bestLag = minIndex }
        }
        guard let tau = bestLag else { return nil }

        // Parabolic interpolation around the dip for sub-sample accuracy.
        var refined = Double(tau)
        if tau > 1 && tau < maxLag {
            let a = normalized[tau - 1]
            let b = normalized[tau]
            let c = normalized[tau + 1]
            let denominator = a - 2 * b + c
            if abs(denominator) > 1e-12 {
                refined += 0.5 * (a - c) / denominator
            }
        }
        let frequency = sampleRate / refined
        guard frequency >= minFrequency * 0.9, frequency <= maxFrequency * 1.1 else { return nil }
        return frequency
    }

    public static func estimatePitch(_ samples: [Float], sampleRate: Double) -> Double? {
        samples.withUnsafeBufferPointer { estimatePitch($0, sampleRate: sampleRate) }
    }

    /// Musical note name for a frequency, e.g. 440 → "A4".
    public static func noteName(for frequency: Double) -> String {
        guard frequency > 0 else { return "—" }
        let names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
        let midi = Int((69 + 12 * log2(frequency / 440)).rounded())
        let index = ((midi % 12) + 12) % 12
        return "\(names[index])\(midi / 12 - 1)"
    }
}
