#if os(macOS)
import KavoshCore
import SwiftUI

@MainActor
struct DashboardView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        ScrollView {
            HStack(alignment: .top, spacing: 22) {
                VStack(spacing: 20) {
                    DeviceHeadingCard()
                    SpecsPanel()
                    DriveHealthCard()
                }
                .frame(width: 340)

                VStack(spacing: 20) {
                    if model.unfinishedRun != nil {
                        ResumeCard()
                    }
                    TestLauncherCard()
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 270), spacing: 20, alignment: .top)], spacing: 20) {
                        LastRunCard()
                        SSDLastRunCard()
                    }
                    MaintenanceCard()
                }
                .frame(maxWidth: .infinity)

                TelemetryPanel()
                    .frame(width: 320)
            }
            .padding(24)
        }
    }
}

enum DateText {
    static func string(_ date: Date) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: Loc.language == .fa ? "fa_IR" : "en_US")
        formatter.dateStyle = .medium
        formatter.timeStyle = .short
        return formatter.string(from: date)
    }
}

// MARK: - Device

@MainActor
struct DeviceHeadingCard: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        RetroCard(fill: Theme.accent) {
            VStack(alignment: .leading, spacing: 8) {
                SectionLabel(T("This Mac", "این مک"), symbol: "laptopcomputer", color: Theme.onAccent.opacity(0.85))
                if let specs = model.specs {
                    HardwareText(specs.modelName, size: 22, weight: .heavy, color: Theme.onAccent)
                        .lineLimit(2)
                        .minimumScaleFactor(0.7)
                    HardwareText("\(specs.cpuName) · \(Fmt.memoryGB(specs.ramCapacityGB))", size: 14, weight: .semibold,
                                 color: Theme.onAccent.opacity(0.92))
                    HardwareText(specs.operatingSystem, size: 12, weight: .medium, color: Theme.onAccent.opacity(0.8))
                } else {
                    Text(T("Loading system specs…", "در حال خواندن مشخصات سیستم…"))
                        .font(Theme.font(15, .bold))
                        .foregroundStyle(Theme.onAccent)
                    ProgressView().controlSize(.small).tint(Theme.onAccent)
                }
            }
        }
    }
}

@MainActor
struct SpecsPanel: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 14) {
                SectionLabel(T("System specs", "مشخصات سیستم"), symbol: "list.bullet.rectangle")
                if let specs = model.specs {
                    SpecGroup(title: T("Processor", "پردازنده"), symbol: "cpu") {
                        HardwareText(specs.cpuName, size: 14, weight: .bold)
                        HardwareText(cpuLine(specs), size: 12, weight: .medium, color: Theme.muted)
                    }
                    SpecGroup(title: T("Graphics", "گرافیک"), symbol: "cube.transparent") {
                        HardwareText(specs.gpuName.isEmpty ? "—" : specs.gpuName, size: 14, weight: .bold)
                        HardwareText(gpuLine(specs), size: 12, weight: .medium, color: Theme.muted)
                    }
                    SpecGroup(title: T("Memory", "حافظه"), symbol: "memorychip") {
                        HardwareText(Fmt.memoryGB(specs.ramCapacityGB) + (specs.ramType.map { " \($0)" } ?? ""), size: 14, weight: .bold)
                        HardwareText(memoryLine(specs), size: 12, weight: .medium, color: Theme.muted)
                    }
                    if let resolution = specs.displayResolution {
                        SpecGroup(title: T("Display", "نمایشگر"), symbol: "display") {
                            HardwareText(specs.displayName ?? T("Built-in display", "نمایشگر داخلی"), size: 14, weight: .bold)
                            HardwareText(resolution + (specs.displayRefreshRateHz.map { " @ \($0) Hz" } ?? ""), size: 12,
                                         weight: .medium, color: Theme.muted)
                        }
                    }
                    if !specs.storageDevices.isEmpty {
                        SpecGroup(title: T("Storage", "ذخیره‌سازی"), symbol: "internaldrive") {
                            ForEach(specs.storageDevices) { drive in
                                VStack(alignment: .leading, spacing: 1) {
                                    HardwareText(drive.displayName, size: 13, weight: .bold)
                                    HardwareText([Fmt.storageSize(drive.sizeBytes), drive.interconnect, drive.location]
                                        .compactMap { $0 }.joined(separator: " · "),
                                                 size: 12, weight: .medium, color: Theme.muted)
                                }
                            }
                        }
                    }
                    SpecGroup(title: T("Model", "مدل"), symbol: "tag") {
                        HardwareText(specs.modelIdentifier, size: 13, weight: .bold)
                        if let firmware = specs.firmwareVersion {
                            HardwareText("Firmware \(firmware)", size: 12, weight: .medium, color: Theme.muted)
                        }
                    }
                } else {
                    ProgressView().controlSize(.small)
                }
            }
        }
    }

    private func cpuLine(_ s: SystemSpecs) -> String {
        var parts = [s.clusterShorthand]
        if let ghz = s.cpuMaxClockGHz { parts.append("\(Fmt.fixed(ghz, 2)) GHz") }
        parts.append(s.architecture == "arm64" ? "Apple silicon" : "Intel")
        return parts.joined(separator: " · ")
    }

    private func gpuLine(_ s: SystemSpecs) -> String {
        var parts: [String] = []
        if let cores = s.gpuCoreCount { parts.append("\(cores)-core GPU") }
        if let metal = s.metalVersion { parts.append(metal) }
        if s.hasUnifiedMemory {
            parts.append("Unified memory")
        } else if let vram = s.gpuMemoryGB {
            parts.append("\(Fmt.fixed(vram, 1)) GB VRAM")
        }
        return parts.isEmpty ? "—" : parts.joined(separator: " · ")
    }

    private func memoryLine(_ s: SystemSpecs) -> String {
        var parts: [String] = []
        if let maker = s.ramManufacturer { parts.append(maker) }
        if let speed = s.ramSpeedMHz { parts.append("\(speed) MHz") }
        if let modules = s.ramModuleCount, !s.hasUnifiedMemory { parts.append("\(modules) module(s)") }
        if s.hasUnifiedMemory { parts.append("Unified") }
        return parts.isEmpty ? "—" : parts.joined(separator: " · ")
    }
}

