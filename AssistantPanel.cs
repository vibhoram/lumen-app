// D-Island (personal fork) - the AI assistant page: chat with Groq, with the screen attached in one click.

using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

public sealed class AssistantPanel : Grid
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly Color AccentA = Color.FromRgb(0x8B, 0x6C, 0xFF);
    private static readonly Color AccentB = Color.FromRgb(0x2D, 0xD4, 0xFF);

    private readonly AppSettings _settings;
    private readonly List<ChatMessage> _history = new();
    private readonly StackPanel _messages = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input;
    private readonly TextBlock _placeholder;
    private readonly Border _inputBox;
    private readonly Border _chip;
    private readonly Image _chipImage;
    private readonly Border _chipThumb;
    private readonly TextBlock _chipText;
    private readonly Border _send;
    private readonly TextBlock _sendGlyph;
    private readonly StackPanel _empty;
    private readonly Border _orb;
    private readonly Border _liveChip;
    private BitmapSource? _pending;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _focused;
    private string? _pendingText;

    public AssistantPanel(AppSettings settings)
    {
        _settings = settings;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- header
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        _orb = MakeOrb();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(_orb);
        title.Children.Add(new TextBlock
        {
            Text = "Ask",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(title);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        tools.Children.Add(Chip("", "Screen", () => _ = GrabScreenAsync()));
        _liveChip = Chip("", "Live", ToggleLive);
        _liveChip.Margin = new Thickness(6, 0, 0, 0);
        tools.Children.Add(_liveChip);
        var clear = Chip("", "", Clear);
        clear.Margin = new Thickness(6, 0, 0, 0);
        tools.Children.Add(clear);
        header.Children.Add(tools);
        Children.Add(header);

        // ---- messages + empty state
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            MaxHeight = 206,
            MinHeight = 92,
            Content = _messages,
        };
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            if (_scroll.ScrollableHeight <= 0) return;
            var at = _scroll.VerticalOffset;
            _scroll.BeginAnimation(OffsetProperty, null);
            _scroll.ScrollToVerticalOffset(at - e.Delta);
            e.Handled = true; // don't flip pages while reading a long answer
        };
        _empty = BuildEmpty();
        var stack = new Grid();
        stack.Children.Add(_scroll);
        stack.Children.Add(_empty);
        // A soft dark well behind the conversation keeps text readable over any wallpaper or window.
        var well = new Border
        {
            Background = B("#59000000"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(10, 10, 10, 2),
            Child = stack,
        };
        SetRow(well, 1);
        Children.Add(well);

        // ---- attachment chip
        _chipImage = new Image { Width = 56, Height = 34, Stretch = Stretch.UniformToFill };
        _chipThumb = new Border { CornerRadius = new CornerRadius(7), Child = _chipImage, ClipToBounds = true };
        _chipText = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), Opacity = 0.9, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis };
        var remove = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Opacity = 0.7, Margin = new Thickness(0, 0, 4, 0) };
        remove.MouseLeftButtonUp += (_, e) => { _pending = null; _pendingText = null; UpdateChip(); e.Handled = true; };
        _chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = B("#1FFFFFFF"),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    _chipThumb,
                    _chipText,
                    remove,
                },
            },
        };
        SetRow(_chip, 2);
        Children.Add(_chip);

        // ---- input
        _input = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            FontSize = 13,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 78,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 8, 0),
        };
        _placeholder = new TextBlock
        {
            Text = "Ask anything…",
            FontSize = 13,
            Foreground = B("#80FFFFFF"),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        };
        _sendGlyph = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _send = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = AccentGradient(),
            Cursor = Cursors.Hand,
            Child = _sendGlyph,
            VerticalAlignment = VerticalAlignment.Bottom,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        _send.MouseLeftButtonUp += (_, e) => { e.Handled = true; Send(); };
        _send.MouseEnter += (_, _) => PopScale(_send, 1.08);
        _send.MouseLeave += (_, _) => PopScale(_send, 1);

        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition());
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var inputHost = new Grid { VerticalAlignment = VerticalAlignment.Center, MinHeight = 22 };
        inputHost.Children.Add(_placeholder);
        inputHost.Children.Add(_input);
        inputGrid.Children.Add(inputHost);
        SetColumn(_send, 1);
        inputGrid.Children.Add(_send);
        _inputBox = new Border
        {
            CornerRadius = new CornerRadius(20),
            Background = B("#1AFFFFFF"),
            BorderBrush = B("#14FFFFFF"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 6, 5, 5),
            Margin = new Thickness(0, 8, 0, 0),
            Child = inputGrid,
        };
        SetRow(_inputBox, 3);
        Children.Add(_inputBox);

        _input.TextChanged += (_, _) => _placeholder.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _input.PreviewMouseLeftButtonDown += (_, _) => BeginTypingRequested?.Invoke(_input);
        _input.GotKeyboardFocus += (_, _) => { _focused = true; UpdateBorder(); };
        _input.LostKeyboardFocus += (_, _) => { _focused = false; UpdateBorder(); };
        _input.PreviewKeyDown += OnInputKeyDown;

        UpdateLive();
    }

    public event Action<TextBox>? BeginTypingRequested;
    public event Action? EndTypingRequested;
    public event Action? OpenSettingsRequested;

    /// <summary>Takes a screenshot of the primary screen (provided by the island).</summary>
    public Func<Task<BitmapSource?>>? ScreenRequested { get; set; }

    public TextBox Input => _input;

