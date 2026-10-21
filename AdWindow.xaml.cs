using System.Windows;
using System.Windows.Threading;
using TomatoFlow.Helpers;
using TomatoFlow.Services;
using LibVLCSharp.Shared;

namespace TomatoFlow;

public partial class AdWindow : Window
{
    private static readonly Lazy<Task<LibVLC?>> WarmVlc = new(() => Task.Run(() =>
    {
        try
        {
            Core.Initialize();
            return (LibVLC?)new LibVLC("--no-video-title-show", "--quiet");
        }
        catch (Exception ex)
        {
            Log.Error("Could not initialize the video engine.", ex);
            return null;
        }
    }));

    private readonly AdService _ads;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _ticker;
    private readonly DispatcherTimer _glowTimer;
    private readonly Random _random = new();
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _media;
    private double _elapsed;
    private bool _videoEnded;
    private bool _playingVideo;
    private bool _disposed;
    private double _glowPhase;
    private int _adNumber;

    public AdWindow(AdService ads, SettingsService settings)
    {
        _ads = ads;
        _settings = settings;
        InitializeComponent();

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _ticker.Tick += OnTick;
        _glowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _glowTimer.Tick += OnGlowTick;
        Closed += (_, _) =>
        {
            _ticker.Stop();
            _glowTimer.Stop();
            DisposePlayer();
        };

        SetupPlayer();
    }

    public bool IsManual { get; private set; }

    public static void Prewarm()
    {
        _ = WarmVlc.Value;
    }

    public static void Shutdown()
    {
        if (!WarmVlc.IsValueCreated || !WarmVlc.Value.IsCompletedSuccessfully)
        {
            return;
        }

        try
        {
            WarmVlc.Value.Result?.Dispose();
        }
        catch
        {
        }
    }

    public void PlayNext(bool manual)
    {
        IsManual = manual;
        _elapsed = 0;
        _videoEnded = false;
        _playingVideo = false;
        _glowPhase = 0;
        _adNumber++;
        AdCounter.Text = $"AD {_adNumber}";
        AdProgress.Value = 0;
        SkipButton.IsEnabled = false;
        SkipButton.Content = "Skip ad";
        CloseButton.Visibility = Visibility.Hidden;
        SkipHint.Text = $"Skip unlocks in {_settings.Settings.AdSkipSeconds}";

        _ads.MarkPlayed();

        if (!IsVisible)
        {
            PositionWindow();
            Show();
        }

        if (_player is null)
        {
            SetupPlayer();
        }

        var video = _ads.NextVideo();
        if (video is null || !TryPlayVideo(video))
        {
            PlayFallback();
        }

        _ticker.Start();
        _glowTimer.Start();
    }

    private void SetupPlayer()
    {
        try
        {
            if (!WarmVlc.IsValueCreated || !WarmVlc.Value.IsCompletedSuccessfully)
            {
                return;
            }

            _libVlc = WarmVlc.Value.Result;
            if (_libVlc is null)
            {
                return;
            }

            _player = new MediaPlayer(_libVlc);
            _player.EndReached += (_, _) => Dispatcher.BeginInvoke(new Action(HandleMediaEnded));
            _player.EncounteredError += (_, _) =>
            {
                Log.Error("LibVLC ad playback failed.", null);
                Dispatcher.BeginInvoke(new Action(PlayFallback));
            };
            Player.MediaPlayer = _player;
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the video player.", ex);
            _player = null;
        }
    }

    private bool TryPlayVideo(string path)
    {
        if (_libVlc is null || _player is null)
        {
            return false;
        }

        try
        {
            FallbackPanel.Visibility = Visibility.Collapsed;
            Player.Visibility = Visibility.Visible;
            _player.Volume = _settings.Settings.AdSound ? 85 : 0;
            _media?.Dispose();
            _media = new Media(_libVlc, new Uri(path, UriKind.Absolute));
            _playingVideo = _player.Play(_media);
            _videoEnded = !_playingVideo;
            return _playingVideo;
        }
        catch (Exception ex)
        {
            Log.Error("Could not play ad video.", ex);
            return false;
        }
    }

    private void PlayFallback()
    {
        _playingVideo = false;
        try
        {
            _player?.Stop();
        }
        catch
        {
        }

        Player.Visibility = Visibility.Collapsed;
        FallbackPanel.Visibility = Visibility.Visible;
        FallbackSlogan.Text = _ads.RandomSlogan();
        FallbackBar.Width = 90 + (_random.NextDouble() * 120);
        _videoEnded = true;
    }

    private void HandleMediaEnded()
    {
        _videoEnded = true;
        if (CanSkip)
        {
            CloseButton.Visibility = Visibility.Visible;
            SkipButton.IsEnabled = true;
            SkipButton.Content = "Skip ad →";
        }
    }

    private void OnGlowTick(object? sender, EventArgs e)
    {
        _glowPhase += 0.06;
        var pulse = (Math.Sin(_glowPhase) + 1) / 2;
        FallbackGlow.Opacity = 0.12 + (pulse * 0.16);
        FallbackGlow.RenderTransformOrigin = new Point(0.5, 0.5);
        FallbackGlow.RenderTransform = new System.Windows.Media.ScaleTransform(0.94 + (pulse * 0.12), 0.94 + (pulse * 0.12));
    }

    private bool CanSkip => _elapsed >= _settings.Settings.AdSkipSeconds;

    private void OnTick(object? sender, EventArgs e)
    {
        _elapsed += 0.12;
        if (_playingVideo && !_videoEnded && _player is { Length: > 0 })
        {
            AdProgress.Value = Math.Clamp(_player.Position * 100, 0, 100);
        }
        else
        {
            AdProgress.Value = Math.Clamp(_elapsed / Math.Max(1, _settings.Settings.AdSkipSeconds) * 100, 0, 100);
        }

        if (!CanSkip)
        {
            var remaining = Math.Max(0, Math.Ceiling(_settings.Settings.AdSkipSeconds - _elapsed));
            SkipHint.Text = _videoEnded ? $"Ad ends in {remaining:0}" : $"Skip unlocks in {remaining:0}";
            return;
        }

        SkipButton.IsEnabled = true;
        SkipButton.Content = "Skip ad →";
        CloseButton.Visibility = Visibility.Visible;
        SkipHint.Text = "You can skip this ad.";

        if (_videoEnded)
        {
            Close();
        }
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        if (!CanSkip && !IsManual)
        {
            return;
        }

        Close();
    }

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        if (IsManual)
        {
            Left = workArea.Left + ((workArea.Width - Width) / 2);
            Top = workArea.Top + ((workArea.Height - Height) / 2);
        }
        else
        {
            Left = workArea.Right - Width - 30;
            Top = workArea.Bottom - Height - 30;
        }
    }

    private void DisposePlayer()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Player.MediaPlayer = null;
            _player?.Stop();
            _player?.Dispose();
            _media?.Dispose();
        }
        catch
        {
        }

        _player = null;
        _media = null;
        _libVlc = null;
    }

    public void ForceClose()
    {
        try
        {
            Close();
        }
        catch
        {
        }
    }
}
