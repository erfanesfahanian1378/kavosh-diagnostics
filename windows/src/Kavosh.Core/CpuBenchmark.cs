using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Kavosh.Core;

// ───────────────────────────── Workload ─────────────────────────────

/// <summary>
/// Scratch memory for one worker thread. Each unit of CPU work:
///  1. counts the primes in an 8-million-number window (segmented sieve, integer + memory),
///  2. mixes a 256 KB buffer with 64-bit hashing (integer ALU),
///  3. sorts 96k pseudo-random numbers (branches, memory),
///  4. renders a small Mandelbrot grid (floating point).
/// Parts 1–3 are pure integer math, so their combined checksum is known in advance on every
/// computer (see <see cref="CpuWorkloadTable"/>, shared with the macOS app). Part 4 is compared to a
/// reference computed on the same machine before the timed run. Any mismatch means the CPU
/// returned a wrong answer.
/// </summary>
public sealed unsafe class CpuWorkspace : IDisposable
{
    internal const int SegmentSize = 1 << 18;   // odd numbers per sieve segment (256 KB of flags)
    internal const int HashWords = 1 << 15;     // 32k × 8 bytes = 256 KB
    internal const int SortCount = 96 * 1024;

    internal readonly byte* Sieve;
    internal readonly ulong* Hash;
    internal readonly uint[] SortScratch = new uint[SortCount];
    private bool _disposed;

    public CpuWorkspace()
    {
        Sieve = (byte*)NativeMemory.AlignedAlloc(SegmentSize, 64);
        Hash = (ulong*)NativeMemory.AlignedAlloc(HashWords * sizeof(ulong), 64);
    }

    ~CpuWorkspace() => Free();

    public void Dispose()
    {
        Free();
        GC.SuppressFinalize(this);
    }

    private void Free()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMemory.AlignedFree(Sieve);
        NativeMemory.AlignedFree(Hash);
    }
}

public static unsafe class CpuWorkload
{
    /// <summary>Bump when the work or the scoring changes, so old and new scores aren't mixed up.</summary>
    public const int ScoringVersion = 1;

    public const int DistinctUnits = 256;
    public const ulong PrimeWindowStart = 10_000_000_000UL;
    public const ulong PrimeWindowSize = 8_000_000UL;

    private static readonly Lazy<uint[]> BasePrimesLazy = new(ComputeBasePrimes);

    /// <summary>Primes up to the square root of the largest number any unit sieves.</summary>
    internal static uint[] BasePrimes => BasePrimesLazy.Value;

    private static uint[] ComputeBasePrimes()
    {
        ulong maxValue = PrimeWindowStart + PrimeWindowSize * DistinctUnits;
        int limit = (int)Math.Sqrt(maxValue) + 2;
        var composite = new bool[limit + 1];
        var primes = new List<uint>();
        for (int i = 2; i <= limit; i++)
        {
            if (composite[i]) continue;
            primes.Add((uint)i);
            for (long j = (long)i * i; j <= limit; j += i) composite[j] = true;
        }
        return primes.ToArray();
    }

    /// <summary>Counts primes in [lo, hi) with an odd-only segmented sieve. <paramref name="lo"/> must be even and &gt; 2.</summary>
    internal static ulong CountPrimes(ulong lo, ulong hi, CpuWorkspace workspace)
    {
        if (lo <= 2 || lo % 2 != 0) throw new ArgumentOutOfRangeException(nameof(lo));
        byte* seg = workspace.Sieve;
        uint[] basePrimes = BasePrimes;
        const ulong segOdds = CpuWorkspace.SegmentSize;
        ulong count = 0;
        ulong segLo = lo; // even
        while (segLo < hi)
        {
            ulong segHi = Math.Min(hi, segLo + segOdds * 2);
            // index i represents the odd number segLo + 2*i + 1
            int n = (int)((segHi - segLo) / 2);
            new Span<byte>(seg, n).Fill(1);
            for (int k = 1; k < basePrimes.Length; k++) // skip 2
            {
                ulong p = basePrimes[k];
                if (p * p >= segHi) break;
                // first odd multiple of p that is >= segLo + 1 and >= p*p
                ulong start = Math.Max(p * p, (segLo + 1 + p - 1) / p * p);
                if (start % 2 == 0) start += p;
                long idx = (long)((start - segLo - 1) / 2);
                long step = (long)p;
                while (idx < n)
                {
                    seg[idx] = 0;
                    idx += step;
                }
            }
            count += (ulong)new ReadOnlySpan<byte>(seg, n).Count((byte)1);
            segLo = segHi;
        }
        return count;
    }

