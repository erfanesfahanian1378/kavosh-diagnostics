import XCTest
@testable import KavoshCore

final class CPUWorkloadTests: XCTestCase {
    func testIntegerChecksumsMatchTable() {
        let workspace = CPUWorkspace()
        for unit in [0, 1, 37, 128, 255, 256 + 3] {
            XCTAssertEqual(CPUWorkload.integerChecksum(unit: unit, workspace: workspace),
                           CPUWorkload.expectedIntegerChecksum(unit: unit), "unit \(unit)")
        }
    }

    func testPrimeCountMatchesKnownValue() {
        // Checked independently with a plain sieve.
        let workspace = CPUWorkspace()
        let lo = CPUWorkload.primeWindowStart
        XCTAssertEqual(CPUWorkload.countPrimes(lo: lo, hi: lo + CPUWorkload.primeWindowSize, workspace: workspace), 347_566)
    }

    func testSmallPrimeWindow() {
        // Primes between 100 and 200: 21 of them.
        let workspace = CPUWorkspace()
        XCTAssertEqual(CPUWorkload.countPrimes(lo: 100, hi: 200, workspace: workspace), 21)
    }

    func testUnitVerifies() {
        let workspace = CPUWorkspace()
        let reference = CPUWorkload.mandelbrot()
        XCTAssertTrue(CPUWorkload.runUnit(5, workspace: workspace, floatReference: reference))
        XCTAssertFalse(CPUWorkload.runUnit(5, workspace: workspace, floatReference: reference &+ 1))
    }

    func testQuickEngineRun() throws {
        var config = CPUBenchmarkEngine.Configuration.quick
        config.multiCoreUnits = 16
        config.singleCoreUnits = 2
        let engine = CPUBenchmarkEngine(configuration: config)
        var phases: Set<String> = []
        let result = try engine.run(isCancelled: { false }) { phases.insert($0.phase.rawValue) }
        XCTAssertGreaterThan(result.score, 0)
        XCTAssertGreaterThan(result.singleCoreScore, 0)
        XCTAssertEqual(result.wrongResults, 0)
        XCTAssertNil(result.sustainedScore)
        XCTAssertTrue(phases.contains("multiCore"))
        XCTAssertTrue(phases.contains("singleCore"))
        XCTAssertTrue(phases.contains("finished"))
    }

    func testEngineCancels() {
        let engine = CPUBenchmarkEngine(configuration: .init(multiCoreUnits: 10_000, singleCoreUnits: 10, durationSeconds: 0))
        let start = Date()
        XCTAssertThrowsError(try engine.run(isCancelled: { Date().timeIntervalSince(start) > 0.3 }) { _ in })
        XCTAssertLessThan(Date().timeIntervalSince(start), 5)
    }

    func testScoreFormulas() {
        let refMs = CPUBenchmarkEngine.referenceMultiCoreMs(units: 960)
        XCTAssertEqual(CPUBenchmarkEngine.multiCoreScore(units: 960, milliseconds: refMs), 1000, accuracy: 0.001)
        XCTAssertEqual(CPUBenchmarkEngine.multiCoreScore(units: 960, milliseconds: refMs / 2), 2000, accuracy: 0.001)
        XCTAssertEqual(CPUBenchmarkEngine.singleCoreScore(units: 10, milliseconds: CPUBenchmarkEngine.referenceSingleCoreMs * 10),
                       100, accuracy: 0.001)
    }
}

final class MemoryTestTests: XCTestCase {
    func testSmallRunHasNoErrors() throws {
        let engine = MemoryTestEngine(sizeBytes: 16 * 1024 * 1024, threads: 2)
        let result = try engine.run(isCancelled: { false }) { _ in }
        XCTAssertEqual(result.errorsDetected, 0)
        XCTAssertTrue(result.passed)
        XCTAssertEqual(result.passes, MemoryTestEngine.defaultPatterns.count)
        XCTAssertGreaterThan(result.averageReadThroughputMBps, 0)
        XCTAssertGreaterThan(result.averageWriteThroughputMBps, 0)
    }

