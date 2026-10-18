using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Kavosh.Core.Loc;

namespace Kavosh.Core;

/// <summary>Where the app keeps its files: %APPDATA%\Kavosh Diagnostics.</summary>
public static class AppPaths
{
    public const string FolderName = "Kavosh Diagnostics";

    /// <summary>Overrides the folder (used by tests).</summary>
    public static string? OverrideDirectory { get; set; }

    public static string SupportDirectory
    {
        get
        {
            string folder = OverrideDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
                FolderName);
            try { Directory.CreateDirectory(folder); } catch { /* reported when a file is written */ }
            return folder;
        }
    }

    public static string SettingsFile => Path.Combine(SupportDirectory, "settings.json");
    public static string LastReportFile => Path.Combine(SupportDirectory, "last-report.json");
    public static string UnfinishedRunFile => Path.Combine(SupportDirectory, "unfinished-run.json");
    public static string LastStorageFile => Path.Combine(SupportDirectory, "last-ssd-test.json");
    public static string HistoryCsv => Path.Combine(SupportDirectory, "results.csv");
}

public enum ScreenWasherPattern { Noise, Rgbw, Gradient }

public enum ScreenWasherDuration { Quick = 300, Standard = 900, Deep = 1800, Infinite = 0 }

public static class ScreenWasherOptions
{
    public static readonly ScreenWasherPattern[] Patterns = { ScreenWasherPattern.Noise, ScreenWasherPattern.Rgbw, ScreenWasherPattern.Gradient };
    public static readonly ScreenWasherDuration[] Durations =
        { ScreenWasherDuration.Quick, ScreenWasherDuration.Standard, ScreenWasherDuration.Deep, ScreenWasherDuration.Infinite };

    public static bool IsFlashing(this ScreenWasherPattern p) => p != ScreenWasherPattern.Gradient;

    public static string Title(this ScreenWasherPattern p) => p switch
    {
        ScreenWasherPattern.Noise => T("Static noise", "نویز ثابت"),
        ScreenWasherPattern.Rgbw => T("RGBW cycle", "چرخه RGBW"),
        _ => T("Gradient sweep", "گرادیان متحرک"),
    };

    public static string Detail(this ScreenWasherPattern p) => p switch
    {
        ScreenWasherPattern.Noise => T("strongest effect (flashes)", "قوی‌ترین اثر (چشمک می‌زند)"),
        ScreenWasherPattern.Rgbw => T("exercises subpixels (flashes)", "زیرپیکسل‌ها را فعال می‌کند (چشمک می‌زند)"),
        _ => T("gentle, no flashing", "ملایم، بدون چشمک"),
    };

    public static double? Seconds(this ScreenWasherDuration d) => d == ScreenWasherDuration.Infinite ? null : (double)(int)d;

    public static string Title(this ScreenWasherDuration d) => d switch
    {
        ScreenWasherDuration.Quick => T("5 minutes — Quick Clean", "۵ دقیقه — پاک‌سازی سریع"),
        ScreenWasherDuration.Standard => T("15 minutes — Standard", "۱۵ دقیقه — استاندارد"),
        ScreenWasherDuration.Deep => T("30 minutes — Deep Cycle", "۳۰ دقیقه — چرخه عمیق"),
        _ => T("Infinite — stop it manually", "نامحدود — دستی متوقف کنید"),
    };

    public static string Detail(this ScreenWasherDuration d) => d switch
    {
        ScreenWasherDuration.Quick => T("Runs for 5 minutes, then stops on its own. Good for faint retention that appeared in the last few minutes.",
                                        "۵ دقیقه اجرا می‌شود و خودش متوقف می‌شود. برای اثر کم‌رنگی که در چند دقیقه اخیر ایجاد شده مناسب است."),
        ScreenWasherDuration.Standard => T("Runs for 15 minutes, then stops on its own. The usual choice for retention that's still visible after a quick pass.",
                                           "۱۵ دقیقه اجرا می‌شود و خودش متوقف می‌شود. انتخاب معمول برای اثری که بعد از پاک‌سازی سریع هنوز دیده می‌شود."),
        ScreenWasherDuration.Deep => T("Runs for 30 minutes, then stops on its own. For stubborn retention from a static image that was on screen for hours.",
                                       "۳۰ دقیقه اجرا می‌شود و خودش متوقف می‌شود. برای اثر سمج تصویری که ساعت‌ها روی صفحه بوده است."),
        _ => T("Runs until you stop it with Esc or the Stop button. Nothing will end it automatically.",
               "تا وقتی با Esc یا دکمه توقف متوقفش کنید اجرا می‌شود. خودبه‌خود تمام نمی‌شود."),
    };
}

