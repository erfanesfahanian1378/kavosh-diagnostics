#if os(macOS)
import KavoshCore
import SwiftUI

// MARK: - Surfaces

/// A card with an ink outline and a hard offset shadow.
@MainActor
struct RetroCard<Content: View>: View {
    var padding: CGFloat = 16
    var fill: Color = Theme.surface
    var shadow: CGFloat = Theme.shadowOffset
    @ViewBuilder var content: Content

    var body: some View {
        content
            .padding(padding)
            .frame(maxWidth: .infinity, alignment: .topLeading)
            .retroSurface(fill: fill, shadow: shadow)
    }
}

extension View {
    func retroSurface(fill: Color = Theme.surface, corner: CGFloat = Theme.corner, shadow: CGFloat = Theme.shadowOffset,
                      border: Color = Theme.ink) -> some View {
        self
            .background(RoundedRectangle(cornerRadius: corner, style: .continuous).fill(fill))
            .overlay(RoundedRectangle(cornerRadius: corner, style: .continuous).strokeBorder(border, lineWidth: Theme.border))
            .background(
                RoundedRectangle(cornerRadius: corner, style: .continuous)
                    .fill(Theme.shadow)
                    .offset(x: shadow, y: shadow)
            )
    }
}

// MARK: - Text

/// Small uppercase heading used above every block ("LIVE RESOURCE USAGE").
@MainActor
struct SectionLabel: View {
    var text: String
    var symbol: String?
    var color: Color = Theme.muted

    init(_ text: String, symbol: String? = nil, color: Color = Theme.muted) {
        self.text = text
        self.symbol = symbol
        self.color = color
    }

    var body: some View {
        HStack(spacing: 6) {
            if let symbol {
                Image(systemName: symbol)
                    .font(.system(size: 11, weight: .bold))
            }
            Text(Loc.language == .fa ? text : text.uppercased())
                .font(Theme.font(11, .bold))
                .tracking(Loc.language == .fa ? 0 : 1.1)
        }
        .foregroundStyle(color)
    }
}

/// Hardware names and numbers: always left-to-right, Latin face.
@MainActor
struct HardwareText: View {
    var text: String
    var size: CGFloat = 13
    var weight: Font.Weight = .semibold
    var color: Color = Theme.ink

    init(_ text: String, size: CGFloat = 13, weight: Font.Weight = .semibold, color: Color = Theme.ink) {
        self.text = text
        self.size = size
        self.weight = weight
        self.color = color
    }

    var body: some View {
        Text(text)
            .font(Theme.number(size, weight))
            .foregroundStyle(color)
            .environment(\.layoutDirection, .leftToRight)
    }
}

// MARK: - Buttons

enum RetroButtonKind {
    case primary, secondary, danger, warning, success, ghost
}

enum RetroButtonSize {
    case small, regular, large
}

struct RetroButtonStyle: ButtonStyle {
    var kind: RetroButtonKind = .secondary
    var size: RetroButtonSize = .regular
    var fullWidth = false

    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        let pressed = configuration.isPressed && isEnabled
        let offset: CGFloat = size == .small ? 2 : 3
        return configuration.label
            .font(font)
            .foregroundStyle(foreground)
            .padding(.horizontal, horizontalPadding)
            .padding(.vertical, verticalPadding)
            .frame(maxWidth: fullWidth ? .infinity : nil)
            .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(background))
            .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Theme.ink, lineWidth: Theme.border))
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(Theme.shadow)
                    .offset(x: pressed ? 0 : offset, y: pressed ? 0 : offset)
            )
            .offset(x: pressed ? offset : 0, y: pressed ? offset : 0)
            .opacity(isEnabled ? 1 : 0.45)
            .contentShape(Rectangle())
            .animation(.easeOut(duration: 0.08), value: pressed)
    }

    private var font: Font {
        switch size {
        case .small: return Theme.font(12, .bold)
        case .regular: return Theme.font(14, .bold)
        case .large: return Theme.font(17, .heavy)
        }
    }

    private var horizontalPadding: CGFloat {
        switch size {
        case .small: return 10
        case .regular: return 16
        case .large: return 22
        }
    }

    private var verticalPadding: CGFloat {
        switch size {
        case .small: return 5
        case .regular: return 9
        case .large: return 14
        }
    }

    private var background: Color {
        switch kind {
        case .primary: return Theme.accent
        case .secondary: return Theme.surface
        case .danger: return Theme.red
        case .warning: return Theme.yellow
        case .success: return Theme.green
        case .ghost: return Theme.surfaceAlt
        }
    }

    private var foreground: Color {
        switch kind {
        case .primary, .danger, .success: return Theme.onAccent
        case .warning: return Theme.onYellow
        case .secondary, .ghost: return Theme.ink
        }
    }
}

extension View {
    func retroButton(_ kind: RetroButtonKind = .secondary, size: RetroButtonSize = .regular, fullWidth: Bool = false) -> some View {
        buttonStyle(RetroButtonStyle(kind: kind, size: size, fullWidth: fullWidth))
    }
}

// MARK: - Badges

