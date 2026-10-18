using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kavosh.Core;

public enum MemoryTestPhase { Allocating, Writing, Verifying, Finished }

public struct MemoryTestProgress
{
    public MemoryTestPhase Phase;
    public int Pass;
    public int Passes;
    public ulong Pattern;
    public double Fraction;
    public double? WriteMBps;
    public double? ReadMBps;
    public long Errors;
    public long TestedBytes;
}

public sealed class MemoryAllocationException : Exception
{
    public MemoryAllocationException(long bytes)
        : base(Loc.T($"Couldn't reserve {bytes / (1024 * 1024)} MB of memory for the test. Close some apps and try again.",
                     $"رزرو {bytes / (1024 * 1024)} مگابایت حافظه برای تست ممکن نشد. چند برنامه را ببندید و دوباره امتحان کنید."))
    { }
}

public enum MemoryPatternKind { Constant, Random, Address }

public readonly record struct MemoryPattern(MemoryPatternKind Kind, ulong Value)
{
    public static MemoryPattern Constant(ulong value) => new(MemoryPatternKind.Constant, value);
    public static MemoryPattern Random(ulong seed) => new(MemoryPatternKind.Random, seed);
    public static readonly MemoryPattern Address = new(MemoryPatternKind.Address, 0);

    /// <summary>Hex label shown while running.</summary>
    public string Label => Kind switch
    {
        MemoryPatternKind.Constant => Value.ToString("X16"),
        MemoryPatternKind.Random => "RANDOM",
        _ => "ADDRESS",
    };

    internal bool CountsForThroughput => Kind == MemoryPatternKind.Constant;

    public ulong ValueAt(long index)
    {
        unchecked
        {
            return Kind switch
            {
                MemoryPatternKind.Constant => Value,
                MemoryPatternKind.Random => SplitMix64.Mix(Value ^ (ulong)index),
                _ => ((ulong)index * 0x9E3779B97F4A7C15UL) ^ 0xA5A5A5A55A5A5A5AUL,
            };
        }
    }
}

/// <summary>
/// Writes patterns into a large block of RAM, reads them back and counts any word that came back
/// different. Also reports how fast memory could be written and read.
/// </summary>
public sealed unsafe class MemoryTestEngine
{
    public static readonly MemoryPattern[] DefaultPatterns =
    {
        MemoryPattern.Constant(0x0000000000000000UL),
        MemoryPattern.Constant(0xFFFFFFFFFFFFFFFFUL),
        MemoryPattern.Constant(0xAAAAAAAAAAAAAAAAUL),
        MemoryPattern.Constant(0x5555555555555555UL),
        MemoryPattern.Random(0x6B61766F73682121UL),
        MemoryPattern.Address,
    };

    public long SizeBytes { get; }
    public int Threads { get; }
    public IReadOnlyList<MemoryPattern> Patterns { get; }

    public MemoryTestEngine(long sizeBytes, int? threads = null, IReadOnlyList<MemoryPattern>? patterns = null)
    {
        SizeBytes = Math.Max(8L * 1024 * 1024, sizeBytes);
        Threads = Math.Max(1, threads ?? Environment.ProcessorCount);
        Patterns = patterns ?? DefaultPatterns;
    }

    /// <summary>A size that stays well clear of memory pressure: 1/8 of RAM, between 256 MiB and 2 GiB.</summary>
    public static long RecommendedSize(ulong physicalMemory, bool underPressure = false)
    {
        const long mib = 1024 * 1024;
        long size = (long)(physicalMemory / 8);
        if (underPressure) size /= 2;
        size = Math.Max(256 * mib, Math.Min(2048 * mib, size));
        return size - size % (64 * mib);
    }

