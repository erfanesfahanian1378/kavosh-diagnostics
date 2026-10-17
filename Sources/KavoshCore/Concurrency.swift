import Foundation

/// A thread-safe "please stop" flag shared between the UI and a background engine.
public final class CancellationFlag: @unchecked Sendable {
    private let lock = NSLock()
    private var value = false

    public init() {}

    public var isCancelled: Bool {
        lock.lock()
        defer { lock.unlock() }
        return value
    }

    public func cancel() {
        lock.lock()
        value = true
        lock.unlock()
    }
}

/// A thread-safe running total.
public final class LockedCounter: @unchecked Sendable {
    private let lock = NSLock()
    private var total = 0

    public init() {}

    public func add(_ amount: Int) {
        lock.lock()
        total += amount
        lock.unlock()
    }

    public var value: Int {
        lock.lock()
        defer { lock.unlock() }
        return total
    }
}

/// Keeps some CPU cores busy (used while measuring battery drain under load).
public final class CPULoadGenerator: @unchecked Sendable {
    private let flag = CancellationFlag()
    private let group = DispatchGroup()
    private var started = false

    public init() {}

    public func start(threads: Int = max(1, ProcessInfo.processInfo.activeProcessorCount / 2)) {
        guard !started else { return }
        started = true
        let reference = CPUWorkload.mandelbrot()
        for index in 0..<max(1, threads) {
            group.enter()
            let thread = Thread { [flag, group] in
                let workspace = CPUWorkspace()
                var unit = index * 7
                while !flag.isCancelled {
                    CPUWorkload.runUnit(unit, workspace: workspace, floatReference: reference)
                    unit += 1
                }
                group.leave()
            }
            thread.qualityOfService = .utility
            thread.start()
        }
    }

    /// Stops the load and waits (briefly) for the threads to finish their current unit.
    public func stop() {
        guard started else { return }
        flag.cancel()
        _ = group.wait(timeout: .now() + .seconds(2))
    }
}