    func testRecommendedSize() {
        let gib: UInt64 = 1 << 30
        XCTAssertEqual(MemoryTestEngine.recommendedSize(physicalMemory: 8 * gib), Int(gib))
        XCTAssertEqual(MemoryTestEngine.recommendedSize(physicalMemory: 64 * gib), 2 * Int(gib))
        XCTAssertEqual(MemoryTestEngine.recommendedSize(physicalMemory: 1 * gib), 256 * 1024 * 1024)
    }
}

final class StorageBenchmarkTests: XCTestCase {
    func testTinyRunProducesAllMeasurements() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("kavosh-test-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let profile = StorageTestProfile(name: "test", runs: 1, secondsPerRun: 0.3, fileSizeBytes: 16 << 20, settleSeconds: 0)
        let engine = StorageBenchmarkEngine(profile: profile)
        let result = try engine.run(in: folder, isCancelled: { false }) { _ in }
        XCTAssertEqual(result.measurements.count, 6)
        for m in result.measurements {
            XCTAssertGreaterThan(m.mbPerSecond, 0, "\(m.kind) \(m.operation)")
            XCTAssertGreaterThan(m.iops, 0)
        }
        // The temporary file is removed.
        let leftovers = try FileManager.default.contentsOfDirectory(atPath: folder.path)
        XCTAssertTrue(leftovers.isEmpty)
    }

    func testMissingFolder() {
        let engine = StorageBenchmarkEngine(profile: .quick)
        XCTAssertEqual(engine.checkTarget(URL(fileURLWithPath: "/definitely/not/here")), .notFound)
    }
}

final class AudioAnalysisTests: XCTestCase {
    func sine(_ frequency: Double, sampleRate: Double = 48_000, count: Int = 4096, amplitude: Float = 0.5) -> [Float] {
        (0..<count).map { amplitude * Float(sin(2 * Double.pi * frequency * Double($0) / sampleRate)) }
    }

    func testPitchOfSine() {
        for f in [110.0, 220.0, 440.0, 880.0] {
            let pitch = AudioAnalysis.estimatePitch(sine(f), sampleRate: 48_000)
            XCTAssertNotNil(pitch)
            XCTAssertEqual(pitch ?? 0, f, accuracy: f * 0.02, "frequency \(f)")
        }
    }

    func testSilenceHasNoPitch() {
        XCTAssertNil(AudioAnalysis.estimatePitch([Float](repeating: 0, count: 4096), sampleRate: 48_000))
    }

    func testLevels() {
        XCTAssertEqual(AudioAnalysis.rmsDecibels(sine(440, amplitude: 1)), -3.01, accuracy: 0.1)
        XCTAssertEqual(AudioAnalysis.meterLevel(decibels: 0), 1)
        XCTAssertEqual(AudioAnalysis.meterLevel(decibels: -60), 0)
        XCTAssertEqual(AudioAnalysis.noteName(for: 440), "A4")
        XCTAssertEqual(AudioAnalysis.noteName(for: 261.63), "C4")
    }
}

final class DriveHealthTests: XCTestCase {
    func makeLog(warning: UInt8 = 0, spare: UInt8 = 100, used: UInt8 = 3, mediaErrors: UInt8 = 0) -> [UInt8] {
        var b = [UInt8](repeating: 0, count: 512)
        b[0] = warning
        b[1] = 0x3A; b[2] = 0x01           // 314 K = ~41 °C
        b[3] = spare
        b[4] = 10
        b[5] = used
        b[48] = 0x40; b[49] = 0x42; b[50] = 0x0F  // 1,000,000 units written = 512 GB
        b[128] = 0xE8; b[129] = 0x03       // 1,000 power-on hours
        b[160] = mediaErrors
        return b
    }

    let device = StorageDeviceInfo(bsdName: "disk0", model: "APPLE SSD AP0512Z", sizeBytes: 500_000_000_000,
                                   medium: "Solid State", interconnect: "Apple Fabric", location: "Internal",
                                   nvmeSmartCapable: true)

