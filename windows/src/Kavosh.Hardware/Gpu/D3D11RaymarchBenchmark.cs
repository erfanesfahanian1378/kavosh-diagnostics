using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Kavosh.Core;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice;
using Vortice.Mathematics;
using static Kavosh.Core.Loc;

namespace Kavosh.Hardware.Gpu;

public enum GpuBenchmarkFailureKind { NoDevice, ShaderCompilation, OutOfMemory, FrameFailed, Cancelled }

public sealed class GpuBenchmarkException : Exception
{
    public GpuBenchmarkFailureKind Kind { get; }

    public GpuBenchmarkException(GpuBenchmarkFailureKind kind, string? detail = null) : base(Describe(kind, detail))
    {
        Kind = kind;
    }

    private static string Describe(GpuBenchmarkFailureKind kind, string? detail) => kind switch
    {
        GpuBenchmarkFailureKind.NoDevice => T("No Direct3D 11 graphics adapter was found.", "هیچ کارت گرافیک سازگار با Direct3D 11 پیدا نشد."),
        GpuBenchmarkFailureKind.ShaderCompilation => T($"The GPU shader couldn't be compiled: {detail}", $"شیدر گرافیکی کامپایل نشد: {detail}"),
        GpuBenchmarkFailureKind.OutOfMemory => T("Not enough graphics memory for the render targets.", "حافظه گرافیکی کافی برای رندر نیست."),
        GpuBenchmarkFailureKind.FrameFailed => T($"The GPU did not complete a frame — the graphics driver may have stopped responding and Windows reset it. Try again; if it keeps happening, update the graphics driver. ({detail})",
                                                $"پردازنده گرافیکی یک فریم را کامل نکرد — ممکن است درایور گرافیک متوقف شده و ویندوز آن را ریست کرده باشد. دوباره امتحان کنید؛ اگر تکرار شد درایور گرافیک را به‌روز کنید. ({detail})"),
        _ => T("The test was stopped.", "تست متوقف شد."),
    };
}

public enum GpuBenchmarkStage { Preparing, WarmingUp, Scored, Sustained, Finished }

public struct GpuBenchmarkProgress
{
    public GpuBenchmarkStage Stage;
    public int Frame;
    public int Of;
    public double Fraction;
    public double? LastFrameMs;
    public double? LiveScore;
    /// <summary>Latest preview frame as BGRA pixels (PreviewWidth × PreviewHeight), or null.</summary>
    public byte[]? Preview;
}

public sealed class GpuBenchmarkConfiguration
{
    public int OutputWidth { get; set; } = 2560;
    public int OutputHeight { get; set; } = 1440;
    public int Supersample { get; set; } = 2;
    /// <summary>
    /// The internal image is drawn in horizontal bands, each submitted on its own, so a slow GPU
    /// never runs one huge command long enough to trip the Windows GPU watchdog (TDR, 2 seconds).
    /// </summary>
    public int Bands { get; set; } = 16;
    public int MinimumWarmUp { get; set; } = 2;
    public int MaximumWarmUp { get; set; } = 10;
    /// <summary>Scored frames come in whole camera orbits (24 frames), so every computer averages the same images.</summary>
    public int OrbitFrames { get; set; } = 24;
    public double MinimumScoredSeconds { get; set; } = 6.0;
    public int MaximumScoredOrbits { get; set; } = 20;
    public double TotalSeconds { get; set; } = 60.0;
    public int PreviewWidth { get; set; } = 640;
    public int PreviewHeight { get; set; } = 360;
}

/// <summary>
/// Renders the fractal scene off-screen at 2560×1440 with 2×2 supersampling (a 5120×2880 internal
/// image) through Direct3D 11 and times each frame.
/// </summary>
public sealed class D3D11RaymarchBenchmark : IDisposable
{
    /// <summary>Reference frame time: it scores 1,000. The same reference as the macOS app (Apple M1, 8-core GPU).</summary>
    public const double ReferenceFrameMs = 260.0;
    public const string ReferenceName = "Apple M1 (8-core GPU)";
    public const int ScoringVersion = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Params
    {
        public Vector2 Resolution;
        public float Time;
        public float Power;
        public int Supersample;
        public int Reserved;
        public Vector2 PreviewSize;
    }

