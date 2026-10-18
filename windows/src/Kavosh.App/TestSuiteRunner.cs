using Kavosh.Core;

namespace Kavosh.App;

public enum StationState { Done, Failed, Skipped, Running, Waiting }

/// <summary>Walks through the stations of a run, collecting each one's result.</summary>
public sealed class TestSuiteRunner
{
    private readonly AppModel _model;

    public FinalReport Report { get; }
    public TestModule? Current { get; private set; }
    public bool Resumed { get; }

    /// <summary>Raised when the current station changes.</summary>
    public event Action? Changed;

    public TestSuiteRunner(FinalReport report, AppModel model, bool resumed)
    {
        Report = report;
        _model = model;
        Resumed = resumed;
        Current = report.Remaining.Count > 0 ? report.Remaining[0] : null;
    }

    public IReadOnlyList<TestModule> Plan => Report.Plan;

    /// <summary>1-based number of the station that's running.</summary>
    public int StepNumber => Current is { } c && Plan.ToList().IndexOf(c) is var i and >= 0 ? i + 1 : Plan.Count;

    public AppSettings Settings => _model.Settings;
    public LiveTelemetry Telemetry => _model.Telemetry;
    public AppModel Model => _model;
    public bool IsSingleTest => Plan.Count == 1;
    public bool IsLastStation => Report.Remaining.Count <= 1;

    public StationState StateOf(TestModule module)
    {
        if (Report.FailureFor(module) is not null) return StationState.Failed;
        if (Report.Skipped.Contains(module)) return StationState.Skipped;
        if (Report.Completed.Contains(module)) return StationState.Done;
        if (module == Current) return StationState.Running;
        return StationState.Waiting;
    }

    public void Complete(TestModule module, Action<FinalReport> update)
    {
        if (Current != module) return;
        update(Report);
        if (!Report.Completed.Contains(module)) Report.Completed.Add(module);
        Advance();
    }

    public void Skip(TestModule module)
    {
        if (Current != module) return;
        if (!Report.Skipped.Contains(module)) Report.Skipped.Add(module);
        Advance();
    }

    public void Fail(TestModule module, string message)
    {
        if (Current != module) return;
        Report.Failures.RemoveAll(f => f.Module == module);
        Report.Failures.Add(new ModuleFailure(module, message));
        Advance();
    }

    private void Advance()
    {
        var remaining = Report.Remaining;
        Current = remaining.Count > 0 ? remaining[0] : null;
        if (Current is null)
        {
            Report.FinishedAt = DateTime.UtcNow;
            _model.RunFinished(Report);
        }
        else
        {
            _model.SaveProgress(Report);
            Changed?.Invoke();
        }
    }

    public void StopNow()
    {
        Current = null;
        _model.RunStopped(Report);
    }
}
