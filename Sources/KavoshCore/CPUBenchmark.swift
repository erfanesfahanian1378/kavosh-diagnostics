import Foundation

// MARK: - Workload

/// One "unit" of CPU work. Every unit does the same kinds of work with different inputs:
///
///  1. counts the primes in an 8-million-number window (segmented sieve, integer + memory),
///  2. mixes a 256 KB buffer with 64-bit hashing (integer ALU),
///  3. sorts 96k pseudo-random numbers (branches, memory),
///  4. renders a small Mandelbrot grid (floating point).
///
/// Parts 1–3 are pure integer math, so their combined checksum is known in advance on
/// every Mac (see `CPUWorkloadTable`). Part 4 is compared to a reference computed before
/// the timed run. Any mismatch means the CPU returned a wrong answer.
public final class CPUWorkspace {
    static let segmentSize = 1 << 18          // odd numbers per sieve segment (256 KB of flags)
    static let hashWords = 1 << 15            // 32k × 8 bytes = 256 KB
    static let sortCount = 96 * 1024

    let sieve: UnsafeMutablePointer<UInt8>
    let hash: UnsafeMutablePointer<UInt64>
    var sortScratch: [UInt32]

    public init() {
        sieve = UnsafeMutablePointer<UInt8>.allocate(capacity: CPUWorkspace.segmentSize)
        hash = UnsafeMutablePointer<UInt64>.allocate(capacity: CPUWorkspace.hashWords)
        sortScratch = [UInt32](repeating: 0, count: CPUWorkspace.sortCount)
    }

    deinit {
        sieve.deallocate()
        hash.deallocate()
    }
}

public enum CPUWorkload {
    /// Bump when the work or the scoring changes, so old and new scores aren't mixed up.
    public static let scoringVersion = 1

    public static let distinctUnits = 256
    public static let primeWindowStart: UInt64 = 10_000_000_000
    public static let primeWindowSize: UInt64 = 8_000_000

    /// Primes up to sqrt of the largest number any unit sieves.
    static let basePrimes: [UInt32] = {
        let maxValue = primeWindowStart + primeWindowSize * UInt64(distinctUnits)
        let limit = Int(Double(maxValue).squareRoot()) + 2
        var composite = [Bool](repeating: false, count: limit + 1)
        var primes: [UInt32] = []
        var i = 2
        while i <= limit {
            if !composite[i] {
                primes.append(UInt32(i))
                var j = i * i
                while j <= limit {
                    composite[j] = true
                    j += i
                }
            }
            i += 1
        }
        return primes
    }()

    /// Counts primes in [lo, hi) with an odd-only segmented sieve.
    static func countPrimes(lo: UInt64, hi: UInt64, workspace: CPUWorkspace) -> UInt64 {
        precondition(lo > 2 && lo % 2 == 0)
        let seg = workspace.sieve
        let segOdds = UInt64(CPUWorkspace.segmentSize)
        var count: UInt64 = 0
        var segLo = lo // even
        while segLo < hi {
            let segHi = min(hi, segLo + segOdds * 2)
            // index i represents the odd number segLo + 2*i + 1
            let n = Int((segHi - segLo) / 2)
            seg.initialize(repeating: 1, count: n)
            for p32 in basePrimes.dropFirst() { // skip 2
                let p = UInt64(p32)
                if p * p >= segHi { break }
                // first odd multiple of p that is >= segLo + 1 and >= p*p
                var start = max(p * p, ((segLo + 1 + p - 1) / p) * p)
                if start % 2 == 0 { start += p }
                var idx = Int((start - segLo - 1) / 2)
                let step = Int(p)
                while idx < n {
                    seg[idx] = 0
                    idx += step
                }
            }
            for i in 0..<n {
                count &+= UInt64(seg[i])
            }
            segLo = segHi
        }
        return count
    }

    static func hashMix(seed: UInt64, workspace: CPUWorkspace) -> UInt64 {
        let buffer = workspace.hash
        let count = CPUWorkspace.hashWords
        let mask = count - 1
        var rng = SplitMix64(seed: seed)
        for i in 0..<count { buffer[i] = rng.next() }
        var acc = seed
        for round in 0..<160 {
            var prev = buffer[mask]
            for i in 0..<count {
                let j = (i &* 7 &+ round &+ 1) & mask
                var x = buffer[i] ^ buffer[j]
                x = (x << 23) | (x >> 41)
                x = x &* 0x9E37_79B9_7F4A_7C15 &+ prev
                buffer[i] = x
                prev = x
            }
            acc ^= prev &+ UInt64(round)
        }
        return acc
    }

