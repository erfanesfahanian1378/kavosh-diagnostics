using System.Diagnostics;
using Avalonia.Threading;
using Kavosh.Core;
using Kavosh.Hardware;
using static Kavosh.Core.Loc;

namespace Kavosh.App;

public enum Screen { Dashboard, Suite, Results, Storage, WasherSetup }

/// <summary>App-wide state: settings, the PC's specs, saved results and which screen is showing.</summary>
public sealed class AppModel
{
    public Screen Screen { get; private set; } = Screen.Dashboard;
    public AppSettings Settings { get; private set; }
    public SystemSpecs? Specs { get; private set; }
    public bool SpecsComplete { get; private set; }
    public DriveHealthReport? DriveHealth { get; private set; }
    public bool DriveHealthLoading { get; private set; }
    public FinalReport? LastReport { get; private set; }
    public StorageBenchmarkResult? LastStorage { get; private set; }
    public FinalReport? UnfinishedRun { get; private set; }
    public FinalReport? ShownReport { get; private set; }
    public TestSuiteRunner? Runner { get; private set; }

    public LiveTelemetry Telemetry { get; } = new();

    /// <summary>The screen to show changed (or a new runner / report is ready).</summary>
    public event Action? ScreenChanged;
    /// <summary>The whole window should be rebuilt (language changed).</summary>
    public event Action? LanguageChanged;
    public event Action? SpecsChanged;
    public event Action? DriveHealthChanged;
    public event Action? ResultsChanged;
    public event Action? SettingsChanged;
    /// <summary>A message to show the user in a dialog.</summary>
    public event Action<string>? Notice;
    /// <summary>Asks the window to save a file: (suggested name, contents).</summary>
    public Func<string, string, Task<string?>>? SaveFileHandler { get; set; }

    public AppModel(bool startServices = true)
    {
        Settings = ResultStore.LoadSettings();
        Loc.Language = Settings.Language;
        LastReport = ResultStore.Load<FinalReport>(AppPaths.LastReportFile);
        LastStorage = ResultStore.Load<StorageBenchmarkResult>(AppPaths.LastStorageFile);
        if (ResultStore.Load<FinalReport>(AppPaths.UnfinishedRunFile) is { IsFinished: false } unfinished) UnfinishedRun = unfinished;
        if (startServices)
        {
            Telemetry.Start();
            LoadSpecs();
            RefreshDriveHealth();
        }
    }

    private static void OnUi(Action action) => Dispatcher.UIThread.Post(action);

    // ── Specs ──

    public void LoadSpecs()
    {
        Task.Run(() =>
        {
            var quick = Platform.QuickSpecs();
            OnUi(() =>
            {
                Specs = quick;
                SpecsChanged?.Invoke();
            });
            var full = Platform.QuickSpecs();
            Platform.AddSlowDetails(full);
            OnUi(() =>
            {
                Specs = full;
                SpecsComplete = true;
                SpecsChanged?.Invoke();
            });
        });
    }

    public void RefreshDriveHealth()
    {
        if (DriveHealthLoading) return;
        DriveHealthLoading = true;
        DriveHealthChanged?.Invoke();
        Task.Run(() =>
        {
            var report = Platform.ReadDriveHealth();
            OnUi(() =>
            {
                DriveHealth = report;
                DriveHealthLoading = false;
                DriveHealthChanged?.Invoke();
            });
        });
    }

    /// <summary>Lets tests and screenshots show data without touching the hardware.</summary>
    internal void SetSampleData(SystemSpecs specs, DriveHealthReport drives, FinalReport? lastReport, StorageBenchmarkResult? storage)
    {
        Specs = specs;
        SpecsComplete = true;
        DriveHealth = drives;
        LastReport = lastReport;
        LastStorage = storage;
    }

    // ── Settings ──

    public void UpdateSettings(Action<AppSettings> change)
    {
        var updated = Settings.Clone();
        change(updated);
        bool languageChanged = updated.Language != Settings.Language;
        Settings = updated;
        Loc.Language = updated.Language;
        ResultStore.SaveSettings(updated);
        if (languageChanged) LanguageChanged?.Invoke();
        else SettingsChanged?.Invoke();
    }

    public void SetLanguage(AppLanguage language) => UpdateSettings(s => s.Language = language);

    // ── Navigation ──

    public void Show(Screen screen)
    {
        Screen = screen;
        ScreenChanged?.Invoke();
    }