#if DEBUG
    /// <summary>Fills the page with sample content (DISLAND_ASSIST_DEMO=1) to check layout and growth.</summary>
    public void DebugDemo()
    {
        var wb = new WriteableBitmap(320, 180, 96, 96, PixelFormats.Bgr32, null);
        var px = new byte[320 * 180 * 4];
        for (var i = 0; i < px.Length; i += 4) { px[i] = 200; px[i + 1] = (byte)(i / 4 % 320); px[i + 2] = 90; }
        wb.WritePixels(new Int32Rect(0, 0, 320, 180), px, 320 * 4, 0);
        ShowEmpty(false);
        AddUser("What does this error mean?", wb);
        var bubble = new AssistantBubble(this);
        _messages.Children.Add(bubble.Root);
        bubble.SetText("**The build failed** because a file is missing.\n\n- Check the path in `settings.json`\n- Re-run the installer\n\n```\ndotnet build --no-incremental\n```\n\nIf it still fails, paste the full log and I'll dig in. This longer answer makes the area scroll so the layout can be checked.");
        bubble.Finish();
        AttachImage(wb);
        AttachText("Selected text from the page");
    }
#endif

    /// <summary>Attaches a piece of text (selection, clipboard item) as context for the next message.</summary>
    public void AttachText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _pendingText = text.Trim();
        UpdateChip();
    }

    /// <summary>Attaches a screenshot to the next message.</summary>
    public void AttachImage(BitmapSource? image)
    {
        if (image is null) return;
        _pending = image;
        UpdateChip();
    }

    // ------------------------------------------------------------------ input

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            EndTypingRequested?.Invoke();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            Send();
        }
        else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && Clipboard.ContainsImage())
        {
            e.Handled = true;
            if (Clipboard.GetImage() is { } image)
            {
                var copy = new WriteableBitmap(image);
                copy.Freeze();
                AttachImage(copy);
            }
        }
    }

    private async Task GrabScreenAsync()
    {
        if (ScreenRequested is null) return;
        AttachImage(await ScreenRequested());
        BeginTypingRequested?.Invoke(_input);
    }

    private void ToggleLive()
    {
        _settings.AssistantLiveScreen = !_settings.AssistantLiveScreen;
        _settings.NotifyChanged();
        _settings.Save();
        UpdateLive();
    }

    private void UpdateLive()
    {
        var on = _settings.AssistantLiveScreen;
        _liveChip.Background = on ? AccentGradient() : B("#1FFFFFFF");
        _liveChip.ToolTip = on ? "Live screen: every message includes your current screen" : "Turn on to include your screen with every message";
    }

    private void Clear()
    {
        Cancel();
        _history.Clear();
        _messages.Children.Clear();
        _pending = null;
        UpdateChip();
        ShowEmpty(true);
    }

    private void UpdateChip()
    {
        if (_pending is null && _pendingText is null)
        {
            _chip.Visibility = Visibility.Collapsed;
            return;
        }
        var wasHidden = _chip.Visibility != Visibility.Visible;
        _chipImage.Source = _pending;
        _chipThumb.Visibility = _pending is null ? Visibility.Collapsed : Visibility.Visible;
        _chipText.Text = (_pending, _pendingText) switch
        {
            (not null, not null) => "Screen + selected text",
            (not null, null) => "Screen attached",
            _ => "\u201C" + _pendingText!.Replace('\n', ' ') + "\u201D",
        };
        _chip.Visibility = Visibility.Visible;
        if (wasHidden) Appear(_chip, 6);
    }

    // ------------------------------------------------------------------ sending

    private void Cancel() => _cts?.Cancel();

    private async void Send(string? preset = null, bool grabScreen = false)
    {
        try
        {
            if (_busy)
            {
                Cancel();
                return;
            }
            var text = (preset ?? _input.Text).Trim();
            var quote = _pendingText;
            if (text.Length == 0 && _pending is null && quote is null && !grabScreen) return;
            if (string.IsNullOrWhiteSpace(_settings.GroqApiKey))
            {
                ShowNoKey();
                return;
            }

            SetBusy(true);
            var image = _pending;
            if (ScreenRequested is not null && (grabScreen || (_settings.AssistantLiveScreen && image is null)))
                image = await ScreenRequested() ?? image;
            if (text.Length == 0) text = "What's going on here? Explain it briefly.";

            _pending = null;
            _pendingText = null;
            UpdateChip();
            _input.Clear();
            var dataUrl = image is null ? null : await Task.Run(() => GroqService.ToDataUrl(image));
            ShowEmpty(false);
            AddUser(text, image, quote);
            var sent = quote is null ? text : $"About this text:\n\"\"\"\n{quote}\n\"\"\"\n\n{text}";
            _history.Add(new ChatMessage("user", sent, dataUrl));
            await RunAsync(text, image);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            SetBusy(false);
        }
    }

    private async Task RunAsync(string userText, BitmapSource? image)
    {
        var bubble = new AssistantBubble(this);
        _messages.Children.Add(bubble.Root);
        Appear(bubble.Root, 10);
        ScrollDown();

        _cts = new CancellationTokenSource();
        var reply = new StringBuilder();
        try
        {
            await foreach (var chunk in GroqService.StreamAsync(_settings.GroqApiKey, _settings.GroqModel, _history.ToList(), _cts.Token))
            {
                reply.Append(chunk);
                bubble.SetText(reply.ToString());
            }
            if (reply.Length == 0) throw new GroqException(0, "The model returned an empty answer.");
            _history.Add(new ChatMessage("assistant", reply.ToString()));
            bubble.Finish();
        }
        catch (OperationCanceledException)
        {
            if (reply.Length > 0) _history.Add(new ChatMessage("assistant", reply.ToString()));
            else _history.RemoveAt(_history.Count - 1);
            bubble.Finish(stopped: true);
        }
        catch (Exception ex)
        {
            _history.RemoveAt(_history.Count - 1); // so a retry doesn't duplicate the question
            var message = ex switch
            {
                GroqException { Status: 401 } => "Groq rejected the API key. Check it in Settings > AI Assistant.",
                GroqException { Status: 429 } => "Rate limit reached. Give it a few seconds, then retry.",
                GroqException g => g.Message,
                HttpRequestException => "Can't reach Groq. Check your connection.",
                _ => ex.Message,
            };
            bubble.Fail(message, () =>
            {
                _messages.Children.Remove(bubble.Root);
                _pending = image;
                UpdateChip();
                Send(userText);
            });
        }
        finally
        {
            SetBusy(false);
            ScrollDown();
        }
    }

    private void UpdateBorder()
    {
        if (_busy)
        {
            // A soft light sweeps along the edge while the model is working.
            var shimmer = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                SpreadMethod = GradientSpreadMethod.Repeat,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x30, 0x8B, 0x6C, 0xFF), 0),
                    new GradientStop(AccentB, 0.5),
                    new GradientStop(Color.FromArgb(0x30, 0x8B, 0x6C, 0xFF), 1),
                },
            };
            var slide = new TranslateTransform();
            shimmer.RelativeTransform = slide;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1, 0, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever });
            _inputBox.BorderBrush = shimmer;
        }
        else _inputBox.BorderBrush = _focused ? AccentGradient() : B("#14FFFFFF");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateBorder();
        _sendGlyph.Text = busy ? "" : "";
        _placeholder.Text = busy ? "Thinking…" : "Ask anything…";
        var pulse = busy
            ? new DoubleAnimation(1, 1.18, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() }
            : null;
        if (_orb.RenderTransform is ScaleTransform st)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            if (!busy) { st.ScaleX = 1; st.ScaleY = 1; }
        }
    }

    // ------------------------------------------------------------------ message UI

    private void AddUser(string text, BitmapSource? image, string? quote = null)
    {
        var body = new StackPanel();
        if (quote is not null)
            body.Children.Add(new Border
            {
                Background = B("#26000000"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 5),
                Child = new TextBlock { Text = quote.Length > 160 ? quote[..160] + "…" : quote, FontSize = 11.5, Opacity = 0.85, TextWrapping = TextWrapping.Wrap },
            });
        if (image is not null)
            body.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 0, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new Image { Source = image, Height = 54, Stretch = Stretch.Uniform },
            });
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Brushes.White });
        var bubble = new Border
        {
            Background = AccentGradient(),
            CornerRadius = new CornerRadius(16, 16, 4, 16),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            MaxWidth = 330,
            Margin = new Thickness(40, 0, 0, 8),
            Child = body,
        };
        _messages.Children.Add(bubble);
        Appear(bubble, 10);
        ScrollDown();
    }

    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "SmoothOffset", typeof(double), typeof(AssistantPanel),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    private void ScrollDown() => Dispatcher.BeginInvoke(() =>
    {
        var target = _scroll.ScrollableHeight;
        if (Math.Abs(target - _scroll.VerticalOffset) < 1) return;
        _scroll.SetValue(OffsetProperty, _scroll.VerticalOffset);
        _scroll.BeginAnimation(OffsetProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }, DispatcherPriority.Background);

    private void ShowNoKey()
    {
        ShowEmpty(false);
        var open = Chip("", "Open Settings", () => OpenSettingsRequested?.Invoke());
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 8, 0, 0);
        var card = new Border
        {
            Background = B("#1FFFFFFF"),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Add your Groq API key", FontWeight = FontWeights.SemiBold, FontSize = 13 },
                    new TextBlock { Text = "Paste it in Settings > AI Assistant (free at console.groq.com).", FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) },
                    open,
                },
            },
        };
        _messages.Children.Add(card);
        Appear(card, 8);
    }

    private StackPanel BuildEmpty()
    {
        var spark = new TextBlock
        {
            Text = "✦",
            FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = AccentGradient(),
            FontFamily = new FontFamily("Segoe UI Symbol"),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var rotate = new RotateTransform();
        spark.RenderTransform = rotate;
        rotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(-8, 8, TimeSpan.FromSeconds(3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });

        var chips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (label, prompt) in new[]
        {
            ("Explain my screen", "Explain what's on my screen."),
            ("Summarize", "Summarize what's on my screen in a few bullets."),
            ("Fix this error", "There's an error on my screen. What does it mean and how do I fix it?"),
            ("Translate", "Translate the text on my screen to English."),
        })
        {
            var chip = Chip("", label, () => Send(prompt, grabScreen: true));
            chip.Margin = new Thickness(3);
            chips.Children.Add(chip);
        }
        return new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                spark,
                new TextBlock { Text = "Ask about anything on your screen", FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) },
                new TextBlock { Text = "One tap grabs your screen and asks", FontSize = 11.5, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center },
                chips,
            },
        };
    }

    private void ShowEmpty(bool show)
    {
        if (show == (_empty.Visibility == Visibility.Visible)) return;
        _empty.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) Appear(_empty, 8);
    }

    // ------------------------------------------------------------------ assistant bubble

    private sealed class AssistantBubble
    {
        private readonly AssistantPanel _owner;
        private readonly StackPanel _host = new();
        private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 4, 2, 4) };
        private readonly DispatcherTimer _render = new() { Interval = TimeSpan.FromMilliseconds(60) };
        private string _text = "";
        private bool _dirty;
        private bool _dotsShown = true;

        public Border Root { get; }

        public AssistantBubble(AssistantPanel owner)
        {
            _owner = owner;
            for (var i = 0; i < 3; i++)
            {
                var move = new TranslateTransform();
                var dot = new Ellipse { Width = 6, Height = 6, Fill = Brushes.White, Margin = new Thickness(2, 0, 2, 0), RenderTransform = move };
                var begin = TimeSpan.FromMilliseconds(i * 140);
                dot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(420)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
                move.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(420)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, EasingFunction = new SineEase() });
                _dots.Children.Add(dot);
            }
            _host.Children.Add(_dots);
            Root = new Border
            {
                Background = B("#1FFFFFFF"),
                BorderBrush = B("#12FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16, 16, 16, 4),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 360,
                Margin = new Thickness(0, 0, 40, 8),
                Child = _host,
            };
            _render.Tick += (_, _) =>
            {
                if (!_dirty) return;
                _dirty = false;
                Render(_text);
            };
        }

        public void SetText(string text)
        {
            _text = text;
            _dirty = true;
            if (_dotsShown)
            {
                _dotsShown = false;
                foreach (UIElement d in _dots.Children) d.BeginAnimation(OpacityProperty, null);
                _host.Children.Remove(_dots);
                Render(text);
                _dirty = false;
                _render.Start();
            }
        }

        public void Finish(bool stopped = false)
        {
            _render.Stop();
            if (_dotsShown)
            {
                _host.Children.Remove(_dots);
                _dotsShown = false;
            }
            Render(_text);
            if (stopped)
                _host.Children.Add(new TextBlock { Text = "Stopped", FontSize = 11, Opacity = 0.5, Margin = new Thickness(0, 4, 0, 0) });
            if (_text.Length > 0)
            {
                var copy = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 11, Opacity = 0.45, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand, Margin = new Thickness(0, 4, 0, 0), ToolTip = "Copy" };
                copy.MouseEnter += (_, _) => copy.Opacity = 1;
                copy.MouseLeave += (_, _) => copy.Opacity = 0.45;
                copy.MouseLeftButtonUp += (_, e) => { e.Handled = true; try { Clipboard.SetText(_text); } catch (Exception) { } };
                _host.Children.Add(copy);
            }
            _owner.ScrollDown();
        }

        public void Fail(string message, Action retry)
        {
            _render.Stop();
            _host.Children.Clear();
            Root.Background = B("#33FF453A");
            _host.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
            var again = Chip("", "Retry", retry);
            again.HorizontalAlignment = HorizontalAlignment.Left;
            again.Margin = new Thickness(0, 7, 0, 0);
            _host.Children.Add(again);
        }

        private void Render(string text)
        {
            var atEnd = _owner._scroll.VerticalOffset >= _owner._scroll.ScrollableHeight - 8;
            _host.Children.Clear();
            var parts = text.Split("```");
            for (var i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 1) AddCode(parts[i]);
                else AddProse(parts[i]);
            }
            if (atEnd) _owner.ScrollDown();
        }

        private void AddCode(string code)
        {
            var nl = code.IndexOf('\n');
            if (nl >= 0 && nl < 20 && !code[..nl].Contains(' ')) code = code[(nl + 1)..]; // drop the language tag
            code = code.Trim('\n');
            var block = new Border
            {
                Background = B("#66000000"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 3, 0, 3),
                Cursor = Cursors.Hand,
                ToolTip = "Click to copy",
                Child = new TextBlock { Text = code, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = B("#E6FFFFFF") },
            };
            block.MouseLeftButtonUp += (_, e) => { e.Handled = true; try { Clipboard.SetText(code); } catch (Exception) { } };
            _host.Children.Add(block);
        }

        private void AddProse(string prose)
        {
            var paragraph = new List<string>();
            void Flush()
            {
                if (paragraph.Count == 0) return;
                _host.Children.Add(Inline(string.Join("\n", paragraph), 0));
                paragraph.Clear();
            }
            foreach (var raw in prose.Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0) { Flush(); continue; }
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    Flush();
                    _host.Children.Add(Inline("•  " + trimmed[2..], 8));
                }
                else if (trimmed.StartsWith('#'))
                {
                    Flush();
                    var tb = Inline(trimmed.TrimStart('#').Trim(), 0);
                    tb.FontWeight = FontWeights.SemiBold;
                    tb.FontSize = 14;
                    _host.Children.Add(tb);
                }
                else paragraph.Add(line);
            }
            Flush();
        }

        private static TextBlock Inline(string text, double indent)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = B("#F2FFFFFF"),
                Margin = new Thickness(indent, 1, 0, 3),
                LineHeight = 18,
            };
            foreach (var part in Regex.Split(text, @"(\*\*.+?\*\*|`[^`]+`)"))
            {
                if (part.Length == 0) continue;
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                    tb.Inlines.Add(new Run(part[2..^2]) { FontWeight = FontWeights.SemiBold });
                else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                    tb.Inlines.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), Background = B("#40000000"), FontSize = 12 });
                else tb.Inlines.Add(new Run(part));
            }
            return tb;
        }
    }

    // ------------------------------------------------------------------ small helpers

    private static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private static Brush AccentGradient() => new LinearGradientBrush(AccentA, AccentB, 35);

    private static Border MakeOrb()
    {
        var orb = MakeOrbCore();
        if (orb.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
            glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                new DoubleAnimation(0.4, 0.95, TimeSpan.FromSeconds(2.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        return orb;
    }

    private static Border MakeOrbCore() => new()
    {
        Width = 22,
        Height = 22,
        CornerRadius = new CornerRadius(11),
        Background = AccentGradient(),
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new ScaleTransform(1, 1),
        Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = AccentA, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.7 },
        Child = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 3, 0, 0),
        },
    };

    private static Border Chip(string glyph, string text, Action click)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph.Length > 0)
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, text.Length > 0 ? 6 : 0, 0) });
        if (text.Length > 0)
            row.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var normal = B("#1FFFFFFF");
        var hover = B("#33FFFFFF");
        var chip = new Border
        {
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(text.Length > 0 ? 10 : 8, 5, text.Length > 0 ? 11 : 8, 5),
            Background = normal,
            Cursor = Cursors.Hand,
            Child = row,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        chip.MouseEnter += (_, _) => { if (ReferenceEquals(chip.Background, normal)) chip.Background = hover; PopScale(chip, 1.04); };
        chip.MouseLeave += (_, _) => { if (ReferenceEquals(chip.Background, hover)) chip.Background = normal; PopScale(chip, 1); };
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return chip;
    }

    private static void PopScale(UIElement element, double to)
    {
        if (element.RenderTransform is not ScaleTransform st) return;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Fade and rise into place.</summary>
    private static void Appear(FrameworkElement element, double rise)
    {
        var move = new TranslateTransform(0, rise);
        element.RenderTransform = move;
        element.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }
}
