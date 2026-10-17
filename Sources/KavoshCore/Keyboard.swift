import Foundation

/// One key on the on-screen keyboard. `code` is the macOS virtual key code.
public struct KeyboardKey: Hashable, Identifiable, Sendable {
    public var code: UInt16
    public var label: String
    /// Width in key units (a letter key is 1).
    public var width: Double
    /// Function keys, fn and the ISO-only key don't count as missing.
    public var optional: Bool
    public var isModifier: Bool

    public var id: UInt16 { code }

    public init(_ code: UInt16, _ label: String, width: Double = 1, optional: Bool = false, isModifier: Bool = false) {
        self.code = code
        self.label = label
        self.width = width
        self.optional = optional
        self.isModifier = isModifier
    }
}

/// A Mac laptop keyboard (US / ANSI) as rows of keys.
public enum MacKeyboardLayout {
    public static let rows: [[KeyboardKey]] = [
        [
            KeyboardKey(53, "esc", width: 1.5),
            KeyboardKey(122, "F1", optional: true), KeyboardKey(120, "F2", optional: true),
            KeyboardKey(99, "F3", optional: true), KeyboardKey(118, "F4", optional: true),
            KeyboardKey(96, "F5", optional: true), KeyboardKey(97, "F6", optional: true),
            KeyboardKey(98, "F7", optional: true), KeyboardKey(100, "F8", optional: true),
            KeyboardKey(101, "F9", optional: true), KeyboardKey(109, "F10", optional: true),
            KeyboardKey(103, "F11", optional: true), KeyboardKey(111, "F12", optional: true)
        ],
        [
            KeyboardKey(50, "`"), KeyboardKey(18, "1"), KeyboardKey(19, "2"), KeyboardKey(20, "3"),
            KeyboardKey(21, "4"), KeyboardKey(23, "5"), KeyboardKey(22, "6"), KeyboardKey(26, "7"),
            KeyboardKey(28, "8"), KeyboardKey(25, "9"), KeyboardKey(29, "0"), KeyboardKey(27, "-"),
            KeyboardKey(24, "="), KeyboardKey(51, "delete", width: 1.5)
        ],
        [
            KeyboardKey(48, "tab", width: 1.5), KeyboardKey(12, "Q"), KeyboardKey(13, "W"), KeyboardKey(14, "E"),
            KeyboardKey(15, "R"), KeyboardKey(17, "T"), KeyboardKey(16, "Y"), KeyboardKey(32, "U"),
            KeyboardKey(34, "I"), KeyboardKey(31, "O"), KeyboardKey(35, "P"), KeyboardKey(33, "["),
            KeyboardKey(30, "]"), KeyboardKey(42, "\\")
        ],
        [
            KeyboardKey(57, "caps", width: 1.8, isModifier: true), KeyboardKey(0, "A"), KeyboardKey(1, "S"),
            KeyboardKey(2, "D"), KeyboardKey(3, "F"), KeyboardKey(5, "G"), KeyboardKey(4, "H"),
            KeyboardKey(38, "J"), KeyboardKey(40, "K"), KeyboardKey(37, "L"), KeyboardKey(41, ";"),
            KeyboardKey(39, "'"), KeyboardKey(36, "return", width: 1.7)
        ],
        [
            KeyboardKey(56, "shift", width: 2.3, isModifier: true), KeyboardKey(6, "Z"), KeyboardKey(7, "X"),
            KeyboardKey(8, "C"), KeyboardKey(9, "V"), KeyboardKey(11, "B"), KeyboardKey(45, "N"),
            KeyboardKey(46, "M"), KeyboardKey(43, ","), KeyboardKey(47, "."), KeyboardKey(44, "/"),
            KeyboardKey(60, "shift", width: 2.3, isModifier: true)
        ],
        [
            KeyboardKey(63, "fn", optional: true, isModifier: true),
            KeyboardKey(59, "control", width: 1.1, isModifier: true),
            KeyboardKey(58, "option", width: 1.1, isModifier: true),
            KeyboardKey(55, "command", width: 1.4, isModifier: true),
            KeyboardKey(49, "space", width: 5.0),
            KeyboardKey(54, "command", width: 1.4, isModifier: true),
            KeyboardKey(61, "option", width: 1.1, isModifier: true),
            KeyboardKey(123, "←"), KeyboardKey(126, "↑"), KeyboardKey(125, "↓"), KeyboardKey(124, "→")
        ]
    ]

    /// The extra key on ISO keyboards (left of Z / below esc), counted only if pressed.
    public static let isoKey = KeyboardKey(10, "§", optional: true)

