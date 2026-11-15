// D-Island (personal fork) - the Lyrics page: the whole song, in a handwriting font, scrolling in time with the music.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DIsland.Services;

namespace DIsland;

public enum LyricsState { None, Loading, Ready, NotFound }

public sealed class LyricsPanel : Grid
{
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "LyricsOffset", typeof(double), typeof(LyricsPanel),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _lines = new();
    private readonly double ViewHeight;
    private readonly TextBlock _title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { FontSize = 13, Opacity = 0.65, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) };
    private readonly List<TextBlock> _blocks = new();
    private List<LyricLine>? _data;
    private FontFamily _font = new("Segoe Print, Ink Free, Segoe UI");
    private double _size = 16;
    private Brush _highlight = SoftWhite(1);
    private int _current = -1;
    private DateTime _userScrolledAt = DateTime.MinValue;

    /// <param name="viewHeight">Height of the scrolling lyrics area.</param>
    /// <param name="header">Show the song title and artist above the lyrics (the full page does; the inline view doesn't).</param>
    public LyricsPanel(double viewHeight = 214, bool header = true)
    {
        ViewHeight = viewHeight;
        _lines.Margin = new Thickness(2, viewHeight * 0.38, 2, viewHeight * 0.38);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(ViewHeight) });

        if (header)
        {
            var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 6), Children = { _title, _artist } };
            Children.Add(heading);
        }

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _lines };
        // Display + ClearType keeps handwriting fonts sharp; Ideal + element Opacity was soft/bitmapty on glass.
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        UseLayoutRounding = false;
        SnapsToDevicePixels = true;
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            _userScrolledAt = DateTime.Now; // reading ahead: don't yank the view back for a few seconds
            _scroll.BeginAnimation(OffsetProperty, null);
            _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - e.Delta);
            e.Handled = true; // never flips the page
        };
        var body = new Grid();
        body.Children.Add(_scroll);
        body.Children.Add(_status);
        SetRow(body, 1);
        Children.Add(body);
        SetState(LyricsState.None, null);
    }

#if DEBUG
    /// <summary>Debug aid (DISLAND_LYRICS_DEMO=1): sample lyrics, to check the look without a song playing.</summary>
    public static List<LyricLine> DemoLines()
    {
        var lines = new List<LyricLine>();
        var text = new[] { "Walking through the neon rain", "Every light remembers my name", "", "Hold me like a fading star", "We can bend the dark apart", "And the city hums along", "Soft and slow, a borrowed song", "Take me where the night is clear" };
        for (var i = 0; i < text.Length; i++) lines.Add(new LyricLine(TimeSpan.FromSeconds(i * 5), text[i]));
        return lines;
    }

    public void DebugDemo()
    {
        SetTrack("Sample Song", "Sample Artist");
        SetState(LyricsState.Ready, DemoLines());
        Update(TimeSpan.FromSeconds(16));
    }
#endif

    /// <summary>Seek to this time (the user clicked a line).</summary>
    public event Action<TimeSpan>? SeekRequested;

    public void SetFont(string family, double size)
    {
        _font = new FontFamily($"{family}, Segoe Print, Ink Free, Segoe UI");
        _size = size;
        foreach (var block in _blocks) { block.FontFamily = _font; block.FontSize = _size; }
    }

    /// <summary>Brush for the line currently being sung (solid or gradient). Dim lines stay soft white.</summary>
    public void SetHighlight(Brush brush)
    {
        _highlight = brush;
        if (_current >= 0 && _current < _blocks.Count) FadeLines();
    }

    public void SetTrack(string title, string artist)
    {
        _title.Text = title;
        _artist.Text = artist;
    }

    public void SetState(LyricsState state, List<LyricLine>? lines)
    {
        _data = lines;
        _lines.Children.Clear();
        _blocks.Clear();
        _current = -1;
        _status.Visibility = state == LyricsState.Ready ? Visibility.Collapsed : Visibility.Visible;
        _status.Text = state switch
        {
            LyricsState.Loading => "Finding the lyrics…",
            LyricsState.NotFound => "No synced lyrics for this one.",
            LyricsState.None => "Play a song and its lyrics show up here.",
            _ => "",
        };
        _status.FontFamily = _font;
        if (state != LyricsState.Ready || lines is null) return;

        foreach (var line in lines)
        {
            var block = new TextBlock
            {
                Text = line.Text.Length == 0 ? "♪" : line.Text,
                FontFamily = _font,
                FontSize = _size,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
                Foreground = SoftWhite(0.55),
                Cursor = Cursors.Hand,
                Tag = line.Time,
            };
            TextOptions.SetTextFormattingMode(block, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(block, TextRenderingMode.ClearType);
            block.MouseLeftButtonUp += (_, e) => { e.Handled = true; SeekRequested?.Invoke(line.Time); };
            _lines.Children.Add(block);
            _blocks.Add(block);
        }
    }

    /// <summary>Move the highlight to the line being sung, and glide it to the middle of the view.</summary>
    public void Update(TimeSpan position)
    {
        if (_data is null || _blocks.Count == 0) return;
        var index = -1;
        for (var i = 0; i < _data.Count; i++)
        {
            if (_data[i].Time <= position + TimeSpan.FromMilliseconds(150)) index = i;
            else break;
        }
        if (index == _current) return;

        if (_current >= 0 && _current < _blocks.Count) Style(_blocks[_current], false);
        _current = index;
        if (index < 0 || index >= _blocks.Count) return;
        Style(_blocks[index], true);
        FadeLines();

        if ((DateTime.Now - _userScrolledAt).TotalSeconds < 4) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_current >= _blocks.Count || _current < 0) return;
            var block = _blocks[_current];
            if (!block.IsLoaded) return;
            var y = block.TranslatePoint(new Point(0, 0), _lines).Y + _lines.Margin.Top;
            var target = Math.Max(0, y + block.ActualHeight / 2 - ViewHeight / 2);
            _scroll.SetValue(OffsetProperty, _scroll.VerticalOffset);
            _scroll.BeginAnimation(OffsetProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void Style(TextBlock block, bool current)
    {
        block.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
    }

    /// <summary>Brush alpha instead of UIElement.Opacity — Opacity forces a bitmap layer and makes lyrics look pixelated.</summary>
    private static SolidColorBrush SoftWhite(double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp((int)(opacity * 255), 0, 255), 255, 255, 255));
        brush.Freeze();
        return brush;
    }

    /// <summary>Lines fade with distance from the one being sung (a mask over the whole list made the text blurry).</summary>
    private void FadeLines()
    {
        for (var i = 0; i < _blocks.Count; i++)
        {
            var distance = _current < 0 ? i + 1 : Math.Abs(i - _current);
            // gentle falloff with a readable floor, so the whole song stays legible while scrolling
            _blocks[i].BeginAnimation(UIElement.OpacityProperty, null);
            _blocks[i].Opacity = 1;
            if (distance == 0)
                _blocks[i].Foreground = _highlight;
            else
            {
                var to = Math.Max(0.42, 0.7 - 0.1 * (distance - 1));
                _blocks[i].Foreground = SoftWhite(to);
            }
        }
    }
}