@MainActor
struct SpecGroup<Content: View>: View {
    var title: String
    var symbol: String
    @ViewBuilder var content: Content

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: symbol)
                .font(.system(size: 13, weight: .bold))
                .frame(width: 28, height: 28)
                .background(RoundedRectangle(cornerRadius: 7).fill(Theme.surfaceAlt))
                .overlay(RoundedRectangle(cornerRadius: 7).strokeBorder(Theme.ink, lineWidth: 1.5))
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(Theme.font(11, .bold))
                    .foregroundStyle(Theme.muted)
                content
            }
            Spacer(minLength: 0)
        }
    }
}

// MARK: - Drives

@MainActor
struct DriveHealthCard: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    SectionLabel(T("SSD / Drive health (SMART)", "سلامت SSD / درایو (SMART)"), symbol: "internaldrive")
                    Spacer()
                    Button {
                        model.refreshDriveHealth()
                    } label: {
                        Image(systemName: "arrow.clockwise")
                    }
                    .retroButton(.ghost, size: .small)
                    .disabled(model.driveHealthLoading)
                    .help(T("Read again", "خواندن دوباره"))
                }
                Text(T("Read from each drive's own SMART / NVMe health log. It counts toward the health verdict — the worst drive decides. Drive speed doesn't.",
                       "از گزارش سلامت SMART / NVMe خود هر درایو خوانده می‌شود و در نتیجه سلامت حساب می‌شود — بدترین درایو تعیین‌کننده است. سرعت درایو حساب نمی‌شود."))
                    .font(Theme.font(11, .medium))
                    .foregroundStyle(Theme.muted)
                    .fixedSize(horizontal: false, vertical: true)

                if let report = model.driveHealth {
                    if report.drives.isEmpty {
                        Text(T("No drives found.", "درایوی پیدا نشد."))
                            .font(Theme.font(13, .medium))
                    }
                    ForEach(report.drives) { drive in
                        DriveHealthRow(drive: drive)
                    }
                } else {
                    HStack(spacing: 8) {
                        ProgressView().controlSize(.small)
                        Text(T("Reading drive health (SMART)…", "در حال خواندن سلامت درایو (SMART)…"))
                            .font(Theme.font(12, .medium))
                            .foregroundStyle(Theme.muted)
                    }
                }
            }
        }
    }
}

