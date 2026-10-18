using static Kavosh.Core.Loc;

namespace Kavosh.Core;

/// <summary>
/// One key on the on-screen keyboard. <see cref="Code"/> is the physical key name (the W3C
/// <c>KeyboardEvent.code</c> names Avalonia uses, e.g. "KeyA" is "A", "ShiftLeft"), so the test
/// works the same with any keyboard language.
/// </summary>
public sealed record KeyboardKey(string Code, string Label, double Width = 1, bool Optional = false, bool IsModifier = false);

/// <summary>A Windows laptop keyboard (US / ANSI) as rows of keys.</summary>
public static class PcKeyboardLayout
{
    private static KeyboardKey K(string code, string label, double width = 1, bool optional = false, bool modifier = false) =>
        new(code, label, width, optional, modifier);

    public static readonly KeyboardKey[][] Rows =
    {
        new[]
        {
            K("Escape", "esc", 1.5),
            K("F1", "F1", optional: true), K("F2", "F2", optional: true), K("F3", "F3", optional: true),
            K("F4", "F4", optional: true), K("F5", "F5", optional: true), K("F6", "F6", optional: true),
            K("F7", "F7", optional: true), K("F8", "F8", optional: true), K("F9", "F9", optional: true),
            K("F10", "F10", optional: true), K("F11", "F11", optional: true), K("F12", "F12", optional: true),
            K("PrintScreen", "prt sc", 1.2, optional: true), K("Delete", "del", 1.2),
        },
        new[]
        {
            K("Backquote", "`"), K("Digit1", "1"), K("Digit2", "2"), K("Digit3", "3"), K("Digit4", "4"),
            K("Digit5", "5"), K("Digit6", "6"), K("Digit7", "7"), K("Digit8", "8"), K("Digit9", "9"),
            K("Digit0", "0"), K("Minus", "-"), K("Equal", "="), K("Backspace", "backspace", 2.0),
        },
        new[]
        {
            K("Tab", "tab", 1.5), K("Q", "Q"), K("W", "W"), K("E", "E"), K("R", "R"), K("T", "T"), K("Y", "Y"),
            K("U", "U"), K("I", "I"), K("O", "O"), K("P", "P"), K("BracketLeft", "["), K("BracketRight", "]"),
            K("Backslash", "\\", 1.5),
        },
        new[]
        {
            K("CapsLock", "caps", 1.8, modifier: true), K("A", "A"), K("S", "S"), K("D", "D"), K("F", "F"),
            K("G", "G"), K("H", "H"), K("J", "J"), K("K", "K"), K("L", "L"), K("Semicolon", ";"), K("Quote", "'"),
            K("Enter", "enter", 2.2),
        },
        new[]
        {
            K("ShiftLeft", "shift", 2.3, modifier: true), K("Z", "Z"), K("X", "X"), K("C", "C"), K("V", "V"),
            K("B", "B"), K("N", "N"), K("M", "M"), K("Comma", ","), K("Period", "."), K("Slash", "/"),
            K("ShiftRight", "shift", 2.7, modifier: true),
        },
        new[]
        {
            K("ControlLeft", "ctrl", 1.3, modifier: true),
            K("Fn", "fn", optional: true, modifier: true),
            K("MetaLeft", "win", 1.1, modifier: true),
            K("AltLeft", "alt", 1.1, modifier: true),
            K("Space", "space", 5.0),
            K("AltRight", "alt", 1.1, modifier: true),
            // Many new laptops put a Copilot key here instead of right ctrl.
            K("ControlRight", "ctrl", 1.1, optional: true, modifier: true),
            K("ArrowLeft", "←"), K("ArrowUp", "↑"), K("ArrowDown", "↓"), K("ArrowRight", "→"),
        },
    };

    /// <summary>The extra key on ISO keyboards (next to left shift), counted only if pressed.</summary>
    public static readonly KeyboardKey IsoKey = K("IntlBackslash", "\\|", optional: true);

    /// <summary>Keys that exist on some keyboards and only count when pressed.</summary>
    public static readonly KeyboardKey[] ExtraKeys =
    {
        IsoKey,
        K("Insert", "ins", optional: true), K("Home", "home", optional: true), K("End", "end", optional: true),
        K("PageUp", "pg up", optional: true), K("PageDown", "pg dn", optional: true),
        K("ContextMenu", "menu", optional: true), K("MetaRight", "win", optional: true),
        K("ScrollLock", "scr lk", optional: true), K("Pause", "pause", optional: true), K("NumLock", "num lk", optional: true),
    };

    public static IEnumerable<KeyboardKey> AllKeys => Rows.SelectMany(r => r);

    public static IEnumerable<KeyboardKey> RequiredKeys => AllKeys.Where(k => !k.Optional);

