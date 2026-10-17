import Foundation

/// Small, fast, deterministic random generator. The benchmarks use it so every Mac
/// does exactly the same work and the results can be checked.
public struct SplitMix64: RandomNumberGenerator, Sendable {
    public var state: UInt64

    public init(seed: UInt64) {
        state = seed
    }

    @inline(__always)
    public mutating func next() -> UInt64 {
        state &+= 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }

    /// Stateless mix of a single value (same function as one `next()` step).
    @inline(__always)
    public static func mix(_ value: UInt64) -> UInt64 {
        var z = value &+ 0x9E37_79B9_7F4A_7C15
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }

    /// Fills raw memory with random bytes.
    public mutating func fill(_ buffer: UnsafeMutableRawBufferPointer) {
        let words = buffer.count / 8
        if words > 0 {
            let typed = buffer.baseAddress!.bindMemory(to: UInt64.self, capacity: words)
            for i in 0..<words { typed[i] = next() }
        }
        let tail = buffer.count % 8
        if tail > 0 {
            var last = next()
            for i in 0..<tail {
                buffer[words * 8 + i] = UInt8(truncatingIfNeeded: last)
                last >>= 8
            }
        }
    }
}