    internal static ulong HashMix(ulong seed, CpuWorkspace workspace)
    {
        ulong* buffer = workspace.Hash;
        const int count = CpuWorkspace.HashWords;
        const int mask = count - 1;
        var rng = new SplitMix64(seed);
        for (int i = 0; i < count; i++) buffer[i] = rng.Next();
        ulong acc = seed;
        unchecked
        {
            for (int round = 0; round < 160; round++)
            {
                ulong prev = buffer[mask];
                for (int i = 0; i < count; i++)
                {
                    int j = (i * 7 + round + 1) & mask;
                    ulong x = buffer[i] ^ buffer[j];
                    x = BitOperations.RotateLeft(x, 23);
                    x = x * 0x9E3779B97F4A7C15UL + prev;
                    buffer[i] = x;
                    prev = x;
                }
                acc ^= prev + (ulong)round;
            }
        }
        return acc;
    }

    internal static ulong SortChecksum(ulong seed, CpuWorkspace workspace)
    {
        var rng = new SplitMix64(seed);
        uint[] scratch = workspace.SortScratch;
        for (int i = 0; i < scratch.Length; i++) scratch[i] = (uint)rng.Next();
        Array.Sort(scratch);
        ulong h = 0xCBF29CE484222325UL;
        unchecked
        {
            for (int i = 0; i < scratch.Length; i += 8) h = (h ^ scratch[i]) * 0x00000100000001B3UL;
        }
        return h;
    }

    /// <summary>Floating point part. The same for every unit.</summary>
    public static ulong Mandelbrot(int width = 192, int height = 128, int maxIterations = 300)
    {
        ulong total = 0;
        double w = width, h = height;
        for (int py = 0; py < height; py++)
        {
            double y0 = py / h * 2.4 - 1.2;
            for (int px = 0; px < width; px++)
            {
                double x0 = px / w * 3.2 - 2.2;
                double x = 0, y = 0;
                int i = 0;
                while (i < maxIterations)
                {
                    double xx = x * x, yy = y * y;
                    if (xx + yy > 4) break;
                    double xt = xx - yy + x0;
                    y = 2 * x * y + y0;
                    x = xt;
                    i++;
                }
                unchecked { total += (ulong)i * (ulong)(px + 1); }
            }
        }
        return total;
    }

    /// <summary>The integer checksum for unit <paramref name="index"/> (taken modulo <see cref="DistinctUnits"/>).</summary>
    public static ulong IntegerChecksum(int index, CpuWorkspace workspace)
    {
        int k = index % DistinctUnits;
        ulong lo = PrimeWindowStart + (ulong)k * PrimeWindowSize;
        ulong primes = CountPrimes(lo, lo + PrimeWindowSize, workspace);
        unchecked
        {
            ulong seed = 0xC0FFEE0000000000UL + (ulong)k;
            ulong mix = HashMix(seed, workspace);
            ulong sorted = SortChecksum(seed ^ 0x5DEECE66D1CE4E5BUL, workspace);
            return (primes * 0x9E3779B97F4A7C15UL) ^ mix ^ (sorted << 1);
        }
    }

    public static ulong ExpectedIntegerChecksum(int index) => CpuWorkloadTable.Expected[index % DistinctUnits];

    /// <summary>Runs one unit. Returns true when every part returned the right answer.</summary>
    public static bool RunUnit(int index, CpuWorkspace workspace, ulong floatReference)
    {
        ulong integer = IntegerChecksum(index, workspace);
        ulong fp = Mandelbrot();
        return integer == ExpectedIntegerChecksum(index) && fp == floatReference;
    }
}

// ───────────────────────────── Engine ─────────────────────────────

