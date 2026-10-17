#if os(macOS)
import AppKit
import IOKit.pwr_mgt
import KavoshCore
import SwiftUI

/// Borderless window that covers the whole screen above the menu bar and Dock.
final class FullscreenWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// Shows one SwiftUI view full screen (display test, screen washer) and routes key presses
/// to a handler while it's up.
@MainActor
final class FullscreenPresenter {
    static let shared = FullscreenPresenter()

    private var window: FullscreenWindow?
    private var keyMonitor: Any?
    private var previousKeyWindow: NSWindow?

    var isPresenting: Bool { window != nil }

    /// `onKey` returns true when it handled the key (the key is then swallowed).
    func present<Content: View>(_ content: Content, onKey: @escaping (NSEvent) -> Bool) {
        dismiss()
        previousKeyWindow = NSApp.keyWindow
        guard let screen = NSApp.keyWindow?.screen ?? NSScreen.main ?? NSScreen.screens.first else { return }

        let window = FullscreenWindow(contentRect: screen.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.level = .screenSaver
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        window.backgroundColor = .black
        window.isOpaque = true
        window.hasShadow = false
        window.acceptsMouseMovedEvents = true

        let root = content
            .environment(\.layoutDirection, Loc.language.isRightToLeft ? .rightToLeft : .leftToRight)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .ignoresSafeArea()
        window.contentView = NSHostingView(rootView: root)
        window.setFrame(screen.frame, display: true)
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)

        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            onKey(event) ? nil : event
        }
        self.window = window
    }

    func dismiss() {
        if let keyMonitor {
            NSEvent.removeMonitor(keyMonitor)
        }
        keyMonitor = nil
        if let window {
            window.orderOut(nil)
            window.close()
        }
        window = nil
        NSCursor.setHiddenUntilMouseMoves(false)
        previousKeyWindow?.makeKeyAndOrderFront(nil)
        previousKeyWindow = nil
    }
}

/// Keeps the display awake while something full-screen is running.
final class DisplaySleepBlocker {
    private var assertionID: UInt32 = 0
    private var active = false

    func begin(reason: String) {
        guard !active else { return }
        let result = IOPMAssertionCreateWithName(kIOPMAssertionTypePreventUserIdleDisplaySleep as CFString,
                                                 IOPMAssertionLevel(kIOPMAssertionLevelOn),
                                                 reason as CFString,
                                                 &assertionID)
        active = result == kIOReturnSuccess
    }

    func end() {
        guard active else { return }
        IOPMAssertionRelease(assertionID)
        active = false
    }

    deinit { end() }
}
#endif
