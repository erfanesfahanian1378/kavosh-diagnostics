namespace Kavosh.Core;

/// <summary>
/// Small, fast, deterministic random generator. The benchmarks use it so every computer does
/// exactly the same work and the results can be checked. Identical to the macOS version.
/// </summary>
public struct SplitMix64
{
    public ulong State;

    public SplitMix64(ulong seed) => State = seed;

    public ulong Next()
    {
        unchecked
        {
            State += 0x9E3779B97F4A7C15UL;
            ulong z = State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Stateless mix of one value (same as a single <see cref="Next"/> step).</summary>
    public static ulong Mix(ulong value)
    {
        unchecked
        {
            ulong z = value + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public void Fill(Span<byte> buffer)
    {
        int words = buffer.Length / 8;
        var typed = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(buffer[..(words * 8)]);
        for (int i = 0; i < typed.Length; i++) typed[i] = Next();
        int tail = buffer.Length % 8;
        if (tail > 0)
        {
            ulong last = Next();
            for (int i = 0; i < tail; i++)
            {
                buffer[words * 8 + i] = (byte)last;
                last >>= 8;
            }
        }
    }
}
