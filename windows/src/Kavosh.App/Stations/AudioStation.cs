using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kavosh.App.Audio;
using Kavosh.App.Ui;
using Kavosh.Core;
using static Kavosh.Core.Loc;

namespace Kavosh.App.Stations;

public sealed class AudioStation : Station
{
    private readonly SpeakerPlayer _player = new();
    private SpeakerSound _playing = SpeakerSound.Silence;
    private bool _leftHeard, _rightHeard, _muted;
    private string? _error;
    private DispatcherTimer? _stopTimer;

    public AudioStation(TestSuiteRunner runner) : base(runner)
    {
        Render();
    }

    protected override void Stop()
    {
        _stopTimer?.Stop();
        _player.Dispose();
    }

    private void Play(SpeakerSound sound)
    {
        try
        {
            _player.Play(sound);
            _player.Muted = _muted;
            _playing = sound;
            _error = null;
            _stopTimer?.Stop();
            _stopTimer = new DispatcherTimer(TimeSpan.FromSeconds(SpeakerSynth.Duration(sound) + 0.2), DispatcherPriority.Normal, (_, _) =>
            {
                _stopTimer?.Stop();
                if (_playing == sound)
                {
                    _playing = SpeakerSound.Silence;
                    Render();
                }
            });
            _stopTimer.Start();
        }
        catch
        {
            _error = T("The sound couldn't be played on this PC's sound output. Check the output device in Settings — System — Sound and try again, or report an issue to move on.",
                       "صدا روی خروجی صدای این رایانه پخش نشد. دستگاه خروجی را در تنظیمات — سیستم — صدا بررسی کنید و دوباره امتحان کنید، یا برای ادامه ایراد را گزارش دهید.");
        }
        Render();
    }

    private void StopPlayback()
    {
        _stopTimer?.Stop();
        _player.Stop();
        _playing = SpeakerSound.Silence;
    }

    private void Render()
    {
        var how = Kv.Card(Kv.Column(8, Kv.SectionLabel(T("How it works", "روش کار"), Icon.Speaker),
            Kv.Instruction(T("Play each tone and confirm you heard it from the correct side. Turn the volume up first.",
                             "هر صدا را پخش کنید و تأیید کنید که از سمت درست شنیدید. اول صدا را زیاد کنید."))));

        var channels = Kv.Columns(18,
            ChannelCard(T("Left channel", "کانال چپ"), _playing == SpeakerSound.ToneLeft, _leftHeard, T("I heard it on the left speaker", "از بلندگوی چپ شنیدم"),
                on => _leftHeard = on, () => Play(SpeakerSound.ToneLeft)),
            ChannelCard(T("Right channel", "کانال راست"), _playing == SpeakerSound.ToneRight, _rightHeard, T("I heard it on the right speaker", "از بلندگوی راست شنیدم"),
                on => _rightHeard = on, () => Play(SpeakerSound.ToneRight)));
        // Left is always on the left, whatever the reading direction.
        channels.FlowDirection = FlowDirection.LeftToRight;

        bool music = _playing == SpeakerSound.Music;
        var mute = new RetroButton(null, _muted ? Icon.Minus : Icon.Speaker, ButtonKind.Ghost, ButtonSize.Regular, () =>
        {
            _muted = !_muted;
            _player.Muted = _muted;
            Render();
        });
        ToolTip.SetTip(mute, T("Mute / unmute", "بی‌صدا / باصدا"));
        var track = Kv.Card(Kv.Column(10,
            Kv.SectionLabel(T("Music track", "قطعه موسیقی"), Icon.Speaker),
            Kv.Text(T("Starts at half volume and ramps up to full over a few seconds — a more realistic listening check than the plain tone above. Listen for crackling, buzzing or distortion.",
                      "با نصف صدا شروع می‌شود و طی چند ثانیه به صدای کامل می‌رسد — بررسی واقعی‌تری از صدای ساده بالا. به خش‌خش، وزوز یا اعوجاج دقت کنید."), 13),
            Kv.Row(10,
                new RetroButton(music ? T("Stop", "توقف") : T("Play track", "پخش قطعه"), music ? Icon.Stop : Icon.Play, ButtonKind.Secondary, ButtonSize.Regular, () =>
                {
                    if (music)
                    {
                        StopPlayback();
                        Render();
                    }
                    else
                    {
                        Play(SpeakerSound.Music);
                    }
                }),
                mute)));

        var stack = Kv.Column(18, how, channels, track);
        if (_error is not null) stack.Children.Add(Kv.Label(_error, Icon.Warning, Palette.Red));
        stack.Children.Add(Kv.Spread(
            ReportIssue.Button(new[]
            {
                T("No sound at all", "اصلاً صدایی نیست"),
                T("Crackling or distorted sound", "صدای خش‌دار یا معوج"),
                T("Sound came from the wrong side", "صدا از سمت اشتباه آمد"),
                T("One side is much quieter", "یک سمت خیلی آرام‌تر است"),
            }, issue => Finish(issue)),
            new RetroButton(Runner.IsLastStation ? T("Finish", "پایان") : T("Next test", "تست بعدی"),
                Loc.Language.IsRightToLeft() ? Icon.ArrowLeft : Icon.ArrowRight, ButtonKind.Primary, ButtonSize.Large, () => Finish(null))));
        Content = stack;
    }

    private static Control ChannelCard(string title, bool playing, bool heard, string confirm, Action<bool> setHeard, Action play)
    {
        return Kv.Card(Kv.Column(12,
            Kv.SectionLabel(title, Icon.Speaker),
            new RetroButton(playing ? T("Playing…", "در حال پخش…") : T("Play tone", "پخش صدا"), playing ? Icon.Pulse : Icon.Play, ButtonKind.Primary, ButtonSize.Regular, play),
            new RetroCheckBox(confirm, heard, setHeard)),
            playing ? Palette.WithOpacity(Palette.YellowColor, 0.35) : Palette.Surface);
    }

    private void Finish(string? issue)
    {
        StopPlayback();
        var result = new AudioTestResult { LeftHeard = _leftHeard, RightHeard = _rightHeard, ReportedIssue = issue };
        Runner.Complete(TestModule.Speakers, r => r.Audio = result);
    }
}

/// <summary>"Report Issue" button with a list of common problems.</summary>
public static class ReportIssue
{
    public static Control Button(IEnumerable<string> options, Action<string> onPick)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedLeft };
        foreach (string option in options.Append(T("Other problem", "مشکل دیگر")))
        {
            var item = new MenuItem { Header = option, FontFamily = Palette.Text };
            item.Click += (_, _) => onPick(option);
            menu.Items.Add(item);
        }
        var button = new RetroButton(T("Report Issue", "گزارش ایراد"), Icon.Warning, ButtonKind.Secondary, ButtonSize.Regular);
        button.Flyout = menu;
        return button;
    }
}
