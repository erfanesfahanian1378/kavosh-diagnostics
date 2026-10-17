#if os(macOS)
import Foundation
import Combine
import KavoshCore

/// Peak and average readings collected while a test runs.
struct TelemetryRecording {
    var peakCPUTemperature: Double?
    var peakGPUTemperature: Double?
    var powerSum: Double = 0
    var powerSamples = 0
    var worstThermal: ThermalLevel = .nominal
    var samples = 0

    var averagePower: Double? { powerSamples > 0 ? powerSum / Double(powerSamples) : nil }

    mutating func add(_ s: SensorSnapshot) {
        samples += 1
        if let t = s.cpuTemperatureMaxCelsius ?? s.cpuTemperatureCelsius {
            peakCPUTemperature = max(peakCPUTemperature ?? t, t)
        }
        if let t = s.gpuTemperatureCelsius {
            peakGPUTemperature = max(peakGPUTemperature ?? t, t)
        }
        if let p = s.systemPowerWatts {
            powerSum += p
            powerSamples += 1
        }
        worstThermal = max(worstThermal, s.thermalState)
    }
}

/// Samples the sensors once a second on a background queue and keeps the last 90 seconds.
@MainActor
final class LiveTelemetry: ObservableObject {
    static let capacity = 90

    @Published private(set) var latest: SensorSnapshot?
    @Published private(set) var cpuHistory: [Double] = []
    @Published private(set) var ramHistory: [Double] = []
    @Published private(set) var gpuHistory: [Double] = []
    @Published private(set) var cpuTemperatureHistory: [Double] = []
    @Published private(set) var powerHistory: [Double] = []

    private let queue = DispatchQueue(label: "dev.kavosh.telemetry", qos: .utility)
    private var timer: DispatchSourceTimer?
    private var recordings: [UUID: TelemetryRecording] = [:]

    func start() {
        guard timer == nil else { return }
        let box = SamplerBox()
        let source = DispatchSource.makeTimerSource(queue: queue)
        source.schedule(deadline: .now() + 0.3, repeating: 1.0, leeway: .milliseconds(100))
        source.setEventHandler { @Sendable [weak self] in
            let snapshot = box.sample()
            onMain { self?.publish(snapshot) }
        }
        source.resume()
        timer = source
    }

    func beginRecording() -> UUID {
        let id = UUID()
        var recording = TelemetryRecording()
        if let latest { recording.add(latest) }
        recordings[id] = recording
        return id
    }

    func endRecording(_ id: UUID) -> TelemetryRecording {
        recordings.removeValue(forKey: id) ?? TelemetryRecording()
    }

    func peek(_ id: UUID) -> TelemetryRecording? {
        recordings[id]
    }

    private func publish(_ snapshot: SensorSnapshot) {
        latest = snapshot
        append(&cpuHistory, snapshot.cpuLoadPercent)
        append(&ramHistory, snapshot.ramLoadPercent)
        append(&gpuHistory, snapshot.gpuLoadPercent ?? 0)
        if let t = snapshot.cpuTemperatureCelsius { append(&cpuTemperatureHistory, t) }
        if let p = snapshot.systemPowerWatts { append(&powerHistory, p) }
        for key in recordings.keys {
            recordings[key]?.add(snapshot)
        }
    }

    private func append(_ array: inout [Double], _ value: Double) {
        array.append(value)
        if array.count > LiveTelemetry.capacity {
            array.removeFirst(array.count - LiveTelemetry.capacity)
        }
    }
}

/// Owns the sampler; only touched from the telemetry queue.
private final class SamplerBox: @unchecked Sendable {
    private var sampler: TelemetrySampler?

    func sample() -> SensorSnapshot {
        if sampler == nil { sampler = TelemetrySampler() }
        return sampler!.sample()
    }
}
#endif