public enum CpuBenchmarkPhase { Preparing, MultiCore, SingleCore, Sustained, Finished }

/// <summary>A snapshot of the run, handed to the UI about ten times a second (a copy, safe to keep).</summary>
public struct CpuBenchmarkProgress
{
    public CpuBenchmarkPhase Phase;
    public double PhaseFraction;
    public double OverallFraction;
    public double Elapsed;
    public double? LiveScore;
    public double? MultiCoreScore;
    public double? SingleCoreScore;
    public IReadOnlyList<double> SustainedSamples;
    public int WrongResults;
}

public sealed class BenchmarkCancelledException : OperationCanceledException
{
    public BenchmarkCancelledException() : base("The test was cancelled.") { }
}

public sealed class CpuBenchmarkConfiguration
{
    public int MultiCoreUnits { get; set; } = 960;
    public int SingleCoreUnits { get; set; } = 40;
    /// <summary>Total test time including the sustained phase. The fixed phases always run fully.</summary>
    public double DurationSeconds { get; set; } = 60;
    public int Threads { get; set; } = Environment.ProcessorCount;
    public double SustainedWindowSeconds { get; set; } = 5;

    /// <summary>A short run for smoke tests.</summary>
    public static CpuBenchmarkConfiguration Quick => new() { MultiCoreUnits = 96, SingleCoreUnits = 8, DurationSeconds = 0 };
}

public sealed class CpuBenchmarkEngine
{
    /// <summary>
    /// Reference machine (Apple M1, 4P + 4E cores) timings, the same as the macOS app so scores
    /// can be compared across platforms. It scores 1,000 multi-core and 100 single-core; a
    /// computer that does the same work in half the time scores double.
    /// </summary>
    public const string ReferenceName = "Apple M1 (8-core)";
    public const double ReferenceUnitMsSingle = 22.0;
    public const double ReferenceMultiCoreThroughput = 5.2; // single-core units in parallel

    public static double ReferenceSingleCoreMs => ReferenceUnitMsSingle;
    public static double ReferenceMultiCoreMs(int units) => ReferenceUnitMsSingle * units / ReferenceMultiCoreThroughput;

    public CpuBenchmarkConfiguration Configuration { get; }

    public CpuBenchmarkEngine(CpuBenchmarkConfiguration? configuration = null)
    {
        Configuration = configuration ?? new CpuBenchmarkConfiguration();
    }

    public static double MultiCoreScore(int units, double milliseconds) =>
        milliseconds > 0 ? 1000 * ReferenceMultiCoreMs(units) / milliseconds : 0;

    public static double SingleCoreScore(int units, double milliseconds) =>
        milliseconds > 0 && units > 0 ? 100 * ReferenceSingleCoreMs / (milliseconds / units) : 0;

