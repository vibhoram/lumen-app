// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// What changed in each version, shown once after D-Island updates (automatically or by running a
/// newer installer) and from Settings. Add an entry here for every release.
/// </summary>
public static class WhatsNew
{
    public sealed record Item(string Glyph, string Color, string Title, string Text);

    public sealed record Release(Version Version, IReadOnlyList<Item> Items);

    public static readonly IReadOnlyList<Release> Releases = new[]
    {
        new Release(new Version(1, 3, 0), new[]
        {
            new Item("", "#FF64D2FF", "Matching glass bubbles",
                "Weather and carry bubbles now share the same live Liquid Glass as the pill."),
            new Item("", "#FFC77DFF", "Clara · Gemini first",
                "Gemini is the main cloud brain; Groq only steps in as a failsafe. Chat has more room, and " +
                "Clara's portrait sits beside each reply."),
            new Item("", "#FF30D158", "Snappier Settings",
                "Settings stays warm in memory so opening it from the island doesn't hitch the collapse."),
            new Item("", "#FFFF9F0A", "Cleaner tool replies",
                "Gemini's fake tool XML spam is stripped; real tools still run."),
        }),
        new Release(new Version(1, 2, 1), new[]
        {
            new Item("", "#FF30D158", "Updates install by themselves",
                "A downloaded update now installs within seconds of the island being free, instead of " +
                "waiting up to 10 minutes. Settings > Updates shows what it's waiting for."),
        }),
        new Release(new Version(1, 2, 0), new[]
        {
            new Item("", "#FF30D158", "To-Do list",
                "A new page for quick tasks. Type, press Enter, tick them off. Saved on this PC."),
            new Item("", "#FFFFD60A", "Notes",
                "A scratch pad in the island that saves as you type. Press Esc to give the keyboard back."),
            new Item("", "#FF64D2FF", "Smoother animations",
                "Switching between Dynamic Island and notch now morphs, and hiding or showing the island " +
                "shrinks it into the top edge and back."),
            new Item("", "#FF5AC8FA", "Your town in Weather",
                "With \"My location\", the weather page now shows where you are instead of \"My location\"."),
            new Item("", "#FFFF9F0A", "Hide shortcut works everywhere",
                "If another app already uses the shortcut, D-Island still responds to it, and Settings tells you."),
            new Item("", "#FFBF5AF2", "What's new",
                "This window, after every update. Find it again in Settings > Updates."),
        }),
    };

    /// <summary>Releases newer than <paramref name="since"/> up to this version, newest first.</summary>
    public static IReadOnlyList<Release> Since(Version? since) =>
        Releases.Where(r => (since is null || r.Version > since) && r.Version <= UpdateService.CurrentVersion)
            .OrderByDescending(r => r.Version)
            .ToList();
}

/// <summary>A Lumen-branded card (purple mark, violet glass) — not the old black D-Island pill.</summary>
public sealed class WhatsNewWindow : Window
{
    private static readonly Brush Secondary = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Color Violet = Color.FromRgb(0xBF, 0x5A, 0xF2);
    private static readonly Color VioletDeep = Color.FromRgb(0x5B, 0x2A, 0x9E);
    private static readonly Color CyanKnob = Color.FromRgb(0x4A, 0xC1, 0xF7);