    public GpuBenchmarkConfiguration Configuration { get; }
    public string AdapterName { get; }
    public FeatureLevel FeatureLevel { get; }

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _scenePixel, _resolvePixel, _previewPixel;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11RasterizerState _scissorState;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11Texture2D _scene, _output, _preview, _staging;
    private readonly ID3D11RenderTargetView _sceneTarget, _outputTarget, _previewTarget;
    private readonly ID3D11ShaderResourceView _sceneView, _outputView;
    private readonly ID3D11Query _done;

    public int InternalWidth => Configuration.OutputWidth * Configuration.Supersample;
    public int InternalHeight => Configuration.OutputHeight * Configuration.Supersample;

    public D3D11RaymarchBenchmark(GpuBenchmarkConfiguration? configuration = null)
    {
        Configuration = configuration ?? new GpuBenchmarkConfiguration();
        if (!OperatingSystem.IsWindows()) throw new GpuBenchmarkException(GpuBenchmarkFailureKind.NoDevice);

        // The high-performance GPU (the discrete one on laptops that have two).
        IDXGIAdapter1? adapter = null;
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory6>();
            factory.EnumAdapterByGpuPreference(0, GpuPreference.HighPerformance, out adapter);
        }
        catch
        {
            adapter = null;
        }

        FeatureLevel[] levels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        var result = D3D11.D3D11CreateDevice(adapter, adapter is null ? DriverType.Hardware : DriverType.Unknown,
            DeviceCreationFlags.BgraSupport, levels, out ID3D11Device? device, out FeatureLevel level, out ID3D11DeviceContext? context);
        if (result.Failure || device is null || context is null)
        {
            adapter?.Dispose();
            throw new GpuBenchmarkException(GpuBenchmarkFailureKind.NoDevice);
        }
        _device = device;
        _context = context;
        FeatureLevel = level;
        using (var dxgiDevice = _device.QueryInterface<IDXGIDevice>())
        using (var actual = dxgiDevice.GetAdapter())
        {
            AdapterName = WindowsSystemProbe.CleanGpuName(actual.Description.Description);
        }
        adapter?.Dispose();

        string profileSuffix = level >= FeatureLevel.Level_11_0 ? "5_0" : "4_0";
        _vertexShader = _device.CreateVertexShader(Compile("kv_fullscreen", "vs_" + profileSuffix).Span);
        _scenePixel = _device.CreatePixelShader(Compile("kv_scene_fragment", "ps_" + profileSuffix).Span);
        _resolvePixel = _device.CreatePixelShader(Compile("kv_resolve_fragment", "ps_" + profileSuffix).Span);
        _previewPixel = _device.CreatePixelShader(Compile("kv_preview_fragment", "ps_" + profileSuffix).Span);

