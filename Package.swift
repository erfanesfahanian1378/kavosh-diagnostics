// swift-tools-version:5.9
//
// Kavosh Diagnostics — a hardware diagnostic & benchmark app for macOS.
//
// Targets:
//   KavoshSensors      C code that talks to IOKit (SMC sensors, NVMe SMART, drive list).
//   KavoshCore         Engines, models and readers. Pure Foundation where possible, so the
//                      logic and its tests also build on Linux.
//   KavoshDiagnostics  The SwiftUI app (macOS only).

import PackageDescription

let infoPlistPath = "\(Context.packageDirectory)/Resources/Info.plist"

let package = Package(
    name: "KavoshDiagnostics",
    defaultLocalization: "en",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "KavoshDiagnostics", targets: ["KavoshDiagnostics"])
    ],
    targets: [
        .target(
            name: "KavoshSensors",
            path: "Sources/KavoshSensors",
            linkerSettings: [
                .linkedFramework("IOKit", .when(platforms: [.macOS])),
                .linkedFramework("CoreFoundation", .when(platforms: [.macOS]))
            ]
        ),
        .target(
            name: "KavoshCore",
            dependencies: ["KavoshSensors"],
            path: "Sources/KavoshCore"
        ),
        .executableTarget(
            name: "KavoshDiagnostics",
            dependencies: ["KavoshCore"],
            path: "Sources/KavoshDiagnostics",
            linkerSettings: [
                // Embeds Info.plist in the binary so camera/microphone permission prompts
                // also work when the app is started with `swift run` or from Xcode.
                .unsafeFlags([
                    "-Xlinker", "-sectcreate",
                    "-Xlinker", "__TEXT",
                    "-Xlinker", "__info_plist",
                    "-Xlinker", infoPlistPath
                ], .when(platforms: [.macOS]))
            ]
        ),
        .testTarget(
            name: "KavoshCoreTests",
            dependencies: ["KavoshCore"],
            path: "Tests/KavoshCoreTests"
        )
    ]
)
