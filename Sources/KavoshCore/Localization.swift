import Foundation

/// UI language. Persian switches the whole layout to right-to-left.
public enum AppLanguage: String, Codable, CaseIterable, Sendable {
    case en
    case fa

    public var isRightToLeft: Bool { self == .fa }

    /// The language's name written in itself, for the language switch.
    public var nativeName: String {
        switch self {
        case .en: return "English"
        case .fa: return "فارسی"
        }
    }

    public var shortName: String {
        switch self {
        case .en: return "EN"
        case .fa: return "فا"
        }
    }
}

/// Holds the language currently shown. The app sets it from its settings and rebuilds
/// the view tree when it changes.
public enum Loc {
    public static var language: AppLanguage = .en
}

/// Picks the English or Persian text for the current language.
///
///     Text(T("Run All Tests", "اجرای همه تست‌ها"))
@inline(__always)
public func T(_ english: String, _ persian: String) -> String {
    Loc.language == .fa ? persian : english
}