    func testParsing() throws {
        let log = try XCTUnwrap(NVMeSmartLog(bytes: makeLog()))
        XCTAssertEqual(log.percentageUsed, 3)
        XCTAssertEqual(log.powerOnHours, 1000)
        XCTAssertEqual(log.terabytesWritten, 0.512, accuracy: 0.0001)
        XCTAssertEqual(log.temperatureCelsius ?? 0, 40.85, accuracy: 0.01)
        XCTAssertNil(NVMeSmartLog(bytes: [UInt8](repeating: 0, count: 512)))
    }

    func testHealthyDrive() {
        let info = DriveHealthEvaluator.evaluate(device: device, smart: NVMeSmartLog(bytes: makeLog()), macOSStatus: "Verified")
        XCTAssertEqual(info.status, .good)
        XCTAssertEqual(info.score, 100)
        XCTAssertTrue(info.issues.isEmpty)
        XCTAssertEqual(info.lifeLeftPercent, 97)
    }

    func testFailingDrive() {
        let info = DriveHealthEvaluator.evaluate(device: device, smart: NVMeSmartLog(bytes: makeLog(warning: 0x05, mediaErrors: 2)),
                                                 macOSStatus: "Verified")
        XCTAssertEqual(info.status, .failing)
        XCTAssertTrue(info.issues.contains(.spareBelowThreshold))
        XCTAssertTrue(info.issues.contains(.reliabilityDegraded))
        XCTAssertTrue(info.issues.contains(.mediaErrors(2)))
    }

    func testNoData() {
        let info = DriveHealthEvaluator.evaluate(device: device, smart: nil, macOSStatus: "Not Supported")
        XCTAssertEqual(info.status, .unknown)
        XCTAssertNil(info.score)
    }

    func testUniqueDrives() {
        var small = device
        small.bsdName = "disk1"
        small.sizeBytes = 500_000_000
        let usb = StorageDeviceInfo(bsdName: "disk4", model: "Portable SSD T7", vendor: "Samsung", sizeBytes: 1_000_000_000_000,
                                    interconnect: "USB", location: "External")
        let image = StorageDeviceInfo(bsdName: "disk5", model: "Disk Image", sizeBytes: 1, interconnect: "Virtual Interface")
        let physical = [small, device, usb, image].filter(DriveHealthEvaluator.isPhysical)
        let unique = DriveHealthEvaluator.uniquePhysicalDrives(physical)
        XCTAssertEqual(unique.map(\.bsdName), ["disk0", "disk4"])
        XCTAssertEqual(usb.displayName, "Samsung Portable SSD T7")
    }
}

func perfectReport() -> FinalReport {
        var report = FinalReport(plan: TestModule.fullRun)
        report.keyboard = KeyboardTestResult(sentenceCorrect: true, totalKeys: 70, pressedKeys: 70,
                                             trackpadMoved: true, trackpadClicked: true, trackpadScrolled: true)
        report.display = DisplayTestResult(issues: [], measuredRefreshRateHz: 120)
        report.audio = AudioTestResult(leftHeard: true, rightHeard: true)
        report.micCamera = MicCameraTestResult(cameraPresent: true, cameraWorks: true, microphonePresent: true, microphoneWorks: true)
        report.battery = BatteryHealthResult(batteryPresent: true, designCapacityMah: 6000, fullChargeCapacityMah: 5900,
                                             healthPercent: 98.3, cycleCount: 40, currentChargePercent: 80)
        report.memory = MemoryTestResult(testedBytes: 1 << 30, passes: 6, averageWriteThroughputMBps: 50_000,
                                         averageReadThroughputMBps: 60_000, errorsDetected: 0, durationSeconds: 3)
        report.drives = DriveHealthReport(drives: [DriveHealthInfo(id: "disk0", model: "SSD", sizeBytes: 1, status: .good, score: 100)])
        return report
}

