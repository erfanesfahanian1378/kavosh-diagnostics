using static Kavosh.Core.Loc;

namespace Kavosh.Core;

/// <summary>Letter grade for the device's overall condition. Lower enum value = better grade.</summary>
public enum DeviceGrade { APlus, A, B, C, D, F }

public enum GradeTone { Excellent, Good, Fair, Poor, Bad }

public static class DeviceGrades
{
    public static DeviceGrade FromScore(double score) => score switch
    {
        >= 97 => DeviceGrade.APlus,
        >= 92 => DeviceGrade.A,
        >= 85 => DeviceGrade.B,
        >= 75 => DeviceGrade.C,
        >= 60 => DeviceGrade.D,
        _ => DeviceGrade.F,
    };

    public static string Letter(this DeviceGrade grade) => grade switch
    {
        DeviceGrade.APlus => "A+",
        DeviceGrade.A => "A",
        DeviceGrade.B => "B",
        DeviceGrade.C => "C",
        DeviceGrade.D => "D",
        _ => "F",
    };

    public static GradeTone Tone(this DeviceGrade grade) => grade switch
    {
        DeviceGrade.APlus or DeviceGrade.A => GradeTone.Excellent,
        DeviceGrade.B => GradeTone.Good,
        DeviceGrade.C => GradeTone.Fair,
        DeviceGrade.D => GradeTone.Poor,
        _ => GradeTone.Bad,
    };

    /// <summary>The grade <paramref name="steps"/> letters lower (never below F).</summary>
    public static DeviceGrade Lowered(this DeviceGrade grade, int steps) =>
        (DeviceGrade)Math.Min((int)DeviceGrade.F, (int)grade + Math.Max(0, steps));

    /// <summary>This grade, but no better than <paramref name="cap"/>.</summary>
    public static DeviceGrade CappedAt(this DeviceGrade grade, DeviceGrade cap) => (DeviceGrade)Math.Max((int)grade, (int)cap);

    public static string Verdict(this DeviceGrade grade) => grade switch
    {
        DeviceGrade.APlus => T("Excellent — like new", "عالی — مثل نو"),
        DeviceGrade.A => T("Excellent condition", "وضعیت عالی"),
        DeviceGrade.B => T("Good condition", "وضعیت خوب"),
        DeviceGrade.C => T("Fair — some issues found", "متوسط — چند ایراد پیدا شد"),
        DeviceGrade.D => T("Poor — needs attention", "ضعیف — نیاز به رسیدگی"),
        _ => T("Needs service", "نیاز به تعمیر"),
    };
}

public enum HealthComponentKind { Keyboard, Display, Speakers, MicCamera, Battery, Memory, Drives, CpuStability }

public static class HealthComponentKinds
{
    public static string Title(this HealthComponentKind kind) => kind switch
    {
        HealthComponentKind.Keyboard => T("Keyboard & Touchpad", "کیبورد و تاچ‌پد"),
        HealthComponentKind.Display => T("Display", "نمایشگر"),
        HealthComponentKind.Speakers => T("Speakers", "بلندگوها"),
        HealthComponentKind.MicCamera => T("Mic & Camera", "میکروفون و دوربین"),
        HealthComponentKind.Battery => T("Battery", "باتری"),
        HealthComponentKind.Memory => T("Memory errors", "خطاهای حافظه"),
        HealthComponentKind.Drives => T("Drive health (SMART)", "سلامت درایو (SMART)"),
        _ => T("CPU calculation errors", "خطاهای محاسبه پردازنده"),
    };

    public static double Weight(this HealthComponentKind kind) => kind switch
    {
        HealthComponentKind.Display => 1.2,
        HealthComponentKind.Speakers => 0.8,
        HealthComponentKind.MicCamera => 0.8,
        HealthComponentKind.Battery => 1.2,
        _ => 1.0,
    };
}

public sealed class HealthComponent
{
    public HealthComponentKind Kind { get; }
    public double Score { get; }
    public List<string> Issues { get; }

    public HealthComponent(HealthComponentKind kind, double score, List<string> issues)
    {
        Kind = kind;
        Score = Math.Clamp(score, 0, 100);
        Issues = issues;
    }
}

