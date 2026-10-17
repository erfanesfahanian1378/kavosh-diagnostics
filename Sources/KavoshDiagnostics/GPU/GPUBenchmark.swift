#if os(macOS)
import CoreGraphics
import Foundation
import KavoshCore
import Metal

enum GPUBenchmarkError: Error, CustomStringConvertible {
    case noDevice
    case shaderCompilation(String)
    case outOfMemory
    case frameFailed(String)
    case cancelled

    var description: String {
        switch self {
        case .noDevice:
            return T("No Metal-capable GPU was found.", "هیچ پردازنده گرافیکی سازگار با Metal پیدا نشد.")
        case .shaderCompilation(let detail):
            return T("The GPU shader couldn't be compiled: \(detail)", "شیدر گرافیکی کامپایل نشد: \(detail)")
        case .outOfMemory:
            return T("Not enough graphics memory for the render targets.", "حافظه گرافیکی کافی برای رندر نیست.")
        case .frameFailed(let detail):
            return T("The GPU did not complete a frame — the graphics driver may have stopped responding and macOS reset the GPU. Try again; if it keeps happening, update macOS. (\(detail))",
                     "پردازنده گرافیکی یک فریم را کامل نکرد — ممکن است درایور گرافیک متوقف شده و macOS آن را ریست کرده باشد. دوباره امتحان کنید؛ اگر تکرار شد macOS را به‌روز کنید. (\(detail))")
        case .cancelled:
            return T("The test was stopped.", "تست متوقف شد.")
        }
    }
}

struct GPUBenchmarkProgress {
    enum Stage: Equatable {
        case preparing
        case warmingUp(frame: Int)
        case scored(frame: Int, of: Int)
        case sustained(frames: Int)
        case finished
    }

    var stage: Stage
    var fraction: Double
    var lastFrameMs: Double?
    var liveScore: Double?
    var preview: CGImage?
}

/// Renders the fractal scene off-screen at 2560×1440 with 2×2 supersampling (a 5120×2880
/// internal image) and times each frame.
final class GPURaymarchBenchmark {
    struct Configuration {
        var outputWidth = 2560
        var outputHeight = 1440
        var supersample = 2
        /// The internal image is drawn in horizontal bands, each its own command buffer, so
        /// a slow GPU never runs one huge command long enough to trip the GPU watchdog.
        var bands = 8
        var minimumWarmUp = 2
        var maximumWarmUp = 10
        /// Scored frames come in whole camera orbits (24 frames), so every Mac averages the
        /// same set of images.
        var orbitFrames = 24
        var minimumScoredSeconds = 6.0
        var maximumScoredOrbits = 20
        var totalSeconds = 60.0
        var previewWidth = 640
        var previewHeight = 360
    }

    /// Reference frame time: it scores 1,000. Estimated for an Apple M1 (8-core GPU).
    static let referenceFrameMs = 260.0
    static let referenceName = "Apple M1 (8-core GPU)"
    static let scoringVersion = 1

    private struct Params {
        var resolution: SIMD2<Float>
        var time: Float
        var power: Float
        var supersample: Int32
        var reserved: Int32
    }

    let configuration: Configuration
    let device: MTLDevice
    private let queue: MTLCommandQueue
    private let scenePipeline: MTLRenderPipelineState
    private let resolvePipeline: MTLRenderPipelineState
    private let previewPipeline: MTLRenderPipelineState
    private let sceneTexture: MTLTexture
    private let outputTexture: MTLTexture
    private let previewTexture: MTLTexture
    private let previewBuffer: MTLBuffer

    var internalWidth: Int { configuration.outputWidth * configuration.supersample }
    var internalHeight: Int { configuration.outputHeight * configuration.supersample }

