import Foundation
#if canImport(Darwin)
import Darwin
#elseif canImport(Glibc)
import Glibc
#endif

public struct StorageTestProfile: Codable, Equatable, Sendable {
    public var name: String
    public var runs: Int
    public var secondsPerRun: Double
    public var fileSizeBytes: Int64
    public var settleSeconds: Double

    public init(name: String, runs: Int, secondsPerRun: Double, fileSizeBytes: Int64, settleSeconds: Double) {
        self.name = name
        self.runs = runs
        self.secondsPerRun = secondsPerRun
        self.fileSizeBytes = fileSizeBytes
        self.settleSeconds = settleSeconds
    }

    /// Same settings as CrystalDiskMark's default: 1 GiB, 5 runs of 5 seconds, best run wins.
    public static let standard = StorageTestProfile(name: "standard", runs: 5, secondsPerRun: 5,
                                                    fileSizeBytes: 1 << 30, settleSeconds: 2)
    /// Faster, a little less precise.
    public static let quick = StorageTestProfile(name: "quick", runs: 2, secondsPerRun: 3,
                                                 fileSizeBytes: 512 << 20, settleSeconds: 1)

    /// The test file plus some headroom.
    public var requiredFreeBytes: Int64 { fileSizeBytes + 300_000_000 }

    public var title: String {
        name == "quick" ? T("Quick", "سریع") : T("Standard", "استاندارد")
    }
}

public struct StorageBenchmarkProgress: Sendable {
    public enum Stage: Equatable, Sendable {
        case preparing
        case settling
        case running(kind: StorageTestKind, operation: StorageOperation, run: Int, of: Int)
        case cleaningUp
        case finished
    }

    public var stage: Stage
    public var overallFraction: Double
    public var liveMBps: Double?
    public var completed: [StorageTestMeasurement]
}

public enum StorageBenchmarkFailure: Error, Equatable, Sendable {
    case notEnoughSpace(neededBytes: Int64, availableBytes: Int64)
    case notWritable
    case writeProtected
    case noDirectIO
    case notFound
    case ioError(String)
    case cancelled

    public var message: String {
        switch self {
        case .notEnoughSpace(let needed, _):
            let gb = Fmt.fixed(Double(needed) / 1_000_000_000, 1)
            return T("There isn't enough free space on this drive. The test needs about \(gb) GB free for its temporary file — free some space, or pick a folder on another drive.",
                     "فضای خالی کافی روی این درایو نیست. تست برای فایل موقت حدود \(gb) گیگابایت فضای خالی لازم دارد — کمی فضا آزاد کنید یا پوشه‌ای روی درایو دیگری انتخاب کنید.")
        case .notWritable:
            return T("This folder can't be written to. Pick a folder you have write access to — for example one inside your home folder — and try again.",
                     "در این پوشه نمی‌توان نوشت. پوشه‌ای انتخاب کنید که اجازه نوشتن در آن را دارید — مثلاً داخل پوشه خانگی — و دوباره امتحان کنید.")
        case .writeProtected:
            return T("This drive is write-protected, so the test file can't be created on it.",
                     "این درایو محافظت‌شده در برابر نوشتن است، پس فایل تست روی آن ساخته نمی‌شود.")
        case .noDirectIO:
            return T("This drive doesn't support the direct, uncached access the test needs (some network shares and virtual drives don't). Pick a folder on a local drive.",
                     "این درایو دسترسی مستقیم و بدون کش را که تست لازم دارد پشتیبانی نمی‌کند (بعضی درایوهای شبکه و مجازی). پوشه‌ای روی یک درایو محلی انتخاب کنید.")
        case .notFound:
            return T("The folder couldn't be found, or its drive isn't ready. Check that the drive is connected and pick the folder again.",
                     "پوشه پیدا نشد یا درایو آن آماده نیست. اتصال درایو را بررسی کنید و دوباره پوشه را انتخاب کنید.")
        case .ioError(let detail):
            return T("A read or write failed during the test: \(detail)", "در حین تست، خواندن یا نوشتن ناموفق بود: \(detail)")
        case .cancelled:
            return T("The test was stopped. Nothing was recorded for it.", "تست متوقف شد. چیزی برای آن ثبت نشد.")
        }
    }
}

