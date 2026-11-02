#if os(macOS)
import AppKit
import KavoshCore
import SwiftUI

/// Who made the app. Shown on the launch screen, in the header and in the About window.
enum AppCredits {
    static let developerName = "Erfan Esfahanian"
    static let developerNamePersian = "عرفان اصفهانیان"
}

/// Shown for a moment when the app opens: the app name and its developer. A click skips it.
@MainActor
struct LaunchSplash: View {
    var body: some View {
        let persian = Loc.language == .fa
        ZStack {
            Theme.background
            VStack(spacing: 14) {
                Image(nsImage: NSApp.applicationIconImage)
                    .resizable()
                    .interpolation(.high)
                    .frame(width: 112, height: 112)
                Text("Kavosh")
                    .font(.system(size: 44, weight: .black, design: .rounded))
                    .environment(\.layoutDirection, .leftToRight)
                SectionLabel(T("Diagnostics & Benchmark", "عیب‌یابی و بنچمارک"))
                Rectangle()
                    .fill(Theme.ink.opacity(0.15))
                    .frame(width: 220, height: 2)
                VStack(spacing: 4) {
                    SectionLabel(T("Developed by", "توسعه‌دهنده"), symbol: "wrench.and.screwdriver.fill", color: Theme.accent)
                    if persian {
                        Text(AppCredits.developerNamePersian)
                            .font(Theme.font(28, .heavy))
                    }
                    HardwareText(AppCredits.developerName, size: persian ? 16 : 28, weight: persian ? .bold : .heavy,
                                 color: persian ? Theme.muted : Theme.ink)
                }
            }
            .padding(40)
            .frame(minWidth: 420)
            .retroSurface(fill: Theme.surface, corner: 22, shadow: 6)
        }
        .foregroundStyle(Theme.ink)
        .contentShape(Rectangle())
    }
}
#endif
