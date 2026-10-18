using System.Management;

namespace Kavosh.Hardware;

/// <summary>Small helpers around WMI queries. Every call is safe: failures return empty results.</summary>
internal static class Wmi
{
    public const string Cimv2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
    public const string RootWmi = @"root\wmi";

    public static List<Dictionary<string, object?>> Query(string query, string scope = Cimv2, TimeSpan? timeout = null)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = timeout ?? TimeSpan.FromSeconds(8), ReturnImmediately = true, Rewindable = false };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query), options);
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyData property in item.Properties)
                    {
                        try { row[property.Name] = property.Value; }
                        catch { /* unreadable property */ }
                    }
                    rows.Add(row);
                }
            }
        }
        catch
        {
            // Access denied, missing class, WMI service problems: treat as "no data".
        }
        return rows;
    }

    public static string? Str(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null) return null;
        string text = value.ToString()?.Trim() ?? "";
        return text.Length == 0 ? null : text;
    }

    public static long? Long(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null) return null;
        try { return Convert.ToInt64(value); }
        catch { return null; }
    }

    public static ulong? ULong(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null) return null;
        try { return Convert.ToUInt64(value); }
        catch { return null; }
    }

    public static bool? Bool(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null) return null;
        try { return Convert.ToBoolean(value); }
        catch { return null; }
    }

    public static byte[]? Bytes(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value as byte[] : null;

    /// <summary>WMI strings stored as arrays of UTF-16 code units (WmiMonitorID).</summary>
    public static string? CodeUnits(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is not Array array) return null;
        var chars = new List<char>();
        foreach (object? item in array)
        {
            int code = Convert.ToInt32(item);
            if (code == 0) break;
            chars.Add((char)code);
        }
        string text = new string(chars.ToArray()).Trim();
        return text.Length == 0 ? null : text;
    }
}
