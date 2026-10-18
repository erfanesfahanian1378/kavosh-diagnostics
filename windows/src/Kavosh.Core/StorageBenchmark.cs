using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static Kavosh.Core.Loc;

namespace Kavosh.Core;

public sealed class StorageTestProfile
{
    public string Name { get; init; } = "standard";
    public int Runs { get; init; }
    public double SecondsPerRun { get; init; }
    public long FileSizeBytes { get; init; }
    public double SettleSeconds { get; init; }

    /// <summary>Same settings as CrystalDiskMark's default: 1 GiB, 5 runs of 5 seconds, best run wins.</summary>
    public static readonly StorageTestProfile Standard = new()
    {
        Name = "standard", Runs = 5, SecondsPerRun = 5, FileSizeBytes = 1L << 30, SettleSeconds = 2,
    };

    /// <summary>Faster, a little less precise.</summary>
    public static readonly StorageTestProfile Quick = new()
    {
        Name = "quick", Runs = 2, SecondsPerRun = 3, FileSizeBytes = 512L << 20, SettleSeconds = 1,
    };

    /// <summary>The test file plus some headroom.</summary>
    public long RequiredFreeBytes => FileSizeBytes + 300_000_000;

    public string Title => Name == "quick" ? T("Quick", "سریع") : T("Standard", "استاندارد");
}

public enum StorageStage { Preparing, Settling, Running, CleaningUp, Finished }

public struct StorageBenchmarkProgress
{
    public StorageStage Stage;
    public StorageTestKind Kind;
    public StorageOperation Operation;
    public int Run;
    public int Of;
    public double OverallFraction;
    public double? LiveMBps;
    public IReadOnlyList<StorageTestMeasurement> Completed;
}

public enum StorageFailureKind { NotEnoughSpace, NotWritable, WriteProtected, NoDirectIO, NotFound, IoError, Cancelled }

public sealed class StorageBenchmarkException : Exception
{
    public StorageFailureKind Kind { get; }
    public long NeededBytes { get; }
    public string? Detail { get; }

    public StorageBenchmarkException(StorageFailureKind kind, long neededBytes = 0, string? detail = null)
        : base(Describe(kind, neededBytes, detail))
    {
        Kind = kind;
        NeededBytes = neededBytes;
        Detail = detail;
    }

    public static string Describe(StorageFailureKind kind, long neededBytes, string? detail)
    {
        switch (kind)
        {
            case StorageFailureKind.NotEnoughSpace:
                string gb = Fmt.Fixed(neededBytes / 1_000_000_000.0, 1);
                return T($"There isn't enough free space on this drive. The test needs about {gb} GB free for its temporary file — free some space, or pick a folder on another drive.",
                         $"فضای خالی کافی روی این درایو نیست. تست برای فایل موقت حدود {gb} گیگابایت فضای خالی لازم دارد — کمی فضا آزاد کنید یا پوشه‌ای روی درایو دیگری انتخاب کنید.");
            case StorageFailureKind.NotWritable:
                return T("This folder can't be written to. Pick a folder you have write access to — for example one inside your user folder — and try again.",
                         "در این پوشه نمی‌توان نوشت. پوشه‌ای انتخاب کنید که اجازه نوشتن در آن را دارید — مثلاً داخل پوشه کاربری — و دوباره امتحان کنید.");
            case StorageFailureKind.WriteProtected:
                return T("This drive is write-protected, so the test file can't be created on it.",
                         "این درایو محافظت‌شده در برابر نوشتن است، پس فایل تست روی آن ساخته نمی‌شود.");
            case StorageFailureKind.NoDirectIO:
                return T("This drive doesn't support the direct, uncached access the test needs (some network shares and virtual drives don't). Pick a folder on a local drive.",
                         "این درایو دسترسی مستقیم و بدون کش را که تست لازم دارد پشتیبانی نمی‌کند (بعضی درایوهای شبکه و مجازی). پوشه‌ای روی یک درایو محلی انتخاب کنید.");
            case StorageFailureKind.NotFound:
                return T("The folder couldn't be found, or its drive isn't ready. Check that the drive is connected and pick the folder again.",
                         "پوشه پیدا نشد یا درایو آن آماده نیست. اتصال درایو را بررسی کنید و دوباره پوشه را انتخاب کنید.");
            case StorageFailureKind.IoError:
                return T($"A read or write failed during the test: {detail}", $"در حین تست، خواندن یا نوشتن ناموفق بود: {detail}");
            default:
                return T("The test was stopped. Nothing was recorded for it.", "تست متوقف شد. چیزی برای آن ثبت نشد.");
        }
    }
}

