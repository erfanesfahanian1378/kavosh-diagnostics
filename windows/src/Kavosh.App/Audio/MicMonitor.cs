using Kavosh.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Kavosh.App.Audio;

/// <summary>
/// Listens to the default Windows microphone and reports its level (dBFS) and estimated pitch
/// about every 85 ms. Events are raised on a background thread.
/// </summary>
public sealed class MicMonitor : IDisposable
{
    private WasapiCapture? _capture;
    private readonly List<float> _window = new();
    private int _sampleRate = 48_000;

    public string? DeviceName { get; private set; }

    /// <summary>(level dB, pitch Hz or null).</summary>
    public event Action<double, double?>? Level;
    public event Action<string>? Failed;

    /// <summary>Returns false when there is no microphone.</summary>
    public bool Start()
    {
        if (!OperatingSystem.IsWindows()) return false;
        MMDevice device;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            return false;
        }
        DeviceName = device.FriendlyName;
        _capture = new WasapiCapture(device, true, 50);
        _sampleRate = _capture.WaveFormat.SampleRate;
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) Failed?.Invoke(e.Exception.Message);
        };
        _capture.StartRecording();
        return true;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format is null) return;
        int channels = Math.Max(1, format.Channels);
        int bytesPerSample = format.BitsPerSample / 8;
        int frameBytes = bytesPerSample * channels;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (format is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        for (int offset = 0; offset + frameBytes <= e.BytesRecorded; offset += frameBytes)
        {
            float sample = isFloat && bytesPerSample == 4
                ? BitConverter.ToSingle(e.Buffer, offset)
                : bytesPerSample == 2 ? BitConverter.ToInt16(e.Buffer, offset) / 32768f
                : bytesPerSample == 4 ? BitConverter.ToInt32(e.Buffer, offset) / 2147483648f
                : 0;
            _window.Add(sample);
        }
        // Analyse in blocks of ~4096 samples (about 85 ms at 48 kHz), like the macOS app.
        while (_window.Count >= 4096)
        {
            var block = _window.GetRange(0, 4096).ToArray();
            _window.RemoveRange(0, 4096);
            double db = AudioAnalysis.RmsDecibels(block);
            double? pitch = AudioAnalysis.EstimatePitch(block, _sampleRate);
            Level?.Invoke(db, pitch);
        }
    }

    public void Dispose()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null) return;
        try
        {
            capture.DataAvailable -= OnData;
            capture.StopRecording();
        }
        catch
        {
            // Device unplugged.
        }
        capture.Dispose();
    }
}
