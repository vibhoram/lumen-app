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
    // Neon cyberpunk: cool cyan into electric violet (matches Clara's portrait glow).
    private static readonly Color AccentA = Color.FromRgb(0x5E, 0xE7, 0xFF);
    private static readonly Color AccentB = Color.FromRgb(0xB0, 0x5C, 0xFF);

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

    /// <summary>Only while a reply is streaming — so you can look away and still see it land. Idle Clara collapses like any other page.</summary>
    public bool KeepOpen => _busy;

    /// <summary>Bumps activity (hotkey / send). Does not pin the island open anymore.</summary>
    public void KeepAlive() => _lastActivity = DateTime.UtcNow;

    /// <summary>Clara chat well — roomy enough for a turn + reply; grows for long answers, shrinks back for short ones.</summary>
    private const double ChatMinHeight = 220;
    private const double ChatMaxHeight = 360;

    /// <summary>Island should remeasure when a reply grows the chat.</summary>
    public event Action? LayoutChanged;

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
        _status.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = _engineLocal ? B("#39FF9A") : B("#C77DFF"), FontSize = 9 });
        _status.Inlines.Add(new System.Windows.Documents.Run(_engineLocal ? $"local · {_engineName}" : $"cloud · {_engineName}"));
    }

    /// <summary>Works out which brain the next plain-text message will use and shows it.</summary>
    private async Task RefreshEngineAsync()
    {
        string? local = _settings.AssistantUseLocalAi ? await LocalAi.PickModelAsync(_settings) : null;
        if (_busy) return;
        if (local is not null) SetEngine(true, local.Split(':')[0]);
        else SetEngine(false, PreferGemini() ? "gemini" : "groq");
    }

    /// <summary>Gemini is the cloud main; Groq only when there's no Gemini key (or as a mid-reply failsafe).</summary>
    private bool PreferGemini() => !string.IsNullOrWhiteSpace(_settings.GeminiApiKey);
    private bool _focused;
    private string? _pendingText;

    public AssistantPanel(AppSettings settings)
    {
        _settings = settings;
        // Auto rows only — Star was eating leftover island height and left a dead empty well under the input.
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
            Height = ChatMinHeight,
            Content = _messages,
        };
        var slim = new SlimScroller(_scroll);
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            slim.Wheel(e.Delta);
            e.Handled = true; // never flips the page while you read
            // After the scroller moves, size the well to whatever message is in view.
            Dispatcher.BeginInvoke(() => { FitChatHeight(allowShrink: true); LayoutChanged?.Invoke(); }, DispatcherPriority.Background);
        };
        _scroll.ScrollChanged += (_, e) =>
        {
            if (_busy || _fitting || e.ExtentHeightChange != 0 && e.VerticalChange == 0) return;
            if (Math.Abs(e.VerticalChange) < 0.5) return; // ignore height-only refits
            FitChatHeight(allowShrink: true);
            LayoutChanged?.Invoke();
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
        // Tiny thumb + X by default; label only on hover so "Screen attached" doesn't eat chat space.
        _chipImage = new Image { Width = 36, Height = 22, Stretch = Stretch.UniformToFill };
        _chipThumb = new Border { CornerRadius = new CornerRadius(6), Child = _chipImage, ClipToBounds = true };
        _chipText = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), Opacity = 0.85, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
        var remove = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Opacity = 0.65, Margin = new Thickness(2, 0, 2, 0) };
        remove.MouseLeftButtonUp += (_, e) => { _pending = null; _pendingText = null; UpdateChip(); e.Handled = true; };
        _chip = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = GlassFillBrush(),
            BorderBrush = GlassRimBrush(),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
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
        _chip.MouseEnter += (_, _) => { if (_chip.Visibility == Visibility.Visible) _chipText.Visibility = Visibility.Visible; };
        _chip.MouseLeave += (_, _) => _chipText.Visibility = Visibility.Collapsed;
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

    /// <summary>Attaches a screenshot to the next message.</summary>
    public void AttachImage(BitmapSource? image)
    {
        if (image is null) return;
        _pending = image;
        KeepAlive();
        UpdateChip();
    }

    /// <summary>Attaches selected / clipboard text to the next message.</summary>
    public void AttachText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _pendingText = text.Trim();
        KeepAlive();
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
        _pendingText = null;
        UpdateChip();
        ShowEmpty(true);
        FitChatHeight(allowShrink: true);
        LayoutChanged?.Invoke();
    }

    /// <summary>Drops the pending screen/text chip (e.g. Clara hotkey toggled closed).</summary>
    public void ClearAttachment()
    {
        _pending = null;
        _pendingText = null;
        UpdateChip();
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
        // Compact by default — full label only on hover (the long "Screen attached" ate chat height).
        var full = (_pending, _pendingText) switch
        {
            (not null, not null) => "Screen + text",
            (not null, null) => "Screen",
            _ => "\u201C" + (_pendingText!.Length > 42 ? _pendingText[..42] + "…" : _pendingText).Replace('\n', ' ') + "\u201D",
        };
        _chipText.Text = full;
        _chipText.Visibility = _chip.IsMouseOver ? Visibility.Visible : Visibility.Collapsed;
        _chip.ToolTip = full;
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
            // No cloud key is fine for plain text if an offline model is available; pictures need Gemini/Groq.
            var hasCloud = PreferGemini() || !string.IsNullOrWhiteSpace(_settings.GroqApiKey);
            if (!hasCloud
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

        // Text-only can stay offline (Ollama). Pictures need the cloud. Cloud main = Gemini; Groq is failsafe only.
        var cloudNeeded = image is not null;
        var localModel = !cloudNeeded && _settings.AssistantUseLocalAi ? await LocalAi.PickModelAsync(_settings) : null;
        var hasCloud = PreferGemini() || !string.IsNullOrWhiteSpace(_settings.GroqApiKey);
        if (localModel is null && !hasCloud) { ShowNoKey(); SetBusy(false); return; }
        var useLocal = localModel is not null;
        var useGemini = PreferGemini();
        string? cKey = null;
        string? cUrl = useLocal ? LocalAi.ChatEndpoint : null;
        var engineModel = useLocal ? localModel! : "";
        void ApplyCloud(bool gemini)
        {
            useGemini = gemini;
            cKey = gemini ? _settings.GeminiApiKey : _settings.GroqApiKey;
            engineModel = gemini
                ? (string.IsNullOrWhiteSpace(_settings.GeminiModel) ? "gemini-2.5-flash" : _settings.GeminiModel)
                : _settings.GroqModel;
            cUrl = gemini ? AppSettings.GeminiEndpoint : null;
            SetEngine(false, gemini ? "gemini" : "groq");
        }
        if (useLocal) SetEngine(true, localModel!.Split(':')[0]);
        else ApplyCloud(useGemini);
        var backupUsed = false;

        // offline → Gemini → Groq. Groq is the last resort, never the first pick when Gemini has a key.
        bool SwitchToBackup()
        {
            if (useLocal)
            {
                if (!hasCloud) return false;
                useLocal = false;
                cUrl = null;
                ApplyCloud(PreferGemini());
                return true;
            }
            if (backupUsed) return false;
            // Failsafe: if we were on Gemini, try Groq; if we somehow started on Groq (no Gemini key), try Gemini.
            var toGemini = !useGemini;
            var key = toGemini ? _settings.GeminiApiKey : _settings.GroqApiKey;
            if (string.IsNullOrWhiteSpace(key)) return false;
            backupUsed = true;
            ApplyCloud(toGemini);
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

                // Local models and Gemini often dump fake tool XML / print(system_status()) as text.
                // Turn those into real calls and scrub the garbage out of the bubble.
                if (calls is null)
                {
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
                    else if (System.Text.RegularExpressions.Regex.IsMatch(turn.ToString(),
                                 @"</?(?:execute_tool|tool_code|tool_call|function_call)\b",
                                 System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        // Pure junk loop with no recoverable tool — wipe it so the user doesn't see the spam.
                        reply.Length = Math.Max(0, reply.Length - turn.Length);
                        turn.Clear();
                        bubble.SetText(NoDashes(reply.ToString()));
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
            var brain = _engineName.Length > 0 ? _engineName : (PreferGemini() ? "gemini" : "groq");
            var message = ex switch
            {
                GroqException { Status: 401 } => $"{brain} rejected the API key. Check it in Settings > Clara.",
                GroqException { Status: 429 } => $"I've hit {brain}'s rate limit. Give it a few seconds, then retry.",
                GroqException g => g.Message,
                HttpRequestException => $"I can't reach {brain}. Check your connection.",
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
        FitChatHeight(allowShrink: !_busy);
        LayoutChanged?.Invoke();
        var target = _scroll.ScrollableHeight;
        if (Math.Abs(target - _scroll.VerticalOffset) < 1) return;
        _scroll.SetValue(OffsetProperty, _scroll.VerticalOffset);
        _scroll.BeginAnimation(OffsetProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }, DispatcherPriority.Background);

    /// <summary>
    /// Sizes the chat well to what's on screen: latest turn while at the bottom, or the message under the
    /// scroll viewport when you roll back. While streaming, only grows (no shrink jitter).
    /// </summary>
    private bool _fitting;

    private void FitChatHeight(bool allowShrink = true)
    {
        if (_fitting) return;
        _fitting = true;
        try
        {
        _messages.UpdateLayout();
        var width = _scroll.ActualWidth > 0 ? _scroll.ActualWidth : 360;
        double need;
        if (_messages.Children.Count == 0)
        {
            need = ChatMinHeight;
        }
        else
        {
            // Prefer the tallest message currently overlapping the viewport (scroll-back expands for that bubble).
            var viewTop = _scroll.VerticalOffset;
            var viewBot = viewTop + Math.Max(_scroll.Height, 1);
            var atBottom = _scroll.ScrollableHeight <= 1 || viewTop >= _scroll.ScrollableHeight - 12;
            need = ChatMinHeight;
            double y = 0;
            FrameworkElement? last = null;
            FrameworkElement? prev = null;
            foreach (var child in _messages.Children)
            {
                if (child is not FrameworkElement el) continue;
                el.Measure(new Size(width, double.PositiveInfinity));
                var h = el.DesiredSize.Height;
                var top = y;
                var bot = y + h;
                y = bot;
                prev = last;
                last = el;
                if (bot > viewTop + 2 && top < viewBot - 2)
                    need = Math.Max(need, Math.Min(h + 8, ChatMaxHeight));
            }
            // Pinned to the bottom (live reply): fit the latest turn (+ user bubble above when short).
            if (atBottom && last is not null)
            {
                last.Measure(new Size(width, double.PositiveInfinity));
                var live = last.DesiredSize.Height;
                if (prev is not null)
                {
                    prev.Measure(new Size(width, double.PositiveInfinity));
                    live += prev.DesiredSize.Height;
                }
                need = Math.Clamp(live + 8, ChatMinHeight, ChatMaxHeight);
            }
            need = Math.Clamp(need, ChatMinHeight, ChatMaxHeight);
        }
        if (!allowShrink && need < _scroll.Height) need = _scroll.Height;
        if (Math.Abs(_scroll.Height - need) >= 1)
            _scroll.Height = need;
        }
        finally { _fitting = false; }
    }

    /// <summary>Called from a streaming bubble so the island grows as tokens arrive.</summary>
    internal void RefitWhileStreaming()
    {
        FitChatHeight(allowShrink: false);
        LayoutChanged?.Invoke();
        if (_scroll.ScrollableHeight > 0)
            _scroll.ScrollToVerticalOffset(_scroll.ScrollableHeight);
    }

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
                new TextBlock { Text = "Hey, I'm Clara.", FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) },
                new TextBlock { Text = greeting, FontSize = 12, Opacity = 0.7, Foreground = B("#B8F0FF"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) },
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
                Padding = new Thickness(14, 10, 14, 10),
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 330,
                Child = _host,
            };
            _bubble.SetValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI Variable Text, Segoe UI, Segoe UI Emoji, Segoe UI Symbol")); // emoji fall back to a font that has them
            // Discord-style: circular pfp left of the bubble (big enough to read her face).
            var avatar = MakeAvatar(32);
            avatar.VerticalAlignment = VerticalAlignment.Top;
            avatar.Margin = new Thickness(0, 0, 10, 0);
            var row = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 10, 10) };
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
                _owner.RefitWhileStreaming(); // grow the island live as tokens land
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
                _owner.RefitWhileStreaming();
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
            _owner.LayoutChanged?.Invoke();
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
            var afterList = false;
            void Flush()
            {
                if (paragraph.Count == 0) return;
                var tb = Inline(string.Join("\n", paragraph), 0);
                // breathing room between blocks so lists don't glue to the paragraph above
                if (afterList) tb.Margin = new Thickness(0, 8, 0, 6);
                else tb.Margin = new Thickness(0, 0, 0, 8);
                _body.Children.Add(tb);
                paragraph.Clear();
                afterList = false;
            }
            foreach (var raw in prose.Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.Length == 0) { Flush(); continue; }
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    Flush();
                    _body.Children.Add(Bullet(trimmed[2..]));
                    afterList = true;
                }
                else if (Regex.Match(trimmed, @"^(\d+\.)\s+(.*)$") is { Success: true } num)
                {
                    Flush();
                    _body.Children.Add(Bullet(num.Groups[2].Value, num.Groups[1].Value));
                    afterList = true;
                }
                else if (trimmed.StartsWith('#'))
                {
                    Flush();
                    var tb = Inline(trimmed.TrimStart('#').Trim(), 0);
                    tb.FontWeight = FontWeights.SemiBold;
                    tb.FontSize = 14.5;
                    tb.LineHeight = 22;
                    tb.Margin = new Thickness(0, afterList ? 10 : 4, 0, 6);
                    _body.Children.Add(tb);
                    afterList = false;
                }
                else paragraph.Add(line);
            }
            Flush();
        }

        private static FrameworkElement Bullet(string text, string? marker = null)
        {
            var row = new Grid { Margin = new Thickness(2, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var mark = new TextBlock
            {
                Text = marker ?? "•",
                FontSize = 13,
                Foreground = B("#CCFFFFFF"),
                Width = 16,
                Margin = new Thickness(0, 1, 4, 0),
                LineHeight = 20,
            };
            var body = Inline(text, 0);
            body.Margin = new Thickness(0);
            body.LineHeight = 20;
            SetColumn(mark, 0);
            SetColumn(body, 1);
            row.Children.Add(mark);
            row.Children.Add(body);
            return row;
        }

        private static TextBlock Inline(string text, double indent)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13.2,
                Foreground = B("#F2FFFFFF"),
                Margin = new Thickness(indent, 0, 0, 6),
                LineHeight = 20,
            };
            foreach (var part in Regex.Split(text, @"(\*\*.+?\*\*|`[^`]+`|https?://[^\s<>\]]+)"))
            {
                if (part.Length == 0) continue;
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                    AddWithEmoji(tb, part[2..^2], FontWeights.SemiBold);
                else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                    tb.Inlines.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), Background = B("#40000000"), FontSize = 12 });
                else if (part.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || part.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var url = part.TrimEnd('.', ',', ')', ']');
                    try
                    {
                        var link = new Hyperlink(new Run(ShortUrl(url)))
                        {
                            NavigateUri = new Uri(url),
                            Foreground = new SolidColorBrush(AccentA),
                            TextDecorations = null,
                            ToolTip = url,
                        };
                        link.RequestNavigate += (_, e) =>
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                            catch (Exception) { }
                            e.Handled = true;
                        };
                        tb.Inlines.Add(link);
                    }
                    catch (Exception) { AddWithEmoji(tb, part, null); }
                }
                else AddWithEmoji(tb, part, null);
            }
            return tb;
        }

        private static string ShortUrl(string url)
        {
            try
            {
                var u = new Uri(url);
                var host = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
                var path = u.AbsolutePath is "/" or "" ? "" : (u.AbsolutePath.Length > 22 ? u.AbsolutePath[..20] + "…" : u.AbsolutePath);
                return host + path;
            }
            catch (Exception) { return url.Length > 36 ? url[..34] + "…" : url; }
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

    /// <summary>Clara's avatar: her cyberpunk portrait if on disk, otherwise a neon android face.</summary>
    private static FrameworkElement MakeAvatar(double size)
    {
        var portrait = LoadPortrait();
        var grid = new Grid
        {
            Width = size,
            Height = size,
            ClipToBounds = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            // Soft magenta bloom — lighter on tiny chat pfps so the face stays sharp.
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0xC0, 0x6B, 0xFF),
                BlurRadius = size < 40 ? size * 0.35 : size * 0.55,
                ShadowDepth = 0,
                Opacity = size < 40 ? 0.35 : 0.45,
            },
        };
        if (portrait is not null)
        {
            // Face-forward crop: UniformToFill + top align keeps eyes in the circle.
            var face = new ImageBrush(portrait)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Top,
            };
            face.Freeze();
            grid.Children.Add(new Ellipse { Fill = face });
        }
        else
        {
            grid.Children.Add(BuildAndroidFace(size));
        }
        // Neon glass rim: cyan highlight → violet shadow (reads clearly at chat-pfp size).
        grid.Children.Add(new Ellipse
        {
            IsHitTestVisible = false,
            StrokeThickness = Math.Max(1.4, size / 18),
            Stroke = new LinearGradientBrush(
                Color.FromArgb(0xF0, 0x7A, 0xF0, 0xFF),
                Color.FromArgb(0xA0, 0xB0, 0x5C, 0xFF), 45),
        });
        return grid;
    }

    /// <summary>
    /// Fallback when no clara.png is present: dark-chrome android, green neck rings, purple temple LED, cyan eyes.
    /// </summary>
    private static FrameworkElement BuildAndroidFace(double size)
    {
        Brush Grad(Color a, Color b, double angle = 90) => new LinearGradientBrush(a, b, angle);
        Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
        var c = new Canvas { Width = 100, Height = 100, ClipToBounds = true };
        // night city bokeh disc
        c.Children.Add(new Ellipse
        {
            Width = 100, Height = 100,
            Fill = new RadialGradientBrush(Hex("#2A1A3A"), Hex("#07060C"))
            { GradientOrigin = new Point(0.35, 0.3), RadiusX = 0.9, RadiusY = 0.9 },
        });
        foreach (var (bx, by, br, col) in new[] { (10.0, 14.0, 6.0, "#66FF4FD8"), (78.0, 18.0, 8.0, "#44FFE66A"), (82.0, 62.0, 5.0, "#5539FF9A"), (12.0, 68.0, 5.0, "#407AEAFF") })
            c.Children.Add(new Ellipse { Width = br * 2, Height = br * 2, Fill = new SolidColorBrush(Hex(col)), Margin = new Thickness(bx, by, 0, 0) });
        // black chrome shoulders
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 0,100 C 6,82 24,74 50,74 C 76,74 94,82 100,100 Z"),
            Fill = Grad(Hex("#2A2E38"), Hex("#0A0B10")),
        });
        // mechanical neck with neon green rings
        c.Children.Add(new Rectangle { Width = 14, Height = 28, RadiusX = 3, RadiusY = 3, Fill = Grad(Hex("#1A1E28"), Hex("#050608")), Margin = new Thickness(43, 56, 0, 0) });
        foreach (var ry in new[] { 60.0, 65.0, 70.0, 75.0 })
            c.Children.Add(new Rectangle { Width = 14, Height = 1.6, Fill = new SolidColorBrush(Hex("#39FF9A")), Opacity = 0.85, Margin = new Thickness(43, ry, 0, 0) });
        // pale face shell
        const string faceData = "M 50,10 C 64,10 71,22 70,38 C 69,50 63,60 50,63 C 37,60 31,50 30,38 C 29,22 36,10 50,10 Z";
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(faceData), Fill = Grad(Hex("#F2E8E4"), Hex("#A8989A"), 25) });
        c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(faceData), Fill = Grad(Color.FromArgb(0x00, 0, 0, 0), Color.FromArgb(0x50, 0x10, 0x08, 0x18), 0) });
        // dark hair fringe
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 30,28 C 34,12 48,6 62,10 C 72,14 74,24 72,34 C 60,22 48,18 36,28 Z"),
            Fill = Grad(Hex("#1A1218"), Hex("#050308")),
        });
        // scalp implants / wires
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 38,16 C 42,12 50,11 58,14"),
            Stroke = new SolidColorBrush(Hex("#8A93A0")), StrokeThickness = 1.1, StrokeStartLineCap = PenLineCap.Round,
        });
        // purple LED grid at temple
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                c.Children.Add(new Ellipse
                {
                    Width = 1.6, Height = 1.6,
                    Fill = new SolidColorBrush(Hex("#C77DFF")),
                    Margin = new Thickness(66 + j * 2.4, 28 + i * 2.4, 0, 0),
                });
        // cyan glowing eyes
        foreach (var (ex, tilt) in new[] { (35.5, -6.0), (53.5, 6.0) })
        {
            c.Children.Add(new Ellipse { Width = 12, Height = 5, Fill = Grad(Hex("#0A1020"), Hex("#020408")), RenderTransform = new RotateTransform(tilt, 6, 2.5), Margin = new Thickness(ex, 34, 0, 0) });
            c.Children.Add(new Ellipse { Width = 3.2, Height = 3.2, Fill = new RadialGradientBrush(Hex("#E8FFFF"), Hex("#3EC8FF")), Margin = new Thickness(ex + 4.4, 34.8, 0, 0) });
        }
        // soft lips
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 44,55 C 48,57.2 53,57.2 57,55"),
            Stroke = new SolidColorBrush(Hex("#C97A86")), StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        // jaw mic / sensor
        c.Children.Add(new Ellipse { Width = 3.2, Height = 3.2, Fill = new SolidColorBrush(Hex("#FFE66A")), Margin = new Thickness(68, 48, 0, 0) });
        // rim light
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 32,18 C 28,32 30,48 38,58"),
            Stroke = new SolidColorBrush(Color.FromArgb(0xA0, 0x7A, 0xF0, 0xFF)),
            StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });

        return new Viewbox { Width = size, Height = size, Child = c, Stretch = Stretch.Fill, Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2) };
    }

    private static BitmapImage? _portraitCache;
    private static bool _portraitTried;

    /// <summary>Looks for a portrait the user dropped in as clara.png/.jpg (AppData first, then the app's Assets folder).</summary>
    private static BitmapImage? LoadPortrait()
    {
        if (_portraitTried) return _portraitCache;
        _portraitTried = true;
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
                    return _portraitCache = image;
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