@MainActor
struct RetroBadge: View {
    var text: String
    var color: Color = Theme.surfaceAlt
    var textColor: Color = Theme.ink
    var symbol: String?

    init(_ text: String, color: Color = Theme.surfaceAlt, textColor: Color = Theme.ink, symbol: String? = nil) {
        self.text = text
        self.color = color
        self.textColor = textColor
        self.symbol = symbol
    }

    var body: some View {
        HStack(spacing: 4) {
            if let symbol {
                Image(systemName: symbol).font(.system(size: 10, weight: .heavy))
            }
            Text(text)
                .font(Theme.font(11, .bold))
                .lineLimit(1)
        }
        .foregroundStyle(textColor)
        .padding(.horizontal, 8)
        .padding(.vertical, 3)
        .background(Capsule().fill(color))
        .overlay(Capsule().strokeBorder(Theme.ink, lineWidth: 1.5))
    }
}

/// Badge for a 0–100 score, colored by how good it is.
@MainActor
struct ScoreBadge: View {
    var score: Double

    var body: some View {
        RetroBadge(Fmt.fixed(score, 0), color: Theme.scoreColor(score), textColor: Theme.onYellow)
            .environment(\.layoutDirection, .leftToRight)
    }
}

@MainActor
struct GradeBadge: View {
    var grade: DeviceGrade?
    var size: CGFloat = 96

    var body: some View {
        let color = grade.map { Theme.color(for: $0.tone) } ?? Theme.surfaceAlt
        Text(grade?.rawValue ?? "—")
            .font(.system(size: size * 0.5, weight: .black, design: .rounded))
            .foregroundStyle(Theme.onYellow)
            .frame(width: size, height: size)
            .retroSurface(fill: color, corner: size * 0.18, shadow: size > 60 ? 5 : 3)
            .environment(\.layoutDirection, .leftToRight)
    }
}

// MARK: - Progress

@MainActor
struct RetroProgressBar: View {
    var value: Double
    var color: Color = Theme.accent
    var height: CGFloat = 14
    var indeterminate = false

    @State private var phase: CGFloat = 0

    var body: some View {
        GeometryReader { geo in
            let fraction = max(0, min(1, value))
            ZStack(alignment: .leading) {
                RoundedRectangle(cornerRadius: height / 2, style: .continuous)
                    .fill(Theme.surfaceAlt)
                if indeterminate {
                    RoundedRectangle(cornerRadius: height / 2, style: .continuous)
                        .fill(color)
                        .frame(width: geo.size.width * 0.3)
                        .offset(x: (geo.size.width * 0.7) * phase)
                } else {
                    RoundedRectangle(cornerRadius: height / 2, style: .continuous)
                        .fill(color)
                        .frame(width: max(fraction > 0 ? height : 0, geo.size.width * fraction))
                }
                RoundedRectangle(cornerRadius: height / 2, style: .continuous)
                    .strokeBorder(Theme.ink, lineWidth: Theme.border)
            }
            .clipShape(RoundedRectangle(cornerRadius: height / 2, style: .continuous))
        }
        .frame(height: height)
        .environment(\.layoutDirection, .leftToRight)
        .onAppear {
            if indeterminate {
                withAnimation(.easeInOut(duration: 0.9).repeatForever(autoreverses: true)) { phase = 1 }
            }
        }
        .animation(.easeOut(duration: 0.25), value: value)
    }
}

// MARK: - Charts

/// A simple area chart of the last N samples, newest on the right.
@MainActor
struct HistoryChart: View {
    var values: [Double]
    var maxValue: Double = 100
    var capacity: Int = 90
    var color: Color = Theme.accent
    var showGrid = true

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width
            let h = geo.size.height
            let top = max(maxValue, values.max() ?? 0, 1)
            let step = w / CGFloat(max(1, capacity - 1))
            let offset = CGFloat(capacity - values.count) * step
            let points: [CGPoint] = values.enumerated().map { index, value in
                CGPoint(x: offset + CGFloat(index) * step,
                        y: h - CGFloat(max(0, min(value, top)) / top) * (h - 2) - 1)
            }
            ZStack {
                if showGrid {
                    Path { path in
                        for i in 1..<4 {
                            let y = h * CGFloat(i) / 4
                            path.move(to: CGPoint(x: 0, y: y))
                            path.addLine(to: CGPoint(x: w, y: y))
                        }
                    }
                    .stroke(Theme.grid, style: StrokeStyle(lineWidth: 1, dash: [3, 4]))
                }
                if points.count > 1 {
                    Path { path in
                        path.move(to: CGPoint(x: points[0].x, y: h))
                        for p in points { path.addLine(to: p) }
                        path.addLine(to: CGPoint(x: points[points.count - 1].x, y: h))
                        path.closeSubpath()
                    }
                    .fill(LinearGradient(colors: [color.opacity(0.45), color.opacity(0.05)], startPoint: .top, endPoint: .bottom))
                    Path { path in
                        path.move(to: points[0])
                        for p in points.dropFirst() { path.addLine(to: p) }
                    }
                    .stroke(color, style: StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round))
                }
            }
        }
        .environment(\.layoutDirection, .leftToRight)
    }
}

