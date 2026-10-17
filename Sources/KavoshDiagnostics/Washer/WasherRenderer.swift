#if os(macOS)
import AppKit
import KavoshCore
import MetalKit
import QuartzCore
import SwiftUI

/// What the washer is drawing right now. Written on the main thread, read by the renderer
/// (also on the main thread, from MTKView's draw callback).
final class WasherRenderState {
    enum Mode: Int32 {
        case noise = 0
        case rgbw = 1
        case gradient = 2
        case solid = 3
    }

    var mode: Mode = .gradient
    var solidColor = SIMD4<Float>(0.5, 0.5, 0.5, 1)
    let startTime = CACurrentMediaTime()
}

enum WasherShaders {
    static let source = """
    #include <metal_stdlib>
    using namespace metal;

    struct WasherParams {
        float2 size;
        float  time;
        uint   frame;
        int    mode;
        int    reserved;
        float4 color;
    };

    struct WasherVertexOut {
        float4 position [[position]];
    };

    vertex WasherVertexOut washer_vertex(uint vid [[vertex_id]]) {
        float2 corner = float2(float((vid << 1) & 2), float(vid & 2));
        WasherVertexOut out;
        out.position = float4(corner * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
        return out;
    }

    static uint washer_hash(uint x, uint y, uint z) {
        uint h = x * 374761393u + y * 668265263u + z * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    fragment float4 washer_fragment(WasherVertexOut v [[stage_in]], constant WasherParams &p [[buffer(0)]]) {
        float2 pos = v.position.xy;
        if (p.mode == 0) {
            // Random color per 2x2 block, new every frame.
            uint2 cell = uint2(pos * 0.5);
            uint h = washer_hash(cell.x, cell.y, p.frame);
            float3 c = float3(float(h & 255u), float((h >> 8) & 255u), float((h >> 16) & 255u)) / 255.0;
            return float4(c, 1.0);
        }
        if (p.mode == 1) {
            // Red, green, blue, white, black — about 10 changes per second at 60 Hz.
            uint stepIndex = (p.frame / 6u) % 5u;
            float3 c = float3(0.0);
            if (stepIndex == 0u) { c = float3(1.0, 0.0, 0.0); }
            else if (stepIndex == 1u) { c = float3(0.0, 1.0, 0.0); }
            else if (stepIndex == 2u) { c = float3(0.0, 0.0, 1.0); }
            else if (stepIndex == 3u) { c = float3(1.0); }
            return float4(c, 1.0);
        }
        if (p.mode == 2) {
            // Slowly drifting full-spectrum gradient.
            float x = pos.x / max(p.size.x, 1.0) + pos.y / max(p.size.y, 1.0) * 0.25 + p.time * 0.06;
            float3 c = 0.5 + 0.5 * cos(6.28318530718 * (x + float3(0.0, 0.33, 0.67)));
            return float4(c, 1.0);
        }
        return float4(p.color.rgb, 1.0);
    }
    """
}

/// MTKView that draws the washer patterns full screen.
@MainActor
struct WasherMetalView: NSViewRepresentable {
    let state: WasherRenderState

    func makeCoordinator() -> WasherRenderer {
        WasherRenderer(state: state)
    }

    func makeNSView(context: Context) -> PassthroughMTKView {
        let view = PassthroughMTKView()
        view.device = context.coordinator.device
        view.colorPixelFormat = .bgra8Unorm
        view.framebufferOnly = true
        view.enableSetNeedsDisplay = false
        view.isPaused = false
        view.preferredFramesPerSecond = 60
        view.clearColor = MTLClearColor(red: 0, green: 0, blue: 0, alpha: 1)
        view.delegate = context.coordinator
        return view
    }

    func updateNSView(_ nsView: PassthroughMTKView, context: Context) {}

    static func dismantleNSView(_ nsView: PassthroughMTKView, coordinator: WasherRenderer) {
        nsView.isPaused = true
        nsView.delegate = nil
    }
}

/// MTKView that lets mouse events through to SwiftUI (for hover and clicks).
final class PassthroughMTKView: MTKView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

final class WasherRenderer: NSObject, MTKViewDelegate {
    private struct Params {
        var size: SIMD2<Float>
        var time: Float
        var frame: UInt32
        var mode: Int32
        var reserved: Int32
        var color: SIMD4<Float>
    }

    let device: MTLDevice?
    private let queue: MTLCommandQueue?
    private let pipeline: MTLRenderPipelineState?
    private let state: WasherRenderState
    private var frame: UInt32 = 0

    init(state: WasherRenderState) {
        self.state = state
        let device = MTLCreateSystemDefaultDevice()
        self.device = device
        queue = device?.makeCommandQueue()
        var pipeline: MTLRenderPipelineState?
        if let device, let library = try? device.makeLibrary(source: WasherShaders.source, options: MTLCompileOptions()) {
            let descriptor = MTLRenderPipelineDescriptor()
            descriptor.vertexFunction = library.makeFunction(name: "washer_vertex")
            descriptor.fragmentFunction = library.makeFunction(name: "washer_fragment")
            descriptor.colorAttachments[0].pixelFormat = .bgra8Unorm
            pipeline = try? device.makeRenderPipelineState(descriptor: descriptor)
        }
        self.pipeline = pipeline
        super.init()
    }

    func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) {}

    func draw(in view: MTKView) {
        guard let pipeline, let queue,
              let pass = view.currentRenderPassDescriptor,
              let drawable = view.currentDrawable,
              let commandBuffer = queue.makeCommandBuffer(),
              let encoder = commandBuffer.makeRenderCommandEncoder(descriptor: pass) else { return }
        frame &+= 1
        var params = Params(
            size: SIMD2<Float>(Float(view.drawableSize.width), Float(view.drawableSize.height)),
            time: Float(CACurrentMediaTime() - state.startTime),
            frame: frame,
            mode: state.mode.rawValue,
            reserved: 0,
            color: state.solidColor
        )
        encoder.setRenderPipelineState(pipeline)
        encoder.setFragmentBytes(&params, length: MemoryLayout<Params>.stride, index: 0)
        encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
        encoder.endEncoding()
        commandBuffer.present(drawable)
        commandBuffer.commit()
    }
}
#endif
