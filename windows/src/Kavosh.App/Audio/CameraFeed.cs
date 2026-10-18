using FlashCap;

namespace Kavosh.App.Audio;

/// <summary>
/// Live webcam picture through DirectShow (FlashCap). Frames arrive as encoded images (BMP or
/// JPEG bytes) on a background thread; at most one is handed out at a time so a slow UI never
/// queues frames up.
/// </summary>
public sealed class CameraFeed : IAsyncDisposable
{
    private CaptureDevice? _device;
    private int _busy;

    public string? DeviceName { get; private set; }

    /// <summary>Raised on a background thread with an encoded image. Call <see cref="FrameDone"/> when it has been shown.</summary>
    public event Action<byte[]>? Frame;

    /// <summary>The first camera, or null when there is none.</summary>
    public static CaptureDeviceDescriptor? FindCamera()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return new CaptureDevices().EnumerateDescriptors()
                .Where(d => d.Characteristics.Any(c => c.PixelFormat != PixelFormats.Unknown))
                .OrderBy(d => d.DeviceType == DeviceTypes.DirectShow ? 0 : 1)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Picks a format near 1280×720 at 15 fps or more.</summary>
    private static VideoCharacteristics Choose(CaptureDeviceDescriptor descriptor)
    {
        var usable = descriptor.Characteristics.Where(c => c.PixelFormat != PixelFormats.Unknown).ToList();
        double Fps(VideoCharacteristics c) => c.FramesPerSecond.Denominator == 0 ? 0 : (double)c.FramesPerSecond.Numerator / c.FramesPerSecond.Denominator;
        return usable
            .OrderBy(c => Fps(c) >= 15 ? 0 : 1)
            .ThenBy(c => Math.Abs(c.Width * c.Height - 1280 * 720))
            .ThenBy(c => c.PixelFormat == PixelFormats.JPEG ? 0 : 1)
            .First();
    }

    public async Task StartAsync(CaptureDeviceDescriptor descriptor)
    {
        DeviceName = descriptor.Name;
        var characteristics = Choose(descriptor);
        _device = await descriptor.OpenAsync(characteristics, TranscodeFormats.Auto, true, 1, scope =>
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            byte[] image;
            try
            {
                image = scope.Buffer.CopyImage();
            }
            catch
            {
                Interlocked.Exchange(ref _busy, 0);
                return;
            }
            Frame?.Invoke(image);
        });
        await _device.StartAsync();
    }

    public void FrameDone() => Interlocked.Exchange(ref _busy, 0);

    public async ValueTask DisposeAsync()
    {
        var device = _device;
        _device = null;
        if (device is null) return;
        try
        {
            await device.StopAsync();
        }
        catch
        {
            // Camera unplugged.
        }
        await device.DisposeAsync();
    }
}