    static func sortChecksum(seed: UInt64, workspace: CPUWorkspace) -> UInt64 {
        var rng = SplitMix64(seed: seed)
        let count = CPUWorkspace.sortCount
        for i in 0..<count {
            workspace.sortScratch[i] = UInt32(truncatingIfNeeded: rng.next())
        }
        workspace.sortScratch.sort()
        var h: UInt64 = 0xCBF2_9CE4_8422_2325
        var i = 0
        while i < count {
            h = (h ^ UInt64(workspace.sortScratch[i])) &* 0x0000_0100_0000_01B3
            i += 8
        }
        return h
    }

    /// Floating point part. The same for every unit.
    public static func mandelbrot(width: Int = 192, height: Int = 128, maxIterations: Int = 300) -> UInt64 {
        var total: UInt64 = 0
        let w = Double(width)
        let h = Double(height)
        for py in 0..<height {
            let y0 = Double(py) / h * 2.4 - 1.2
            for px in 0..<width {
                let x0 = Double(px) / w * 3.2 - 2.2
                var x = 0.0
                var y = 0.0
                var i = 0
                while i < maxIterations {
                    let xx = x * x
                    let yy = y * y
                    if xx + yy > 4 { break }
                    let xt = xx - yy + x0
                    y = 2 * x * y + y0
                    x = xt
                    i += 1
                }
                total &+= UInt64(i) &* UInt64(px &+ 1)
            }
        }
        return total
    }

    /// The integer checksum for unit `index` (index is taken modulo `distinctUnits`).
    public static func integerChecksum(unit index: Int, workspace: CPUWorkspace) -> UInt64 {
        let k = index % distinctUnits
        let lo = primeWindowStart + UInt64(k) * primeWindowSize
        let primes = countPrimes(lo: lo, hi: lo + primeWindowSize, workspace: workspace)
        let seed = 0xC0FF_EE00_0000_0000 &+ UInt64(k)
        let mix = hashMix(seed: seed, workspace: workspace)
        let sorted = sortChecksum(seed: seed ^ 0x5DEE_CE66_D1CE_4E5B, workspace: workspace)
        return (primes &* 0x9E37_79B9_7F4A_7C15) ^ mix ^ (sorted << 1)
    }

    public static func expectedIntegerChecksum(unit index: Int) -> UInt64 {
        CPUWorkloadTable.expected[index % distinctUnits]
    }

    /// Runs one unit. Returns true when every part returned the right answer.
    @discardableResult
    public static func runUnit(_ index: Int, workspace: CPUWorkspace, floatReference: UInt64) -> Bool {
        let integer = integerChecksum(unit: index, workspace: workspace)
        let float = mandelbrot()
        return integer == expectedIntegerChecksum(unit: index) && float == floatReference
    }
}

// MARK: - Engine

public struct CpuBenchmarkProgress: Sendable {
    public enum Phase: String, Sendable {
        case preparing, multiCore, singleCore, sustained, finished
    }

    public var phase: Phase
    public var phaseFraction: Double
    public var overallFraction: Double
    public var elapsed: Double
    public var liveScore: Double?
    public var multiCoreScore: Double?
    public var singleCoreScore: Double?
    public var sustainedSamples: [Double]
    public var wrongResults: Int
}

public enum CPUBenchmarkError: Error {
    case cancelled
}

public final class CPUBenchmarkEngine {
    public struct Configuration: Sendable {
        public var multiCoreUnits: Int
        public var singleCoreUnits: Int
        /// Total test time including the sustained phase. The fixed phases always run fully.
        public var durationSeconds: Double
        public var threads: Int
        public var sustainedWindowSeconds: Double

        public init(multiCoreUnits: Int = 960, singleCoreUnits: Int = 40, durationSeconds: Double = 60,
                    threads: Int = ProcessInfo.processInfo.activeProcessorCount, sustainedWindowSeconds: Double = 5) {
            self.multiCoreUnits = multiCoreUnits
            self.singleCoreUnits = singleCoreUnits
            self.durationSeconds = durationSeconds
            self.threads = max(1, threads)
            self.sustainedWindowSeconds = sustainedWindowSeconds
        }

        /// A short run for smoke tests.
        public static var quick: Configuration {
            Configuration(multiCoreUnits: 96, singleCoreUnits: 8, durationSeconds: 0)
        }
    }

    /// Reference machine (Apple M1, 4P + 4E cores) timings. It scores 1,000 multi-core
    /// and 100 single-core, and a Mac that does the same work in half the time scores double.
    public static let referenceName = "Apple M1 (8-core)"
    public static let referenceUnitMsSingle: Double = 22.0
    public static let referenceMultiCoreThroughput: Double = 5.2 // single-core units in parallel