final class ScoringTests: XCTestCase {

    func testPerfect() {
        let evaluation = BenchmarkScoring.evaluate(perfectReport())
        XCTAssertEqual(evaluation.grade, .aPlus)
        XCTAssertEqual(evaluation.score ?? 0, 100, accuracy: 0.01)
        XCTAssertEqual(evaluation.totalIssues, 0)
    }

    func testWornBatteryLowersGrade() {
        var report = perfectReport()
        report.battery?.healthPercent = 75
        let evaluation = BenchmarkScoring.evaluate(report)
        XCTAssertEqual(evaluation.batteryPenaltySteps, 1)
        XCTAssertNotNil(evaluation.grade)
        XCTAssertNotEqual(evaluation.grade, evaluation.gradeFromScore)
    }

    func testMemoryErrorsCapGrade() {
        var report = perfectReport()
        report.memory?.errorsDetected = 3
        let evaluation = BenchmarkScoring.evaluate(report)
        XCTAssertEqual(evaluation.grade, .d)
        XCTAssertTrue(evaluation.cappedBy.contains(.memory))
    }

    func testBrokenKeyboardPullsScoreDown() {
        var report = perfectReport()
        report.keyboard?.missingKeys = ["A", "S", "D", "F", "G", "H", "J", "K"]
        report.keyboard?.trackpadClicked = false
        let evaluation = BenchmarkScoring.evaluate(report)
        XCTAssertLessThan(evaluation.score ?? 100, 92)
    }

    func testNoHardwareChecks() {
        var report = FinalReport(plan: TestModule.performanceRun)
        report.gpu = nil
        let evaluation = BenchmarkScoring.evaluate(report)
        XCTAssertNil(evaluation.grade)
    }

    func testGradeHelpers() {
        XCTAssertEqual(DeviceGrade.aPlus.lowered(by: 2), .b)
        XCTAssertEqual(DeviceGrade.f.lowered(by: 3), .f)
        XCTAssertEqual(DeviceGrade.a.cappedAt(.d), .d)
        XCTAssertEqual(DeviceGrade.f.cappedAt(.d), .f)
        XCTAssertEqual(DeviceGrade(score: 97), .aPlus)
        XCTAssertEqual(DeviceGrade(score: 59.9), .f)
    }

    func testBatteryIssues() {
        let issues = BatteryHealthResult.evaluateIssues(healthPercent: 78, cycleCount: 1200, isPluggedIn: true,
                                                        isCharging: false, chargePercent: 40, fullyCharged: false)
        XCTAssertTrue(issues.contains(.healthCritical(78)))
        XCTAssertTrue(issues.contains(.highCycleCount(1200)))
        XCTAssertTrue(issues.contains(.notCharging))
        // Optimized charging holding at 80% is fine.
        XCTAssertTrue(BatteryHealthResult.evaluateIssues(healthPercent: 99, cycleCount: 10, isPluggedIn: true, isCharging: false,
                                                         chargePercent: 80, fullyCharged: false).isEmpty)
    }
}

final class MiscTests: XCTestCase {
    func testCSV() {
        XCTAssertEqual(CSVExport.escape("plain"), "plain")
        XCTAssertEqual(CSVExport.escape("a,b"), "\"a,b\"")
        XCTAssertEqual(CSVExport.escape("say \"hi\""), "\"say \"\"hi\"\"\"")
        let report = perfectReport()
        let row = CSVExport.row(report: report, evaluation: BenchmarkScoring.evaluate(report), storage: nil)
        // Count fields while respecting quotes.
        var fields = 1
        var quoted = false
        for c in row {
            if c == "\"" { quoted.toggle() }
            if c == "," && !quoted { fields += 1 }
        }
        XCTAssertEqual(fields, CSVExport.header.count)
    }

