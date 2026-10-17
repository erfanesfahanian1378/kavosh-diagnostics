#if os(macOS)
import KavoshCore
import SwiftUI

/// Live CPU / memory / GPU load charts plus temperatures, power and fans.
@MainActor
struct TelemetryPanel: View {
    @EnvironmentObject private var telemetry: LiveTelemetry
    var compact = false

    var body: some View {
        VStack(spacing: compact ? 12 : 16) {
            RetroCard(padding: 14) {
                VStack(alignment: .leading, spacing: 14) {
                    SectionLabel(T("Live resource usage", "مصرف زنده منابع"), symbol: "waveform.path.ecg")
                    LoadChartBlock(
                        title: T("CPU load — last 90s", "بار پردازنده — ۹۰ ثانیه اخیر"),
                        value: telemetry.latest.map { Fmt.percent($0.cpuLoadPercent) } ?? "—",
                        history: telemetry.cpuHistory,
                        color: Theme.accent
                    ) {
                        if let cores = telemetry.latest?.perCoreLoadPercent, !cores.isEmpty, !compact {
                            BarStrip(values: cores, color: Theme.accent.opacity(0.8))
                                .frame(height: 22)
                        }
                    }
                    LoadChartBlock(
                        title: T("Memory load", "بار حافظه"),
                        value: telemetry.latest.map { "\(Fmt.fixed($0.ramUsedGB, 1)) / \(Fmt.fixed($0.ramTotalGB, 0)) GB" } ?? "—",
                        history: telemetry.ramHistory,
                        color: Theme.purple
                    ) {
                        if let pressure = telemetry.latest?.memoryPressure {
                            RetroBadge(pressure.title, color: pressureColor(pressure), textColor: Theme.onYellow)
                        }
                    }
                    LoadChartBlock(
                        title: T("GPU load", "بار گرافیک"),
                        value: telemetry.latest?.gpuLoadPercent.map { Fmt.percent($0) } ?? "—",
                        history: telemetry.gpuHistory,
                        color: Theme.teal
                    ) {
                        EmptyView()
                    }
                }
            }
            SensorGrid(compact: compact)
        }
    }

    private func pressureColor(_ level: MemoryPressureLevel) -> Color {
        switch level {
        case .normal: return Theme.green
        case .warning: return Theme.yellow
        case .critical: return Theme.red
        }
    }
}

@MainActor
struct LoadChartBlock<Extra: View>: View {
    var title: String
    var value: String
    var history: [Double]
    var color: Color
    @ViewBuilder var extra: Extra

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(title)
                    .font(Theme.font(11, .bold))
                    .foregroundStyle(Theme.muted)
                Spacer()
                HardwareText(value, size: 14, weight: .heavy, color: color)
            }
            HistoryChart(values: history, maxValue: 100, capacity: LiveTelemetry.capacity, color: color)
                .frame(height: 54)
                .padding(4)
                .background(RoundedRectangle(cornerRadius: 8).fill(Theme.surfaceAlt.opacity(0.6)))
                .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.ink.opacity(0.3), lineWidth: 1))
            extra
        }
    }
}

@MainActor
struct SensorGrid: View {
    @EnvironmentObject private var telemetry: LiveTelemetry
    var compact = false

    var body: some View {
        let s = telemetry.latest
        RetroCard(padding: 14) {
            VStack(alignment: .leading, spacing: 12) {
                SectionLabel(T("Live telemetry", "حسگرهای زنده"), symbol: "thermometer.medium")
                LazyVGrid(columns: [GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10)], spacing: 10) {
                    SensorCell(title: T("CPU temperature", "دمای پردازنده"),
                               value: Fmt.celsius(s?.cpuTemperatureCelsius),
                               color: Theme.temperatureColor(s?.cpuTemperatureCelsius))
                    SensorCell(title: T("GPU temperature", "دمای گرافیک"),
                               value: Fmt.celsius(s?.gpuTemperatureCelsius),
                               color: Theme.temperatureColor(s?.gpuTemperatureCelsius))
                    SensorCell(title: T("System power", "توان سیستم"),
                               value: Fmt.watts(s?.systemPowerWatts),
                               color: Theme.ink)
                    SensorCell(title: T("System fan", "فن سیستم"),
                               value: fanText(s),
                               color: Theme.ink)
                }
                HStack {
                    Text(T("Thermal state", "وضعیت حرارتی"))
                        .font(Theme.font(11, .bold))
                        .foregroundStyle(Theme.muted)
                    Spacer()
                    RetroBadge(s?.thermalState.title ?? "—", color: thermalColor(s?.thermalState), textColor: Theme.onYellow)
                }
                if let s, !s.hasSensorAccess {
                    Text(T("This Mac's temperature, fan and power sensors can't be read. The thermal state above comes from macOS itself and is still accurate.",
                           "حسگرهای دما، فن و توان این مک خوانده نمی‌شوند. وضعیت حرارتی بالا از خود macOS می‌آید و همچنان دقیق است."))
                        .font(Theme.font(11, .medium))
                        .foregroundStyle(Theme.muted)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private func fanText(_ s: SensorSnapshot?) -> String {
        guard let s else { return "—" }
        if let count = s.fanCount, count == 0 { return T("Fanless", "بدون فن") }
        guard !s.fanRPM.isEmpty else { return "—" }
        return s.fanRPM.map { "\(Fmt.number($0, decimals: 0)) rpm" }.joined(separator: " / ")
    }

    private func thermalColor(_ level: ThermalLevel?) -> Color {
        switch level {
        case .some(.nominal): return Theme.green
        case .some(.fair): return Theme.yellow
        case .some(.serious): return Theme.orange
        case .some(.critical): return Theme.red
        case .none: return Theme.surfaceAlt
        }
    }
}

@MainActor
struct SensorCell: View {
    var title: String
    var value: String
    var color: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(title)
                .font(Theme.font(10, .bold))
                .foregroundStyle(Theme.muted)
                .lineLimit(1)
            HardwareText(value, size: 16, weight: .heavy, color: color)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
        }
        .padding(9)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 8).fill(Theme.surfaceAlt.opacity(0.6)))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.ink.opacity(0.3), lineWidth: 1))
    }
}
#endif