/// <summary>
/// Measures a drive the way CrystalDiskMark does: a pre-filled test file, uncached direct I/O
/// (FILE_FLAG_NO_BUFFERING on Windows), incompressible random data, several timed runs per test,
/// best run reported.
/// </summary>
public sealed unsafe class StorageBenchmarkEngine
{
    public static readonly StorageTestKind[] Tests = StorageTestKinds.All;

    /// <summary>FILE_FLAG_NO_BUFFERING: bypasses the Windows file cache.</summary>
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    public StorageTestProfile Profile { get; }

    public StorageBenchmarkEngine(StorageTestProfile? profile = null)
    {
        Profile = profile ?? StorageTestProfile.Standard;
    }

    // ── Target checks ──

    public static long? AvailableBytes(string folder)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root)) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"Local Disk (C:)" style name for the drive holding the folder.</summary>
    public static string? VolumeName(string folder)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root)) return null;
            var drive = new DriveInfo(root);
            string letter = drive.Name.TrimEnd('\\', '/');
            string label = drive.VolumeLabel;
            if (string.IsNullOrWhiteSpace(label)) return OperatingSystem.IsWindows() ? $"{T("Local Disk", "دیسک محلی")} ({letter})" : drive.Name;
            return OperatingSystem.IsWindows() ? $"{label} ({letter})" : label;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns a problem with the folder, or null when it looks usable.</summary>
    public StorageBenchmarkException? CheckTarget(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return new StorageBenchmarkException(StorageFailureKind.NotFound);
        }
        catch
        {
            return new StorageBenchmarkException(StorageFailureKind.NotFound);
        }
        if (AvailableBytes(folder) is { } available && available < Profile.RequiredFreeBytes)
            return new StorageBenchmarkException(StorageFailureKind.NotEnoughSpace, Profile.RequiredFreeBytes);
        // Probe write access with a tiny file.
        string probe = Path.Combine(folder, $".kavosh-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
        }
        catch (Exception e)
        {
            return Map(e);
        }
        finally
        {
            try { File.Delete(probe); } catch { /* already gone */ }
        }
        return null;
    }

    internal static StorageBenchmarkException Map(Exception e)
    {
        switch (e)
        {
            case StorageBenchmarkException s:
                return s;
            case UnauthorizedAccessException:
                return new StorageBenchmarkException(StorageFailureKind.NotWritable);
            case DirectoryNotFoundException:
            case FileNotFoundException:
            case DriveNotFoundException:
                return new StorageBenchmarkException(StorageFailureKind.NotFound);
            case IOException io:
                int code = io.HResult & 0xFFFF;
                return code switch
                {
                    0x13 => new StorageBenchmarkException(StorageFailureKind.WriteProtected),           // ERROR_WRITE_PROTECT
                    0x15 => new StorageBenchmarkException(StorageFailureKind.NotFound),                 // ERROR_NOT_READY
                    0x27 or 0x70 => new StorageBenchmarkException(StorageFailureKind.NotEnoughSpace),   // disk full
                    0x57 => new StorageBenchmarkException(StorageFailureKind.NoDirectIO),               // ERROR_INVALID_PARAMETER
                    0x05 => new StorageBenchmarkException(StorageFailureKind.NotWritable),              // ERROR_ACCESS_DENIED
                    _ => new StorageBenchmarkException(StorageFailureKind.IoError, detail: io.Message),
                };
            default:
                return new StorageBenchmarkException(StorageFailureKind.IoError, detail: e.Message);
        }
    }

    private static SafeFileHandle OpenTestFile(string path, FileMode mode)
    {
        var options = OperatingSystem.IsWindows() ? NoBuffering : FileOptions.None;
        return File.OpenHandle(path, mode, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, options);
    }

    // ── Run ──

    public StorageBenchmarkResult Run(string folder, Func<bool> isCancelled, Action<StorageBenchmarkProgress> progress)
    {
        if (CheckTarget(folder) is { } problem) throw problem;

        string path = Path.Combine(folder, $".kavosh-speedtest-{Guid.NewGuid():N}.tmp");
        try
        {
            int totalRuns = Tests.Length * 2 * Profile.Runs;
            const double prepareWeight = 0.08;
            var completed = new List<StorageTestMeasurement>();
            var report = new StorageBenchmarkProgress { Stage = StorageStage.Preparing, Completed = Array.Empty<StorageTestMeasurement>() };
            progress(report);

            // 1. Fill the test file with incompressible data.
            PrepareFile(path, isCancelled, fraction =>
            {
                report.OverallFraction = fraction * prepareWeight;
                progress(report);
            });

            int runsDone = 0;
            foreach (var kind in Tests)
            {
                foreach (var op in StorageTestKinds.Operations)
                {
                    report.Stage = StorageStage.Settling;
                    report.LiveMBps = null;
                    progress(report);
                    Settle(isCancelled);

                    StorageTestMeasurement? best = null;
                    for (int run = 1; run <= Profile.Runs; run++)
                    {
                        if (isCancelled()) throw new StorageBenchmarkException(StorageFailureKind.Cancelled);
                        report.Stage = StorageStage.Running;
                        report.Kind = kind;
                        report.Operation = op;
                        report.Run = run;
                        report.Of = Profile.Runs;
                        progress(report);
                        double baseRuns = runsDone;
                        var measurement = Measure(kind, op, path, isCancelled, (live, runFraction) =>
                        {
                            report.LiveMBps = live;
                            report.OverallFraction = prepareWeight + (1 - prepareWeight) * (baseRuns + runFraction) / totalRuns;
                            progress(report);
                        });
                        if (best is null || measurement.MbPerSecond > best.MbPerSecond) best = measurement;
                        runsDone++;
                    }
                    if (best is not null) completed.Add(best);
                    report.Completed = completed.ToArray();
                    progress(report);
                }
            }

            report.Stage = StorageStage.CleaningUp;
            progress(report);
            report.Stage = StorageStage.Finished;
            report.OverallFraction = 1;
            progress(report);

            return new StorageBenchmarkResult
            {
                TargetPath = folder,
                VolumeName = VolumeName(folder),
                ProfileName = Profile.Name,
                FileSizeBytes = Profile.FileSizeBytes,
                RunsPerTest = Profile.Runs,
                Measurements = completed,
            };
        }
        catch (Exception e) when (e is not StorageBenchmarkException)
        {
            throw Map(e);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    private void Settle(Func<bool> isCancelled)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < Profile.SettleSeconds)
        {
            if (isCancelled()) throw new StorageBenchmarkException(StorageFailureKind.Cancelled);
            Thread.Sleep(100);
        }
    }

    private void PrepareFile(string path, Func<bool> isCancelled, Action<double> report)
    {
        const int chunk = 8 * 1024 * 1024;
        byte* buffer = (byte*)NativeMemory.AlignedAlloc(chunk, 4096);
        try
        {
            var rng = new SplitMix64(0x5EED0FD15CUL);
            var span = new Span<byte>(buffer, chunk);
            rng.Fill(span);
            ulong* words = (ulong*)buffer;

            using var handle = OpenTestFile(path, FileMode.CreateNew);
            if (OperatingSystem.IsWindows()) File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.Temporary);
            long offset = 0;
            long lastReport = 0;
            while (offset < Profile.FileSizeBytes)
            {
                if (isCancelled()) throw new StorageBenchmarkException(StorageFailureKind.Cancelled);
                // Change a few words so no two chunks are identical.
                words[0] = rng.Next();
                words[chunk / 16] = rng.Next();
                int toWrite = (int)Math.Min(chunk, Profile.FileSizeBytes - offset);
                RandomAccess.Write(handle, new ReadOnlySpan<byte>(buffer, toWrite), offset);
                offset += toWrite;
                long now = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(lastReport, now).TotalSeconds > 0.2)
                {
                    report((double)offset / Profile.FileSizeBytes);
                    lastReport = now;
                }
            }
            using (var stream = new FileStream(handle, FileAccess.ReadWrite, 0)) stream.Flush(flushToDisk: true);
            report(1);
        }
        catch (IOException io) when ((io.HResult & 0xFFFF) is 0x27 or 0x70)
        {
            throw new StorageBenchmarkException(StorageFailureKind.NotEnoughSpace, Profile.RequiredFreeBytes);
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
        }
    }

    /// <summary>One timed run with <c>queueDepth</c> threads keeping requests in flight.</summary>
    private StorageTestMeasurement Measure(StorageTestKind kind, StorageOperation operation, string path,
        Func<bool> isCancelled, Action<double, double> live)
    {
        int threads = kind.QueueDepth();
        int blockSize = kind.BlockSize();
        long blocksInFile = Profile.FileSizeBytes / blockSize;
        var counters = new IoCounters(threads);
        double seconds = Profile.SecondsPerRun;
        long start = Stopwatch.GetTimestamp();
        long deadline = start + (long)(seconds * Stopwatch.Frequency);
        using var finished = new CountdownEvent(threads);

        for (int t = 0; t < threads; t++)
        {
            int threadIndex = t;
            var thread = new Thread(() =>
            {
                byte* buffer = null;
                SafeFileHandle? handle = null;
                try
                {
                    // Each thread has its own handle: Windows serialises synchronous I/O on a shared one.
                    handle = OpenTestFile(path, FileMode.Open);
                    buffer = (byte*)NativeMemory.AlignedAlloc((nuint)blockSize, 4096);
                    var rng = new SplitMix64(unchecked((ulong)(threadIndex + 1) * 0x9E3779B9UL ^ (ulong)start));
                    rng.Fill(new Span<byte>(buffer, blockSize));
                    long sequentialIndex = threadIndex;
                    long bytes = 0, ops = 0, latency = 0, lastEnd = start;
                    while (true)
                    {
                        long now = Stopwatch.GetTimestamp();
                        if (now >= deadline || counters.IsStopped) break;
                        long block;
                        if (kind.IsSequential())
                        {
                            block = sequentialIndex % blocksInFile;
                            sequentialIndex += threads;
                        }
                        else
                        {
                            block = (long)(rng.Next() % (ulong)blocksInFile);
                        }
                        long offset = block * blockSize;
                        long opStart = Stopwatch.GetTimestamp();
                        int result;
                        if (operation == StorageOperation.Read)
                        {
                            result = RandomAccess.Read(handle, new Span<byte>(buffer, blockSize), offset);
                        }
                        else
                        {
                            *(ulong*)buffer = rng.Next();
                            RandomAccess.Write(handle, new ReadOnlySpan<byte>(buffer, blockSize), offset);
                            result = blockSize;
                        }
                        long opEnd = Stopwatch.GetTimestamp();
                        bytes += result;
                        ops++;
                        latency += opEnd - opStart;
                        lastEnd = opEnd;
                        if ((ops & 15) == 0) counters.Update(threadIndex, bytes, ops, latency, lastEnd);
                    }
                    counters.Update(threadIndex, bytes, ops, latency, lastEnd);
                }
                catch (Exception e)
                {
                    counters.Fail(e);
                }
                finally
                {
                    handle?.Dispose();
                    if (buffer != null) NativeMemory.AlignedFree(buffer);
                    finished.Signal();
                }
            })
            {
                IsBackground = true,
                Name = "Kavosh I/O",
            };
            thread.Start();
        }

        long lastBytes = 0;
        long lastTime = start;
        while (!finished.Wait(250))
        {
            if (isCancelled()) counters.Stop();
            long now = Stopwatch.GetTimestamp();
            var totals = counters.Totals();
            double dt = (double)(now - lastTime) / Stopwatch.Frequency;
            if (dt > 0)
            {
                double mbps = (totals.Bytes - lastBytes) / dt / 1_000_000;
                live(Math.Max(0, mbps), Math.Min(1, (double)(now - start) / (seconds * Stopwatch.Frequency)));
            }
            lastBytes = totals.Bytes;
            lastTime = now;
        }
        if (isCancelled()) throw new StorageBenchmarkException(StorageFailureKind.Cancelled);
        if (counters.Error is { } error) throw Map(error);

        var final = counters.Totals();
        long elapsedTicks = Math.Max(1, final.LastEnd > start ? final.LastEnd - start : (long)(seconds * Stopwatch.Frequency));
        double elapsed = (double)elapsedTicks / Stopwatch.Frequency;
        return new StorageTestMeasurement
        {
            Kind = kind,
            Operation = operation,
            MbPerSecond = final.Bytes / elapsed / 1_000_000,
            Iops = final.Ops / elapsed,
            AverageLatencyMs = final.Ops > 0 ? (double)final.LatencyTicks / Stopwatch.Frequency * 1000 / final.Ops : 0,
        };
    }
}