    func testKeyChatter() {
        var tracker = KeyPressTracker()
        tracker.keyDown(code: 0, timestamp: 1.000, isRepeat: false)
        tracker.keyDown(code: 0, timestamp: 1.010, isRepeat: false)   // 10 ms later: chatter
        tracker.keyDown(code: 1, timestamp: 2.000, isRepeat: false)
        tracker.keyDown(code: 1, timestamp: 2.300, isRepeat: false)   // normal double-tap
        tracker.keyDown(code: 2, timestamp: 3.000, isRepeat: false)
        tracker.keyDown(code: 2, timestamp: 3.005, isRepeat: true)    // auto-repeat ignored
        XCTAssertEqual(tracker.doublePressed, [0])
        XCTAssertTrue(tracker.isPressed(1))
        XCTAssertEqual(tracker.lastKeyLabel, "D")
        let result = tracker.result(sentenceCorrect: true, trackpadMoved: true, trackpadClicked: true, trackpadScrolled: false)
        XCTAssertEqual(result.doublePressedKeys, ["A"])
        XCTAssertEqual(result.trackpadChecksPassed, 2)
        XCTAssertFalse(result.missingKeys.isEmpty)
    }

    func testKeyboardLayout() {
        let codes = MacKeyboardLayout.allKeys.map(\.code)
        XCTAssertEqual(codes.count, Set(codes).count, "duplicate key codes")
        XCTAssertEqual(MacKeyboardLayout.key(for: 49)?.label, "space")
    }

    func testTypingPrompt() {
        XCTAssertTrue(TypingPrompt.matches("  the quick brown fox jumps over the lazy dog 1234567890 "))
        XCTAssertFalse(TypingPrompt.matches("the quick brown fox"))
        XCTAssertEqual(TypingPrompt.mismatchedIndices("Tha"), [2])
    }

    func testFormatting() {
        XCTAssertEqual(Fmt.number(1234.5, decimals: 1), "1,234.5")
        XCTAssertEqual(Fmt.storageSize(512_110_190_592), "512 GB")
        XCTAssertEqual(Fmt.storageSize(2_000_398_934_016), "2.00 TB")
        XCTAssertEqual(Fmt.duration(65), "1:05")
        XCTAssertEqual(Fmt.memoryGB(16), "16 GB")
    }

    func testLocalization() {
        Loc.language = .fa
        XCTAssertEqual(T("Hello", "سلام"), "سلام")
        Loc.language = .en
        XCTAssertEqual(T("Hello", "سلام"), "Hello")
    }

    func testSettingsDecodeTolerant() throws {
        let data = Data(#"{"language":"fa","washerRotate":false}"#.utf8)
        let settings = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertEqual(settings.language, .fa)
        XCTAssertFalse(settings.washerRotate)
        XCTAssertEqual(settings.performanceTestSeconds, 60)
    }

    func testReportRoundTrip() throws {
        var report = perfectReport()
        report.battery?.issues = [.highCycleCount(1100), .notCharging]
        report.drives?.drives[0].issues = [.mediaErrors(4)]
        let data = try JSONEncoder().encode(report)
        let back = try JSONDecoder().decode(FinalReport.self, from: data)
        XCTAssertEqual(back, report)
    }

    func testGpuResultMath() {
        let r = GpuBenchmarkResult.make(frameTimesMs: [10, 10, 20, 10], sustainedMeanMs: 20, totalFrames: 40, seconds: 1,
                                        adapterName: "Test", outputWidth: 2560, outputHeight: 1440, renderWidth: 5120,
                                        renderHeight: 2880, samplesPerPixel: 4, referenceFrameMs: 12.5, scoringVersion: 1)
        XCTAssertEqual(r.averageFrameTimeMs, 12.5, accuracy: 0.001)
        XCTAssertEqual(r.score, 1000, accuracy: 0.001)
        XCTAssertEqual(r.sustainedScore ?? 0, 625, accuracy: 0.001)
        XCTAssertEqual(r.minFps, 50, accuracy: 0.001)
        XCTAssertEqual(r.maxFps, 100, accuracy: 0.001)
        XCTAssertEqual(r.onePercentLowFps, 50, accuracy: 0.001)
    }
}
