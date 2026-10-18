using System.Globalization;

namespace Kavosh.Core;

/// <summary>Number formatting. Numbers always use Latin digits so values line up in both languages.</summary>
public static class Fmt
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>"1,234.5" style with a fixed number of decimals.</summary>
    public static string Number(double value, int decimals = 0)
    {
        if (!double.IsFinite(value)) return "—";
        return value.ToString("N" + Math.Clamp(decimals, 0, 4), Invariant);
    }

    public static string Number(long value) => value.ToString("N0", Invariant);

    /// <summary>Plain "12.3" without grouping.</summary>
    public static string Fixed(double value, int decimals = 1)
    {
        if (!double.IsFinite(value)) return "—";
        return value.ToString("F" + Math.Clamp(decimals, 0, 6), Invariant);
    }

    public static string Percent(double value, int decimals = 0) => Fixed(value, decimals) + "%";

    /// <summary>Decimal gigabytes like Windows Explorer's drive labels ("512 GB", "1.02 TB").</summary>
    public static string StorageSize(ulong bytes)
    {
        double gb = bytes / 1_000_000_000.0;
        if (gb >= 1000) return Fixed(gb / 1000, 2) + " TB";
        if (gb >= 100) return Fixed(gb, 0) + " GB";
        if (gb >= 10) return Fixed(gb, 1) + " GB";
        return Fixed(gb, 2) + " GB";
    }

    public static string MemoryGB(double gb)
    {
        if (Math.Abs(gb - Math.Round(gb)) < 0.05) return $"{(int)Math.Round(gb)} GB";
        return Fixed(gb, 1) + " GB";
    }

    public static string MBps(double value) => Number(value, value < 100 ? 1 : 0) + " MB/s";

    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "—";
        long total = (long)Math.Round(seconds);
        long m = total / 60, s = total % 60;
        if (m >= 60) return string.Format(Invariant, "{0}:{1:00}:{2:00}", m / 60, m % 60, s);
        return string.Format(Invariant, "{0}:{1:00}", m, s);
    }

    public static string Celsius(double? value) => value is { } v && double.IsFinite(v) ? Fixed(v, 0) + "°C" : "—";

    public static string Watts(double? value) => value is { } v && double.IsFinite(v) ? Fixed(v, 1) + " W" : "—";
}