    public static var allKeys: [KeyboardKey] { rows.flatMap { $0 } }
    public static var requiredKeys: [KeyboardKey] { allKeys.filter { !$0.optional } }

    public static func key(for code: UInt16) -> KeyboardKey? {
        if code == isoKey.code { return isoKey }
        return allKeys.first { $0.code == code }
    }

    /// Modifier keys report presses through "flags changed" events. This maps their key code
    /// to the flag bit that is set while the key is down (NSEvent.ModifierFlags raw values).
    public static func modifierFlag(for code: UInt16) -> UInt? {
        switch code {
        case 56, 60: return 1 << 17 // shift
        case 59, 62: return 1 << 18 // control
        case 58, 61: return 1 << 19 // option
        case 55, 54: return 1 << 20 // command
        case 57: return 1 << 16     // caps lock
        case 63: return 1 << 23     // fn
        default: return nil
        }
    }
}

/// Tracks which keys were pressed and spots keys that register twice ("chatter").
public struct KeyPressTracker: Sendable {
    public private(set) var counts: [UInt16: Int] = [:]
    public private(set) var doublePressed: Set<UInt16> = []
    public private(set) var lastKeyLabel: String?
    private var lastDown: [UInt16: TimeInterval] = [:]

    /// Two presses of the same key closer than this are a hardware double-press.
    public static let chatterWindow: TimeInterval = 0.035

    public init() {}

    public mutating func keyDown(code: UInt16, timestamp: TimeInterval, isRepeat: Bool) {
        guard !isRepeat else { return }
        if let previous = lastDown[code], timestamp - previous < KeyPressTracker.chatterWindow {
            doublePressed.insert(code)
        }
        lastDown[code] = timestamp
        counts[code, default: 0] += 1
        lastKeyLabel = MacKeyboardLayout.key(for: code)?.label ?? "#\(code)"
    }

    public func isPressed(_ code: UInt16) -> Bool { (counts[code] ?? 0) > 0 }

    public var pressedKeyCount: Int { counts.keys.count }

    public var missingRequiredKeys: [KeyboardKey] {
        MacKeyboardLayout.requiredKeys.filter { !isPressed($0.code) }
    }

    public var requiredProgress: (pressed: Int, total: Int) {
        let required = MacKeyboardLayout.requiredKeys
        // Two shift/command/option keys share a label; count codes, not labels.
        let pressed = required.filter { isPressed($0.code) }.count
        return (pressed, required.count)
    }

    public func result(sentenceCorrect: Bool, trackpadMoved: Bool, trackpadClicked: Bool,
                       trackpadScrolled: Bool) -> KeyboardTestResult {
        let progress = requiredProgress
        return KeyboardTestResult(
            bypassed: false,
            sentenceCorrect: sentenceCorrect,
            totalKeys: progress.total,
            pressedKeys: progress.pressed,
            missingKeys: missingRequiredKeys.map { MacKeyboardLayout.describe($0) },
            doublePressedKeys: doublePressed.sorted().map { code in
                MacKeyboardLayout.key(for: code).map { MacKeyboardLayout.describe($0) } ?? "#\(code)"
            },
            trackpadMoved: trackpadMoved,
            trackpadClicked: trackpadClicked,
            trackpadScrolled: trackpadScrolled
        )
    }
}

extension MacKeyboardLayout {
    /// Key label with a side for keys that exist twice ("left shift").
    public static func describe(_ key: KeyboardKey) -> String {
        switch key.code {
        case 56: return T("left shift", "شیفت چپ")
        case 60: return T("right shift", "شیفت راست")
        case 55: return T("left command", "کامند چپ")
        case 54: return T("right command", "کامند راست")
        case 58: return T("left option", "آپشن چپ")
        case 61: return T("right option", "آپشن راست")
        default: return key.label
        }
    }
}

/// The sentence typed in step 1 of the keyboard test.
public enum TypingPrompt {
    public static let sentence = "The quick brown fox jumps over the lazy dog 1234567890"

    /// Typed text matches when it equals the sentence, ignoring case and outer spaces.
    public static func matches(_ typed: String) -> Bool {
        typed.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() == sentence.lowercased()
    }

    /// Positions (in the prompt) where the typed text differs, for highlighting.
    public static func mismatchedIndices(_ typed: String) -> Set<Int> {
        let a = Array(sentence.lowercased())
        let b = Array(typed.lowercased())
        var result: Set<Int> = []
        for i in 0..<min(a.count, b.count) where a[i] != b[i] {
            result.insert(i)
        }
        return result
    }
}