/// Measures a drive the way CrystalDiskMark does: a pre-filled test file, uncached direct
/// I/O, incompressible random data, several timed runs per test, best run reported.
public final class StorageBenchmarkEngine {
    public let profile: StorageTestProfile
    public static let tests: [StorageTestKind] = [.seq1mQ8T1, .rnd4kQ32T1, .rnd4kQ1T1]

    public init(profile: StorageTestProfile = .standard) {
        self.profile = profile
    }

    // MARK: Target checks

    public static func availableBytes(at folder: URL) -> Int64? {
        #if os(macOS)
        if let values = try? folder.resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey]),
           let important = values.volumeAvailableCapacityForImportantUsage {
            return important
        }
        #endif
        if let attrs = try? FileManager.default.attributesOfFileSystem(forPath: folder.path),
           let free = attrs[.systemFreeSize] as? NSNumber {
            return free.int64Value
        }
        return nil
    }

    public static func volumeName(for folder: URL) -> String? {
        #if os(macOS)
        if let values = try? folder.resourceValues(forKeys: [.volumeLocalizedNameKey]) {
            return values.volumeLocalizedName
        }
        #endif
        return nil
    }

    /// Returns a problem with the folder, or nil when it looks usable.
    public func checkTarget(_ folder: URL) -> StorageBenchmarkFailure? {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: folder.path, isDirectory: &isDirectory), isDirectory.boolValue else {
            return .notFound
        }
        if let available = StorageBenchmarkEngine.availableBytes(at: folder), available < profile.requiredFreeBytes {
            return .notEnoughSpace(neededBytes: profile.requiredFreeBytes, availableBytes: available)
        }
        if !FileManager.default.isWritableFile(atPath: folder.path) {
            return .notWritable
        }
        return nil
    }

    // MARK: Run

    public func run(in folder: URL, isCancelled: @escaping () -> Bool,
                    progress: @escaping (StorageBenchmarkProgress) -> Void) throws -> StorageBenchmarkResult {
        if let problem = checkTarget(folder) { throw problem }

        let path = folder.appendingPathComponent(".kavosh-speedtest-\(UUID().uuidString).tmp").path
        let fd = open(path, O_RDWR | O_CREAT | O_TRUNC, 0o600)
        if fd < 0 { throw StorageBenchmarkEngine.failure(forOpenErrno: errno) }
        defer {
            close(fd)
            unlink(path)
        }
        #if canImport(Darwin)
        if fcntl(fd, F_NOCACHE, 1) != 0 { throw StorageBenchmarkFailure.noDirectIO }
        #endif

        let totalRuns = StorageBenchmarkEngine.tests.count * 2 * profile.runs
        let prepareWeight = 0.08
        var completed: [StorageTestMeasurement] = []
        var report = StorageBenchmarkProgress(stage: .preparing, overallFraction: 0, liveMBps: nil, completed: [])
        progress(report)

        // 1. Fill the test file with incompressible data.
        try prepareFile(fd: fd, isCancelled: isCancelled) { fraction in
            report.overallFraction = fraction * prepareWeight
            progress(report)
        }

        var runsDone = 0
        for kind in StorageBenchmarkEngine.tests {
            for op in StorageOperation.allCases {
                report.stage = .settling
                report.liveMBps = nil
                progress(report)
                try settle(isCancelled: isCancelled)

                var best: StorageTestMeasurement?
                for run in 1...profile.runs {
                    if isCancelled() { throw StorageBenchmarkFailure.cancelled }
                    report.stage = .running(kind: kind, operation: op, run: run, of: profile.runs)
                    progress(report)
                    let base = Double(runsDone)
                    let measurement = try measure(kind: kind, operation: op, fd: fd, isCancelled: isCancelled) { live, runFraction in
                        report.liveMBps = live
                        report.overallFraction = prepareWeight + (1 - prepareWeight) * (base + runFraction) / Double(totalRuns)
                        progress(report)
                    }
                    if best == nil || measurement.mbPerSecond > best!.mbPerSecond { best = measurement }
                    runsDone += 1
                }
                if let best { completed.append(best) }
                report.completed = completed
                progress(report)
            }
        }

        report.stage = .cleaningUp
        progress(report)
        report.stage = .finished
        report.overallFraction = 1
        progress(report)

        return StorageBenchmarkResult(targetPath: folder.path,
                                      volumeName: StorageBenchmarkEngine.volumeName(for: folder),
                                      profileName: profile.name,
                                      fileSizeBytes: profile.fileSizeBytes,
                                      runsPerTest: profile.runs,
                                      measurements: completed)
    }

    static func failure(forOpenErrno code: Int32) -> StorageBenchmarkFailure {
        switch code {
        case EACCES, EPERM: return .notWritable
        case EROFS: return .writeProtected
        case ENOENT, ENOTDIR: return .notFound
        case ENOSPC: return .notEnoughSpace(neededBytes: 0, availableBytes: 0)
        default: return .ioError(String(cString: strerror(code)))
        }
    }

    private func settle(isCancelled: () -> Bool) throws {
        let end = Date().addingTimeInterval(profile.settleSeconds)
        while Date() < end {
            if isCancelled() { throw StorageBenchmarkFailure.cancelled }
            Thread.sleep(forTimeInterval: 0.1)
        }
    }

    private func prepareFile(fd: Int32, isCancelled: () -> Bool, progress: (Double) -> Void) throws {
        let chunk = 8 * 1024 * 1024
        guard let buffer = StorageBenchmarkEngine.alignedBuffer(size: chunk) else {
            throw StorageBenchmarkFailure.ioError("out of memory")
        }
        defer { free(buffer) }
        var rng = SplitMix64(seed: 0x5EED_0F_D15C)
        rng.fill(UnsafeMutableRawBufferPointer(start: buffer, count: chunk))
        let words = buffer.bindMemory(to: UInt64.self, capacity: chunk / 8)

        var offset: Int64 = 0
        var lastReport = Date.distantPast
        while offset < profile.fileSizeBytes {
            if isCancelled() { throw StorageBenchmarkFailure.cancelled }
            // Change a few words so no two chunks are identical.
            words[0] = rng.next()
            words[chunk / 16] = rng.next()
            let toWrite = Int(min(Int64(chunk), profile.fileSizeBytes - offset))
            let written = pwrite(fd, buffer, toWrite, off_t(offset))
            if written < 0 {
                let code = errno
                if code == ENOSPC {
                    throw StorageBenchmarkFailure.notEnoughSpace(neededBytes: profile.requiredFreeBytes, availableBytes: 0)
                }
                throw StorageBenchmarkFailure.ioError(String(cString: strerror(code)))
            }
            offset += Int64(written)
            if Date().timeIntervalSince(lastReport) > 0.2 {
                progress(Double(offset) / Double(profile.fileSizeBytes))
                lastReport = Date()
            }
        }
        #if canImport(Darwin)
        _ = fcntl(fd, F_FULLFSYNC)
        #else
        _ = fsync(fd)
        #endif
        progress(1)
    }

    static func alignedBuffer(size: Int) -> UnsafeMutableRawPointer? {
        var pointer: UnsafeMutableRawPointer?
        guard posix_memalign(&pointer, 4096, size) == 0 else { return nil }
        return pointer
    }

    /// One timed run with `queueDepth` threads keeping requests in flight.
    private func measure(kind: StorageTestKind, operation: StorageOperation, fd: Int32, isCancelled: @escaping () -> Bool,
                         live: (Double, Double) -> Void) throws -> StorageTestMeasurement {
        let threads = kind.queueDepth
        let blockSize = kind.blockSize
        let blocksInFile = Int(profile.fileSizeBytes / Int64(blockSize))
        let counters = IOCounters(threads: threads)
        let seconds = profile.secondsPerRun
        let start = DispatchTime.now().uptimeNanoseconds
        let deadline = start + UInt64(seconds * 1_000_000_000)
        let group = DispatchGroup()

        for t in 0..<threads {
            group.enter()
            let thread = Thread {
                defer { group.leave() }
                guard let buffer = StorageBenchmarkEngine.alignedBuffer(size: blockSize) else {
                    counters.fail("out of memory")
                    return
                }
                defer { free(buffer) }
                var rng = SplitMix64(seed: UInt64(t + 1) &* 0x9E37_79B9 ^ UInt64(start))
                rng.fill(UnsafeMutableRawBufferPointer(start: buffer, count: blockSize))
                let firstWord = buffer.bindMemory(to: UInt64.self, capacity: 1)
                var sequentialIndex = t
                var bytes: Int64 = 0
                var ops: Int64 = 0
                var latency: UInt64 = 0
                var lastEnd = start

                while true {
                    let now = DispatchTime.now().uptimeNanoseconds
                    if now >= deadline || counters.isStopped { break }
                    let block: Int
                    if kind.isSequential {
                        block = sequentialIndex % blocksInFile
                        sequentialIndex += threads
                    } else {
                        block = Int(rng.next() % UInt64(blocksInFile))
                    }
                    let offset = off_t(block) * off_t(blockSize)
                    let opStart = DispatchTime.now().uptimeNanoseconds
                    let result: Int
                    if operation == .read {
                        result = pread(fd, buffer, blockSize, offset)
                    } else {
                        firstWord.pointee = rng.next()
                        result = pwrite(fd, buffer, blockSize, offset)
                    }
                    let opEnd = DispatchTime.now().uptimeNanoseconds
                    if result < 0 {
                        counters.fail(String(cString: strerror(errno)))
                        break
                    }
                    bytes += Int64(result)
                    ops += 1
                    latency += opEnd - opStart
                    lastEnd = opEnd
                    if ops % 16 == 0 {
                        counters.update(thread: t, bytes: bytes, ops: ops, latencyNs: latency, lastEnd: lastEnd)
                    }
                }
                counters.update(thread: t, bytes: bytes, ops: ops, latencyNs: latency, lastEnd: lastEnd)
            }
            thread.qualityOfService = .userInitiated
            thread.start()
        }

        var lastBytes: Int64 = 0
        var lastTime = start
        while group.wait(timeout: .now() + .milliseconds(250)) == .timedOut {
            if isCancelled() { counters.stop() }
            let now = DispatchTime.now().uptimeNanoseconds
            let totals = counters.totals()
            let dt = Double(now - lastTime) / 1_000_000_000
            if dt > 0 {
                let mbps = Double(totals.bytes - lastBytes) / dt / 1_000_000
                live(max(0, mbps), min(1, Double(now - start) / (seconds * 1_000_000_000)))
            }
            lastBytes = totals.bytes
            lastTime = now
        }
        if isCancelled() { throw StorageBenchmarkFailure.cancelled }
        if let error = counters.error { throw StorageBenchmarkFailure.ioError(error) }

        let totals = counters.totals()
        let elapsedNs = max(1, totals.lastEnd > start ? totals.lastEnd - start : UInt64(seconds * 1_000_000_000))
        let elapsed = Double(elapsedNs) / 1_000_000_000
        return StorageTestMeasurement(
            kind: kind,
            operation: operation,
            mbPerSecond: Double(totals.bytes) / elapsed / 1_000_000,
            iops: Double(totals.ops) / elapsed,
            averageLatencyMs: totals.ops > 0 ? Double(totals.latencyNs) / Double(totals.ops) / 1_000_000 : 0
        )
    }
}