@MainActor
struct DriveHealthRow: View {
    var drive: DriveHealthInfo

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 2) {
                    HardwareText(drive.model, size: 13, weight: .bold)
                    HardwareText([Fmt.storageSize(drive.sizeBytes), drive.busType].compactMap { $0 }.joined(separator: " · "),
                                 size: 11, weight: .medium, color: Theme.muted)
                }
                Spacer()
                RetroBadge(drive.status.title, color: statusColor, textColor: Theme.onYellow)
            }
            if drive.hasHealthData {
                FlowLayout(spacing: 6) {
                    if let life = drive.lifeLeftPercent {
                        RetroBadge(T("Life left \(life)%", "عمر باقی‌مانده \(life)٪"), color: Theme.surfaceAlt)
                    }
                    if let spare = drive.availableSparePercent {
                        RetroBadge(T("Spare \(spare)%", "یدک \(spare)٪"), color: Theme.surfaceAlt)
                    }
                    if let tb = drive.dataWrittenTB {
                        RetroBadge(T("\(Fmt.fixed(tb, 1)) TB written", "\(Fmt.fixed(tb, 1)) ترابایت نوشته‌شده"), color: Theme.surfaceAlt)
                    }
                    if let hours = drive.powerOnHours {
                        RetroBadge(T("\(Fmt.number(hours)) h powered on", "\(Fmt.number(hours)) ساعت روشن"), color: Theme.surfaceAlt)
                    }
                    if let temp = drive.temperatureCelsius {
                        RetroBadge(Fmt.celsius(temp), color: Theme.surfaceAlt)
                    }
                    if let errors = drive.mediaErrors, errors > 0 {
                        RetroBadge(T("\(errors) media errors", "\(errors) خطای رسانه"), color: Theme.red, textColor: .white)
                    }
                }
                ForEach(drive.issues, id: \.self) { issue in
                    Label(issue.text, systemImage: "exclamationmark.triangle.fill")
                        .font(Theme.font(11, .semibold))
                        .foregroundStyle(Theme.red)
                        .fixedSize(horizontal: false, vertical: true)
                }
            } else {
                Text(T("This drive didn't report any health data (common behind USB adapters), so it isn't counted.",
                       "این درایو داده سلامتی گزارش نکرد (پشت مبدل‌های USB رایج است)، پس حساب نمی‌شود."))
                    .font(Theme.font(11, .medium))
                    .foregroundStyle(Theme.muted)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 9).fill(Theme.surfaceAlt.opacity(0.5)))
        .overlay(RoundedRectangle(cornerRadius: 9).strokeBorder(Theme.ink.opacity(0.25), lineWidth: 1))
    }

    private var statusColor: Color {
        switch drive.status {
        case .good: return Theme.green
        case .warning: return Theme.yellow
        case .failing: return Theme.red
        case .unknown: return Theme.surfaceAlt
        }
    }
}

// MARK: - Resume

@MainActor
struct ResumeCard: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        if let run = model.unfinishedRun {
            RetroCard(fill: Theme.yellow) {
                VStack(alignment: .leading, spacing: 10) {
                    SectionLabel(T("Unfinished test run", "اجرای ناتمام تست"), symbol: "pause.circle", color: Theme.onYellow)
                    let finished = run.completed.count + run.skipped.count + run.failures.count
                    Text(T("\(finished) of \(run.plan.count) stations finished before the run stopped. Resume to run only the rest — finished results are kept exactly as measured.",
                           "\(finished) از \(run.plan.count) ایستگاه پیش از توقف اجرا تمام شد. با ادامه، فقط بقیه اجرا می‌شوند — نتایج تمام‌شده دقیقاً همان‌طور که اندازه‌گیری شده حفظ می‌شوند."))
                        .font(Theme.font(13, .medium))
                        .foregroundStyle(Theme.onYellow)
                        .fixedSize(horizontal: false, vertical: true)
                    HStack(spacing: 10) {
                        Button {
                            model.resumeUnfinished()
                        } label: {
                            Label(T("Resume", "ادامه"), systemImage: "play.fill")
                        }
                        .retroButton(.primary)
                        Button(T("Discard", "کنار گذاشتن")) {
                            model.discardUnfinished()
                        }
                        .retroButton(.secondary)
                        .help(T("Stop offering to resume.", "دیگر پیشنهاد ادامه نده."))
                    }
                }
            }
        }
    }
}

// MARK: - Launcher

@MainActor
struct TestLauncherCard: View {
    @EnvironmentObject private var model: AppModel