    init(configuration: Configuration = Configuration()) throws {
        self.configuration = configuration
        guard let device = MTLCreateSystemDefaultDevice() else { throw GPUBenchmarkError.noDevice }
        guard let queue = device.makeCommandQueue() else { throw GPUBenchmarkError.noDevice }
        self.device = device
        self.queue = queue

        let library: MTLLibrary
        do {
            library = try device.makeLibrary(source: GPUShaders.source, options: MTLCompileOptions())
        } catch {
            throw GPUBenchmarkError.shaderCompilation(error.localizedDescription)
        }

        func pipeline(_ fragment: String, _ format: MTLPixelFormat) throws -> MTLRenderPipelineState {
            let descriptor = MTLRenderPipelineDescriptor()
            descriptor.vertexFunction = library.makeFunction(name: "kv_fullscreen")
            descriptor.fragmentFunction = library.makeFunction(name: fragment)
            descriptor.colorAttachments[0].pixelFormat = format
            do {
                return try device.makeRenderPipelineState(descriptor: descriptor)
            } catch {
                throw GPUBenchmarkError.shaderCompilation(error.localizedDescription)
            }
        }
        scenePipeline = try pipeline("kv_scene_fragment", .rgba16Float)
        resolvePipeline = try pipeline("kv_resolve_fragment", .rgba8Unorm)
        previewPipeline = try pipeline("kv_preview_fragment", .bgra8Unorm)

        func texture(_ format: MTLPixelFormat, _ width: Int, _ height: Int) throws -> MTLTexture {
            let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: format, width: width, height: height, mipmapped: false)
            descriptor.usage = [.renderTarget, .shaderRead]
            descriptor.storageMode = .private
            guard let texture = device.makeTexture(descriptor: descriptor) else { throw GPUBenchmarkError.outOfMemory }
            return texture
        }
        sceneTexture = try texture(.rgba16Float, configuration.outputWidth * configuration.supersample,
                                   configuration.outputHeight * configuration.supersample)
        outputTexture = try texture(.rgba8Unorm, configuration.outputWidth, configuration.outputHeight)
        previewTexture = try texture(.bgra8Unorm, configuration.previewWidth, configuration.previewHeight)
        guard let buffer = device.makeBuffer(length: configuration.previewWidth * configuration.previewHeight * 4,
                                             options: .storageModeShared) else {
            throw GPUBenchmarkError.outOfMemory
        }
        previewBuffer = buffer
    }

    // MARK: Frames

    /// Renders frame `index` and returns how long it took, in milliseconds.
    private func renderFrame(index: Int) throws -> Double {
        let config = configuration
        var params = Params(
            resolution: SIMD2<Float>(Float(internalWidth), Float(internalHeight)),
            time: Float(index % config.orbitFrames) / Float(config.orbitFrames),
            power: 8,
            supersample: Int32(config.supersample),
            reserved: 0
        )
        let start = DispatchTime.now().uptimeNanoseconds
        let bandHeight = (internalHeight + config.bands - 1) / config.bands
        var buffers: [MTLCommandBuffer] = []

        for band in 0..<config.bands {
            let y = band * bandHeight
            let height = min(bandHeight, internalHeight - y)
            guard height > 0 else { continue }
            guard let commandBuffer = queue.makeCommandBuffer() else { throw GPUBenchmarkError.frameFailed("no command buffer") }
            let pass = MTLRenderPassDescriptor()
            pass.colorAttachments[0].texture = sceneTexture
            pass.colorAttachments[0].loadAction = .load
            pass.colorAttachments[0].storeAction = .store
            guard let encoder = commandBuffer.makeRenderCommandEncoder(descriptor: pass) else {
                throw GPUBenchmarkError.frameFailed("no render encoder")
            }
            encoder.setRenderPipelineState(scenePipeline)
            encoder.setScissorRect(MTLScissorRect(x: 0, y: y, width: internalWidth, height: height))
            encoder.setFragmentBytes(&params, length: MemoryLayout<Params>.stride, index: 0)
            encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
            encoder.endEncoding()
            commandBuffer.commit()
            buffers.append(commandBuffer)
        }

        guard let finish = queue.makeCommandBuffer() else { throw GPUBenchmarkError.frameFailed("no command buffer") }
        let resolvePass = MTLRenderPassDescriptor()
        resolvePass.colorAttachments[0].texture = outputTexture
        resolvePass.colorAttachments[0].loadAction = .dontCare
        resolvePass.colorAttachments[0].storeAction = .store
        if let encoder = finish.makeRenderCommandEncoder(descriptor: resolvePass) {
            encoder.setRenderPipelineState(resolvePipeline)
            encoder.setFragmentBytes(&params, length: MemoryLayout<Params>.stride, index: 0)
            encoder.setFragmentTexture(sceneTexture, index: 0)
            encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
            encoder.endEncoding()
        }
        let previewPass = MTLRenderPassDescriptor()
        previewPass.colorAttachments[0].texture = previewTexture
        previewPass.colorAttachments[0].loadAction = .dontCare
        previewPass.colorAttachments[0].storeAction = .store
        var previewSize = SIMD2<Float>(Float(config.previewWidth), Float(config.previewHeight))
        if let encoder = finish.makeRenderCommandEncoder(descriptor: previewPass) {
            encoder.setRenderPipelineState(previewPipeline)
            encoder.setFragmentBytes(&previewSize, length: MemoryLayout<SIMD2<Float>>.stride, index: 0)
            encoder.setFragmentTexture(outputTexture, index: 0)
            encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
            encoder.endEncoding()
        }
        if let blit = finish.makeBlitCommandEncoder() {
            blit.copy(from: previewTexture, sourceSlice: 0, sourceLevel: 0,
                      sourceOrigin: MTLOrigin(x: 0, y: 0, z: 0),
                      sourceSize: MTLSize(width: config.previewWidth, height: config.previewHeight, depth: 1),
                      to: previewBuffer, destinationOffset: 0,
                      destinationBytesPerRow: config.previewWidth * 4,
                      destinationBytesPerImage: config.previewWidth * config.previewHeight * 4)
            blit.endEncoding()
        }
        finish.commit()
        finish.waitUntilCompleted()
        buffers.append(finish)

        for buffer in buffers where buffer.status == .error {
            throw GPUBenchmarkError.frameFailed(buffer.error?.localizedDescription ?? "unknown error")
        }
        return Double(DispatchTime.now().uptimeNanoseconds - start) / 1_000_000
    }

    private func previewImage() -> CGImage? {
        let width = configuration.previewWidth
        let height = configuration.previewHeight
        let data = Data(bytes: previewBuffer.contents(), count: width * height * 4)
        guard let provider = CGDataProvider(data: data as CFData) else { return nil }
        let info = CGBitmapInfo(rawValue: CGImageAlphaInfo.noneSkipFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
        return CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: width * 4,
                       space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: info, provider: provider, decode: nil,
                       shouldInterpolate: true, intent: .defaultIntent)
    }

    // MARK: Run

    func run(isCancelled: () -> Bool, progress: (GPUBenchmarkProgress) -> Void) throws -> GpuBenchmarkResult {
        let config = configuration
        let started = Date()
        let total = max(config.totalSeconds, 1)
        func fraction() -> Double { min(0.99, Date().timeIntervalSince(started) / total) }
        func score(_ ms: Double) -> Double { ms > 0 ? 1000 * GPURaymarchBenchmark.referenceFrameMs / ms : 0 }

        progress(GPUBenchmarkProgress(stage: .preparing, fraction: 0))

        // Warm up until frame times settle.
        var warm: [Double] = []
        while warm.count < config.maximumWarmUp {
            if isCancelled() { throw GPUBenchmarkError.cancelled }
            let ms = try renderFrame(index: warm.count)
            warm.append(ms)
            progress(GPUBenchmarkProgress(stage: .warmingUp(frame: warm.count), fraction: fraction(), lastFrameMs: ms,
                                          liveScore: nil, preview: previewImage()))
            if warm.count >= config.minimumWarmUp {
                let recent = warm.suffix(3)
                let mean = recent.reduce(0, +) / Double(recent.count)
                if let lo = recent.min(), let hi = recent.max(), mean > 0, (hi - lo) / mean < 0.06 { break }
            }
        }

        // Scored run: whole orbits until enough time has passed.
        var times: [Double] = []
        let scoredStart = Date()
        var orbits = 0
        repeat {
            for i in 0..<config.orbitFrames {
                if isCancelled() { throw GPUBenchmarkError.cancelled }
                let ms = try renderFrame(index: i)
                times.append(ms)
                let mean = times.reduce(0, +) / Double(times.count)
                progress(GPUBenchmarkProgress(stage: .scored(frame: times.count, of: (orbits + 1) * config.orbitFrames),
                                              fraction: fraction(), lastFrameMs: ms, liveScore: score(mean),
                                              preview: previewImage()))
            }
            orbits += 1
        } while Date().timeIntervalSince(scoredStart) < config.minimumScoredSeconds && orbits < config.maximumScoredOrbits

        // Sustained: keep rendering for the rest of the chosen duration.
        var sustained: [Double] = []
        var frame = 0
        if config.totalSeconds - Date().timeIntervalSince(started) > 10 {
            while Date().timeIntervalSince(started) < config.totalSeconds {
                if isCancelled() { throw GPUBenchmarkError.cancelled }
                let ms = try renderFrame(index: frame)
                frame += 1
                sustained.append(ms)
                let recent = sustained.suffix(config.orbitFrames)
                progress(GPUBenchmarkProgress(stage: .sustained(frames: sustained.count), fraction: fraction(), lastFrameMs: ms,
                                              liveScore: score(recent.reduce(0, +) / Double(recent.count)),
                                              preview: previewImage()))
            }
        }
        // Sustained = the last third of the soak.
        var sustainedMean: Double?
        if sustained.count >= 3 {
            let tail = sustained.suffix(max(3, sustained.count / 3))
            sustainedMean = tail.reduce(0, +) / Double(tail.count)
        }

        progress(GPUBenchmarkProgress(stage: .finished, fraction: 1, lastFrameMs: times.last, liveScore: nil, preview: previewImage()))

        return GpuBenchmarkResult.make(
            frameTimesMs: times,
            sustainedMeanMs: sustainedMean,
            totalFrames: warm.count + times.count + sustained.count,
            seconds: Date().timeIntervalSince(started),
            adapterName: device.name,
            outputWidth: config.outputWidth,
            outputHeight: config.outputHeight,
            renderWidth: internalWidth,
            renderHeight: internalHeight,
            samplesPerPixel: config.supersample * config.supersample,
            referenceFrameMs: GPURaymarchBenchmark.referenceFrameMs,
            scoringVersion: GPURaymarchBenchmark.scoringVersion
        )
    }
}
#endif