/// Per-thread I/O counters, read by the coordinating thread for the live graph.
final class IOCounters: @unchecked Sendable {
    private let lock = NSLock()
    private var bytes: [Int64]
    private var ops: [Int64]
    private var latency: [UInt64]
    private var lastEnd: [UInt64]
    private var stopped = false
    private var firstError: String?

    init(threads: Int) {
        bytes = Array(repeating: 0, count: threads)
        ops = Array(repeating: 0, count: threads)
        latency = Array(repeating: 0, count: threads)
        lastEnd = Array(repeating: 0, count: threads)
    }

    func update(thread: Int, bytes b: Int64, ops o: Int64, latencyNs l: UInt64, lastEnd e: UInt64) {
        lock.lock()
        bytes[thread] = b
        ops[thread] = o
        latency[thread] = l
        lastEnd[thread] = e
        lock.unlock()
    }

    func totals() -> (bytes: Int64, ops: Int64, latencyNs: UInt64, lastEnd: UInt64) {
        lock.lock()
        defer { lock.unlock() }
        return (bytes.reduce(0, +), ops.reduce(0, +), latency.reduce(0, +), lastEnd.max() ?? 0)
    }

    var isStopped: Bool {
        lock.lock()
        defer { lock.unlock() }
        return stopped
    }

    func stop() {
        lock.lock()
        stopped = true
        lock.unlock()
    }

    var error: String? {
        lock.lock()
        defer { lock.unlock() }
        return firstError
    }

    func fail(_ message: String) {
        lock.lock()
        if firstError == nil { firstError = message }
        stopped = true
        lock.unlock()
    }
}