    public WhatsNewWindow(IReadOnlyList<WhatsNew.Release> releases, Version? updatedFrom)
    {
        Title = "What's new in Lumen";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        try { Icon = BitmapFrame.Create(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Lumen.ico"))); }
        catch (Exception) { Icon = null; }

        var content = new StackPanel { Margin = new Thickness(28, 26, 28, 24) };

        // Lumen mark: purple rounded tile + glass toggle (or the real PNG when present).
        var mark = BuildLumenMark();
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        mark.Margin = new Thickness(0, 0, 0, 2);
        content.Children.Add(mark);

        var current = UpdateService.CurrentVersion;
        content.Children.Add(new TextBlock
        {
            Text = updatedFrom is null ? "What's new in Lumen" : "Lumen is up to date",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = updatedFrom is null ? $"Version {current}" : $"Updated from {updatedFrom} to {current}. Here's what's new:",
            Foreground = Secondary,
            Margin = new Thickness(0, 4, 0, 10),
        });

        var list = new StackPanel();
        foreach (var release in releases)
        {
            if (releases.Count > 1)
                list.Children.Add(new TextBlock
                {
                    Text = $"VERSION {release.Version}",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Secondary,
                    Margin = new Thickness(0, 12, 0, 0),
                });
            foreach (var item in release.Items) list.Children.Add(ItemRow(item));
        }
        content.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 420,
            Style = (Style)Application.Current.FindResource("SlimScroll"),
            Focusable = false,
        });

        var notes = new Button { Style = (Style)Application.Current.FindResource("ChipButton"), Content = "Release notes", Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        notes.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    $"https://github.com/{UpdateService.Repository}/releases/tag/v{current}") { UseShellExecute = true });
            }
            catch (Exception) { /* no browser */ }
        };
        var okFill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x9B, 0x4D, 0xFF), 0),
                new GradientStop(Color.FromRgb(0x5E, 0x5C, 0xE6), 0.55),
                new GradientStop(CyanKnob, 1),
            },
        };
        okFill.Freeze();
        var ok = new Button
        {
            Style = (Style)Application.Current.FindResource("ChipButton"),
            Content = "Got it",
            Padding = new Thickness(24, 8, 24, 8),
            Background = okFill,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ok.Click += (_, _) => Close();
        var buttons = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(notes);
        buttons.Children.Add(ok);
        content.Children.Add(buttons);

        var cardBg = new LinearGradientBrush
        {
            StartPoint = new Point(0.15, 0), EndPoint = new Point(0.9, 1),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x22, 0x18, 0x30), 0),
                new GradientStop(Color.FromRgb(0x16, 0x14, 0x1E), 0.45),
                new GradientStop(Color.FromRgb(0x12, 0x12, 0x18), 1),
            },
        };
        cardBg.Freeze();
        var card = new Border
        {
            CornerRadius = new CornerRadius(24),
            Background = cardBg,
            BorderBrush = Frozen(Color.FromArgb(0x55, 0xBF, 0x5A, 0xF2)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(18),
            Effect = new DropShadowEffect
            {
                Color = Violet, BlurRadius = 36, ShadowDepth = 0, Opacity = 0.55,
            },
            Child = content,
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        Content = card;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        // Arrive with a soft float + scale (Lumen, not a hard pop).
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        var scale = new ScaleTransform(0.92, 0.92);
        var slide = new TranslateTransform(0, 14);
        group.Children.Add(scale);
        group.Children.Add(slide);
        card.RenderTransform = group;
        card.Opacity = 0;
        Loaded += (_, _) =>
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var time = TimeSpan.FromMilliseconds(340);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, time) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, time) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, time) { EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, time) { EasingFunction = ease });
            PulseMark(mark);
        };
    }

    /// <summary>Real Lumen.png when shipped; otherwise a drawn purple toggle mark.</summary>
    private static FrameworkElement BuildLumenMark()
    {
        var png = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "lumen-mark.png");
        if (File.Exists(png))
        {
            var img = new Image
            {
                Width = 52, Height = 52, Stretch = Stretch.Uniform,
                Source = new BitmapImage(new Uri(png)),
                Effect = new DropShadowEffect { Color = Violet, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 },
            };
            return img;
        }

        // Fallback: purple tile + glass track + cyan knob (matches Lumen.ico).
        var tile = new Grid { Width = 52, Height = 52 };
        var fill = new LinearGradientBrush
        {
            StartPoint = new Point(0.2, 0), EndPoint = new Point(0.9, 1),
            GradientStops =
            {
                new GradientStop(VioletDeep, 0),
                new GradientStop(Violet, 0.55),
                new GradientStop(Color.FromRgb(0xD4, 0x7A, 0xFF), 1),
            },
        };
        fill.Freeze();
        tile.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = fill,
            Effect = new DropShadowEffect { Color = Violet, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.65 },
        });
        var track = new Border
        {
            Width = 30, Height = 16, CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderBrush = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };
        var knob = new Ellipse
        {
            Width = 12, Height = 12, Fill = Frozen(CyanKnob),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 2, 0),
            Effect = new DropShadowEffect { Color = CyanKnob, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8 },
        };
        var trackHost = new Grid { Width = 30, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        trackHost.Children.Add(track);
        trackHost.Children.Add(knob);
        tile.Children.Add(trackHost);
        return tile;
    }

    private static void PulseMark(FrameworkElement mark)
    {
        mark.RenderTransformOrigin = new Point(0.5, 0.5);
        var s = new ScaleTransform(1, 1);
        mark.RenderTransform = s;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var up = new DoubleAnimation(1, 1.06, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, up);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, up);
    }

    private static UIElement ItemRow(WhatsNew.Item item)
    {
        var color = (Color)ColorConverter.ConvertFromString(item.Color);
        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        var badge = new Grid { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Top };
        badge.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Frozen(Color.FromArgb(0x2E, color.R, color.G, color.B)) });
        badge.Children.Add(new TextBlock
        {
            Style = (Style)Application.Current.FindResource("Glyph"),
            Text = item.Glyph,
            FontSize = 16,
            Foreground = Frozen(color),
        });
        row.Children.Add(badge);

        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        text.Children.Add(new TextBlock { Text = item.Text, Foreground = Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