    private let columns = [GridItem(.adaptive(minimum: 175), spacing: 12)]

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 16) {
                SectionLabel(T("Diagnostic & benchmark", "عیب‌یابی و بنچمارک"), symbol: "stethoscope")

                FlowLayout(spacing: 14) {
                    Button {
                        model.startRun(TestModule.fullRun)
                    } label: {
                        Label(T("Run All Tests", "اجرای همه تست‌ها"), systemImage: "play.fill")
                    }
                    .retroButton(.primary, size: .large)

                    Button {
                        model.startRun(TestModule.performanceRun)
                    } label: {
                        Label(T("Performance test only", "فقط تست کارایی"), systemImage: "speedometer")
                    }
                    .retroButton(.secondary, size: .regular)
                }

                Text(T("Runs every check one after another: CPU, GPU and memory first, then battery, display, speakers, mic & camera, keyboard & trackpad and drive health. The interactive steps tell you exactly what to do.",
                       "همه بررسی‌ها پشت سر هم اجرا می‌شوند: اول پردازنده، گرافیک و حافظه، سپس باتری، نمایشگر، بلندگو، میکروفون و دوربین، کیبورد و ترک‌پد و سلامت درایو. مراحل تعاملی دقیقاً می‌گویند چه کار کنید."))
                    .font(Theme.font(12, .medium))
                    .foregroundStyle(Theme.muted)
                    .fixedSize(horizontal: false, vertical: true)

                VStack(alignment: .leading, spacing: 8) {
                    SectionLabel(T("CPU/GPU test duration", "مدت تست پردازنده/گرافیک"), symbol: "timer")
                    RetroSegmented(options: AppSettings.durationChoices.map { ($0, durationLabel($0)) },
                                   selection: $model.settings.performanceTestSeconds)
                        .environment(\.layoutDirection, .leftToRight)
                    Text(T("Longer runs add a sustained phase that shows whether this Mac holds its speed as it heats up.",
                           "اجرای طولانی‌تر یک مرحله بار پایدار اضافه می‌کند که نشان می‌دهد آیا این مک با گرم شدن سرعتش را حفظ می‌کند."))
                        .font(Theme.font(11, .medium))
                        .foregroundStyle(Theme.muted)
                }

                Toggle(isOn: $model.settings.noBuiltInInputs) {
                    Text(T("This Mac has no built-in keyboard, trackpad or camera to test (a Mac mini, Mac Studio or Mac Pro).",
                           "این مک کیبورد، ترک‌پد یا دوربین داخلی برای تست ندارد (مک مینی، مک استودیو یا مک پرو)."))
                }
                .toggleStyle(RetroCheckboxStyle())

                Rectangle().fill(Theme.ink.opacity(0.15)).frame(height: 1)

                SectionLabel(T("Or run an individual test", "یا یک تست جداگانه اجرا کنید"), symbol: "square.grid.2x2")
                LazyVGrid(columns: columns, spacing: 12) {
                    ForEach(TestModule.allCases) { module in
                        TestTile(symbol: module.symbol, title: module.title, subtitle: module.subtitle) {
                            model.startRun([module])
                        }
                    }
                    TestTile(symbol: "speedometer", title: T("SSD Speed Test", "تست سرعت SSD"),
                             subtitle: T("Sequential and random read/write, measured like CrystalDiskMark",
                                         "خواندن/نوشتن ترتیبی و تصادفی، مثل CrystalDiskMark")) {
                        model.screen = .storage
                    }
                }
            }
        }
    }

    private func durationLabel(_ seconds: Int) -> String {
        seconds < 60 ? "\(seconds) s" : "\(seconds / 60) min"
    }
}

@MainActor
struct TestTile: View {
    var symbol: String
    var title: String
    var subtitle: String
    var action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 6) {
                Image(systemName: symbol)
                    .font(.system(size: 17, weight: .bold))
                    .frame(width: 34, height: 34)
                    .background(RoundedRectangle(cornerRadius: 8).fill(hovering ? Theme.yellow : Theme.surfaceAlt))
                    .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.ink, lineWidth: 1.5))
                Text(title)
                    .font(Theme.font(13, .bold))
                    .multilineTextAlignment(.leading)
                Text(subtitle)
                    .font(Theme.font(11, .medium))
                    .foregroundStyle(Theme.muted)
                    .lineLimit(3)
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
            }
            .padding(12)
            .frame(maxWidth: .infinity, minHeight: 128, alignment: .topLeading)
            .retroSurface(fill: Theme.surface, corner: 10, shadow: hovering ? 5 : 3)
            .offset(x: hovering ? -1 : 0, y: hovering ? -1 : 0)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .animation(.easeOut(duration: 0.12), value: hovering)
    }
}

// MARK: - Last results