/// <summary>Per-thread I/O counters, read by the coordinating thread for the live graph.</summary>
internal sealed class IoCounters
{
    private readonly object _lock = new();
    private readonly long[] _bytes, _ops, _latency, _lastEnd;
    private bool _stopped;
    private Exception? _firstError;

    public IoCounters(int threads)
    {
        _bytes = new long[threads];
        _ops = new long[threads];
        _latency = new long[threads];
        _lastEnd = new long[threads];
    }

    public void Update(int thread, long bytes, long ops, long latencyTicks, long lastEnd)
    {
        lock (_lock)
        {
            _bytes[thread] = bytes;
            _ops[thread] = ops;
            _latency[thread] = latencyTicks;
            _lastEnd[thread] = lastEnd;
        }
    }

    public (long Bytes, long Ops, long LatencyTicks, long LastEnd) Totals()
    {
        lock (_lock) return (_bytes.Sum(), _ops.Sum(), _latency.Sum(), _lastEnd.Max());
    }

    public bool IsStopped
    {
        get { lock (_lock) return _stopped; }
    }

    public void Stop()
    {
        lock (_lock) _stopped = true;
    }

    public Exception? Error
    {
        get { lock (_lock) return _firstError; }
    }

    public void Fail(Exception error)
    {
        lock (_lock)
        {
            _firstError ??= error;
            _stopped = true;
        }
    }
}