public sealed class HealthEvaluation
{
    /// <summary>0–100, null when no hardware check ran.</summary>
    public double? Score { get; init; }
    public DeviceGrade? Grade { get; init; }
    /// <summary>The grade before any battery penalty or cap.</summary>
    public DeviceGrade? GradeFromScore { get; init; }
    public List<HealthComponent> Components { get; init; } = new();
    public int BatteryPenaltySteps { get; init; }
    /// <summary>Problems serious enough to limit the grade (memory errors, CPU errors, a failing drive).</summary>
    public List<HealthComponentKind> CappedBy { get; init; } = new();

    public int TotalIssues => Components.Sum(c => c.Issues.Count);
}

/// <summary>
/// Turns a run's measurements into a health score and grade. Only the hardware checks count:
/// keyboard, display, speakers, mic &amp; camera, battery, memory errors, CPU calculation errors and
/// drive health. CPU and GPU <em>speed</em> never affect the grade.
/// </summary>
public static class BenchmarkScoring
{
    public static HealthEvaluation Evaluate(FinalReport report)
    {
        var components = new List<HealthComponent>();

        if (report.Keyboard is { Bypassed: false } keyboard)
        {
            double score = 100;
            var issues = new List<string>();
            if (keyboard.MissingKeys.Count > 0)
            {
                score -= Math.Min(60, keyboard.MissingKeys.Count * 8);
                string keys = string.Join(" ", keyboard.MissingKeys);
                issues.Add(T($"Keys that didn't register: {keys}", $"کلیدهایی که کار نکردند: {keys}"));
            }
            if (keyboard.DoublePressedKeys.Count > 0)
            {
                score -= Math.Min(40, keyboard.DoublePressedKeys.Count * 10);
                string keys = string.Join(" ", keyboard.DoublePressedKeys);
                issues.Add(T($"Keys that registered twice: {keys}", $"کلیدهایی که دوبار ثبت شدند: {keys}"));
            }
            if (!keyboard.SentenceCorrect)
            {
                score -= 5;
                issues.Add(T("The typed sentence didn't match", "جمله تایپ‌شده مطابقت نداشت"));
            }
            int misses = 3 - keyboard.TrackpadChecksPassed;
            if (misses > 0)
            {
                score -= misses * 15;
                issues.Add(T($"Touchpad: {keyboard.TrackpadChecksPassed} of 3 checks detected",
                             $"تاچ‌پد: {keyboard.TrackpadChecksPassed} از ۳ بررسی تشخیص داده شد"));
            }
            components.Add(new HealthComponent(HealthComponentKind.Keyboard, score, issues));
        }

        if (report.Display is { } display)
        {
            double score = 100 - display.Issues.Sum(i => i.Deduction());
            components.Add(new HealthComponent(HealthComponentKind.Display, score, display.Issues.Select(i => i.ShortText()).ToList()));
        }

        if (report.Audio is { } audio)
        {
            double score = 100;
            var issues = new List<string>();
            if (!audio.LeftHeard)
            {
                score -= 50;
                issues.Add(T("Left speaker not heard", "بلندگوی چپ شنیده نشد"));
            }
            if (!audio.RightHeard)
            {
                score -= 50;
                issues.Add(T("Right speaker not heard", "بلندگوی راست شنیده نشد"));
            }
            if (!string.IsNullOrEmpty(audio.ReportedIssue))
            {
                score -= 20;
                issues.Add(audio.ReportedIssue);
            }
            components.Add(new HealthComponent(HealthComponentKind.Speakers, score, issues));
        }

        if (report.MicCamera is { } mc)
        {
            var parts = new List<double>();
            var issues = new List<string>();
            if (mc.CameraPresent)
            {
                parts.Add(mc.CameraWorks ? 100 : 0);
                if (!mc.CameraWorks) issues.Add(T("Camera not working", "دوربین کار نمی‌کند"));
            }
            if (mc.MicrophonePresent)
            {
                parts.Add(mc.MicrophoneWorks ? 100 : 0);
                if (!mc.MicrophoneWorks) issues.Add(T("Microphone not working", "میکروفون کار نمی‌کند"));
            }
            else
            {
                parts.Add(0);
                issues.Add(T("No microphone was found", "میکروفونی پیدا نشد"));
            }
            if (!string.IsNullOrEmpty(mc.ReportedIssue)) issues.Add(mc.ReportedIssue);
            double score = parts.Count == 0 ? 100 : parts.Average();
            components.Add(new HealthComponent(HealthComponentKind.MicCamera, score, issues));
        }

        int batteryPenalty = 0;
        if (report.Battery is { BatteryPresent: true } battery)
        {
            double health = battery.HealthPercent;
            double score = health >= 95 ? 100
                : health >= 80 ? 60 + (health - 80) / 15 * 40
                : Math.Max(0, (health - 50) / 30 * 60);
            foreach (var issue in battery.Issues)
            {
                if (issue.Kind == BatteryIssueKind.HighCycleCount) score -= 10;
                else if (issue.Kind == BatteryIssueKind.NotCharging) score -= 15;
            }
            if (health > 0 && health < 70) batteryPenalty = 2;
            else if (health > 0 && health < 80) batteryPenalty = 1;
            components.Add(new HealthComponent(HealthComponentKind.Battery, score, battery.Issues.Select(i => i.Text).ToList()));
        }

        var caps = new List<(HealthComponentKind Kind, DeviceGrade Cap)>();

        if (report.Memory is { } memory)
        {
            var issues = memory.ErrorsDetected > 0
                ? new List<string> { T($"{memory.ErrorsDetected} memory errors detected", $"{memory.ErrorsDetected} خطای حافظه پیدا شد") }
                : new List<string>();
            components.Add(new HealthComponent(HealthComponentKind.Memory, memory.ErrorsDetected > 0 ? 0 : 100, issues));
            if (memory.ErrorsDetected > 0) caps.Add((HealthComponentKind.Memory, DeviceGrade.D));
        }

        if (report.Cpu is { } cpu)
        {
            if (cpu.WrongResults > 0)
            {
                components.Add(new HealthComponent(HealthComponentKind.CpuStability, 0, new List<string>
                {
                    T($"The CPU returned wrong results in {cpu.WrongResults} calculation(s)",
                      $"پردازنده در {cpu.WrongResults} محاسبه نتیجه اشتباه داد"),
                }));
                caps.Add((HealthComponentKind.CpuStability, DeviceGrade.D));
            }
            else
            {
                components.Add(new HealthComponent(HealthComponentKind.CpuStability, 100, new List<string>()));
            }
        }

        if (report.Drives is { WorstScore: { } worstDrive } drives)
        {
            var issues = drives.Drives.SelectMany(d => d.Issues.Select(i => $"{d.Model}: {i.Text}")).ToList();
            components.Add(new HealthComponent(HealthComponentKind.Drives, worstDrive, issues));
            if (worstDrive < 20) caps.Add((HealthComponentKind.Drives, DeviceGrade.F));
        }

        if (components.Count == 0) return new HealthEvaluation();

        double totalWeight = components.Sum(c => c.Kind.Weight());
        double weighted = components.Sum(c => c.Score * c.Kind.Weight());
        // A single failing part pulls the average down hard, so a broken keyboard can't hide
        // behind seven perfect scores.
        double worst = components.Min(c => c.Score);
        double total = weighted / totalWeight;
        if (worst < 60) total = Math.Min(total, (total + worst) / 2 + 20);
        total = Math.Clamp(total, 0, 100);

        var baseGrade = DeviceGrades.FromScore(total);
        var grade = baseGrade.Lowered(batteryPenalty);
        var cappedBy = new List<HealthComponentKind>();
        foreach (var (kind, cap) in caps)
        {
            grade = grade.CappedAt(cap);
            cappedBy.Add(kind);
        }

        return new HealthEvaluation
        {
            Score = total,
            Grade = grade,
            GradeFromScore = baseGrade,
            Components = components,
            BatteryPenaltySteps = batteryPenalty,
            CappedBy = cappedBy,
        };
    }
}