    public static var referenceSingleCoreMs: Double { referenceUnitMsSingle }
    public static func referenceMultiCoreMs(units: Int) -> Double {
        referenceUnitMsSingle * Double(units) / referenceMultiCoreThroughput
    }

    public let configuration: Configuration

    public init(configuration: Configuration = Configuration()) {
        self.configuration = configuration
    }

    public static func multiCoreScore(units: Int, milliseconds: Double) -> Double {
        guard milliseconds > 0 else { return 0 }
        return 1000 * referenceMultiCoreMs(units: units) / milliseconds
    }

    public static func singleCoreScore(units: Int, milliseconds: Double) -> Double {
        guard milliseconds > 0, units > 0 else { return 0 }
        return 100 * referenceSingleCoreMs / (milliseconds / Double(units))
    }

    /// Runs the three stages. Blocks the calling thread; call it from a background queue.
    public func run(isCancelled: @escaping () -> Bool,
                    progress: @escaping (CpuBenchmarkProgress) -> Void) throws -> CpuBenchmarkResult {
        let config = configuration
        let started = Date()
        var current = CpuBenchmarkProgress(phase: .preparing, phaseFraction: 0, overallFraction: 0, elapsed: 0,
                                           liveScore: nil, multiCoreScore: nil, singleCoreScore: nil,
                                           sustainedSamples: [], wrongResults: 0)
        progress(current)

        // Warm up the base primes table and compute the floating-point reference.
        _ = CPUWorkload.basePrimes.count
        let floatReference = CPUWorkload.mandelbrot()
        if isCancelled() { throw CPUBenchmarkError.cancelled }

        // Rough time budget for the overall progress bar.
        let estimatedFixedSeconds = 6.0
        let total = max(config.durationSeconds, estimatedFixedSeconds)
        func overall(_ elapsed: Double) -> Double { min(0.99, elapsed / total) }

        // Stage 1: multi-core, fixed work.
        current.phase = .multiCore
        progress(current)
        let multi = ParallelRunner(threads: config.threads, floatReference: floatReference, unitOffset: 0)
        let multiStart = Date()
        try multi.run(totalUnits: config.multiCoreUnits, deadline: nil, isCancelled: isCancelled) { done, wrong in
            let elapsed = Date().timeIntervalSince(multiStart)
            current.phaseFraction = Double(done) / Double(config.multiCoreUnits)
            current.elapsed = Date().timeIntervalSince(started)
            current.overallFraction = overall(current.elapsed)
            current.wrongResults = wrong
            if done > 0 && elapsed > 0.3 {
                current.liveScore = CPUBenchmarkEngine.multiCoreScore(units: done, milliseconds: elapsed * 1000)
            }
            progress(current)
        }
        let multiMs = Date().timeIntervalSince(multiStart) * 1000
        let multiScore = CPUBenchmarkEngine.multiCoreScore(units: config.multiCoreUnits, milliseconds: multiMs)
        var wrongTotal = multi.wrongCount
        current.multiCoreScore = multiScore
        current.liveScore = nil

        // Stage 2: single-core, fixed work.
        current.phase = .singleCore
        current.phaseFraction = 0
        progress(current)
        let single = ParallelRunner(threads: 1, floatReference: floatReference, unitOffset: 17)
        let singleStart = Date()
        try single.run(totalUnits: config.singleCoreUnits, deadline: nil, isCancelled: isCancelled) { done, wrong in
            let elapsed = Date().timeIntervalSince(singleStart)
            current.phaseFraction = Double(done) / Double(max(1, config.singleCoreUnits))
            current.elapsed = Date().timeIntervalSince(started)
            current.overallFraction = overall(current.elapsed)
            current.wrongResults = wrongTotal + wrong
            if done > 0 {
                current.liveScore = CPUBenchmarkEngine.singleCoreScore(units: done, milliseconds: elapsed * 1000)
            }
            progress(current)
        }
        let singleMs = Date().timeIntervalSince(singleStart) * 1000
        let singleScore = CPUBenchmarkEngine.singleCoreScore(units: config.singleCoreUnits, milliseconds: singleMs)
        wrongTotal += single.wrongCount
        current.singleCoreScore = singleScore
        current.liveScore = nil

        // Stage 3: sustained load for the rest of the chosen duration.
        var samples: [Double] = []
        let remaining = config.durationSeconds - Date().timeIntervalSince(started)
        if remaining >= config.sustainedWindowSeconds * 2 {
            current.phase = .sustained
            current.phaseFraction = 0
            progress(current)
            let sustained = ParallelRunner(threads: config.threads, floatReference: floatReference, unitOffset: 101)
            let sustainedStart = Date()
            let deadline = sustainedStart.addingTimeInterval(remaining)
            var windowStart = sustainedStart
            var windowUnitsAtStart = 0
            try sustained.run(totalUnits: nil, deadline: deadline, isCancelled: isCancelled) { done, wrong in
                let now = Date()
                let windowElapsed = now.timeIntervalSince(windowStart)
                if windowElapsed >= config.sustainedWindowSeconds {
                    let units = done - windowUnitsAtStart
                    let score = CPUBenchmarkEngine.multiCoreScore(units: units, milliseconds: windowElapsed * 1000)
                    samples.append(score)
                    windowStart = now
                    windowUnitsAtStart = done
                    current.liveScore = score
                }
                current.sustainedSamples = samples
                current.phaseFraction = min(1, now.timeIntervalSince(sustainedStart) / remaining)
                current.elapsed = now.timeIntervalSince(started)
                current.overallFraction = overall(current.elapsed)
                current.wrongResults = wrongTotal + wrong
                progress(current)
            }
            wrongTotal += sustained.wrongCount
        }

        // Sustained = the average of the last three windows (the last ~15 seconds).
        var sustainedScore: Double?
        if !samples.isEmpty {
            let tail = samples.suffix(3)
            sustainedScore = tail.reduce(0, +) / Double(tail.count)
        }
        let throttled = (sustainedScore.map { $0 < multiScore * 0.90 }) ?? false

        current.phase = .finished
        current.phaseFraction = 1
        current.overallFraction = 1
        current.wrongResults = wrongTotal
        current.elapsed = Date().timeIntervalSince(started)
        progress(current)

        return CpuBenchmarkResult(
            score: multiScore,
            singleCoreScore: singleScore,
            sustainedScore: sustainedScore,
            sustainedSamples: samples,
            multiCoreBatchMs: multiMs,
            singleCoreBatchMs: singleMs,
            multiCoreUnits: config.multiCoreUnits,
            singleCoreUnits: config.singleCoreUnits,
            threadsUsed: config.threads,
            durationSeconds: Date().timeIntervalSince(started),
            wrongResults: wrongTotal,
            thermalThrottleDetected: throttled,
            scoringVersion: CPUWorkload.scoringVersion
        )
    }
}