    /// <summary>Runs the three stages. Blocks the calling thread; call it from a background thread.</summary>
    public CpuBenchmarkResult Run(Func<bool> isCancelled, Action<CpuBenchmarkProgress> progress)
    {
        var config = Configuration;
        int threads = Math.Max(1, config.Threads);
        var clock = Stopwatch.StartNew();
        var current = new CpuBenchmarkProgress { Phase = CpuBenchmarkPhase.Preparing, SustainedSamples = Array.Empty<double>() };
        progress(current);

        // Warm up the base primes table and compute the floating-point reference.
        _ = CpuWorkload.BasePrimes.Length;
        ulong floatReference = CpuWorkload.Mandelbrot();
        if (isCancelled()) throw new BenchmarkCancelledException();

        // Rough time budget for the overall progress bar.
        double total = Math.Max(config.DurationSeconds, 6.0);
        double Overall(double elapsed) => Math.Min(0.99, elapsed / total);

        // Stage 1: multi-core, fixed work.
        current.Phase = CpuBenchmarkPhase.MultiCore;
        progress(current);
        var multi = new ParallelRunner(threads, floatReference, unitOffset: 0);
        var multiClock = Stopwatch.StartNew();
        multi.Run(config.MultiCoreUnits, null, isCancelled, (done, wrong) =>
        {
            double elapsed = multiClock.Elapsed.TotalSeconds;
            current.PhaseFraction = (double)done / config.MultiCoreUnits;
            current.Elapsed = clock.Elapsed.TotalSeconds;
            current.OverallFraction = Overall(current.Elapsed);
            current.WrongResults = wrong;
            if (done > 0 && elapsed > 0.3) current.LiveScore = MultiCoreScore(done, elapsed * 1000);
            progress(current);
        });
        double multiMs = multiClock.Elapsed.TotalMilliseconds;
        double multiScore = MultiCoreScore(config.MultiCoreUnits, multiMs);
        int wrongTotal = multi.WrongCount;
        current.MultiCoreScore = multiScore;
        current.LiveScore = null;

        // Stage 2: single-core, fixed work.
        current.Phase = CpuBenchmarkPhase.SingleCore;
        current.PhaseFraction = 0;
        progress(current);
        var single = new ParallelRunner(1, floatReference, unitOffset: 17);
        var singleClock = Stopwatch.StartNew();
        single.Run(config.SingleCoreUnits, null, isCancelled, (done, wrong) =>
        {
            double elapsed = singleClock.Elapsed.TotalSeconds;
            current.PhaseFraction = (double)done / Math.Max(1, config.SingleCoreUnits);
            current.Elapsed = clock.Elapsed.TotalSeconds;
            current.OverallFraction = Overall(current.Elapsed);
            current.WrongResults = wrongTotal + wrong;
            if (done > 0) current.LiveScore = SingleCoreScore(done, elapsed * 1000);
            progress(current);
        });
        double singleMs = singleClock.Elapsed.TotalMilliseconds;
        double singleScore = SingleCoreScore(config.SingleCoreUnits, singleMs);
        wrongTotal += single.WrongCount;
        current.SingleCoreScore = singleScore;
        current.LiveScore = null;

        // Stage 3: sustained load for the rest of the chosen duration.
        var samples = new List<double>();
        double remaining = config.DurationSeconds - clock.Elapsed.TotalSeconds;
        if (remaining >= config.SustainedWindowSeconds * 2)
        {
            current.Phase = CpuBenchmarkPhase.Sustained;
            current.PhaseFraction = 0;
            progress(current);
            var sustained = new ParallelRunner(threads, floatReference, unitOffset: 101);
            var sustainedClock = Stopwatch.StartNew();
            double windowStart = 0;
            int windowUnitsAtStart = 0;
            sustained.Run(null, TimeSpan.FromSeconds(remaining), isCancelled, (done, wrong) =>
            {
                double now = sustainedClock.Elapsed.TotalSeconds;
                double windowElapsed = now - windowStart;
                if (windowElapsed >= config.SustainedWindowSeconds)
                {
                    double score = MultiCoreScore(done - windowUnitsAtStart, windowElapsed * 1000);
                    samples.Add(score);
                    windowStart = now;
                    windowUnitsAtStart = done;
                    current.LiveScore = score;
                }
                current.SustainedSamples = samples.ToArray();
                current.PhaseFraction = Math.Min(1, now / remaining);
                current.Elapsed = clock.Elapsed.TotalSeconds;
                current.OverallFraction = Overall(current.Elapsed);
                current.WrongResults = wrongTotal + wrong;
                progress(current);
            });
            wrongTotal += sustained.WrongCount;
        }

        // Sustained = the average of the last three windows (the last ~15 seconds).
        double? sustainedScore = samples.Count > 0 ? samples.TakeLast(3).Average() : null;
        bool throttled = sustainedScore is { } s && s < multiScore * 0.90;

        current.Phase = CpuBenchmarkPhase.Finished;
        current.PhaseFraction = 1;
        current.OverallFraction = 1;
        current.WrongResults = wrongTotal;
        current.Elapsed = clock.Elapsed.TotalSeconds;
        progress(current);

        return new CpuBenchmarkResult
        {
            Score = multiScore,
            SingleCoreScore = singleScore,
            SustainedScore = sustainedScore,
            SustainedSamples = samples,
            MultiCoreBatchMs = multiMs,
            SingleCoreBatchMs = singleMs,
            MultiCoreUnits = config.MultiCoreUnits,
            SingleCoreUnits = config.SingleCoreUnits,
            ThreadsUsed = threads,
            DurationSeconds = clock.Elapsed.TotalSeconds,
            WrongResults = wrongTotal,
            ThermalThrottleDetected = throttled,
            ScoringVersion = CpuWorkload.ScoringVersion,
        };
    }
}

