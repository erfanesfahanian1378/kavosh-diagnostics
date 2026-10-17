#if os(macOS)
import AppKit
import CoreText
import KavoshCore
import SwiftUI

struct KavoshApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var model = AppModel()

    var body: some Scene {
        WindowGroup("Kavosh Diagnostics") {
            RootView()
                .environmentObject(model)
                .environmentObject(model.telemetry)
                .frame(minWidth: 1180, minHeight: 760)
        }
        .defaultSize(width: 1360, height: 880)
        .windowResizability(.contentMinSize)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandGroup(after: .appInfo) {
                Button(T("Show Results Folder", "نمایش پوشه نتایج")) {
                    NSWorkspace.shared.activateFileViewerSelecting([AppPaths.supportDirectory])
                }
            }
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationWillFinishLaunching(_ notification: Notification) {
        FontRegistry.registerBundledFonts()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }
}

/// Registers the Vazirmatn font (used for Persian) from Contents/Resources/Fonts.
enum FontRegistry {
    private(set) static var vazirmatnAvailable = false

    @MainActor
    static func registerBundledFonts() {
        var folders: [URL] = []
        if let resources = Bundle.main.resourceURL {
            folders.append(resources.appendingPathComponent("Fonts"))
        }
        // When run with `swift run`, look next to the package's Resources folder.
        let executable = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath()
        var probe = executable.deletingLastPathComponent()
        for _ in 0..<5 {
            folders.append(probe.appendingPathComponent("Resources/Fonts"))
            probe = probe.deletingLastPathComponent()
        }
        for folder in folders {
            guard let files = try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil) else { continue }
            let fonts = files.filter { $0.pathExtension.lowercased() == "ttf" }
            guard !fonts.isEmpty else { continue }
            for font in fonts {
                CTFontManagerRegisterFontsForURL(font as CFURL, .process, nil)
            }
            vazirmatnAvailable = NSFont(name: "Vazirmatn-Bold", size: 12) != nil
            if vazirmatnAvailable { return }
        }
    }
}

/// Runs `work` on the main thread, in order, as main-actor code.
func onMain(_ work: @escaping @MainActor () -> Void) {
    DispatchQueue.main.async {
        MainActor.assumeIsolated {
            work()
        }
    }
}
#endif
