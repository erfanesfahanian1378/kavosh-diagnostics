using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Kavosh.App.Audio;

/// <summary>What the synthesizer is playing.</summary>
public enum SpeakerSound { Silence, ToneLeft, ToneRight, Music }

/// <summary>
/// The test sounds, synthesized sample by sample (identical to the macOS app): 660 Hz beeps for
/// each channel and a short C–G–Am–F music loop panned between the speakers.
/// </summary>
public sealed class SpeakerSynth : ISampleProvider
{
    public const double ToneSeconds = 3.0;
    public const double MusicSeconds = 17.5;

    private readonly double _sampleRate;
    private readonly int _channels;
    private volatile SpeakerSound _sound = SpeakerSound.Silence;
    private long _startSample;
    private long _sampleCounter;
    private uint _noise = 0x12345678;

    public bool Muted { get; set; }

    public WaveFormat WaveFormat { get; }

    public SpeakerSynth(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = Math.Max(1, channels);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, _channels);
    }

    public void Play(SpeakerSound sound)
    {
        Interlocked.Exchange(ref _startSample, Interlocked.Read(ref _sampleCounter));
        _sound = sound;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var sound = _sound;
        bool muted = Muted;
        int frames = count / _channels;
        long start = Interlocked.Read(ref _startSample);
        for (int i = 0; i < frames; i++)
        {
            long n = _sampleCounter - start;
            _sampleCounter++;
            double t = n / _sampleRate;
            double l = 0, r = 0;
            switch (sound)
            {
                case SpeakerSound.ToneLeft or SpeakerSound.ToneRight when t < ToneSeconds:
                    double value = Beep(t);
                    if (sound == SpeakerSound.ToneLeft) l = value;
                    else r = value;
                    break;
                case SpeakerSound.Music when t < MusicSeconds:
                    (l, r) = Music(t);
                    break;
            }
            if (muted) l = r = 0;
            int at = offset + i * _channels;
            buffer[at] = (float)l;
            if (_channels > 1) buffer[at + 1] = (float)r;
            for (int c = 2; c < _channels; c++) buffer[at + c] = 0;
        }
        return frames * _channels;
    }

    /// <summary>660 Hz beeps: 0.35 s on, 0.15 s off, with short fades so there are no clicks.</summary>
    public static double Beep(double t)
    {
        const double period = 0.5;
        double local = t % period;
        if (local >= 0.35) return 0;
        double fade = Math.Min(1, Math.Min(local / 0.01, (0.35 - local) / 0.01));
        return 0.35 * fade * Math.Sin(2 * Math.PI * 660 * local);
    }

    private static double Frequency(double midi) => 440 * Math.Pow(2, (midi - 69) / 12);

    // C – G – Am – F, 110 bpm, arpeggio + bass + hi-hat. Volume ramps from half to full.
    private static readonly double[][] Chords = { new double[] { 60, 64, 67 }, new double[] { 55, 59, 62 }, new double[] { 57, 60, 64 }, new double[] { 53, 57, 60 } };
    private static readonly int[] Arpeggio = { 0, 1, 2, 1, 0, 2, 1, 2 };

    private (double, double) Music(double t)
    {
        const double beat = 60.0 / 110.0;
        const double eighth = beat / 2;
        const double barLength = beat * 4;
        int bar = (int)(t / barLength) % Chords.Length;
        var chord = Chords[bar];

        // Arpeggio note, alternating between the left and right side.
        int eighthIndex = (int)(t / eighth);
        double noteTime = t - eighthIndex * eighth;
        int toneIndex = Arpeggio[eighthIndex % Arpeggio.Length];
        double octave = (eighthIndex / 4) % 2 == 0 ? 12 : 0;
        double f = Frequency(chord[toneIndex] + octave);
        double pluckEnvelope = Math.Min(1, noteTime / 0.004) * Math.Exp(-noteTime * 5.5);
        double pluck = 0.20 * pluckEnvelope * (Math.Sin(2 * Math.PI * f * noteTime) + 0.35 * Math.Sin(4 * Math.PI * f * noteTime));
        double pan = 0.5 + 0.42 * Math.Sin(eighthIndex * Math.PI / 2);

        // Bass on every beat.
        int beatIndex = (int)(t / beat);
        double beatTime = t - beatIndex * beat;
        double bassFrequency = Frequency(chord[0] - 24);
        double bassEnvelope = Math.Min(1, beatTime / 0.006) * Math.Exp(-beatTime * 3.2);
        double bass = 0.26 * bassEnvelope * (Math.Sin(2 * Math.PI * bassFrequency * beatTime) + 0.25 * Math.Sin(6 * Math.PI * bassFrequency * beatTime));

        // Hi-hat on the off-beats.
        double hat = 0;
        if (eighthIndex % 2 == 1)
        {
            unchecked { _noise = _noise * 1_664_525 + 1_013_904_223; }
            double white = (int)_noise / (double)int.MaxValue;
            hat = 0.05 * white * Math.Exp(-noteTime * 60);
        }

        double ramp = 0.5 + 0.5 * Math.Min(1, t / 4);
        double fadeOut = Math.Min(1, (MusicSeconds - t) / 0.5);
        double gain = ramp * Math.Max(0, fadeOut);
        return (gain * (pluck * (1 - pan) * 1.4 + bass + hat), gain * (pluck * pan * 1.4 + bass + hat));
    }

    public static double Duration(SpeakerSound sound) => sound switch
    {
        SpeakerSound.ToneLeft or SpeakerSound.ToneRight => ToneSeconds,
        SpeakerSound.Music => MusicSeconds,
        _ => 0,
    };
}

/// <summary>Plays the synthesizer through the default Windows sound output (WASAPI, shared mode).</summary>
public sealed class SpeakerPlayer : IDisposable
{
    private WasapiOut? _output;
    private SpeakerSynth? _synth;

    public bool Muted
    {
        get => _synth?.Muted ?? false;
        set
        {
            if (_synth is not null) _synth.Muted = value;
        }
    }

    /// <summary>Starts a sound. Throws when there's no working sound output.</summary>
    public void Play(SpeakerSound sound)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (_output is null)
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var mix = device.AudioClient.MixFormat;
            _synth = new SpeakerSynth(mix.SampleRate, mix.Channels);
            _output = new WasapiOut(AudioClientShareMode.Shared, true, 80);
            _output.Init(_synth.ToWaveProvider());
        }
        _synth!.Play(sound);
        if (_output.PlaybackState != PlaybackState.Playing) _output.Play();
    }

    public void Stop()
    {
        _synth?.Play(SpeakerSound.Silence);
        try { _output?.Stop(); } catch { /* device gone */ }
    }

    public void Dispose()
    {
        Stop();
        _output?.Dispose();
        _output = null;
        _synth = null;
    }
}