@MainActor
struct LastRunCard: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 12) {
                SectionLabel(T("Device health (last run)", "سلامت دستگاه (آخرین اجرا)"), symbol: "heart.text.square")
                if let report = model.lastReport {
                    let evaluation = BenchmarkScoring.evaluate(report)
                    HStack(alignment: .center, spacing: 14) {
                        GradeBadge(grade: evaluation.grade, size: 64)
                        VStack(alignment: .leading, spacing: 4) {
                            if let score = evaluation.score {
                                HardwareText(T("\(Fmt.fixed(score, 1)) / 100", "\(Fmt.fixed(score, 1)) / ۱۰۰"), size: 18, weight: .heavy)
                                Text(evaluation.grade?.verdict ?? "")
                                    .font(Theme.font(13, .bold))
                            } else {
                                Text(T("Performance test only", "فقط تست کارایی"))
                                    .font(Theme.font(13, .bold))
                            }
                            Text(DateText.string(report.finishedAt ?? report.startedAt))
                                .font(Theme.font(11, .medium))
                                .foregroundStyle(Theme.muted)
                        }
                    }
                    HStack(spacing: 8) {
                        Button(T("View results", "مشاهده نتایج")) { model.showReport(report) }
                            .retroButton(.secondary, size: .small)
                        Button(T("Open CSV", "باز کردن CSV")) { model.openHistoryCSV() }
                            .retroButton(.ghost, size: .small)
                    }
                } else {
                    Text(T("Not run yet. Run all tests to get a health grade for this Mac.",
                           "هنوز اجرا نشده. همه تست‌ها را اجرا کنید تا نمره سلامت این مک را بگیرید."))
                        .font(Theme.font(12, .medium))
                        .foregroundStyle(Theme.muted)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }
}

@MainActor
struct SSDLastRunCard: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 10) {
                SectionLabel(T("SSD speed (last run)", "سرعت SSD (آخرین اجرا)"), symbol: "speedometer")
                if let result = model.lastStorage {
                    VStack(spacing: 6) {
                        speedRow(T("Sequential read", "خواندن ترتیبی"), result.measurement(.seq1mQ8T1, .read))
                        speedRow(T("Sequential write", "نوشتن ترتیبی"), result.measurement(.seq1mQ8T1, .write))
                        speedRow(T("Random read", "خواندن تصادفی"), result.measurement(.rnd4kQ1T1, .read))
                        speedRow(T("Random write", "نوشتن تصادفی"), result.measurement(.rnd4kQ1T1, .write))
                    }
                    Button(T("Click for full results", "نتایج کامل")) { model.screen = .storage }
                        .retroButton(.secondary, size: .small)
                } else {
                    Text(T("Not run yet.", "هنوز اجرا نشده."))
                        .font(Theme.font(12, .medium))
                        .foregroundStyle(Theme.muted)
                    Button(T("Run SSD speed test", "اجرای تست سرعت SSD")) { model.screen = .storage }
                        .retroButton(.secondary, size: .small)
                }
            }
        }
    }

    private func speedRow(_ title: String, _ m: StorageTestMeasurement?) -> some View {
        KeyValueRow(key: title, value: m.map { Fmt.mbps($0.mbPerSecond) } ?? "—")
    }
}

@MainActor
struct MaintenanceCard: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        RetroCard {
            VStack(alignment: .leading, spacing: 12) {
                SectionLabel(T("Maintenance tools", "ابزارهای نگهداری"), symbol: "wrench.and.screwdriver")
                HStack(alignment: .center, spacing: 14) {
                    Image(systemName: "sparkles.tv")
                        .font(.system(size: 22, weight: .bold))
                        .frame(width: 46, height: 46)
                        .retroSurface(fill: Theme.purple, corner: 10, shadow: 3)
                        .foregroundStyle(.white)
                    VStack(alignment: .leading, spacing: 3) {
                        Text(T("Screen Washer & Burn-in Fixer", "صفحه‌شوی و رفع سوختگی"))
                            .font(Theme.font(14, .bold))
                        Text(T("Clears temporary image retention on OLED/LCD panels. Warning: flashes the screen.",
                               "اثر تصویر موقت روی پنل‌های OLED/LCD را پاک می‌کند. هشدار: صفحه چشمک می‌زند."))
                            .font(Theme.font(12, .medium))
                            .foregroundStyle(Theme.muted)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    Spacer()
                    Button(T("Open", "باز کردن")) { model.screen = .washerSetup }
                        .retroButton(.secondary)
                }
            }
        }
    }
}
#endif
