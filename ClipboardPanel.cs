// D-Island (personal fork) - the Clipboard page: everything you copied, with filters, search, expand, and one-click "ask Clara".

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

public sealed class ClipboardPanel : Grid
{
    private enum Filter { All, Images, Links, Text, Pinned }

    private const double CardWidth = 116;
    private const double CardHeight = 94;

    private readonly ClipboardHistory _history;
    private readonly WrapPanel _grid = new();
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText;
    private readonly TextBox _search;
    private readonly TextBlock _searchHint;
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _detail = new() { Visibility = Visibility.Collapsed };
    private readonly Border _detailBody = new();
    private readonly DispatcherTimer _rebuild = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Filter _filter = Filter.All;
    private ClipItem? _open;

    public ClipboardPanel(ClipboardHistory history)
    {
        _history = history;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ---- top row: search + filters + clear
        var top = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _search = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            FontSize = 12.5,
            Width = 38,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
        };
        _searchHint = new TextBlock { Text = "Search", FontSize = 12.5, Opacity = 0.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        var searchBox = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = AssistantPanel.B("#1AFFFFFF"),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "", FontFamily = AssistantPanel.IconFont, FontSize = 11, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) },
                    new Grid { Children = { _searchHint, _search } },
                },
            },
        };
        top.Children.Add(searchBox);

        foreach (var (f, label) in new[] { (Filter.All, "All"), (Filter.Images, "Images"), (Filter.Links, "Links"), (Filter.Text, "Text"), (Filter.Pinned, "") })
        {
            var chip = AssistantPanel.Chip(f == Filter.Pinned ? "" : "", label, () => { _filter = f; RefreshFilters(); QueueRebuild(); });
            if (f == Filter.Pinned) chip.ToolTip = "Pinned";
            chip.Margin = new Thickness(0, 0, 4, 0);
            chip.Tag = f;
            _filters.Children.Add(chip);
        }
        var filterScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _filters };
        SetColumn(filterScroll, 1);
        top.Children.Add(filterScroll);

        var clear = AssistantPanel.Chip("", "", () => { _history.Clear(keepPinned: true); });
        clear.ToolTip = "Clear history (pinned items stay)";
        // one-time hold: stop saving anything (screenshots too) until switched off again
        var pauseText = new TextBlock { Text = "", FontFamily = AssistantPanel.IconFont, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var pause = new Border
        {
            CornerRadius = new CornerRadius(13), Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand,
            Child = pauseText, ToolTip = "Stop saving everything you copy until you switch this off (this session only)",
            BorderBrush = AssistantPanel.GlassRimBrush(), BorderThickness = new Thickness(1),
        };
        void ShowPause()
        {
            // idle: just a pause icon (saves room); on hold: a bright "On hold" label so you can't forget it is on
            pauseText.FontFamily = _history.Paused ? new FontFamily("Segoe UI Variable Text, Segoe UI") : AssistantPanel.IconFont;
            pauseText.Text = _history.Paused ? "On hold" : "";
            pause.Background = _history.Paused ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)) : AssistantPanel.GlassFillBrush();
            pauseText.Foreground = _history.Paused ? new SolidColorBrush(Color.FromRgb(0x20, 0x14, 0x00)) : Brushes.White;
        }
        pause.MouseLeftButtonUp += (_, e) => { e.Handled = true; _history.Paused = !_history.Paused; };
        _history.PausedChanged += () => Dispatcher.InvokeAsync(ShowPause);
        ShowPause();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { pause, clear } };
        SetColumn(right, 2);
        top.Children.Add(right);
        Children.Add(top);

        // ---- grid of cards
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            MaxHeight = 232,
            MinHeight = 110,
            Content = _grid,
        };
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            if (_scroll.ScrollableHeight <= 0) return;
            _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - e.Delta);
            e.Handled = true; // scroll the cards; don't flip pages
        };

        _emptyText = new TextBlock { FontSize = 12, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        _empty = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "", FontFamily = AssistantPanel.IconFont, FontSize = 24, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Nothing here yet", FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) },
                _emptyText,
            },
        };

        // ---- expanded view (overlay)
        _detail.Children.Add(new Border { Background = AssistantPanel.B("#F01B1B20"), CornerRadius = new CornerRadius(14) });
        _detail.Children.Add(_detailBody);

        var content = new Grid();
        content.Children.Add(_scroll);
        content.Children.Add(_empty);
        content.Children.Add(_detail);
        SetRow(content, 1);
        Children.Add(content);

        _search.TextChanged += (_, _) =>
        {
            _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            QueueRebuild();
        };
        _search.PreviewMouseLeftButtonDown += (_, _) => BeginTypingRequested?.Invoke(_search);
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            _search.Clear();
            EndTypingRequested?.Invoke();
            e.Handled = true;
        };

        _rebuild.Tick += (_, _) => { _rebuild.Stop(); Rebuild(); };
        _history.Changed += QueueRebuild;
        RefreshFilters();
        Rebuild();
    }

    public event Action<TextBox>? BeginTypingRequested;
    public event Action? EndTypingRequested;
    /// <summary>The user wants Clara to look at this item (text or image).</summary>
    public event Action<ClipItem>? AskClaraRequested;

    private void QueueRebuild()
    {
        _rebuild.Stop();
        _rebuild.Start();
    }

    private void RefreshFilters()
    {
        foreach (Border chip in _filters.Children)
            chip.Background = (Filter)chip.Tag == _filter ? AssistantPanel.AccentGradient() : AssistantPanel.B("#1FFFFFFF");
    }

    private IEnumerable<ClipItem> Visible()
    {
        var query = _search.Text.Trim();
        return _history.Items.Where(i =>
            (_filter switch
            {
                Filter.Images => i.Kind == ClipKind.Image,
                Filter.Links => i.Kind == ClipKind.Link,
                Filter.Text => i.Kind is ClipKind.Text or ClipKind.Files,
                Filter.Pinned => i.Pinned,
                _ => true,
            })
            && (query.Length == 0 || i.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (query.Equals("image", StringComparison.OrdinalIgnoreCase) && i.Kind == ClipKind.Image)));
    }

    private void Rebuild()
    {
        if (_open is not null && !_history.Items.Contains(_open)) CloseDetail();
        var items = Visible().ToList();
        _grid.Children.Clear();
        var n = 0;
        foreach (var item in items)
        {
            var card = Card(item);
            _grid.Children.Add(card);
            if (n++ < 9) // stagger the first few; the rest just appear
            {
                card.Opacity = 0;
                card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(n * 30), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }
        _scroll.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        _emptyText.Text = _history.Items.Count == 0 ? "Copy something and it shows up here" : "No matches";
    }

    // ------------------------------------------------------------------ cards

    private Border Card(ClipItem item)
    {
        var face = new Grid();
        switch (item.Kind)
        {
            case ClipKind.Image:
                face.Children.Add(new Image { Source = _history.LoadImage(item, 260), Stretch = Stretch.UniformToFill });
                break;
            case ClipKind.Link:
            {
                Uri.TryCreate(item.Text.Trim(), UriKind.Absolute, out var uri);
                face.Children.Add(new StackPanel
                {
                    Margin = new Thickness(10, 9, 10, 0),
                    Children =
                    {
                        new TextBlock { Text = "", FontFamily = AssistantPanel.IconFont, FontSize = 13, Foreground = AssistantPanel.AccentGradient() },
                        new TextBlock { Text = uri?.Host.Replace("www.", "") ?? item.Text, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = item.Text, FontSize = 10.5, Opacity = 0.55, TextWrapping = TextWrapping.Wrap, MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                });
                break;
            }
            case ClipKind.Files:
                face.Children.Add(new StackPanel
                {
                    Margin = new Thickness(10, 9, 10, 0),
                    Children =
                    {
                        new TextBlock { Text = "", FontFamily = AssistantPanel.IconFont, FontSize = 14, Opacity = 0.9 },
                        new TextBlock { Text = string.Join("\n", item.Text.Split('\n').Take(3).Select(Path.GetFileName)), FontSize = 11.5, Margin = new Thickness(0, 5, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 52 },
                    },
                });
                break;
            default:
                face.Children.Add(new TextBlock
                {
                    Text = item.Text.Trim(),
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(10, 9, 10, 20),
                    LineHeight = 15,
                    Opacity = 0.92,
                });
                break;
        }

        // caption strip
        var caption = new TextBlock
        {
            Text = $"{KindLabel(item)} · {Ago(item.Time)}",
            FontSize = 10,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(9, 0, 0, 6),
        };
        face.Children.Add(new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 26,
            Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xB0, 0, 0, 0), 90),
        });
        face.Children.Add(caption);
        if (item.Pinned)
            face.Children.Add(new TextBlock { Text = "", FontFamily = AssistantPanel.IconFont, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 8, 0), Foreground = AssistantPanel.B("#FFFFD60A") });

        // hover actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Tool("", "Copy", () => CopyBack(item)));
        actions.Children.Add(Tool("", "Ask Clara", () => AskClaraRequested?.Invoke(item)));
        actions.Children.Add(ToolText("AI", "Ask Google AI Mode", () => OpenAiMode(item, card0: null)));
        actions.Children.Add(Tool(item.Pinned ? "" : "", item.Pinned ? "Unpin" : "Pin", () => _history.SetPinned(item, !item.Pinned)));
        actions.Children.Add(Tool("", "Delete", () => _history.Delete(item)));
        var scrim = new Border { Background = AssistantPanel.B("#B8101014"), Opacity = 0, Child = actions };
        face.Children.Add(scrim);

        var card = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            CornerRadius = new CornerRadius(12),
            Background = AssistantPanel.B("#26FFFFFF"),
            BorderBrush = AssistantPanel.B("#14FFFFFF"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 6),
            ClipToBounds = true,
            Cursor = Cursors.Hand,
            Child = face,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        card.MouseEnter += (_, _) =>
        {
            scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            AssistantPanel.PopScale(card, 1.025);
        };
        card.MouseLeave += (_, _) =>
        {
            scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            AssistantPanel.PopScale(card, 1);
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount >= 2) { OpenDetail(item); return; } // double-click expands
            CopyBack(item);
            Flash(card, "Copied");
        };
        return card;
    }

    private static Border ToolText(string text, string tip, Action click)
    {
        var tool = Tool("", tip, click);
        tool.Child = new TextBlock { Text = text, FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return tool;
    }

    private void OpenAiMode(ClipItem item, Border? card0)
    {
        var message = GoogleAiMode.Open(item, _history);
        AiModeUsed?.Invoke(message);
    }

    /// <summary>A short status line after handing something to Google AI Mode.</summary>
    public event Action<string>? AiModeUsed;

    private static Border Tool(string glyph, string tip, Action click)
    {
        var tool = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Brushes.Transparent,
            Margin = new Thickness(1, 0, 1, 0),
            ToolTip = tip,
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = glyph, FontFamily = AssistantPanel.IconFont, FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        tool.MouseEnter += (_, _) => tool.Background = AssistantPanel.B("#40FFFFFF");
        tool.MouseLeave += (_, _) => tool.Background = Brushes.Transparent;
        tool.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return tool;
    }

    private static void Flash(Border card, string text)
    {
        if (card.Child is not Grid face) return;
        var toast = new Border
        {
            Background = AssistantPanel.AccentGradient(),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    // the tick is an icon-font glyph; the word must use the normal font (in the icon font it showed as boxes)
                    new TextBlock { Text = "", FontFamily = AssistantPanel.IconFont, FontSize = 11, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) },
                    new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            Opacity = 0,
        };
        face.Children.Add(toast);
        // pop in, hold, fade out
        var hold = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
        hold.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.15)));
        hold.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.75)));
        hold.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        hold.Completed += (_, _) => face.Children.Remove(toast);
        toast.BeginAnimation(OpacityProperty, hold);
    }

    // ------------------------------------------------------------------ expanded view

    private void OpenDetail(ClipItem item)
    {
        _open = item;
        var body = new Grid { Margin = new Thickness(12, 10, 12, 10) };
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        FrameworkElement view;
        if (item.Kind == ClipKind.Image)
        {
            view = new Image { Source = _history.LoadImage(item), Stretch = Stretch.Uniform, MaxHeight = 170 };
        }
        else
        {
            var text = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, LineHeight = 17 };
            if (item.Kind == ClipKind.Link) text.Foreground = AssistantPanel.B("#FF64D2FF");
            view = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, MaxHeight = 170, Content = text };
            view.PreviewMouseWheel += (s, e) =>
            {
                var sv = (ScrollViewer)s;
                if (sv.ScrollableHeight <= 0) return;
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
                e.Handled = true;
            };
        }
        body.Children.Add(view);

        var bar = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Add(string glyph, string label, Action act)
        {
            var chip = AssistantPanel.Chip(glyph, label, act);
            chip.Margin = new Thickness(0, 0, 5, 4);
            bar.Children.Add(chip);
        }
        Add("", "Back", CloseDetail);
        Add("", "Copy", () => CopyBack(item));
        Add("", "Ask Clara", () => AskClaraRequested?.Invoke(item));
        Add("✦", "Google AI Mode", () => OpenAiMode(item, null));
        if (item.Kind == ClipKind.Image) Add("", "Search image", () => SearchImage(item));
        else if (item.Kind == ClipKind.Link) Add("", "Open", () => OpenUrl(item.Text.Trim()));
        else if (item.Kind == ClipKind.Text) Add("", "Search web", () => OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(item.Text.Trim()[..Math.Min(200, item.Text.Trim().Length)])));
        Add(item.Pinned ? "" : "", item.Pinned ? "Unpin" : "Pin", () => { _history.SetPinned(item, !item.Pinned); OpenDetail(item); });
        Add("", "Delete", () => _history.Delete(item));
        Grid.SetRow(bar, 1);
        body.Children.Add(bar);

        _detailBody.Child = body;
        _detail.Visibility = Visibility.Visible;
        AssistantPanel.Appear(_detail, 8);
    }

    private void CloseDetail()
    {
        _open = null;
        _detail.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ actions

    private void CopyBack(ClipItem item)
    {
        try
        {
            switch (item.Kind)
            {
                case ClipKind.Image:
                    if (_history.LoadImage(item) is { } image) Clipboard.SetImage(image);
                    break;
                case ClipKind.Files:
                    var files = new System.Collections.Specialized.StringCollection();
                    files.AddRange(item.Text.Split('\n'));
                    Clipboard.SetFileDropList(files);
                    break;
                default:
                    Clipboard.SetText(item.Text);
                    break;
            }
        }
        catch (Exception) { /* clipboard busy */ }
    }

    /// <summary>Google Lens takes pasted images: the picture is put on the clipboard and Lens opens, ready for Ctrl+V.</summary>
    private void SearchImage(ClipItem item)
    {
        CopyBack(item);
        OpenUrl("https://lens.google.com/");
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception) { }
    }

    private static string KindLabel(ClipItem item) => item.Kind switch
    {
        ClipKind.Image => "Image",
        ClipKind.Link => "Link",
        ClipKind.Files => "Files",
        _ => "Text",
    };

    private static string Ago(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalSeconds < 45) return "now";
        if (span.TotalMinutes < 60) return $"{(int)Math.Max(1, span.TotalMinutes)}m";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
        return span.TotalDays < 2 ? "yesterday" : $"{(int)span.TotalDays}d";
    }
}