    public void GoToDashboard()
    {
        if (Runner is not null) return;
        Show(Screen.Dashboard);
    }

    // ── Test runs ──

    public void StartRun(IEnumerable<TestModule> plan)
    {
        // Desktops with no built-in keyboard/touchpad/camera bypass those checks instead of failing them.
        var report = new FinalReport(plan, Specs, Settings.NoBuiltInInputs);
        Runner = new TestSuiteRunner(report, this, resumed: false);
        Show(Screen.Suite);
    }

    public void ResumeUnfinished()
    {
        if (UnfinishedRun is not { } saved) return;
        saved.Specs ??= Specs;
        Runner = new TestSuiteRunner(saved, this, resumed: true);
        UnfinishedRun = null;
        Show(Screen.Suite);
    }

    public void DiscardUnfinished()
    {
        UnfinishedRun = null;
        ResultStore.Remove(AppPaths.UnfinishedRunFile);
        ResultsChanged?.Invoke();
    }

    public void SaveProgress(FinalReport report)
    {
        if (report.Completed.Count == 0 && report.Failures.Count == 0 && report.Skipped.Count == 0) return;
        try { ResultStore.Save(report, AppPaths.UnfinishedRunFile); } catch { /* not fatal */ }
    }

    public void RunFinished(FinalReport report)
    {
        report.FinishedAt ??= DateTime.UtcNow;
        report.Specs ??= Specs;
        var evaluation = BenchmarkScoring.Evaluate(report);
        try { ResultStore.Save(report, AppPaths.LastReportFile); } catch { /* not fatal */ }
        try { ResultStore.AppendToHistory(report, evaluation, LastStorage); } catch { /* not fatal */ }
        ResultStore.Remove(AppPaths.UnfinishedRunFile);
        UnfinishedRun = null;
        LastReport = report;
        if (report.Drives is not null) DriveHealth = report.Drives;
        ShownReport = report;
        Runner = null;
        ResultsChanged?.Invoke();
        Show(Screen.Results);
    }

    public void RunStopped(FinalReport report)
    {
        Runner = null;
        if (report.Completed.Count > 0 || report.Failures.Count > 0 || report.Skipped.Count > 0)
        {
            SaveProgress(report);
            UnfinishedRun = report;
        }
        ResultsChanged?.Invoke();
        Show(Screen.Dashboard);
    }

    public void ShowReport(FinalReport report)
    {
        ShownReport = report;
        Show(Screen.Results);
    }

    // ── SSD test ──

    public void StorageFinished(StorageBenchmarkResult result)
    {
        LastStorage = result;
        try { ResultStore.Save(result, AppPaths.LastStorageFile); } catch { /* not fatal */ }
        ResultsChanged?.Invoke();
    }

    // ── Export ──

    public async void ExportCsv(FinalReport report)
    {
        string model = (string.IsNullOrWhiteSpace(report.Specs?.ModelIdentifier) ? report.Specs?.ModelName ?? "PC" : report.Specs!.ModelIdentifier)
            .Replace(',', '-').Replace(' ', '-');
        foreach (char c in Path.GetInvalidFileNameChars()) model = model.Replace(c, '-');
        string name = $"Kavosh-{model}-{(report.FinishedAt ?? report.StartedAt).ToLocalTime():yyyy-MM-dd-HHmm}.csv";
        string text = CsvExport.Document(report, BenchmarkScoring.Evaluate(report), LastStorage);
        if (SaveFileHandler is null) return;
        try
        {
            string? path = await SaveFileHandler(name, text);
            if (path is not null) RevealInExplorer(path);
        }
        catch (Exception e)
        {
            Notice?.Invoke(T($"Couldn't save the CSV file: {e.Message}", $"ذخیره فایل CSV ممکن نشد: {e.Message}"));
        }
    }

    public void OpenHistoryCsv()
    {
        string path = AppPaths.HistoryCsv;
        if (File.Exists(path)) OpenWithShell(path);
        else Notice?.Invoke(T("No CSV has been generated yet — finish a test run first.", "هنوز فایل CSV ساخته نشده است — ابتدا یک اجرای تست را کامل کنید."));
    }

    public void ShowNotice(string message) => Notice?.Invoke(message);

    public static void OpenWithShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* nothing registered for it */ }
    }

    public static void RevealInExplorer(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch { /* Explorer not available */ }
    }
}