    public MemoryTestResult Run(Func<bool> isCancelled, Action<MemoryTestProgress> progress)
    {
        var started = Stopwatch.StartNew();
        long words = SizeBytes / 8;
        long bytes = words * 8;
        var current = new MemoryTestProgress
        {
            Phase = MemoryTestPhase.Allocating, Passes = Patterns.Count, TestedBytes = bytes,
        };
        progress(current);

        ulong* memory;
        try
        {
            memory = (ulong*)NativeMemory.AlignedAlloc((nuint)bytes, 4096);
        }
        catch (OutOfMemoryException)
        {
            throw new MemoryAllocationException(bytes);
        }
        if (memory == null) throw new MemoryAllocationException(bytes);

        try
        {
            var writeRates = new List<double>();
            var readRates = new List<double>();
            long errorCount = 0;
            int chunkCount = Threads * 4;
            long chunkWords = (words + chunkCount - 1) / chunkCount;
            var options = new ParallelOptions { MaxDegreeOfParallelism = Threads };

            for (int passIndex = 0; passIndex < Patterns.Count; passIndex++)
            {
                if (isCancelled()) throw new BenchmarkCancelledException();
                var pattern = Patterns[passIndex];

                // Write.
                current.Phase = MemoryTestPhase.Writing;
                current.Pass = passIndex + 1;
                current.Pattern = pattern.ValueAt(0);
                current.Fraction = (double)(passIndex * 2) / (Patterns.Count * 2);
                progress(current);
                var writeClock = Stopwatch.StartNew();
                Parallel.For(0, chunkCount, options, chunk =>
                {
                    long lo = chunk * chunkWords;
                    long hi = Math.Min(words, lo + chunkWords);
                    if (lo >= hi) return;
                    if (pattern.Kind == MemoryPatternKind.Constant)
                    {
                        FillRange(memory + lo, hi - lo, pattern.Value);
                    }
                    else
                    {
                        for (long i = lo; i < hi; i++) memory[i] = pattern.ValueAt(i);
                    }
                });
                double writeSeconds = writeClock.Elapsed.TotalSeconds;
                if (pattern.CountsForThroughput && writeSeconds > 0)
                {
                    writeRates.Add(bytes / writeSeconds / 1_000_000);
                    current.WriteMBps = writeRates.Average();
                }
                if (isCancelled()) throw new BenchmarkCancelledException();

                // Verify.
                current.Phase = MemoryTestPhase.Verifying;
                current.Fraction = (double)(passIndex * 2 + 1) / (Patterns.Count * 2);
                progress(current);
                var readClock = Stopwatch.StartNew();
                Parallel.For(0, chunkCount, options, chunk =>
                {
                    long lo = chunk * chunkWords;
                    long hi = Math.Min(words, lo + chunkWords);
                    if (lo >= hi) return;
                    long errors = 0;
                    if (pattern.Kind == MemoryPatternKind.Constant)
                    {
                        errors = CountMismatches(memory + lo, hi - lo, pattern.Value);
                    }
                    else
                    {
                        for (long i = lo; i < hi; i++)
                        {
                            if (memory[i] != pattern.ValueAt(i)) errors++;
                        }
                    }
                    if (errors > 0) Interlocked.Add(ref errorCount, errors);
                });
                double readSeconds = readClock.Elapsed.TotalSeconds;
                if (pattern.CountsForThroughput && readSeconds > 0)
                {
                    readRates.Add(bytes / readSeconds / 1_000_000);
                    current.ReadMBps = readRates.Average();
                }
                current.Errors = Interlocked.Read(ref errorCount);
            }

            current.Phase = MemoryTestPhase.Finished;
            current.Fraction = 1;
            progress(current);

            return new MemoryTestResult
            {
                TestedBytes = bytes,
                Passes = Patterns.Count,
                AverageWriteThroughputMBps = writeRates.Count == 0 ? 0 : writeRates.Average(),
                AverageReadThroughputMBps = readRates.Count == 0 ? 0 : readRates.Average(),
                ErrorsDetected = Interlocked.Read(ref errorCount),
                DurationSeconds = started.Elapsed.TotalSeconds,
            };
        }
        finally
        {
            NativeMemory.AlignedFree(memory);
        }
    }

    // Spans are limited to int.MaxValue elements, so long ranges go in slices.
    private const int Slice = 1 << 26;

    private static void FillRange(ulong* start, long count, ulong value)
    {
        while (count > 0)
        {
            int n = (int)Math.Min(count, Slice);
            new Span<ulong>(start, n).Fill(value);
            start += n;
            count -= n;
        }
    }

    private static long CountMismatches(ulong* start, long count, ulong value)
    {
        long errors = 0;
        while (count > 0)
        {
            int n = (int)Math.Min(count, Slice);
            var span = new ReadOnlySpan<ulong>(start, n);
            // Vectorised scan; look word by word only when something differs.
            if (span.IndexOfAnyExcept(value) >= 0) errors += n - span.Count(value);
            start += n;
            count -= n;
        }
        return errors;
    }
}
