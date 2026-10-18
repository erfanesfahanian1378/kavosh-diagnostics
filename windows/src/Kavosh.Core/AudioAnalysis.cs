namespace Kavosh.Core;

/// <summary>Microphone level and pitch estimation.</summary>
public static class AudioAnalysis
{
    /// <summary>Root-mean-square level in dBFS (0 = full scale, -160 for silence).</summary>
    public static double RmsDecibels(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return -160;
        double sum = 0;
        foreach (float s in samples) sum += (double)s * s;
        double rms = Math.Sqrt(sum / samples.Length);
        return rms > 0 ? Math.Max(-160, 20 * Math.Log10(rms)) : -160;
    }

    /// <summary>Maps dBFS to 0…1 for a level meter (-60 dB → 0, 0 dB → 1).</summary>
    public static double MeterLevel(double decibels, double floor = -60)
    {
        if (decibels <= floor) return 0;
        return Math.Min(1, (decibels - floor) / -floor);
    }

    /// <summary>
    /// Estimates the fundamental frequency with a YIN-style difference function. Returns null for
    /// silence or noise without a clear pitch.
    /// </summary>
    public static double? EstimatePitch(ReadOnlySpan<float> samples, double sampleRate, double minFrequency = 70, double maxFrequency = 1200)
    {
        int n = samples.Length;
        if (n < 256 || sampleRate <= 0) return null;
        if (RmsDecibels(samples) <= -50) return null;

        int minLag = Math.Max(2, (int)(sampleRate / maxFrequency));
        int maxLag = Math.Min(n / 2, (int)(sampleRate / minFrequency));
        if (maxLag <= minLag + 2) return null;

        int window = n - maxLag;
        var difference = new double[maxLag + 1];
        for (int lag = 1; lag <= maxLag; lag++)
        {
            double sum = 0;
            for (int i = 0; i < window; i++)
            {
                double d = (double)samples[i] - samples[i + lag];
                sum += d * d;
            }
            difference[lag] = sum;
        }

        // Cumulative mean normalized difference.
        var normalized = new double[maxLag + 1];
        Array.Fill(normalized, 1.0);
        double running = 0;
        for (int lag = 1; lag <= maxLag; lag++)
        {
            running += difference[lag];
            normalized[lag] = running > 0 ? difference[lag] * lag / running : 1;
        }

        const double threshold = 0.15;
        int? bestLag = null;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            if (normalized[lag] < threshold)
            {
                int l = lag;
                while (l + 1 <= maxLag && normalized[l + 1] < normalized[l]) l++;
                bestLag = l;
                break;
            }
        }
        if (bestLag is null)
        {
            // No confident dip; take the global minimum only when it is reasonably clear.
            double minValue = double.PositiveInfinity;
            int minIndex = minLag;
            for (int l = minLag; l <= maxLag; l++)
            {
                if (normalized[l] < minValue)
                {
                    minValue = normalized[l];
                    minIndex = l;
                }
            }
            if (minValue < 0.35) bestLag = minIndex;
        }
        if (bestLag is not { } tau) return null;

        // Parabolic interpolation around the dip for sub-sample accuracy.
        double refined = tau;
        if (tau > 1 && tau < maxLag)
        {
            double a = normalized[tau - 1], b = normalized[tau], c = normalized[tau + 1];
            double denominator = a - 2 * b + c;
            if (Math.Abs(denominator) > 1e-12) refined += 0.5 * (a - c) / denominator;
        }
        double frequency = sampleRate / refined;
        if (frequency < minFrequency * 0.9 || frequency > maxFrequency * 1.1) return null;
        return frequency;
    }

    /// <summary>Musical note name for a frequency, e.g. 440 → "A4".</summary>
    public static string NoteName(double frequency)
    {
        if (frequency <= 0) return "—";
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        int midi = (int)Math.Round(69 + 12 * Math.Log2(frequency / 440), MidpointRounding.AwayFromZero);
        int index = ((midi % 12) + 12) % 12;
        return $"{names[index]}{midi / 12 - 1}";
    }
}