        _constants = _device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<Params>(), BindFlags.ConstantBuffer, ResourceUsage.Default));
        _scissorState = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { ScissorEnable = true });
        _sampler = _device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp));

        try
        {
            _scene = Texture(Format.R16G16B16A16_Float, InternalWidth, InternalHeight);
            _output = Texture(Format.R8G8B8A8_UNorm, Configuration.OutputWidth, Configuration.OutputHeight);
            _preview = Texture(Format.B8G8R8A8_UNorm, Configuration.PreviewWidth, Configuration.PreviewHeight);
            _staging = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)Configuration.PreviewWidth,
                (uint)Configuration.PreviewHeight, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            throw new GpuBenchmarkException(GpuBenchmarkFailureKind.OutOfMemory);
        }
        _sceneTarget = _device.CreateRenderTargetView(_scene);
        _outputTarget = _device.CreateRenderTargetView(_output);
        _previewTarget = _device.CreateRenderTargetView(_preview);
        _sceneView = _device.CreateShaderResourceView(_scene);
        _outputView = _device.CreateShaderResourceView(_output);
        _done = _device.CreateQuery(new QueryDescription(QueryType.Event));
    }

    private ID3D11Texture2D Texture(Format format, int width, int height) =>
        _device.CreateTexture2D(new Texture2DDescription(format, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default));

    private static ReadOnlyMemory<byte> Compile(string entry, string profile)
    {
        try
        {
            return Compiler.Compile(RaymarchShader.Source, entry, "kavosh.hlsl", profile, ShaderFlags.OptimizationLevel3);
        }
        catch (Exception e)
        {
            throw new GpuBenchmarkException(GpuBenchmarkFailureKind.ShaderCompilation, e.Message);
        }
    }

    // ── Frames ──

    private void Draw(ID3D11RenderTargetView target, int width, int height, ID3D11PixelShader shader, ID3D11ShaderResourceView? input,
        RawRect? scissor)
    {
        _context.OMSetRenderTargets(target);
        _context.RSSetViewport(new Viewport(0, 0, width, height));
        _context.RSSetState(_scissorState);
        _context.RSSetScissorRect(scissor ?? new RawRect(0, 0, width, height));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(shader);
        _context.PSSetConstantBuffer(0, _constants);
        _context.PSSetSampler(0, _sampler);
        _context.PSSetShaderResource(0, input!);
        _context.Draw(3, 0);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        _context.PSSetShaderResource(0, null!);
    }

    /// <summary>Renders frame <paramref name="index"/> and returns how long it took, in milliseconds.</summary>
    private double RenderFrame(int index)
    {
        var config = Configuration;
        var parameters = new Params
        {
            Resolution = new Vector2(InternalWidth, InternalHeight),
            Time = (float)(index % config.OrbitFrames) / config.OrbitFrames,
            Power = 8,
            Supersample = config.Supersample,
            PreviewSize = new Vector2(config.PreviewWidth, config.PreviewHeight),
        };
        _context.UpdateSubresource(in parameters, _constants);
        var clock = Stopwatch.StartNew();

        int bandHeight = (InternalHeight + config.Bands - 1) / config.Bands;
        for (int band = 0; band < config.Bands; band++)
        {
            int y = band * bandHeight;
            int height = Math.Min(bandHeight, InternalHeight - y);
            if (height <= 0) continue;
            Draw(_sceneTarget, InternalWidth, InternalHeight, _scenePixel, null, new RawRect(0, y, InternalWidth, y + height));
            _context.Flush();
        }
        Draw(_outputTarget, config.OutputWidth, config.OutputHeight, _resolvePixel, _sceneView, null);
        Draw(_previewTarget, config.PreviewWidth, config.PreviewHeight, _previewPixel, _outputView, null);
        _context.CopyResource(_staging, _preview);
        _context.End(_done);
        _context.Flush();

        // Wait for the GPU to finish everything above.
        while (!_context.IsDataAvailable(_done))
        {
            var removed = _device.DeviceRemovedReason;
            if (removed.Failure) throw new GpuBenchmarkException(GpuBenchmarkFailureKind.FrameFailed, $"0x{removed.Code:X8}");
            Thread.SpinWait(200);
            if (clock.Elapsed.TotalSeconds > 30) throw new GpuBenchmarkException(GpuBenchmarkFailureKind.FrameFailed, "timeout");
        }
        double ms = clock.Elapsed.TotalMilliseconds;
        var reason = _device.DeviceRemovedReason;
        if (reason.Failure) throw new GpuBenchmarkException(GpuBenchmarkFailureKind.FrameFailed, $"0x{reason.Code:X8}");
        return ms;
    }

    private unsafe byte[] PreviewPixels()
    {
        int width = Configuration.PreviewWidth, height = Configuration.PreviewHeight;
        var pixels = new byte[width * height * 4];
        var mapped = _context.Map(_staging, 0, MapMode.Read);
        try
        {
            byte* source = (byte*)mapped.DataPointer;
            fixed (byte* destination = pixels)
            {
                for (int row = 0; row < height; row++)
                    Buffer.MemoryCopy(source + row * mapped.RowPitch, destination + row * width * 4, width * 4, width * 4);
            }
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }
        return pixels;
    }

    // ── Run ──

    public GpuBenchmarkResult Run(Func<bool> isCancelled, Action<GpuBenchmarkProgress> progress)
    {
        var config = Configuration;
        var started = Stopwatch.StartNew();
        double total = Math.Max(config.TotalSeconds, 1);
        double Fraction() => Math.Min(0.99, started.Elapsed.TotalSeconds / total);
        static double Score(double ms) => ms > 0 ? 1000 * ReferenceFrameMs / ms : 0;

        progress(new GpuBenchmarkProgress { Stage = GpuBenchmarkStage.Preparing });

        // Warm up until frame times settle.
        var warm = new List<double>();
        while (warm.Count < config.MaximumWarmUp)
        {
            if (isCancelled()) throw new GpuBenchmarkException(GpuBenchmarkFailureKind.Cancelled);
            double ms = RenderFrame(warm.Count);
            warm.Add(ms);
            progress(new GpuBenchmarkProgress
            {
                Stage = GpuBenchmarkStage.WarmingUp, Frame = warm.Count, Fraction = Fraction(), LastFrameMs = ms, Preview = PreviewPixels(),
            });
            if (warm.Count >= config.MinimumWarmUp)
            {
                var recent = warm.TakeLast(3).ToList();
                double mean = recent.Average();
                if (mean > 0 && (recent.Max() - recent.Min()) / mean < 0.06) break;
            }
        }

        // Scored run: whole orbits until enough time has passed.
        var times = new List<double>();
        var scoredClock = Stopwatch.StartNew();
        int orbits = 0;
        do
        {
            for (int i = 0; i < config.OrbitFrames; i++)
            {
                if (isCancelled()) throw new GpuBenchmarkException(GpuBenchmarkFailureKind.Cancelled);
                double ms = RenderFrame(i);
                times.Add(ms);
                progress(new GpuBenchmarkProgress
                {
                    Stage = GpuBenchmarkStage.Scored, Frame = times.Count, Of = (orbits + 1) * config.OrbitFrames, Fraction = Fraction(),
                    LastFrameMs = ms, LiveScore = Score(times.Average()), Preview = PreviewPixels(),
                });
            }
            orbits++;
        } while (scoredClock.Elapsed.TotalSeconds < config.MinimumScoredSeconds && orbits < config.MaximumScoredOrbits);

        // Sustained: keep rendering for the rest of the chosen duration.
        var sustained = new List<double>();
        int frame = 0;
        if (config.TotalSeconds - started.Elapsed.TotalSeconds > 10)
        {
            while (started.Elapsed.TotalSeconds < config.TotalSeconds)
            {
                if (isCancelled()) throw new GpuBenchmarkException(GpuBenchmarkFailureKind.Cancelled);
                double ms = RenderFrame(frame++);
                sustained.Add(ms);
                progress(new GpuBenchmarkProgress
                {
                    Stage = GpuBenchmarkStage.Sustained, Frame = sustained.Count, Fraction = Fraction(), LastFrameMs = ms,
                    LiveScore = Score(sustained.TakeLast(config.OrbitFrames).Average()), Preview = PreviewPixels(),
                });
            }
        }
        // Sustained = the last third of the soak.
        double? sustainedMean = sustained.Count >= 3 ? sustained.TakeLast(Math.Max(3, sustained.Count / 3)).Average() : null;

        progress(new GpuBenchmarkProgress
        {
            Stage = GpuBenchmarkStage.Finished, Fraction = 1, LastFrameMs = times.Count > 0 ? times[^1] : null, Preview = PreviewPixels(),
        });

        return GpuBenchmarkResult.Make(times, sustainedMean, warm.Count + times.Count + sustained.Count, started.Elapsed.TotalSeconds,
            AdapterName, config.OutputWidth, config.OutputHeight, InternalWidth, InternalHeight,
            config.Supersample * config.Supersample, ReferenceFrameMs, ScoringVersion);
    }

    public void Dispose()
    {
        _context.ClearState();
        _done.Dispose();
        _sceneView.Dispose();
        _outputView.Dispose();
        _sceneTarget.Dispose();
        _outputTarget.Dispose();
        _previewTarget.Dispose();
        _staging.Dispose();
        _preview.Dispose();
        _output.Dispose();
        _scene.Dispose();
        _sampler.Dispose();
        _scissorState.Dispose();
        _constants.Dispose();
        _previewPixel.Dispose();
        _resolvePixel.Dispose();
        _scenePixel.Dispose();
        _vertexShader.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
