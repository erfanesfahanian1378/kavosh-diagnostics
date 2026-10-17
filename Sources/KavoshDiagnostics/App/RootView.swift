#if os(macOS)
import AppKit
import KavoshCore
import SwiftUI

@MainActor
struct RootView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        VStack(spacing: 0) {
            HeaderBar()
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .background(Theme.background)
        .foregroundStyle(Theme.ink)
        .environment(\.layoutDirection, model.settings.language.isRightToLeft ? .rightToLeft : .leftToRight)
        .id(model.settings.language)
        .alert(T("Notice", "پیام"), isPresented: noticeBinding) {
            Button(T("OK", "باشه"), role: .cancel) {}
        } message: {
            Text(model.notice ?? "")
        }
    }

    @ViewBuilder
    private var content: some View {
        switch model.screen {
        case .dashboard:
            DashboardView()
        case .suite:
            if let runner = model.runner {
                TestSuiteView(runner: runner)
            } else {
                DashboardView()
            }
        case .results:
            if let report = model.shownReport {
                ResultView(report: report)
            } else {
                DashboardView()
            }
        case .storage:
            StorageTestView()
        case .washerSetup:
            ScreenWasherSetupView()
        }
    }

    private var noticeBinding: Binding<Bool> {
        Binding(get: { model.notice != nil }, set: { if !$0 { model.notice = nil } })
    }
}

@MainActor
struct HeaderBar: View {
    @EnvironmentObject private var model: AppModel
    @EnvironmentObject private var telemetry: LiveTelemetry

    var body: some View {
        HStack(spacing: 14) {
            Button {
                if model.runner == nil { model.goToDashboard() }
            } label: {
                HStack(spacing: 12) {
                    Image(nsImage: NSApp.applicationIconImage)
                        .resizable()
                        .interpolation(.high)
                        .frame(width: 36, height: 36)
                    VStack(alignment: .leading, spacing: 1) {
                        Text("Kavosh")
                            .font(.system(size: 21, weight: .black, design: .rounded))
                        SectionLabel(T("Diagnostics & Benchmark", "عیب‌یابی و بنچمارک"))
                    }
                }
            }
            .buttonStyle(.plain)

            Spacer()

            statusLine

            if model.screen == .dashboard {
                languageSwitch
            }
        }
        .padding(.horizontal, 24)
        .padding(.vertical, 12)
        .background(Theme.surface)
        .overlay(alignment: .bottom) {
            Rectangle().fill(Theme.ink).frame(height: Theme.border)
        }
    }

    @ViewBuilder
    private var statusLine: some View {
        HStack(spacing: 8) {
            if let thermal = telemetry.latest?.thermalState, thermal >= .serious {
                RetroBadge(thermal.title, color: thermal == .critical ? Theme.red : Theme.orange,
                           textColor: Theme.onYellow, symbol: "thermometer.high")
            }
            if !model.specsComplete {
                ProgressView().controlSize(.small)
                Text(T("Loading system specs…", "در حال خواندن مشخصات سیستم…"))
                    .font(Theme.font(12, .medium))
                    .foregroundStyle(Theme.muted)
            } else if let runner = model.runner, let current = runner.current {
                Circle().fill(Theme.orange).frame(width: 8, height: 8)
                Text(T("Testing: \(current.title)", "در حال تست: \(current.title)"))
                    .font(Theme.font(12, .semibold))
            } else {
                Circle().fill(Theme.green).frame(width: 8, height: 8)
                Text(T("Ready", "آماده"))
                    .font(Theme.font(12, .semibold))
            }
        }
    }

    private var languageSwitch: some View {
        HStack(spacing: 6) {
            ForEach(AppLanguage.allCases, id: \.self) { language in
                Button(language.shortName) { model.setLanguage(language) }
                    .retroButton(model.settings.language == language ? .primary : .secondary, size: .small)
                    .help(language.nativeName)
            }
        }
        .environment(\.layoutDirection, .leftToRight)
    }
}
#endif