/// A row of bars, e.g. per-core CPU load.
@MainActor
struct BarStrip: View {
    var values: [Double]
    var maxValue: Double = 100
    var color: Color = Theme.accent

    var body: some View {
        GeometryReader { geo in
            let count = max(1, values.count)
            let spacing: CGFloat = 2
            let barWidth = max(1, (geo.size.width - spacing * CGFloat(count - 1)) / CGFloat(count))
            HStack(alignment: .bottom, spacing: spacing) {
                ForEach(Array(values.enumerated()), id: \.offset) { _, value in
                    RoundedRectangle(cornerRadius: 2)
                        .fill(color)
                        .frame(width: barWidth, height: max(2, geo.size.height * CGFloat(min(value, maxValue) / maxValue)))
                }
            }
            .frame(width: geo.size.width, height: geo.size.height, alignment: .bottomLeading)
        }
        .environment(\.layoutDirection, .leftToRight)
    }
}

// MARK: - Small building blocks

/// Label + big value, used for live readings.
@MainActor
struct StatTile: View {
    var title: String
    var value: String
    var detail: String?
    var color: Color = Theme.ink
    var symbol: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            SectionLabel(title, symbol: symbol)
            HardwareText(value, size: 22, weight: .heavy, color: color)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
            if let detail {
                Text(detail)
                    .font(Theme.font(11, .medium))
                    .foregroundStyle(Theme.muted)
                    .lineLimit(2)
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .retroSurface(fill: Theme.surface, corner: 10, shadow: 3)
    }
}

/// "Label ........ value" row.
@MainActor
struct KeyValueRow: View {
    var key: String
    var value: String
    var valueColor: Color = Theme.ink

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Text(key)
                .font(Theme.font(13, .medium))
                .foregroundStyle(Theme.muted)
            Spacer(minLength: 8)
            HardwareText(value, size: 13, weight: .semibold, color: valueColor)
                .multilineTextAlignment(.trailing)
                .lineLimit(2)
        }
    }
}

/// Check / cross / dash icon for pass, fail and not-run states.
@MainActor
struct StatusIcon: View {
    enum State { case pass, fail, warn, skipped, pending }
    var state: State

    var body: some View {
        let (symbol, color): (String, Color) = {
            switch state {
            case .pass: return ("checkmark", Theme.green)
            case .fail: return ("xmark", Theme.red)
            case .warn: return ("exclamationmark", Theme.yellow)
            case .skipped: return ("minus", Theme.surfaceAlt)
            case .pending: return ("circle.dotted", Theme.surfaceAlt)
            }
        }()
        Image(systemName: symbol)
            .font(.system(size: 11, weight: .black))
            .foregroundStyle(state == .pass || state == .fail ? Color.white : Theme.ink)
            .frame(width: 22, height: 22)
            .background(Circle().fill(color))
            .overlay(Circle().strokeBorder(Theme.ink, lineWidth: 1.5))
    }
}

/// Checkbox-style toggle in the retro look.
struct RetroCheckboxStyle: ToggleStyle {
    func makeBody(configuration: Configuration) -> some View {
        Button {
            configuration.isOn.toggle()
        } label: {
            HStack(alignment: .top, spacing: 10) {
                Image(systemName: "checkmark")
                    .font(.system(size: 11, weight: .black))
                    .foregroundStyle(Theme.onAccent)
                    .opacity(configuration.isOn ? 1 : 0)
                    .frame(width: 20, height: 20)
                    .background(RoundedRectangle(cornerRadius: 5).fill(configuration.isOn ? Theme.accent : Theme.surface))
                    .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(Theme.ink, lineWidth: Theme.border))
                configuration.label
                    .font(Theme.font(13, .medium))
                    .foregroundStyle(Theme.ink)
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// Segmented picker drawn as a row of retro buttons.
@MainActor
struct RetroSegmented<Value: Hashable>: View {
    var options: [(Value, String)]
    @Binding var selection: Value

    var body: some View {
        FlowLayout(spacing: 8) {
            ForEach(Array(options.enumerated()), id: \.offset) { _, option in
                Button(option.1) { selection = option.0 }
                    .retroButton(selection == option.0 ? .primary : .secondary, size: .small)
            }
        }
    }
}

/// Simple wrapping layout for chips and buttons.
struct FlowLayout: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxWidth = proposal.width ?? .infinity
        var x: CGFloat = 0
        var y: CGFloat = 0
        var rowHeight: CGFloat = 0
        var widest: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > 0 && x + size.width > maxWidth {
                y += rowHeight + spacing
                x = 0
                rowHeight = 0
            }
            x += size.width + spacing
            widest = max(widest, x - spacing)
            rowHeight = max(rowHeight, size.height)
        }
        return CGSize(width: min(widest, maxWidth), height: y + rowHeight)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX
        var y = bounds.minY
        var rowHeight: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > bounds.minX && x + size.width > bounds.maxX {
                y += rowHeight + spacing
                x = bounds.minX
                rowHeight = 0
            }
            subview.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(size))
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
        }
    }
}
#endif
