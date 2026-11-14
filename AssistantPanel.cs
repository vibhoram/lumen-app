// D-Island (personal fork) - the AI assistant page: chat with Groq, with the screen attached in one click.

using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    internal static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly Color AccentA = Color.FromRgb(0x3B, 0x9D, 0xFF);
    private static readonly Color AccentB = Color.FromRgb(0x0A, 0x7A, 0xFF);

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
    private readonly FrameworkElement _orb;
    private readonly TextBlock _status;
    private readonly Border _liveChip;
    private BitmapSource? _pending;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private DateTime _lastActivity = DateTime.MinValue;
    private bool _engineLocal;
    private string _engineName = "";

    /// <summary>True while she is replying, and for a few seconds after: the island must not collapse under the cursor while you read.</summary>
    public bool KeepOpen => _busy || (DateTime.UtcNow - _lastActivity).TotalSeconds < 10;

    /// <summary>A tiny always-visible badge: green dot "local" (the model on this PC) or blue dot "cloud" (Groq).</summary>
    private void SetEngine(bool local, string name)
    {
        _engineLocal = local;
        _engineName = name;
        ShowEngine();
    }

    private void ShowEngine()
    {
        _status.Inlines.Clear();
        if (_engineName.Length == 0) { _status.Text = "online"; return; }
        _status.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = _engineLocal ? B("#30D158") : B("#4DA3FF"), FontSize = 9 });
        _status.Inlines.Add(new System.Windows.Documents.Run(_engineLocal ? $"local · {_engineName}" : $"cloud · {_engineName}"));
    }

    /// <summary>Works out which brain the next plain-text message will use and shows it.</summary>
    private async Task RefreshEngineAsync()
    {
        string? local = _settings.AssistantUseLocalAi ? await LocalAi.PickModelAsync(_settings) : null;
        if (_busy) return;
        if (local is not null) SetEngine(true, local.Split(':')[0]);
        else SetEngine(false, _settings.CloudProvider == CloudProvider.Gemini ? "gemini" : "groq");
    }
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
        // load the offline model as soon as the page opens, so the first reply isn't slow
        IsVisibleChanged += (_, _) => { if (IsVisible) { _ = LocalAi.WarmUpAsync(_settings); _ = RefreshEngineAsync(); } };
        _orb = MakeAvatar(34);
        _status = new TextBlock { Text = "online", FontSize = 10.5, Opacity = 0.55 };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(_orb);
        title.Children.Add(new StackPanel
        {
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = ClaraBrain.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, LineHeight = 16 },
                _status,
            },
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
            MaxHeight = 250,
            MinHeight = 92,
            Content = _messages,
        };
        var slim = new SlimScroller(_scroll);
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            slim.Wheel(e.Delta);
            e.Handled = true; // never flips the page while you read
        };
        _empty = BuildEmpty();
        var stack = new Grid();
        stack.Children.Add(slim.Root);
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
            Background = GlassFillBrush(),
            BorderBrush = GlassRimBrush(),
            BorderThickness = new Thickness(1),
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
            Text = "Message Clara…",
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
            Background = GlassFillBrush(),
            BorderBrush = GlassRimBrush(),
            BorderThickness = new Thickness(1.2),
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

    /// <summary>One line of "what's going on right now" (time, music, battery) for Clara; null when switched off.</summary>
    public Func<string>? ContextProvider { get; set; }

    /// <summary>Runs one of Clara's tools on the PC: (name, arguments JSON) → (worked, result for the model, short label for the user).</summary>
    public Func<string, string, Task<(bool Ok, string Result, string Summary)>>? ToolRunner { get; set; }

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
        bubble.SetText("emoji test 😭 👀 ✨ 😅 🤝 🫠 😮‍💨 🥲 💀 🔥 ☕ 🧠 ✈️ 🛠️\n\n**The build failed** because a file is missing.\n\n- Check the path in `settings.json`\n- Re-run the installer\n\n```\ndotnet build --no-incremental\n```\n\nIf it still fails, paste the full log and I'll dig in. This longer answer makes the area scroll so the layout can be checked.");
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
        _liveChip.Background = on ? AccentGradient() : GlassFillBrush();
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
            // No Groq key is fine for plain text if an offline model is available; pictures need the key.
            if (string.IsNullOrWhiteSpace(_settings.CloudKey)
                && (_pending is not null || grabScreen || _settings.AssistantLiveScreen || !_settings.AssistantUseLocalAi || await LocalAi.PickModelAsync(_settings) is null))
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
            ClaraTopics.Add(text);
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

        var start = _history.Count - 1; // index of the user's message, for cleanup on failure
        _cts = new CancellationTokenSource();
        var reply = new StringBuilder();
        var tools = ClaraBrain.Tools();

        // Which brain answers: text-only chats run offline on this PC (Ollama); anything with a picture, or right after one, goes to Groq.
        var cloudNeeded = image is not null; // only the picture you are sending right now needs the cloud; text follow-ups stay offline
        var localModel = !cloudNeeded && _settings.AssistantUseLocalAi ? await LocalAi.PickModelAsync(_settings) : null;
        if (localModel is null && string.IsNullOrWhiteSpace(_settings.CloudKey)) { ShowNoKey(); SetBusy(false); return; }
        var useLocal = localModel is not null;
        SetEngine(useLocal, useLocal ? localModel!.Split(':')[0] : _settings.CloudProvider == CloudProvider.Gemini ? "gemini" : "groq");
        var engineModel = useLocal ? localModel! : _settings.CloudModel;
        string? cKey = useLocal ? null : _settings.CloudKey;
        string? cUrl = useLocal ? LocalAi.ChatEndpoint : _settings.CloudEndpoint is { Length: > 0 } primaryUrl ? primaryUrl : null;
        var backupUsed = false;

        // If the brain in use fails before saying anything (offline model down, rate limit, bad key), quietly move to the next one:
        // offline -> your chosen cloud brain -> the other cloud brain (Groq is the safety net for Gemini and the other way round).
        bool SwitchToBackup()
        {
            if (useLocal)
            {
                if (string.IsNullOrWhiteSpace(_settings.CloudKey)) return false;
                useLocal = false;
                cKey = _settings.CloudKey;
                engineModel = _settings.CloudModel;
                cUrl = _settings.CloudEndpoint is { Length: > 0 } u ? u : null;
                SetEngine(false, _settings.CloudProvider == CloudProvider.Gemini ? "gemini" : "groq");
                return true;
            }
            if (backupUsed) return false;
            var toGemini = _settings.CloudProvider != CloudProvider.Gemini;
            var key = toGemini ? _settings.GeminiApiKey : _settings.GroqApiKey;
            if (string.IsNullOrWhiteSpace(key)) return false;
            backupUsed = true;
            cKey = key;
            engineModel = toGemini ? (string.IsNullOrWhiteSpace(_settings.GeminiModel) ? "gemini-2.5-flash" : _settings.GeminiModel) : _settings.GroqModel;
            cUrl = toGemini ? AppSettings.GeminiEndpoint : null;
            SetEngine(false, toGemini ? "gemini" : "groq");
            return true;
        }
        try
        {
            // A turn can need several rounds: the model asks for tools, we run them, it then answers.
            for (var round = 0; round < 5; round++)
            {
                var context = _settings.ClaraContext ? ContextProvider?.Invoke() : null;
                var system = ClaraBrain.SystemPrompt(useLocal ? $"{engineModel}, running offline on this PC" : engineModel, context, ClaraMemory.Shared.Facts, _settings.UserName, _settings.UserBio);
                // the tool list is a big chunk of the request: only send it when the message sounds like a command (keeps the cloud request under the free per-minute limit)
                var offerTools = LocalAi.LooksActionable(userText) || round > 0;
                system += Environment.NewLine + Environment.NewLine + "REMINDER: answer in one short casual line unless they asked for detail. No long explanations, no lists unless needed.";
                var turn = new StringBuilder();
                List<ToolCall>? calls = null;
                try
                {
                await foreach (var ev in GroqService.StreamAsync(cKey, engineModel, system, _history.ToList(), offerTools ? tools : null, _cts.Token, cUrl, useLocal ? 220 : 700))
                {
                    if (ev.Text is { } text)
                    {
                        turn.Append(text);
                        reply.Append(text);
                        bubble.SetText(NoDashes(reply.ToString()));
                        _lastActivity = DateTime.UtcNow;
                    }
                    if (ev.Calls is { } c) calls = c.ToList();
                }
                }
                catch (Exception) when (!_cts.IsCancellationRequested && turn.Length == 0 && SwitchToBackup())
                {
                    round--; // run this round again on the next brain
                    continue;
                }

                if (calls is null && useLocal)
                {
                    // the local model wrote a command as text instead of calling the tool: run it, and hide the text
                    var (found, cleaned) = ClaraBrain.ExtractTextCalls(turn.ToString());
                    if (found.Count > 0)
                    {
                        reply.Length = Math.Max(0, reply.Length - turn.Length);
                        turn.Clear();
                        turn.Append(cleaned);
                        reply.Append(cleaned);
                        bubble.SetText(NoDashes(reply.ToString()));
                        calls = found;
                    }
                }
                if (calls is null)
                {
                    if (turn.Length > 0) _history.Add(new ChatMessage("assistant", NoDashes(turn.ToString())));
                    break;
                }

                var callsJson = new JsonArray(calls.Select(c => (JsonNode?)new JsonObject
                {
                    ["id"] = c.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments },
                }).ToArray()).ToJsonString();
                _history.Add(new ChatMessage("assistant", turn.ToString(), ToolCallsJson: callsJson));
                if (turn.Length > 0) reply.Append("\n\n");
                foreach (var call in calls)
                {
                    (bool Ok, string Result, string Summary) outcome = ToolRunner is null
                        ? (false, "Tools aren't available.", "")
                        : await ToolRunner(call.Name, call.Arguments);
                    if (outcome.Summary.Length > 0) bubble.AddAction(outcome.Ok, outcome.Summary);
                    _history.Add(new ChatMessage("tool", outcome.Result, ToolCallId: call.Id));
                }
            }
            if (reply.Length == 0 && !bubble.HasActions) throw new GroqException(0, "The model returned an empty answer.");
            bubble.Finish();
        }
        catch (OperationCanceledException)
        {
            if (reply.Length == 0 && !bubble.HasActions) _history.RemoveRange(start, _history.Count - start);
            bubble.Finish(stopped: true);
        }
        catch (Exception ex)
        {
            _history.RemoveRange(start, _history.Count - start); // so a retry doesn't duplicate the question
            var message = ex switch
            {
                GroqException { Status: 401 } => "Groq rejected the API key. Check it in Settings > Clara.",
                GroqException { Status: 429 } => "I've hit Groq's rate limit. Give it a few seconds, then retry.",
                GroqException g => g.Message,
                HttpRequestException => "I can't reach Groq. Check your connection.",
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
            // A soft light sweeps along the edge while Clara is working.
            var shimmer = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                SpreadMethod = GradientSpreadMethod.Repeat,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x30, 0x3B, 0x9D, 0xFF), 0),
                    new GradientStop(AccentB, 0.5),
                    new GradientStop(Color.FromArgb(0x30, 0x3B, 0x9D, 0xFF), 1),
                },
            };
            var slide = new TranslateTransform();
            shimmer.RelativeTransform = slide;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1, 0, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever });
            _inputBox.BorderBrush = shimmer;
        }
        else _inputBox.BorderBrush = _focused ? AccentGradient() : GlassRimBrush();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateBorder();
        _sendGlyph.Text = busy ? "" : "";
        _placeholder.Text = busy ? "Clara's thinking…" : "Message Clara…";
        _lastActivity = DateTime.UtcNow;
        if (busy) { _status.Inlines.Clear(); _status.Text = "thinking…"; } else ShowEngine();
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
                    new TextBlock { Text = "Add a free cloud key", FontWeight = FontWeights.SemiBold, FontSize = 13 },
                    new TextBlock { Text = "Gemini is free with a Google sign-in (aistudio.google.com/apikey), or use Groq. Paste a key in Settings > Clara.", FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) },
                    open,
                },
            },
        };
        _messages.Children.Add(card);
        Appear(card, 8);
    }

    private StackPanel BuildEmpty()
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour switch
        {
            < 5 => "Still up? I'm here.",
            < 12 => "Good morning. What are we doing?",
            < 18 => "Hey. What's on your mind?",
            _ => "Good evening. What are we doing?",
        };
        var avatar = MakeAvatar(60);
        avatar.HorizontalAlignment = HorizontalAlignment.Center;

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
        foreach (var (label, prompt) in new[]
        {
            ("Plan my day", "Help me plan my day. Ask me what's on my list, then suggest an order and offer to start a focus session."),
            ("Study buddy", "I'm about to study. Ask me what I'm working on and set me up with a focus session."),
            ("Why is my PC slow?", "Check my system status and tell me what's using memory; offer to free some RAM."),
            ("Free RAM", "Free up some RAM for me."),
        })
        {
            var chip = Chip("", label, () => Send(prompt, grabScreen: false));
            chip.Margin = new Thickness(3);
            chips.Children.Add(chip);
        }
        return new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                avatar,
                new TextBlock { Text = "Hey, I'm Clara.", FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) },
                new TextBlock { Text = greeting, FontSize = 12, Opacity = 0.65, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) },
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
        private readonly WrapPanel _actions = new() { Margin = new Thickness(0, 0, 0, 2) };
        private readonly StackPanel _body = new();
        private readonly Border _bubble;
        private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 4, 2, 4) };
        private readonly DispatcherTimer _render = new() { Interval = TimeSpan.FromMilliseconds(60) };
        private string _text = "";
        private bool _dirty;
        private bool _dotsShown = true;

        public FrameworkElement Root { get; }
        public bool HasActions => _actions.Children.Count > 0;

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
            _body.Children.Add(_dots);
            _host.Children.Add(_actions);
            _host.Children.Add(_body);
            _bubble = new Border
            {
                Background = B("#1FFFFFFF"),
                BorderBrush = B("#12FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16, 16, 16, 4),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 330,
                Child = _host,
            };
            _bubble.SetValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI Variable Text, Segoe UI, Segoe UI Emoji, Segoe UI Symbol")); // emoji fall back to a font that has them
            var avatar = MakeAvatar(22);
            avatar.VerticalAlignment = VerticalAlignment.Top;
            avatar.Margin = new Thickness(0, 2, 8, 0);
            // a grid, so the bubble is limited to the room that is actually there (a stack panel let it run off the right edge)
            var row = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 14, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(avatar);
            SetColumn(_bubble, 1);
            row.Children.Add(_bubble);
            Root = row;
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
                _body.Children.Remove(_dots);
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
                _body.Children.Remove(_dots);
                _dotsShown = false;
            }
            Render(_text);
            if (stopped)
                _body.Children.Add(new TextBlock { Text = "Stopped", FontSize = 11, Opacity = 0.5, Margin = new Thickness(0, 4, 0, 0) });
            if (_text.Length > 0)
            {
                // a small vector "copy" icon (two overlapping rounded sheets) that turns into a tick when pressed
                var back = new System.Windows.Shapes.Rectangle { Width = 8, Height = 9, RadiusX = 2, RadiusY = 2, Stroke = Brushes.White, StrokeThickness = 1.2, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
                var front = new System.Windows.Shapes.Rectangle { Width = 8, Height = 9, RadiusX = 2, RadiusY = 2, Stroke = Brushes.White, StrokeThickness = 1.2, Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x2B, 0x30)), Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
                var tick = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 1.5,7 L 5,10.5 L 12,2.5"), Stroke = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Visibility = Visibility.Collapsed };
                var iconBox = new Grid { Width = 14, Height = 14 };
                iconBox.Children.Add(back);
                iconBox.Children.Add(front);
                iconBox.Children.Add(tick);
                var copy = new Border
                {
                    Child = iconBox, Opacity = 0.5, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 2, -6, -4),
                    CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand, ToolTip = "Copy",
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
                };
                copy.MouseEnter += (_, _) => { copy.Opacity = 1; PopScale(copy, 1.12); };
                copy.MouseLeave += (_, _) => { copy.Opacity = 0.5; PopScale(copy, 1); };
                copy.MouseLeftButtonUp += async (_, e) =>
                {
                    e.Handled = true;
                    try { Clipboard.SetText(_text); } catch (Exception) { return; }
                    back.Visibility = front.Visibility = Visibility.Collapsed;
                    tick.Visibility = Visibility.Visible;
                    await Task.Delay(1100);
                    tick.Visibility = Visibility.Collapsed;
                    back.Visibility = front.Visibility = Visibility.Visible;
                };
                _body.Children.Add(copy);
            }
            _owner.ScrollDown();
        }

        /// <summary>A small badge for something Clara just did on the PC.</summary>
        public void AddAction(bool ok, string summary)
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 3, 9, 3),
                Margin = new Thickness(0, 0, 5, 4),
                Background = B(ok ? "#2630D158" : "#33FF453A"),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = ok ? "\uE73E" : "\uE711", FontFamily = IconFont, FontSize = 10, Foreground = B(ok ? "#FF30D158" : "#FFFF6A60"), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = summary, FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            _actions.Children.Add(badge);
            Appear(badge, 4);
        }

        public void Fail(string message, Action retry)
        {
            _render.Stop();
            _actions.Children.Clear();
            _body.Children.Clear();
            _bubble.Background = B("#33FF453A");
            _body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
            var again = Chip("", "Retry", retry);
            again.HorizontalAlignment = HorizontalAlignment.Left;
            again.Margin = new Thickness(0, 7, 0, 0);
            _body.Children.Add(again);
        }

        private void Render(string text)
        {
            var atEnd = _owner._scroll.VerticalOffset >= _owner._scroll.ScrollableHeight - 8;
            _body.Children.Clear();
            text = Regex.Replace(text, "<think>.*?(</think>|$)", "", RegexOptions.Singleline).TrimStart(); // some models leak their reasoning
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
            _body.Children.Add(block);
        }

        private void AddProse(string prose)
        {
            var paragraph = new List<string>();
            void Flush()
            {
                if (paragraph.Count == 0) return;
                _body.Children.Add(Inline(string.Join("\n", paragraph), 0));
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
                    _body.Children.Add(Inline("•  " + trimmed[2..], 8));
                }
                else if (trimmed.StartsWith('#'))
                {
                    Flush();
                    var tb = Inline(trimmed.TrimStart('#').Trim(), 0);
                    tb.FontWeight = FontWeights.SemiBold;
                    tb.FontSize = 14;
                    _body.Children.Add(tb);
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
                    AddWithEmoji(tb, part[2..^2], FontWeights.SemiBold);
                else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                    tb.Inlines.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), Background = B("#40000000"), FontSize = 12 });
                else AddWithEmoji(tb, part, null);
            }
            return tb;
        }

        private static bool IsEmoji(string element)
        {
            var cp = char.ConvertToUtf32(element, 0);
            return cp >= 0x1F000 || element.Contains('️') || element.Contains('‍')
                   || cp is >= 0x2600 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF or >= 0x2300 and <= 0x23FF or 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or >= 0x2190 and <= 0x21FF && element.Length > 1;
        }

        /// <summary>Adds text, drawing every emoji as a real colour image (WPF itself would show a flat one-colour outline).</summary>
        private static void AddWithEmoji(TextBlock tb, string text, FontWeight? weight)
        {
            var plain = new System.Text.StringBuilder();
            void Flush()
            {
                if (plain.Length == 0) return;
                var run = new Run(plain.ToString());
                if (weight is { } w) run.FontWeight = w;
                tb.Inlines.Add(run);
                plain.Clear();
            }
            var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext())
            {
                var element = (string)e.Current;
                if (element.Length >= 2 && IsEmoji(element) && EmojiRenderer.Render(element, 36) is { } image)
                {
                    Flush();
                    tb.Inlines.Add(new InlineUIContainer(new Image { Source = image, Width = 17, Height = 17, Margin = new Thickness(1, 0, 1, -3) })
                    { BaselineAlignment = BaselineAlignment.Center });
                }
                else plain.Append(element);
            }
            Flush();
        }
    }

    // ------------------------------------------------------------------ small helpers

    internal static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    /// <summary>The one accent: a calm iOS blue, lit slightly from above (no rainbow gradients).</summary>
    internal static Brush AccentGradient() => new LinearGradientBrush(AccentA, AccentB, 90);

    /// <summary>Clara's avatar: her portrait if one is on disk, otherwise a luminous glass presence.</summary>
    private static FrameworkElement MakeAvatar(double size)
    {
        var portrait = LoadPortrait();
        var grid = new Grid
        {
            Width = size,
            Height = size,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, BlurRadius = size * 0.4, ShadowDepth = 0, Opacity = 0.22 },
        };
        if (portrait is not null)
        {
            grid.Children.Add(new Ellipse { Fill = new ImageBrush(portrait) { Stretch = Stretch.UniformToFill } });
        }
        else
        {
            grid.Children.Add(BuildAndroidFace(size));
        }
        // Glass rim: bright where the light hits (top-left), faint elsewhere.
        grid.Children.Add(new Ellipse
        {
            StrokeThickness = Math.Max(1, size / 30),
            Stroke = new LinearGradientBrush(Color.FromArgb(0xC0, 255, 255, 255), Color.FromArgb(0x25, 255, 255, 255), 45),
        });
        return grid;
    }

    /// <summary>
    /// Clara's face when no portrait file is present: a sleek white android in soft daylight (white helmet, dark visor with a
    /// blue glow, an ear lens, a mechanical neck with an orange wire), drawn from vector shapes in a 100x100 box.
    /// </summary>
    private static FrameworkElement BuildAndroidFace(double size)
    {
        // adult proportions: a long oval face with a defined jaw, narrow almond eyes, a slim neck, moody dusk light
        Brush Grad(Color a, Color b, double angle = 90) => new LinearGradientBrush(a, b, angle);
        Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
        var c = new Canvas { Width = 100, Height = 100, ClipToBounds = true };
        c.Children.Add(new Ellipse { Width = 100, Height = 100, Fill = new RadialGradientBrush(Hex("#EDE6DA"), Hex("#4E5A6A")) { GradientOrigin = new Point(0.3, 0.25), RadiusX = 0.85, RadiusY = 0.85 } });
        foreach (var (bx, by, br, ba) in new[] { (8.0, 16.0, 7.0, 0x30), (80.0, 10.0, 9.0, 0x22), (84.0, 58.0, 6.0, 0x28), (14.0, 62.0, 5.0, 0x22) })
            c.Children.Add(new Ellipse { Width = br * 2, Height = br * 2, Fill = new SolidColorBrush(Color.FromArgb((byte)ba, 0xFF, 0xC8, 0x90)), Margin = new Thickness(bx, by, 0, 0) });
        // shoulders and collar plates
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M 0,100 C 4,84 22,78 50,78 C 78,78 96,84 100,100 Z"), Fill = Grad(Hex("#E9EDF2"), Hex("#8E99A8")) });
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M 30,100 C 33,90 40,85 50,85 C 60,85 67,90 70,100"), Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0x25, 0x2B, 0x35)), StrokeThickness = 1.1 });
        // slim neck: dark mechanics, ribs, one orange wire
        c.Children.Add(new Rectangle { Width = 11, Height = 26, RadiusX = 3, RadiusY = 3, Fill = Grad(Hex("#3B4250"), Hex("#10131A")), Margin = new Thickness(44.5, 58, 0, 0) });
        foreach (var ry in new[] { 64.0, 68.0, 72.0, 76.0 })
            c.Children.Add(new Rectangle { Width = 11, Height = 1, Fill = new SolidColorBrush(Color.FromArgb(0x6A, 0x9A, 0xA4, 0xB4)), Margin = new Thickness(44.5, ry, 0, 0) });
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M 54,60 C 57,67 56,74 53,82"), Stroke = new SolidColorBrush(Hex("#FF9F3D")), StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        // face shell: long oval with a soft jaw, lit from the left
        const string faceData = "M 50,9 C 64,9 71,22 70,38 C 69,50 63,61 50,64 C 37,61 31,50 30,38 C 29,22 36,9 50,9 Z";
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(faceData), Fill = Grad(Hex("#FBFCFD"), Hex("#B4BDC9"), 20) });
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(faceData), Fill = Grad(Color.FromArgb(0x00, 0, 0, 0), Color.FromArgb(0x55, 0x1C, 0x24, 0x33), 0) }); // shade on the right side
        // helmet seam, temple plate, cheek line
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M 33,26 C 40,15 56,13 67,22"), Stroke = new SolidColorBrush(Color.FromArgb(0x6A, 0x5F, 0x69, 0x78)), StrokeThickness = 0.9 });
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M 36,52 C 41,57 46,59 50,59.5"), Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0x5F, 0x69, 0x78)), StrokeThickness = 0.8 });
        // narrow almond eyes with a small glowing iris
        foreach (var (ex, tilt) in new[] { (36.0, -7.0), (54.0, 7.0) })
        {
            c.Children.Add(new Ellipse { Width = 11, Height = 4.2, Fill = Grad(Hex("#222833"), Hex("#07090D")), RenderTransform = new RotateTransform(tilt, 5.5, 2.1), Margin = new Thickness(ex, 35, 0, 0) });
            c.Children.Add(new Ellipse { Width = 2.6, Height = 2.6, Fill = new RadialGradientBrush(Hex("#DFF4FF"), Hex("#3E9CFF")), Margin = new Thickness(ex + 4.2, 35.8, 0, 0) });
            c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse($"M {ex - 1},{32.5 + (tilt < 0 ? 1.2 : -0.2)} Q {ex + 5.5},{30.5} {ex + 12},{32.5 + (tilt < 0 ? -0.2 : 1.2)}"), Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0x53, 0x61)), StrokeThickness = 0.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        // slim nose and muted lips
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M 50.5,40 C 49,46 48.5,49 51.5,50"), Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0x5F, 0x69, 0x78)), StrokeThickness = 0.8, StrokeStartLineCap = PenLineCap.Round });
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M 45,56 C 48,57.6 53,57.6 56,56"), Stroke = new SolidColorBrush(Hex("#C98A8E")), StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        // headset lens on the right ear
        c.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Grad(Hex("#3B4250"), Hex("#0E1016")), Margin = new Thickness(66, 36, 0, 0) });
        c.Children.Add(new Ellipse { Width = 3.4, Height = 3.4, Fill = new RadialGradientBrush(Hex("#DFF4FF"), Hex("#3E9CFF")), Margin = new Thickness(68.3, 38.3, 0, 0) });
        // a thin rim light down the lit edge
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M 33,20 C 30,32 31,46 38,57"), Stroke = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)), StrokeThickness = 1.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });

        return new Viewbox { Width = size, Height = size, Child = c, Stretch = Stretch.Fill, Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2) };
    }

    /// <summary>Looks for a portrait the user dropped in as clara.png/.jpg (AppData first, then the app's Assets folder).</summary>
    private static BitmapImage? LoadPortrait()
    {
        var folders = new[]
        {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIsland"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets"),
        };
        foreach (var folder in folders)
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
            {
                var path = System.IO.Path.Combine(folder, "clara" + ext);
                if (!File.Exists(path)) continue;
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path);
                    image.DecodePixelWidth = 256;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
                catch (Exception) { /* unreadable: fall back to the built-in look */ }
            }
        return null;
    }

    /// <summary>Liquid Glass fill and rim for chat-box controls (same look as the App.xaml button styles).</summary>
    /// <summary>Clara never uses em or en dashes (a style rule the model sometimes slips on): swap them for a comma or a hyphen.</summary>
    internal static string NoDashes(string text) =>
        text.Replace(" — ", ", ").Replace("—", ", ").Replace(" – ", ", ").Replace("–", "-");

    internal static Brush GlassFillBrush() => new LinearGradientBrush(new GradientStopCollection
    {
        new GradientStop(Color.FromArgb(0x30, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0x14, 255, 255, 255), 0.55), new GradientStop(Color.FromArgb(0x1C, 255, 255, 255), 1),
    }, 90);

    internal static Brush GlassRimBrush() => new LinearGradientBrush(new GradientStopCollection
    {
        new GradientStop(Color.FromArgb(0xC8, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0x22, 255, 255, 255), 0.30), new GradientStop(Color.FromArgb(0x14, 255, 255, 255), 0.62), new GradientStop(Color.FromArgb(0x78, 255, 255, 255), 1),
    }, 45);

    internal static Border Chip(string glyph, string text, Action click)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph.Length > 0)
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, text.Length > 0 ? 6 : 0, 0) });
        if (text.Length > 0)
            row.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var normal = GlassFillBrush();
        var hover = B("#33FFFFFF");
        var chip = new Border
        {
            BorderBrush = GlassRimBrush(),
            BorderThickness = new Thickness(1),
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

    internal static void PopScale(UIElement element, double to)
    {
        if (element.RenderTransform is not ScaleTransform st) return;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Fade and rise into place.</summary>
    internal static void Appear(FrameworkElement element, double rise)
    {
        var move = new TranslateTransform(0, rise);
        element.RenderTransform = move;
        element.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }
}
