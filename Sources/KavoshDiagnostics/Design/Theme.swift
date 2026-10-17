#if os(macOS)
import AppKit
import KavoshCore
import SwiftUI

/// The app's "retro" look: warm paper background, ink outlines, hard offset shadows and a
/// few loud accent colors. Every color has a light and a dark variant.
enum Theme {
    // MARK: Colors

    static let background = dynamic(light: 0xF4EEE1, dark: 0x121218)
    static let surface = dynamic(light: 0xFFFDF8, dark: 0x1C1C24)
    static let surfaceAlt = dynamic(light: 0xEFE7D6, dark: 0x25252F)
    static let ink = dynamic(light: 0x17141C, dark: 0xECE8DF)
    static let muted = dynamic(light: 0x6E6676, dark: 0x9C98A6)
    static let shadow = dynamic(light: 0x17141C, dark: 0x000000)
    static let grid = dynamic(light: 0xE2D9C6, dark: 0x2E2E3A)

    static let accent = dynamic(light: 0x2F6BFF, dark: 0x5B8CFF)
    static let red = dynamic(light: 0xF0452A, dark: 0xFF6A4D)
    static let yellow = dynamic(light: 0xFFC93C, dark: 0xFFD45E)
    static let green = dynamic(light: 0x1FAF6B, dark: 0x3DD68C)
    static let teal = dynamic(light: 0x0FA89A, dark: 0x2DD4BF)
    static let purple = dynamic(light: 0x7C4DFF, dark: 0x9F7BFF)
    static let orange = dynamic(light: 0xFF8A1F, dark: 0xFFA24D)

    /// Text on top of an accent-colored fill.
    static let onAccent = Color.white
    static let onYellow = Color(hex: 0x17141C)

    static func color(for tone: DeviceGrade.Tone) -> Color {
        switch tone {
        case .excellent: return green
        case .good: return teal
        case .fair: return yellow
        case .poor: return orange
        case .bad: return red
        }
    }

    /// Green → yellow → red for a 0–100 "goodness" score.
    static func scoreColor(_ score: Double) -> Color {
        switch score {
        case 90...: return green
        case 75..<90: return teal
        case 60..<75: return yellow
        case 40..<60: return orange
        default: return red
        }
    }

    /// Cool → hot for a temperature.
    static func temperatureColor(_ celsius: Double?) -> Color {
        guard let c = celsius else { return muted }
        switch c {
        case ..<60: return green
        case 60..<80: return yellow
        case 80..<95: return orange
        default: return red
        }
    }

    // MARK: Metrics

    static let corner: CGFloat = 12
    static let border: CGFloat = 2
    static let shadowOffset: CGFloat = 4

    // MARK: Fonts

    static func font(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        if Loc.language == .fa && FontRegistry.vazirmatnAvailable {
            return .custom(vazirName(weight), size: size)
        }
        return .system(size: size, weight: weight, design: .rounded)
    }

    /// Numbers and hardware names stay in the Latin rounded face in both languages.
    static func number(_ size: CGFloat, _ weight: Font.Weight = .bold) -> Font {
        .system(size: size, weight: weight, design: .rounded).monospacedDigit()
    }

    static func mono(_ size: CGFloat, _ weight: Font.Weight = .medium) -> Font {
        .system(size: size, weight: weight, design: .monospaced)
    }

    private static func vazirName(_ weight: Font.Weight) -> String {
        switch weight {
        case .black, .heavy: return "Vazirmatn-Black"
        case .bold: return "Vazirmatn-Bold"
        case .semibold: return "Vazirmatn-ExtraBold"
        case .medium: return "Vazirmatn-Medium"
        default: return "Vazirmatn-Regular"
        }
    }

    // MARK: Helpers

    private static func dynamic(light: UInt32, dark: UInt32) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            let isDark = appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            return NSColor(hex: isDark ? dark : light)
        })
    }
}

extension NSColor {
    convenience init(hex: UInt32, alpha: CGFloat = 1) {
        self.init(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
                  green: CGFloat((hex >> 8) & 0xFF) / 255,
                  blue: CGFloat(hex & 0xFF) / 255,
                  alpha: alpha)
    }
}

extension Color {
    init(hex: UInt32, opacity: Double = 1) {
        self.init(.sRGB,
                  red: Double((hex >> 16) & 0xFF) / 255,
                  green: Double((hex >> 8) & 0xFF) / 255,
                  blue: Double(hex & 0xFF) / 255,
                  opacity: opacity)
    }
}
#endif
