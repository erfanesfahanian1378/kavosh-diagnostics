import Foundation

/// Number formatting used across the app. Numbers always use Latin digits so values
/// line up the same way in both languages.
public enum Fmt {
    private static let posix = Locale(identifier: "en_US_POSIX")

    private static let formatters: [NumberFormatter] = (0...4).map { decimals in
        let formatter = NumberFormatter()
        formatter.locale = posix
        formatter.numberStyle = .decimal
        formatter.usesGroupingSeparator = true
        formatter.groupingSeparator = ","
        formatter.decimalSeparator = "."
        formatter.minimumFractionDigits = decimals
        formatter.maximumFractionDigits = decimals
        return formatter
    }

    /// "1,234.5" style with a fixed number of decimals (0...4).
    public static func number(_ value: Double, decimals: Int = 0) -> String {
        guard value.isFinite else { return "—" }
        let d = max(0, min(4, decimals))
        return formatters[d].string(from: NSNumber(value: value)) ?? fixed(value, d)
    }

    public static func number(_ value: Int) -> String {
        number(Double(value), decimals: 0)
    }

    /// Plain "12.3" without grouping.
    public static func fixed(_ value: Double, _ decimals: Int = 1) -> String {
        guard value.isFinite else { return "—" }
        return String(format: "%.\(decimals)f", locale: posix, value)
    }

    public static func percent(_ value: Double, decimals: Int = 0) -> String {
        "\(fixed(value, decimals))%"
    }

    /// Decimal gigabytes like macOS Finder shows them ("512 GB", "1.02 TB").
    public static func storageSize(_ bytes: UInt64) -> String {
        let gb = Double(bytes) / 1_000_000_000
        if gb >= 1000 { return "\(fixed(gb / 1000, 2)) TB" }
        if gb >= 100 { return "\(fixed(gb, 0)) GB" }
        if gb >= 10 { return "\(fixed(gb, 1)) GB" }
        return "\(fixed(gb, 2)) GB"
    }

    /// Memory sizes in binary gigabytes ("16 GB").
    public static func memoryGB(_ gb: Double) -> String {
        if abs(gb - gb.rounded()) < 0.05 { return "\(Int(gb.rounded())) GB" }
        return "\(fixed(gb, 1)) GB"
    }

    public static func mbps(_ value: Double) -> String {
        "\(number(value, decimals: value < 100 ? 1 : 0)) MB/s"
    }

    public static func duration(_ seconds: Double) -> String {
        guard seconds.isFinite, seconds >= 0 else { return "—" }
        let total = Int(seconds.rounded())
        let m = total / 60
        let s = total % 60
        if m >= 60 {
            return String(format: "%ld:%02ld:%02ld", m / 60, m % 60, s)
        }
        return String(format: "%ld:%02ld", m, s)
    }

    public static func celsius(_ value: Double?) -> String {
        guard let value, value.isFinite else { return "—" }
        return "\(fixed(value, 0))°C"
    }

    public static func watts(_ value: Double?) -> String {
        guard let value, value.isFinite else { return "—" }
        return "\(fixed(value, 1)) W"
    }
}