/// <summary>User preferences, saved as JSON.</summary>
public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.En;
    /// <summary>CPU and GPU test length in seconds (includes the sustained phase).</summary>
    public int PerformanceTestSeconds { get; set; } = 60;
    public string StorageProfile { get; set; } = "standard";
    public ScreenWasherDuration WasherDuration { get; set; } = ScreenWasherDuration.Standard;
    public ScreenWasherPattern WasherPattern { get; set; } = ScreenWasherPattern.Gradient;
    public bool WasherRotate { get; set; } = true;
    public bool NoBuiltInInputs { get; set; }

    public static readonly int[] DurationChoices = { 30, 60, 180, 300, 600 };

    [JsonIgnore]
    public StorageTestProfile StorageTestProfile => StorageProfile == "quick" ? StorageTestProfile.Quick : StorageTestProfile.Standard;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Reads settings, keeping defaults for anything missing or unreadable (older or partial files).</summary>
    public static AppSettings Parse(string json)
    {
        var settings = new AppSettings();
        JsonObject? root;
        try { root = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return settings; }
        if (root is null) return settings;

        T? Read<T>(string name)
        {
            try { return root[name] is { } node ? node.Deserialize<T>(ResultStore.Options) : default; }
            catch { return default; }
        }

        if (Read<AppLanguage?>("language") is { } language) settings.Language = language;
        if (Read<int?>("performanceTestSeconds") is { } seconds and > 0) settings.PerformanceTestSeconds = seconds;
        if (Read<string>("storageProfile") is { } profile) settings.StorageProfile = profile;
        if (Read<ScreenWasherDuration?>("washerDuration") is { } duration && Enum.IsDefined(duration)) settings.WasherDuration = duration;
        if (Read<ScreenWasherPattern?>("washerPattern") is { } pattern) settings.WasherPattern = pattern;
        if (Read<bool?>("washerRotate") is { } rotate) settings.WasherRotate = rotate;
        if (Read<bool?>("noBuiltInInputs") is { } noInputs) settings.NoBuiltInInputs = noInputs;
        return settings;
    }
}

/// <summary>Reads and writes the app's JSON files.</summary>
public static class ResultStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static void Save<T>(T value, string path)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(value), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    public static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }

    public static void Remove(string path)
    {
        try { File.Delete(path); } catch { /* already gone */ }
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            return File.Exists(AppPaths.SettingsFile) ? AppSettings.Parse(File.ReadAllText(AppPaths.SettingsFile)) : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        try { Save(settings, AppPaths.SettingsFile); } catch { /* not fatal */ }
    }

    /// <summary>Appends a run to the history CSV, writing the header first if the file is new.</summary>
    public static void AppendToHistory(FinalReport report, HealthEvaluation evaluation, StorageBenchmarkResult? storage)
    {
        string path = AppPaths.HistoryCsv;
        var text = new StringBuilder();
        if (!File.Exists(path)) text.Append(string.Join(",", CsvExport.Header)).Append("\r\n");
        text.Append(CsvExport.Row(report, evaluation, storage)).Append("\r\n");
        // A byte-order mark on a new file lets Excel open the Persian text correctly.
        bool isNew = !File.Exists(path);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        if (isNew) stream.Write(Encoding.UTF8.GetPreamble());
        stream.Write(Encoding.UTF8.GetBytes(text.ToString()));
    }
}

