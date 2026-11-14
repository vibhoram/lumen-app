// D-Island (personal fork) - music extras: progress bar looks, time on the collapsed pill, real album art, synced lyrics.

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private readonly DispatcherTimer _musicTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private string _trackKey = "";
    private CancellationTokenSource? _lookupCts;
    private List<LyricLine>? _lyricLines;
    private Artwork? _artOverride;

    private static readonly string[] BrowserHints = { "chrome", "edge", "firefox", "vivaldi", "opera", "brave", "comet", "arc", "browser", "chromium" };

    private void WireMusic()
    {
        _musicTimer.Tick += (_, _) => MusicTick();
    }

    /// <summary>Applies the music-related settings (progress look, time, lyrics visibility).</summary>
    private void ApplyMusicSettings()
    {
        ProgressBar.Style = _settings.ProgressStyle;
        MediaLineSmall.Visibility = _settings.CompactProgressLine ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.CompactMediaTime == CompactTime.Off) MediaTimeSmall.Visibility = Visibility.Collapsed;
        if (!_settings.LyricsEnabled) { _lyricLines = null; MediaLyricLarge.Visibility = Visibility.Collapsed; }
        MusicTick();
    }

    private void MusicTrackChanged(MediaInfo? media)
    {
        ProgressBar.Playing = media?.IsPlaying == true;
        if (media is not null)
        {
            ProgressBar.Accent = BrightenAccent(media.Accent);
            var bars = new SolidColorBrush(BrightenAccent(media.Accent));
            bars.Freeze();
            foreach (System.Windows.Shapes.Rectangle bar in Equalizer.Children) bar.Fill = bars;
        }
        if (media?.IsPlaying == true) _musicTimer.Start();
        else _musicTimer.Stop();

        var key = media is null ? "" : $"{media.Title}|{media.Artist}|{(int)media.Duration.TotalSeconds}";
        if (key == _trackKey) { MusicTick(); return; }
        _trackKey = key;
        _lookupCts?.Cancel();
        _lyricLines = null;
        _artOverride = null;
        MediaLyricLarge.Visibility = Visibility.Collapsed;
        ProgressBar.Seed = key;
        if (media is null) return;

        var cts = _lookupCts = new CancellationTokenSource();
        _ = LoadTrackExtrasAsync(media, key, cts.Token);
        MusicTick();
    }

    private static Color BrightenAccent(Color c)
    {
        // Accents taken from dark artwork vanish on the island; lift them toward white a bit.
        var l = 0.55;
        return Color.FromRgb((byte)(c.R + (255 - c.R) * l), (byte)(c.G + (255 - c.G) * l), (byte)(c.B + (255 - c.B) * l));
    }

    private async Task LoadTrackExtrasAsync(MediaInfo media, string key, CancellationToken cancel)
    {
        try
        {
            var browser = BrowserHints.Any(h => media.AppName.Contains(h, StringComparison.OrdinalIgnoreCase) || media.AppId.Contains(h, StringComparison.OrdinalIgnoreCase));
            if (_settings.AutoAlbumArt && (browser || media.Art is null))
            {
                var bytes = await MusicLookup.ArtAsync(media.Title, media.Artist, cancel);
                if (bytes is not null && !cancel.IsCancellationRequested && await Artwork.DecodeAsync(bytes, 600) is { } art && key == _trackKey)
                {
                    _artOverride = art;
                    OnMediaChanged();
                }
            }
            if (_settings.LyricsEnabled)
            {
                var lines = await MusicLookup.LyricsAsync(media.Title, media.Artist, media.Duration, cancel);
                if (!cancel.IsCancellationRequested && key == _trackKey)
                {
                    _lyricLines = lines;
                    MusicTick();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void MusicTick()
    {
        var media = _media.Current;
        if (media is null) return;
        var position = media.EstimatePosition(DateTimeOffset.Now);
        var fraction = media.Duration.Ticks > 0 ? Math.Clamp(position / media.Duration, 0, 1) : 0;

        if (_settings.CompactMediaTime != CompactTime.Off && media.Duration.Ticks > 0)
        {
            MediaTimeSmall.Text = _settings.CompactMediaTime == CompactTime.Elapsed
                ? FormatMediaTime(position)
                : "-" + FormatMediaTime(media.Duration - position);
            MediaTimeSmall.Visibility = Visibility.Visible;
        }
        else MediaTimeSmall.Visibility = Visibility.Collapsed;
        MediaLineScale.ScaleX = fraction;
        if (_state.IsExpanded) ProgressBar.Fraction = fraction;

        var line = _lyricLines is { } lines ? MusicLookup.LineAt(lines, position).Current : "";
        MediaLyricLarge.Text = line;
        MediaLyricLarge.Visibility = _settings.LyricsInExpanded && line.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var title = string.IsNullOrWhiteSpace(media.Title) ? "Unknown title" : media.Title;
        var shown = _settings.LyricsInCompact && line.Length > 0 ? "♪ " + line : title;
        var artist = _settings.ShowArtistOnPill && !(_settings.LyricsInCompact && line.Length > 0) ? media.Artist : "";
        MediaArtistSmall.Text = artist;
        MediaArtistSmall.Visibility = artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (shown != MediaTitleSmall.Text)
        {
            MediaTitleSmall.Text = shown;
            _marqueeKey = null; // new text: re-measure the scroll
            UpdateMarquee();
        }
    }

    private string? _marqueeKey;

    /// <summary>
    /// A title that doesn't fit scrolls to its end and back. Only while the music pill is on screen: a running
    /// animation keeps the whole window rendering, so it stops the moment the pill isn't shown.
    /// </summary>
    private void UpdateMarquee()
    {
        var onScreen = _currentView == MediaView && !IslandHidden && !_state.IsExpanded;
        if (!onScreen)
        {
            if (_marqueeKey is not null)
            {
                MediaTitleShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                MediaTitleShift.X = 0;
                _marqueeKey = null;
            }
            return;
        }
        var key = MediaTitleSmall.Text;
        if (key == _marqueeKey) return;
        _marqueeKey = key;
        MediaTitleShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        MediaTitleShift.X = 0;

        MediaTitleSmall.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var available = MediaTitleClip.ActualWidth > 0 ? MediaTitleClip.ActualWidth : 190;
        var overflow = MediaTitleSmall.DesiredSize.Width - available;
        if (overflow <= 2) return;

        var distance = overflow + 14;
        var travel = TimeSpan.FromSeconds(distance / 30); // 30 px per second, easy to read
        var hold = TimeSpan.FromSeconds(1.8);
        var back = TimeSpan.FromSeconds(0.7);
        var total = hold + travel + TimeSpan.FromSeconds(1.2) + back;
        var frames = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { Duration = total, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        frames.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, hold));
        frames.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(-distance, hold + travel));
        frames.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(-distance, hold + travel + TimeSpan.FromSeconds(1.2)));
        frames.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(0, total, new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }));
        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(frames, 30);
        MediaTitleShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, frames);
    }
}
