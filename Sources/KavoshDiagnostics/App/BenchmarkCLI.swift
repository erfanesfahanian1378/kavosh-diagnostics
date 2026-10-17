#if os(macOS)
import Foundation
import KavoshCore

/// Command-line mode. Lets you check every reading and run the engines without the UI:
///
///     KavoshDiagnostics --dump
///     KavoshDiagnostics --cpu [--quick] [--duration 60]
///     KavoshDiagnostics --memory [--quick]
///     KavoshDiagnostics --gpu [--duration 30]
///     KavoshDiagnostics --ssd /Volumes/MyDrive [--quick]
enum BenchmarkCLI {
    static let commands: Set<String> = ["--dump", "--cpu", "--memory", "--gpu", "--ssd", "--help", "-h"]

    /// Returns true when the arguments asked for command-line mode (and it ran).
    static func handle(arguments: [String]) -> Bool {
        guard let command = arguments.first(where: { commands.contains($0) }) else { return false }
        setvbuf(stdout, nil, _IOLBF, 0)
        let quick = arguments.contains("--quick")
        let duration = value(after: "--duration", in: arguments).flatMap(Double.init)
        switch command {
        case "--dump":
            dump()
        case "--cpu":
            runCPU(quick: quick, duration: duration)
        case "--memory":
            runMemory(quick: quick)
        case "--gpu":
            runGPU(duration: duration ?? 20)
        case "--ssd":
            let path = value(after: "--ssd", in: arguments) ?? FileManager.default.temporaryDirectory.path
            runSSD(path: path, quick: quick)
        default:
            printHelp()
        }
        return true
    }

    private static func value(after flag: String, in arguments: [String]) -> String? {
        guard let index = arguments.firstIndex(of: flag), index + 1 < arguments.count else { return nil }
        let next = arguments[index + 1]
        return next.hasPrefix("--") ? nil : next
    }

    private static func printHelp() {
        print("""
        Kavosh Diagnostics — command-line mode

          --dump                       print specs, live sensors, battery and drive health
          --cpu [--quick] [--duration S]   run the CPU benchmark
          --memory [--quick]           run the memory integrity test
          --gpu [--duration S]         run the Metal GPU benchmark
          --ssd [PATH] [--quick]       run the SSD speed test in PATH (default: temp folder)

        With no arguments the app window opens.
        """)
    }

    private static func json<T: Encodable>(_ value: T) -> String {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        guard let data = try? encoder.encode(value) else { return "{}" }
        return String(decoding: data, as: UTF8.self)
    }

    // MARK: Dump

    private static func dump() {
        print("== specs")
        var specs = SystemProbe.quickSpecs()
        SystemProbe.addSlowDetails(to: &specs)
        print(json(specs))

        print("\n== sensors")
        SMCSensors.shared.discoverIfNeeded()
        print("smc available: \(SMCSensors.shared.isAvailable)  keys: \(SMCSensors.shared.keySummary)")
        let sampler = TelemetrySampler()
        Thread.sleep(forTimeInterval: 1)
        print(json(sampler.sample()))

        print("\n== battery")
        if let battery = BatteryReader.read() {
            print("design \(battery.designCapacityMah) mAh, full charge \(battery.fullChargeCapacityMah) mAh, health \(Fmt.fixed(battery.healthPercent, 1))%")
            print("cycles \(battery.cycleCount), charge \(battery.chargePercent)%, charging \(battery.isCharging), plugged in \(battery.isPluggedIn)")
            print("amperage \(battery.amperageMa) mA, voltage \(battery.voltageMv) mV, discharge \(battery.dischargeWatts.map { Fmt.fixed($0, 2) + " W" } ?? "—")")
            print("temperature \(Fmt.celsius(battery.temperatureCelsius)), condition \(battery.condition ?? "—")")
        } else {
            print("none")
        }

        print("\n== drives")
        print(json(DriveReader.allDevices()))
        print(json(DriveReader.readHealth()))
    }

    // MARK: Engines

