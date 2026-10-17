#if os(macOS)
import Foundation

/// Starts either the command-line mode (`--dump`, `--cpu`, …) or the app window.
@main
@MainActor
enum KavoshEntry {
    static func main() {
        if BenchmarkCLI.handle(arguments: Array(CommandLine.arguments.dropFirst())) {
            return
        }
        KavoshApp.main()
    }
}
#else
// The app itself is macOS-only. This stub lets the package (and its tests) build elsewhere.
@main
enum KavoshEntry {
    static func main() {
        print("Kavosh Diagnostics runs on macOS only.")
    }
}
#endif
