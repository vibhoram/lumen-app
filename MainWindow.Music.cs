// D-Island (personal fork) - music extras: progress bar looks, time on the collapsed pill, real album art, synced lyrics.

using System.Windows;
using System.Windows.Controls;
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
    private List<LyricLine>? _rawLyrics;
    private bool _romanizedApplied = true;
    private LyricsPanel _lyricsPanel = null!;
    private LyricsPanel _lyricsInline = null!;
    private bool _lyricsOpen;
    private FontFamily? _defaultTitleFont;
    private Artwork? _artOverride;

    private static readonly string[] BrowserHints = { "chrome", "edge", "firefox", "vivaldi", "opera", "brave", "comet", "arc", "browser", "chromium" };

    private void WireMusic()
    {
        _musicTimer.Tick += (_, _) => MusicTick();
        MediaTitleClip.SizeChanged += (_, _) => { _marqueeKey = null; UpdateMarquee(); };
    }

    /// <summary>Applies the music-related settings (progress look, time, lyrics visibility).</summary>
    private void ApplyMusicSettings()
    {
        ApplyEqualizerLook(_media.Current?.Accent ?? Colors.White);
        ApplyMusicLayout();
        ProgressBar.Style = _settings.ProgressStyle;
        if (_rawLyrics is not null && _romanizedApplied != _settings.RomanizeHindi) { _romanizedApplied = _settings.RomanizeHindi; ReshapeLyrics(); }
        _lyricsPanel?.SetFont(_settings.LyricsFont, _settings.LyricsFontSize);
        _lyricsInline?.SetFont(_settings.LyricsFont, _settings.LyricsFontSize);
        MediaLyricLarge.FontFamily = new FontFamily($"{_settings.LyricsFont}, Segoe Print, Segoe UI");
        ApplyLyricsLook();
        MediaLineSmall.Visibility = _settings.CompactProgressLine ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.CompactMediaTime == CompactTime.Off) MediaTimeSmall.Visibility = Visibility.Collapsed;
        if (!_settings.LyricsEnabled) { _lyricLines = null; MediaLyricLarge.Visibility = Visibility.Collapsed; }
        MusicTick();
    }

    /// <summary>Current-lyric colour: classic green by default, or cover / accent / custom / gradients.</summary>
    private void ApplyLyricsLook()
    {
        var brush = ResolveLyricsBrush();
        MediaLyricLarge.Foreground = brush;
        _lyricsPanel?.SetHighlight(brush);
        _lyricsInline?.SetHighlight(brush);
        // Collapsed pill title becomes the lyric line when that option is on — match the colour then.
        if (_settings.LyricsInCompact && MediaTitleSmall.Text.StartsWith("♪ ", StringComparison.Ordinal))
            MediaTitleSmall.Foreground = brush;
        else
            MediaTitleSmall.ClearValue(TextBlock.ForegroundProperty);
    }

    private Brush ResolveLyricsBrush()
    {
        static SolidColorBrush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        static Brush Gradient(Color a, Color b)
        {
            var brush = new LinearGradientBrush(ColorTools.Readable(a), ColorTools.Readable(b), 0);
            brush.Freeze();
            return brush;
        }

        var green = Color.FromRgb(0x30, 0xD1, 0x58);
        switch (_settings.LyricsColorMode)
        {
            case LyricsColorMode.Cover:
                return Solid(ColorTools.Readable(_palette.Count > 0 ? _palette[0] : green));
            case LyricsColorMode.Accent:
            {
                var accent = ResolveAccent() ?? _media.Current?.Accent ?? green;
                return Solid(ColorTools.Readable(accent));
            }
            case LyricsColorMode.White:
                return Solid(Colors.White);
            case LyricsColorMode.Custom:
                return Solid(ColorTools.Readable(ParseColor(_settings.LyricsCustomColor) ?? green));
            case LyricsColorMode.CoverGradient:
            {
                var a = _palette.Count > 0 ? _palette[0] : green;
                var b = _palette.Count > 1 ? _palette[1] : ColorTools.Mix(a, Colors.White, 0.35);
                return Gradient(a, b);
            }
            case LyricsColorMode.CustomGradient:
            {
                var a = ParseColor(_settings.LyricsCustomColor) ?? green;
                var b = ParseColor(_settings.LyricsCustomColorB) ?? Color.FromRgb(0x64, 0xD2, 0xFF);
                return Gradient(a, b);
            }
            default:
                return Solid(green);
        }
    }

    /// <summary>Applies the display choices (Hindi in English letters) to the raw synced lyrics.</summary>
    private List<LyricLine>? ShapeLyrics(List<LyricLine>? raw) =>
        raw is null || !_settings.RomanizeHindi ? raw
            : raw.Select(l => Transliterate.HasDevanagari(l.Text) ? new LyricLine(l.Time, Transliterate.ToLatin(l.Text)) : l).ToList();

    /// <summary>The "write Hindi in English letters" setting changed while lyrics are showing.</summary>
    private void ReshapeLyrics()
    {
        if (_rawLyrics is null) return;
        _lyricLines = ShapeLyrics(_rawLyrics);
        _lyricsPanel?.SetState(LyricsState.Ready, _lyricLines);
        _lyricsInline?.SetState(LyricsState.Ready, _lyricLines);
        MusicTick();
    }

    private void MusicTrackChanged(MediaInfo? media)
    {
        ProgressBar.Playing = media?.IsPlaying == true;
        if (media is not null)
        {
            ProgressBar.Accent = BrightenAccent(media.Accent);
            ApplyEqualizerLook(media.Accent);
        }
        if (media?.IsPlaying == true) _musicTimer.Start();
        else _musicTimer.Stop();

        // AppId is part of the key so Spotify ↔ Vivaldi on different tracks (or the same song) always resets.
        var key = media is null ? "" : $"{media.AppId}|{media.Title}|{media.Artist}|{(int)media.Duration.TotalSeconds}";
        if (key == _trackKey) { MusicTick(); return; }
        _trackKey = key;
        _lookupCts?.Cancel();
        _lyricLines = null;
        _rawLyrics = null;
        _artOverride = null; // also cleared earlier in OnMediaChanged before brushes bind
        MediaLyricLarge.Visibility = Visibility.Collapsed;
        ProgressBar.Seed = key;
        _lyricsInline.SetState(LyricsState.None, null);
        UpdateLyricsUi();
        _lyricsPanel.SetTrack(media is null ? "" : MusicLookup.Clean(media.Title, media.Artist).Title, media?.Artist ?? "");
        _lyricsPanel.SetState(media is null ? LyricsState.None : _settings.LyricsEnabled ? LyricsState.Loading : LyricsState.NotFound, null);
        if (media is null) return;

        var cts = _lookupCts = new CancellationTokenSource();
        _ = LoadTrackExtrasAsync(media, key, cts.Token);
        MusicTick();
    }

    private List<Color> _palette = new() { Color.FromRgb(0xCF, 0xD6, 0xFF), Color.FromRgb(0xFF, 0xC8, 0xE8), Color.FromRgb(0xBF, 0xF2, 0xFF), Color.FromRgb(0xFF, 0xE2, 0xB0) };

    /// <summary>The cover's colours drive the bars, the glow behind them and the disc's ring; they ease from song to song.</summary>
    private void UpdatePalette()
    {
        var art = _shownArt?.Still;
        if (art is not null) _palette = ColorTools.Palette(art, 4);
        ApplyEqualizerLook(_media.Current?.Accent ?? Colors.White);
        MediaWave.Colors = _palette;
        ProgressBar.Palette = _palette.Select(BrightenAccent).ToList(); // the progress bar wears the cover's colours too
        ApplyLyricsLook(); // cover / gradient lyrics track the new art
    }

    private static void EaseColor(GradientStop stop, Color to)
    {
        if (stop.IsFrozen) return;
        stop.BeginAnimation(GradientStop.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(to, TimeSpan.FromMilliseconds(650)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }

    /// <summary>Looks for the audio bars: Aura takes the cover's colours (like Samsung's Now Bar); the others are fixed palettes.</summary>
    private void ApplyEqualizerLook(Color accent)
    {
        MediaWave.Style = _settings.WaveStyle;
        var i = 0;
        foreach (System.Windows.Shapes.Rectangle bar in Equalizer.Children)
        {
            if (_settings.EqualizerLook == EqualizerLook.Aura)
            {
                var top = _palette[i % _palette.Count];
                var bottom = ColorTools.Mix(_palette[(i + 1) % _palette.Count], Colors.White, 0.12);
                if (bar.Fill is LinearGradientBrush existing && !existing.IsFrozen && existing.GradientStops.Count == 2)
                {
                    EaseColor(existing.GradientStops[0], top);
                    EaseColor(existing.GradientStops[1], bottom);
                }
                else bar.Fill = new LinearGradientBrush(top, bottom, 90);
            }
            else
            {
                Brush fill = _settings.EqualizerLook switch
                {
                    EqualizerLook.Song => new LinearGradientBrush(Colors.White, BrightenAccent(accent), 90),
                    EqualizerLook.Mint => new LinearGradientBrush(Color.FromRgb(0xE8, 0xFF, 0xF4), Color.FromRgb(0x3F, 0xE0, 0xA0), 90),
                    EqualizerLook.Sunset => new SolidColorBrush(new[] { Color.FromRgb(0xFF, 0x7A, 0x9C), Color.FromRgb(0xFF, 0xA8, 0x5C), Color.FromRgb(0xFF, 0xD6, 0x6B), Color.FromRgb(0xA6, 0x8B, 0xFF) }[i % 4]),
                    _ => new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xA9, 0xAF, 0xC2), 90),
                };
                bar.Fill = fill;
            }
            i++;
        }
        var glow = _settings.EqualizerLook == EqualizerLook.Aura ? _palette[0] : Colors.White;
        EaseColor(AuraStopA, glow);
        EaseColor(AuraStopB, Color.FromArgb(0, glow.R, glow.G, glow.B));
        AuraGlow.Visibility = _settings.EqualizerLook == EqualizerLook.Aura ? Visibility.Visible : Visibility.Collapsed;
        // the disc's progress ring and the pill's thin line take the lead colour too
        _discRing = new SolidColorBrush(_settings.EqualizerLook == EqualizerLook.Aura ? _palette[0] : Colors.White);
        _discRing.Freeze();
        MediaDiscRing.Stroke = _discRing;
    }

    private Brush _discRing = Brushes.White;

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
                    _rawLyrics = lines;
                    _romanizedApplied = _settings.RomanizeHindi;
                    _lyricLines = ShapeLyrics(lines);
                    _lyricsPanel.SetState(lines is null ? LyricsState.NotFound : LyricsState.Ready, _lyricLines);
                    _lyricsInline.SetState(lines is null ? LyricsState.NotFound : LyricsState.Ready, _lyricLines);
                    UpdateLyricsUi();
                    MusicTick();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.LogError(ex); }
    }

    /// <summary>When more than one app has media, the expanded view lists them as chips (playing ones lit) and you pick which to show.</summary>
    private void UpdateMediaSources()
    {
        var sources = _media.Sources;
        var many = sources.Count > 1;
        MediaSources.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        MediaSourceBadge.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        MediaSourceBadgeText.Text = sources.Count.ToString();
        if (!many) return;
        MediaSources.Children.Clear();
        var current = _media.Current?.AppId;
        foreach (var source in sources)
        {
            var selected = source.AppId == current;
            var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Fill = source.IsPlaying ? (Brush)FindResource("Green") : new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)) };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(8, 3, 10, 3),
                Margin = new Thickness(0, 0, 5, 3),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = selected ? new LinearGradientBrush(Color.FromArgb(0x99, 0x8B, 0x6C, 0xFF), Color.FromArgb(0x99, 0x2D, 0xD4, 0xFF), 35) : new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, new TextBlock { Text = source.Name, FontSize = 11, FontWeight = FontWeights.SemiBold } } },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };
            var id = source.AppId;
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                var scale = (ScaleTransform)chip.RenderTransform;
                var pop = new System.Windows.Media.Animation.DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new System.Windows.Media.Animation.ElasticEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                        Oscillations = 1,
                        Springiness = 5,
                    },
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                _media.ChooseSource(id);
            };
            if (selected)
            {
                var scale = (ScaleTransform)chip.RenderTransform;
                var settle = new System.Windows.Media.Animation.DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(480))
                {
                    EasingFunction = new System.Windows.Media.Animation.ElasticEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                        Oscillations = 1,
                        Springiness = 5.5,
                    },
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
            }
            MediaSources.Children.Add(chip);
        }
    }

    private void MusicTick()
    {
        var media = _media.Current;
        if (media is null) return;
        var position = media.EstimatePosition(DateTimeOffset.Now);
        var fraction = media.Duration.Ticks > 0 ? Math.Clamp(position / media.Duration, 0, 1) : 0;

        MediaTimeSmall.Inlines.Clear();
        var any = false;
        if (_settings.ClockOnMusicPill)
        {
            MediaTimeSmall.Inlines.Add(new System.Windows.Documents.Run(DateTime.Now.ToString("h:mm tt")) { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            any = true;
        }
        if (_settings.CompactMediaTime != CompactTime.Off && media.Duration.Ticks > 0)
        {
            if (any) MediaTimeSmall.Inlines.Add(new System.Windows.Documents.LineBreak());
            MediaTimeSmall.Inlines.Add(new System.Windows.Documents.Run(_settings.CompactMediaTime == CompactTime.Elapsed ? FormatMediaTime(position) : "-" + FormatMediaTime(media.Duration - position)) { FontSize = 10, Foreground = (Brush)FindResource("Secondary") });
            any = true;
        }
        MediaTimeSmall.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        MediaWave.Playing = media.IsPlaying;
        MediaLineSmall.Visibility = _settings.CompactProgressLine && !_settings.MusicWave ? Visibility.Visible : Visibility.Collapsed;
        MediaLineScale.ScaleX = fraction;
        if (_state.IsExpanded) ProgressBar.Fraction = fraction;
        UpdateBubbleRings(); // the disc's ring and the activity rings follow the song

        var line = _lyricLines is { } lines ? MusicLookup.LineAt(lines, position).Current : "";
        MediaLyricLarge.Text = line;
        if (_state.IsExpanded && _page == ExpandedPage.Lyrics) _lyricsPanel.Update(position);
        if (_state.IsExpanded && _lyricsOpen && _lyricLines is not null && _page == ExpandedPage.Home) _lyricsInline.Update(position);
        // The line's room is reserved for the whole song (an empty line keeps its height), so the island never jumps
        // between lines; it resizes once, when lyrics appear or go.
        var lyricVisibility = _settings.LyricsInExpanded && _lyricLines is not null ? Visibility.Visible : Visibility.Collapsed;
        if (MediaLyricLarge.Visibility != lyricVisibility)
        {
            MediaLyricLarge.Visibility = lyricVisibility;
            if (_state.IsExpanded && _page == ExpandedPage.Home) Dispatcher.InvokeAsync(Render);
        }
        if (line.Length == 0) MediaLyricLarge.Text = " ";
        var title = string.IsNullOrWhiteSpace(media.Title) ? "Unknown title" : media.Title;
        var shown = _settings.LyricsInCompact && line.Length > 0 ? "♪ " + line : title;
        var artist = _settings.ShowArtistOnPill && !(_settings.LyricsInCompact && line.Length > 0) ? media.Artist : "";
        MediaArtistSmall.Text = artist;
        MediaArtistSmall.Visibility = artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _defaultTitleFont ??= MediaTitleSmall.FontFamily;
        var lyricOnPill = _settings.LyricsInCompact && line.Length > 0;
        MediaTitleSmall.FontFamily = lyricOnPill ? MediaLyricLarge.FontFamily : _defaultTitleFont;
        if (shown != MediaTitleSmall.Text)
        {
            MediaTitleSmall.Text = shown;
            _marqueeKey = null; // new text: re-measure the scroll
            UpdateMarquee();
        }
        if (lyricOnPill) ApplyLyricsLook();
        else MediaTitleSmall.ClearValue(TextBlock.ForegroundProperty);
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
        MediaTitleClip.UpdateLayout();
        var available = MediaTitleClip.ActualWidth;
        if (available < 40) { _marqueeKey = null; return; } // not laid out yet: SizeChanged tries again
        var overflow = MediaTitleSmall.DesiredSize.Width - available;
        if (overflow <= 2) return;

        var distance = overflow + 26; // far enough that the end of the title clears the edge fade
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

    // ------------------------------------------------------------------ lyrics button + inline lyrics

    /// <summary>The Lyrics button shows only when lyrics exist; pressing it opens them under the controls.</summary>
    private void UpdateLyricsUi()
    {
        var ready = _lyricLines is not null;
        var open = ready && _lyricsOpen;
        var appear = ready && LyricsToggle.Visibility != Visibility.Visible;
        LyricsToggle.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        LyricsToggle.Background = open
            ? new LinearGradientBrush(Color.FromRgb(0x3B, 0x9D, 0xFF), Color.FromRgb(0x0A, 0x7A, 0xFF), 90)
            : (Brush)FindResource("GlassFill");
        if (appear)
        {
            var pop = new System.Windows.Media.Animation.DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(520))
            {
                EasingFunction = new System.Windows.Media.Animation.ElasticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                    Oscillations = 1,
                    Springiness = 5,
                },
            };
            LyricsToggleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            LyricsToggleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        var wasOpen = LyricsInlineHost.Visibility == Visibility.Visible;
        LyricsInlineHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open && !wasOpen) MusicTick();
        if (open != wasOpen && _state.IsExpanded) Render(); // the island grows or shrinks to fit
    }

    private void OnLyricsToggleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true; // don't collapse the island
        _lyricsOpen = !_lyricsOpen;
        var pop = new System.Windows.Media.Animation.DoubleAnimation(1.08, TimeSpan.FromMilliseconds(110)) { AutoReverse = true };
        LyricsToggleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        LyricsToggleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        UpdateLyricsUi();
    }
}