/// <summary>Spreadsheet export. Column names stay in English and match the macOS app, so files line up.</summary>
public static class CsvExport
{
    public static readonly string[] Header =
    {
        "Recorded (UTC)", "Model", "Model Identifier", "CPU", "CPU Cores", "Memory (GB)", "GPU", "GPU Cores",
        "OS", "Health Score", "Grade",
        "CPU Single Core", "CPU Multi Core", "CPU Sustained", "CPU Wrong Results", "CPU Throttled",
        "GPU Score", "GPU Sustained", "GPU (avg fps)",
        "RAM Write (MB/s)", "RAM Read (MB/s)", "Memory Errors",
        "Battery Health (%)", "Battery Cycles", "Battery Discharge (W)",
        "Display Issues", "Keyboard Issues", "Speaker Issues", "Mic/Camera Issues", "Worst Drive Score",
        "SSD Seq Read (MB/s)", "SSD Seq Write (MB/s)", "SSD Random Read (MB/s)", "SSD Random Write (MB/s)",
        "SSD RND4K Q1T1 Read (MB/s)", "SSD RND4K Q1T1 Write (MB/s)",
    };

    public static string Escape(string field)
    {
        if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }

    private static string Num(double? value, int decimals = 1) =>
        value is { } v && double.IsFinite(v) ? Fmt.Fixed(v, decimals) : "";

    public static string Row(FinalReport report, HealthEvaluation evaluation, StorageBenchmarkResult? storage)
    {
        var specs = report.Specs;
        string Issues(HealthComponentKind kind) =>
            evaluation.Components.FirstOrDefault(c => c.Kind == kind) is { } c ? c.Issues.Count.ToString(CultureInfo.InvariantCulture) : "";
        string Mbps(StorageTestKind kind, StorageOperation op) => Num(storage?.Measurement(kind, op)?.MbPerSecond, 1);
        var battery = report.Battery is { BatteryPresent: true } b ? b : null;
        string[] fields =
        {
            (report.FinishedAt ?? report.StartedAt).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            specs?.ModelName ?? "",
            specs?.ModelIdentifier ?? "",
            specs?.CpuName ?? "",
            specs is null ? "" : specs.CpuCores.ToString(CultureInfo.InvariantCulture),
            Num(specs?.RamCapacityGB, 0),
            specs?.GpuName ?? "",
            specs?.GpuCoreCount?.ToString(CultureInfo.InvariantCulture) ?? "",
            specs?.OperatingSystem ?? "",
            Num(evaluation.Score, 1),
            evaluation.Grade?.Letter() ?? "",
            Num(report.Cpu?.SingleCoreScore, 0),
            Num(report.Cpu?.Score, 0),
            Num(report.Cpu?.SustainedScore, 0),
            report.Cpu?.WrongResults.ToString(CultureInfo.InvariantCulture) ?? "",
            report.Cpu is null ? "" : report.Cpu.ThermalThrottleDetected ? "yes" : "no",
            Num(report.Gpu?.Score, 0),
            Num(report.Gpu?.SustainedScore, 0),
            Num(report.Gpu?.AverageFps, 2),
            Num(report.Memory?.AverageWriteThroughputMBps, 0),
            Num(report.Memory?.AverageReadThroughputMBps, 0),
            report.Memory?.ErrorsDetected.ToString(CultureInfo.InvariantCulture) ?? "",
            battery is null ? "" : Num(battery.HealthPercent, 1),
            battery?.CycleCount.ToString(CultureInfo.InvariantCulture) ?? "",
            Num(report.Battery?.DischargeRateWatts, 1),
            Issues(HealthComponentKind.Display),
            Issues(HealthComponentKind.Keyboard),
            Issues(HealthComponentKind.Speakers),
            Issues(HealthComponentKind.MicCamera),
            Num(report.Drives?.WorstScore, 0),
            Mbps(StorageTestKind.Seq1mQ8T1, StorageOperation.Read),
            Mbps(StorageTestKind.Seq1mQ8T1, StorageOperation.Write),
            Mbps(StorageTestKind.Rnd4kQ32T1, StorageOperation.Read),
            Mbps(StorageTestKind.Rnd4kQ32T1, StorageOperation.Write),
            Mbps(StorageTestKind.Rnd4kQ1T1, StorageOperation.Read),
            Mbps(StorageTestKind.Rnd4kQ1T1, StorageOperation.Write),
        };
        return string.Join(",", fields.Select(Escape));
    }

    /// <summary>A complete CSV document for one run.</summary>
    public static string Document(FinalReport report, HealthEvaluation evaluation, StorageBenchmarkResult? storage) =>
        string.Join(",", Header) + "\r\n" + Row(report, evaluation, storage) + "\r\n";
}