/// <summary>Runs units on N threads, handing out unit numbers one at a time.</summary>
internal sealed class ParallelRunner
{
    private readonly int _threads;
    private readonly ulong _floatReference;
    private readonly int _unitOffset;
    private readonly object _lock = new();
    private int _nextUnit;
    private int _doneUnits;
    private int _wrong;
    private bool _stop;

    public ParallelRunner(int threads, ulong floatReference, int unitOffset)
    {
        _threads = threads;
        _floatReference = floatReference;
        _unitOffset = unitOffset;
    }

    public int WrongCount
    {
        get { lock (_lock) return _wrong; }
    }

    /// <summary>
    /// Blocks until <paramref name="totalUnits"/> are done or <paramref name="duration"/> passes.
    /// <paramref name="tick"/> is called on the calling thread about ten times a second with
    /// (units done, wrong results).
    /// </summary>
    public void Run(int? totalUnits, TimeSpan? duration, Func<bool> isCancelled, Action<int, int> tick)
    {
        using var finished = new CountdownEvent(_threads);
        for (int t = 0; t < _threads; t++)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    using var workspace = new CpuWorkspace();
                    while (true)
                    {
                        int unit;
                        lock (_lock)
                        {
                            if (_stop || (totalUnits is { } limit && _nextUnit >= limit)) break;
                            unit = _nextUnit++;
                        }
                        bool ok = CpuWorkload.RunUnit(unit + _unitOffset, workspace, _floatReference);
                        lock (_lock)
                        {
                            _doneUnits++;
                            if (!ok) _wrong++;
                        }
                    }
                }
                finally
                {
                    finished.Signal();
                }
            }, maxStackSize: 1 << 20)
            {
                IsBackground = true,
                Name = "Kavosh CPU worker",
            };
            thread.Start();
        }

        var clock = Stopwatch.StartNew();
        bool cancelled = false;
        while (!finished.Wait(100))
        {
            var (done, wrong) = Snapshot();
            tick(done, wrong);
            if (!cancelled && isCancelled())
            {
                cancelled = true;
                SetStop();
            }
            if (duration is { } d && clock.Elapsed >= d) SetStop();
        }
        var (finalDone, finalWrong) = Snapshot();
        tick(finalDone, finalWrong);
        if (cancelled || isCancelled()) throw new BenchmarkCancelledException();
    }

    private (int Done, int Wrong) Snapshot()
    {
        lock (_lock) return (_doneUnits, _wrong);
    }

    private void SetStop()
    {
        lock (_lock) _stop = true;
    }
}

/// <summary>Keeps some CPU cores busy (used while measuring battery drain under load).</summary>
public sealed class CpuLoadGenerator
{
    private volatile bool _stop;
    private CountdownEvent? _finished;

    public bool IsRunning => _finished is not null;

    public void Start(int? threads = null)
    {
        if (_finished is not null) return;
        int count = Math.Max(1, threads ?? Environment.ProcessorCount / 2);
        _stop = false;
        _finished = new CountdownEvent(count);
        ulong reference = CpuWorkload.Mandelbrot();
        for (int index = 0; index < count; index++)
        {
            int first = index * 7;
            var finished = _finished;
            var thread = new Thread(() =>
            {
                try
                {
                    using var workspace = new CpuWorkspace();
                    int unit = first;
                    while (!_stop) CpuWorkload.RunUnit(unit++, workspace, reference);
                }
                finally
                {
                    finished.Signal();
                }
            }, maxStackSize: 1 << 20)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "Kavosh load",
            };
            thread.Start();
        }
    }

    /// <summary>Stops the load and waits (briefly) for the threads to finish their current unit.</summary>
    public void Stop()
    {
        if (_finished is null) return;
        _stop = true;
        // Not disposed: a worker still finishing its unit signals it after this returns.
        _finished.Wait(TimeSpan.FromSeconds(2));
        _finished = null;
    }
}