    private static readonly Dictionary<string, KeyboardKey> ByCode =
        AllKeys.Concat(ExtraKeys).GroupBy(k => k.Code).ToDictionary(g => g.Key, g => g.First());

    public static KeyboardKey? Key(string code) => ByCode.TryGetValue(code, out var key) ? key : null;

    /// <summary>Key label with a side for keys that exist twice ("left shift").</summary>
    public static string Describe(KeyboardKey key) => key.Code switch
    {
        "ShiftLeft" => T("left shift", "شیفت چپ"),
        "ShiftRight" => T("right shift", "شیفت راست"),
        "ControlLeft" => T("left ctrl", "کنترل چپ"),
        "ControlRight" => T("right ctrl", "کنترل راست"),
        "AltLeft" => T("left alt", "آلت چپ"),
        "AltRight" => T("right alt", "آلت راست"),
        "MetaLeft" => T("windows key", "کلید ویندوز"),
        _ => key.Label,
    };
}

/// <summary>Tracks which keys were pressed and spots keys that register twice ("chatter").</summary>
public sealed class KeyPressTracker
{
    private readonly Dictionary<string, int> _counts = new();
    private readonly HashSet<string> _doublePressed = new();
    private readonly Dictionary<string, double> _lastDown = new();
    private readonly HashSet<string> _held = new();

    /// <summary>Two presses of the same key closer than this are a hardware double-press.</summary>
    public const double ChatterWindow = 0.035;

    public IReadOnlyDictionary<string, int> Counts => _counts;
    public IReadOnlyCollection<string> DoublePressed => _doublePressed;
    public string? LastKeyLabel { get; private set; }

    /// <summary>
    /// Records a key press. Windows repeats key-down while a key is held, so a key-down for a key
    /// that is already down counts as a repeat (pass <paramref name="isRepeat"/> to force it).
    /// </summary>
    public void KeyDown(string code, double timestamp, bool isRepeat = false)
    {
        if (isRepeat || !_held.Add(code)) return;
        if (_lastDown.TryGetValue(code, out double previous) && timestamp - previous < ChatterWindow)
            _doublePressed.Add(code);
        _lastDown[code] = timestamp;
        _counts[code] = _counts.GetValueOrDefault(code) + 1;
        LastKeyLabel = PcKeyboardLayout.Key(code)?.Label ?? code;
    }

    public void KeyUp(string code) => _held.Remove(code);

    /// <summary>Forget held keys (for example when the window loses focus mid-press).</summary>
    public void ReleaseAll() => _held.Clear();

    public bool IsPressed(string code) => _counts.GetValueOrDefault(code) > 0;

    public int PressedKeyCount => _counts.Count;

    public List<KeyboardKey> MissingRequiredKeys => PcKeyboardLayout.RequiredKeys.Where(k => !IsPressed(k.Code)).ToList();

    public (int Pressed, int Total) RequiredProgress
    {
        get
        {
            var required = PcKeyboardLayout.RequiredKeys.ToList();
            return (required.Count(k => IsPressed(k.Code)), required.Count);
        }
    }

    public KeyboardTestResult Result(bool sentenceCorrect, bool trackpadMoved, bool trackpadClicked, bool trackpadScrolled)
    {
        var progress = RequiredProgress;
        return new KeyboardTestResult
        {
            Bypassed = false,
            SentenceCorrect = sentenceCorrect,
            TotalKeys = progress.Total,
            PressedKeys = progress.Pressed,
            MissingKeys = MissingRequiredKeys.Select(PcKeyboardLayout.Describe).ToList(),
            DoublePressedKeys = _doublePressed.OrderBy(c => c, StringComparer.Ordinal)
                .Select(code => PcKeyboardLayout.Key(code) is { } key ? PcKeyboardLayout.Describe(key) : code)
                .ToList(),
            TrackpadMoved = trackpadMoved,
            TrackpadClicked = trackpadClicked,
            TrackpadScrolled = trackpadScrolled,
        };
    }
}

/// <summary>The sentence typed in step 1 of the keyboard test.</summary>
public static class TypingPrompt
{
    public const string Sentence = "The quick brown fox jumps over the lazy dog 1234567890";

    /// <summary>Typed text matches when it equals the sentence, ignoring case and outer spaces.</summary>
    public static bool Matches(string typed) =>
        string.Equals(typed.Trim(), Sentence, StringComparison.OrdinalIgnoreCase);

    /// <summary>Positions (in the prompt) where the typed text differs, for highlighting.</summary>
    public static HashSet<int> MismatchedIndices(string typed)
    {
        string a = Sentence.ToLowerInvariant();
        string b = typed.ToLowerInvariant();
        var result = new HashSet<int>();
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i]) result.Add(i);
        }
        return result;
    }
}