/// Runs units on N threads, handing out unit numbers one at a time.
final class ParallelRunner {
    private let threads: Int
    private let floatReference: UInt64
    private let unitOffset: Int
    private let lock = NSLock()
    private var nextUnit = 0
    private var doneUnits = 0
    private var wrong = 0
    private var stop = false

    init(threads: Int, floatReference: UInt64, unitOffset: Int) {
        self.threads = threads
        self.floatReference = floatReference
        self.unitOffset = unitOffset
    }

    var wrongCount: Int {
        lock.lock()
        defer { lock.unlock() }
        return wrong
    }

    /// Blocks until `totalUnits` are done or `deadline` passes. `tick` is called on the
    /// calling thread about ten times a second with (units done, wrong results).
    func run(totalUnits: Int?, deadline: Date?, isCancelled: () -> Bool, tick: (Int, Int) -> Void) throws {
        let group = DispatchGroup()
        for _ in 0..<threads {
            group.enter()
            let thread = Thread { [self] in
                let workspace = CPUWorkspace()
                while true {
                    lock.lock()
                    if stop || (totalUnits != nil && nextUnit >= totalUnits!) {
                        lock.unlock()
                        break
                    }
                    let unit = nextUnit
                    nextUnit += 1
                    lock.unlock()

                    let ok = CPUWorkload.runUnit(unit + unitOffset, workspace: workspace, floatReference: floatReference)

                    lock.lock()
                    doneUnits += 1
                    if !ok { wrong += 1 }
                    lock.unlock()
                }
                group.leave()
            }
            thread.qualityOfService = .userInitiated
            thread.stackSize = 1 << 20
            thread.start()
        }

        var cancelled = false
        while group.wait(timeout: .now() + .milliseconds(100)) == .timedOut {
            let (done, w) = snapshot()
            tick(done, w)
            if !cancelled && isCancelled() {
                cancelled = true
                setStop()
            }
            if let deadline, Date() >= deadline {
                setStop()
            }
        }
        let (done, w) = snapshot()
        tick(done, w)
        if cancelled || isCancelled() { throw CPUBenchmarkError.cancelled }
    }

    private func snapshot() -> (Int, Int) {
        lock.lock()
        defer { lock.unlock() }
        return (doneUnits, wrong)
    }

    private func setStop() {
        lock.lock()
        stop = true
        lock.unlock()
    }
}