    private static func runCPU(quick: Bool, duration: Double?) {
        var configuration = quick ? CPUBenchmarkEngine.Configuration.quick : CPUBenchmarkEngine.Configuration()
        if let duration { configuration.durationSeconds = duration }
        print("CPU benchmark: \(configuration.threads) threads, \(configuration.multiCoreUnits) multi-core units, \(configuration.singleCoreUnits) single-core units")
        let engine = CPUBenchmarkEngine(configuration: configuration)
        var lastPhase = ""
        do {
            let result = try engine.run(isCancelled: { false }) { progress in
                if progress.phase.rawValue != lastPhase {
                    lastPhase = progress.phase.rawValue
                    print("  stage: \(lastPhase)")
                }
            }
            print(String(format: "  multi-core   %8.1f   (%.1f ms for %ld units on %ld threads)",
                         result.score, result.multiCoreBatchMs, result.multiCoreUnits, result.threadsUsed))
            print(String(format: "  single-core  %8.1f   (%.1f ms for %ld units)",
                         result.singleCoreScore, result.singleCoreBatchMs, result.singleCoreUnits))
            if let sustained = result.sustainedScore {
                print(String(format: "  sustained    %8.1f", sustained))
            }
            print("  verified: \(result.resultsVerified) (wrong results: \(result.wrongResults))   throttled: \(result.thermalThrottleDetected)")
            if result.wrongResults > 0 { exit(2) }
        } catch {
            print("failed: \(error)")
            exit(1)
        }
    }

    private static func runMemory(quick: Bool) {
        let size = quick ? 128 * 1024 * 1024 : MemoryTestEngine.recommendedSize()
        let engine = MemoryTestEngine(sizeBytes: size)
        do {
            let r = try engine.run(isCancelled: { false }) { _ in }
            print(String(format: "memory  %ld MiB tested  write %.0f MB/s  read %.0f MB/s  errors %ld  passed %@  (%.2f s)",
                         r.testedMiB, r.averageWriteThroughputMBps, r.averageReadThroughputMBps, r.errorsDetected,
                         r.passed ? "yes" : "NO", r.durationSeconds))
            if !r.passed { exit(2) }
        } catch {
            print("failed: \(error)")
            exit(1)
        }
    }

    private static func runGPU(duration: Double) {
        do {
            var configuration = GPURaymarchBenchmark.Configuration()
            configuration.totalSeconds = duration
            let benchmark = try GPURaymarchBenchmark(configuration: configuration)
            print("GPU benchmark on \(benchmark.device.name): \(benchmark.internalWidth)×\(benchmark.internalHeight) internal")
            var lastStage = ""
            let r = try benchmark.run(isCancelled: { false }) { progress in
                let stage = "\(progress.stage)"
                let name = String(stage.prefix { $0 != "(" })
                if name != lastStage {
                    lastStage = name
                    print("  stage: \(name)")
                }
            }
            print(String(format: "  score        %8.1f   (%.2f ms/frame, %.2f fps; slowest %.2f fps, fastest %.2f fps)",
                         r.score, r.averageFrameTimeMs, r.averageFps, r.minFps, r.maxFps))
            if let sustained = r.sustainedScore, let ms = r.sustainedAverageFrameTimeMs {
                print(String(format: "  sustained    %8.1f   (%.2f ms/frame)", sustained, ms))
            }
        } catch {
            print("failed: \(error)")
            exit(1)
        }
    }

    private static func runSSD(path: String, quick: Bool) {
        let engine = StorageBenchmarkEngine(profile: quick ? .quick : .standard)
        print("SSD speed test in \(path) (\(engine.profile.name))")
        do {
            let r = try engine.run(in: URL(fileURLWithPath: path), isCancelled: { false }) { _ in }
            for m in r.measurements {
                print(String(format: "  %@ %@  %9.1f MB/s  %9.1f MiB/s  %10.0f IOPS  %7.3f ms",
                             m.kind.label.padding(toLength: 11, withPad: " ", startingAt: 0),
                             m.operation.rawValue.padding(toLength: 5, withPad: " ", startingAt: 0),
                             m.mbPerSecond, m.mibPerSecond, m.iops, m.averageLatencyMs))
            }
        } catch let failure as StorageBenchmarkFailure {
            print("failed: \(failure.message)")
            exit(1)
        } catch {
            print("failed: \(error)")
            exit(1)
        }
    }
}
#endif
