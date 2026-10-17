import Foundation

public struct MemoryTestProgress: Sendable {
    public enum Phase: String, Sendable { case allocating, writing, verifying, finished }

    public var phase: Phase
    public var pass: Int
    public var passes: Int
    public var pattern: UInt64
    public var fraction: Double
    public var writeMBps: Double?
    public var readMBps: Double?
    public var errors: Int
    public var testedBytes: Int
}

public enum MemoryTestError: Error {
    case cancelled
    case allocationFailed
}

/// Writes patterns into a large block of RAM, reads them back and counts any bit that
/// came back different. Also reports how fast memory could be written and read.
public final class MemoryTestEngine {
    public enum Pattern: Sendable, Equatable {
        case constant(UInt64)
        case random(seed: UInt64)
        case address

        /// Hex label shown while running.
        public var label: String {
            switch self {
            case .constant(let v): return String(format: "%016llX", v)
            case .random: return "RANDOM"
            case .address: return "ADDRESS"
            }
        }

        var countsForThroughput: Bool {
            if case .constant = self { return true }
            return false
        }

        @inline(__always)
        func value(at index: Int) -> UInt64 {
            switch self {
            case .constant(let v): return v
            case .random(let seed): return SplitMix64.mix(seed ^ UInt64(index))
            case .address: return UInt64(index) &* 0x9E37_79B9_7F4A_7C15 ^ 0xA5A5_A5A5_5A5A_5A5A
            }
        }
    }

    public static let defaultPatterns: [Pattern] = [
        .constant(0x0000_0000_0000_0000),
        .constant(0xFFFF_FFFF_FFFF_FFFF),
        .constant(0xAAAA_AAAA_AAAA_AAAA),
        .constant(0x5555_5555_5555_5555),
        .random(seed: 0x6B61_766F_7368_2121),
        .address
    ]

    public let sizeBytes: Int
    public let threads: Int
    public let patterns: [Pattern]

    public init(sizeBytes: Int, threads: Int = ProcessInfo.processInfo.activeProcessorCount,
                patterns: [Pattern] = MemoryTestEngine.defaultPatterns) {
        self.sizeBytes = max(8 * 1024 * 1024, sizeBytes)
        self.threads = max(1, threads)
        self.patterns = patterns
    }

    /// A size that stays well clear of memory pressure: 1/8 of RAM, between 256 MiB and 2 GiB.
    public static func recommendedSize(physicalMemory: UInt64 = ProcessInfo.processInfo.physicalMemory,
                                       underPressure: Bool = false) -> Int {
        let mib = 1024 * 1024
        var size = Int(physicalMemory / 8)
        if underPressure { size /= 2 }
        size = max(256 * mib, min(2048 * mib, size))
        return size - size % (64 * mib)
    }

    public func run(isCancelled: @escaping () -> Bool,
                    progress: @escaping (MemoryTestProgress) -> Void) throws -> MemoryTestResult {
        let started = Date()
        let words = sizeBytes / 8
        var current = MemoryTestProgress(phase: .allocating, pass: 0, passes: patterns.count,
                                         pattern: 0, fraction: 0, writeMBps: nil, readMBps: nil,
                                         errors: 0, testedBytes: words * 8)
        progress(current)

        guard let raw = malloc(words * 8) else { throw MemoryTestError.allocationFailed }
        defer { free(raw) }
        let memory = raw.bindMemory(to: UInt64.self, capacity: words)

        var writeRates: [Double] = []
        var readRates: [Double] = []
        let errorCounter = LockedCounter()
        let chunkCount = threads * 4
        let chunkWords = (words + chunkCount - 1) / chunkCount

        for (passIndex, pattern) in patterns.enumerated() {
            if isCancelled() { throw MemoryTestError.cancelled }
            let patternValue = pattern.value(at: 0)

            // Write.
            current.phase = .writing
            current.pass = passIndex + 1
            current.pattern = patternValue
            current.fraction = Double(passIndex * 2) / Double(patterns.count * 2)
            progress(current)
            let writeStart = Date()
            DispatchQueue.concurrentPerform(iterations: chunkCount) { chunk in
                let lo = chunk * chunkWords
                let hi = min(words, lo + chunkWords)
                guard lo < hi else { return }
                switch pattern {
                case .constant(let v):
                    (memory + lo).update(repeating: v, count: hi - lo)
                default:
                    var i = lo
                    while i < hi {
                        memory[i] = pattern.value(at: i)
                        i += 1
                    }
                }
            }
            let writeSeconds = Date().timeIntervalSince(writeStart)
            if pattern.countsForThroughput && writeSeconds > 0 {
                writeRates.append(Double(words * 8) / writeSeconds / 1_000_000)
                current.writeMBps = writeRates.reduce(0, +) / Double(writeRates.count)
            }
            if isCancelled() { throw MemoryTestError.cancelled }

            // Verify.
            current.phase = .verifying
            current.fraction = Double(passIndex * 2 + 1) / Double(patterns.count * 2)
            progress(current)
            let readStart = Date()
            DispatchQueue.concurrentPerform(iterations: chunkCount) { chunk in
                let lo = chunk * chunkWords
                let hi = min(words, lo + chunkWords)
                guard lo < hi else { return }
                var errors = 0
                switch pattern {
                case .constant(let v):
                    // Accumulate differences without branching, then look closer only if needed.
                    var diff: UInt64 = 0
                    var i = lo
                    while i < hi {
                        diff |= memory[i] ^ v
                        i += 1
                    }
                    if diff != 0 {
                        var j = lo
                        while j < hi {
                            if memory[j] != v { errors += 1 }
                            j += 1
                        }
                    }
                default:
                    var i = lo
                    while i < hi {
                        if memory[i] != pattern.value(at: i) { errors += 1 }
                        i += 1
                    }
                }
                if errors > 0 { errorCounter.add(errors) }
            }
            let readSeconds = Date().timeIntervalSince(readStart)
            if pattern.countsForThroughput && readSeconds > 0 {
                readRates.append(Double(words * 8) / readSeconds / 1_000_000)
                current.readMBps = readRates.reduce(0, +) / Double(readRates.count)
            }
            current.errors = errorCounter.value
        }

        current.phase = .finished
        current.fraction = 1
        progress(current)

        return MemoryTestResult(
            testedBytes: words * 8,
            passes: patterns.count,
            averageWriteThroughputMBps: writeRates.isEmpty ? 0 : writeRates.reduce(0, +) / Double(writeRates.count),
            averageReadThroughputMBps: readRates.isEmpty ? 0 : readRates.reduce(0, +) / Double(readRates.count),
            errorsDetected: errorCounter.value,
            durationSeconds: Date().timeIntervalSince(started)
        )
    }
}
