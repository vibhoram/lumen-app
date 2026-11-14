// Lumen (personal fork) - big clock text in the iOS 26 lock-screen looks: plain, outlined, or glass (translucent
// digits with a lit rim and a soft inner glow). Drawn from the glyph outlines, so it stays sharp at any size.

using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DIsland;

public enum ClockLook { Solid, Outline, Glass }

public sealed class GlassText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(GlassText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(GlassText),
        new FrameworkPropertyMetadata(new FontFamily("Segoe UI Variable"), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(GlassText),
        new FrameworkPropertyMetadata(36.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(GlassText),
        new FrameworkPropertyMetadata(FontWeights.Light, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontStretchProperty = DependencyProperty.Register(nameof(FontStretch), typeof(FontStretch), typeof(GlassText),
        new FrameworkPropertyMetadata(FontStretches.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LookProperty = DependencyProperty.Register(nameof(Look), typeof(ClockLook), typeof(GlassText),
        new FrameworkPropertyMetadata(ClockLook.Glass, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public FontStretch FontStretch { get => (FontStretch)GetValue(FontStretchProperty); set => SetValue(FontStretchProperty, value); }
    public ClockLook Look { get => (ClockLook)GetValue(LookProperty); set => SetValue(LookProperty, value); }

    private FormattedText Format(Brush brush) => new(Text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretch), FontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);


    // ---- iOS lock-screen digits: each stroke is a centre line drawn as two parallel hairlines (the edges of a thin stroke)
    private static readonly Dictionary<char, string> DigitPaths = new()
    {
        ['0'] = "M30,2 C12,2 8,20 8,50 C8,80 12,98 30,98 C48,98 52,80 52,50 C52,20 48,2 30,2 Z",
        ['1'] = "M14,24 L36,2 L36,98",
        ['2'] = "M10,24 C10,8 22,2 30,2 C44,2 52,12 50,26 C48,40 30,60 10,98 L52,98",
        ['3'] = "M10,12 C18,2 44,0 48,20 C50,36 36,46 24,48 C40,48 52,58 52,76 C52,94 36,100 24,98 C16,97 10,92 8,86",
        ['4'] = "M42,98 L42,2 L8,66 L56,66",
        ['5'] = "M50,2 L14,2 L10,46 C20,38 44,38 50,56 C56,76 44,98 28,98 C18,98 10,92 8,86",
        ['6'] = "M46,8 C36,0 14,4 10,40 C8,70 12,98 30,98 C48,98 52,78 48,64 C44,50 28,46 16,56",
        ['7'] = "M8,2 L52,2 L26,98",
        ['8'] = "M30,48 C14,46 12,26 20,12 C28,0 46,2 48,18 C50,34 38,46 30,48 C14,52 8,70 14,86 C20,100 42,100 48,84 C54,68 44,52 30,48 Z",
        ['9'] = "M14,92 C24,100 46,96 50,60 C52,30 48,2 30,2 C12,2 8,22 12,36 C16,50 32,54 44,44",
    };
    private static readonly Dictionary<char, Geometry> DigitGeo = new();

    private static Geometry DigitGeometry(char c)
    {
        if (!DigitGeo.TryGetValue(c, out var g))
        {
            g = Geometry.Parse(DigitPaths[c]);
            g.Freeze();
            DigitGeo[c] = g;
        }
        return g;
    }

    /// <summary>The text laid out as outlined lock-screen lettering: custom digits, the font's outline for anything else.</summary>
    private (List<(Geometry Geo, bool Stroked)> Parts, double Width, double Height) LayoutLock()
    {
        var parts = new List<(Geometry, bool)>();
        var h = FontSize * 0.92;
        var scale = h / 100.0;
        var x = 2.0;
        var stroke = FontSize * 0.07; // gap between the two hairlines
        foreach (var c in Text ?? "")
        {
            if (c >= '0' && c <= '9')
            {
                var geo = DigitGeometry(c).Clone();
                geo.Transform = new TransformGroup { Children = { new ScaleTransform(scale, scale), new TranslateTransform(x, 4) } };
                parts.Add((geo.GetWidenedPathGeometry(new Pen(Brushes.White, stroke) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }), false));
                x += 60 * scale + FontSize * 0.1;
            }
            else if (c == ':')
            {
                var r = FontSize * 0.055;
                foreach (var cy in new[] { 4 + h * 0.30, 4 + h * 0.70 })
                    parts.Add((new EllipseGeometry(new Point(x + r + 3, cy), r + stroke / 2, r + stroke / 2).GetWidenedPathGeometry(new Pen(Brushes.White, 0.01)), false));
                x += FontSize * 0.34;
            }
            else if (c == ' ') x += FontSize * 0.26;
            else
            {
                var ft = new FormattedText(c.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily, FontStyles.Normal, FontWeights.Light, FontStretch), FontSize * 0.46, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                parts.Add((ft.BuildGeometry(new Point(x, 4 + h - ft.Height * 0.86)), true));
                x += ft.WidthIncludingTrailingWhitespace + FontSize * 0.04;
            }
        }
        return (parts, x + 2, h + 8);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Look == ClockLook.Outline)
        {
            var (_, w, h) = LayoutLock();
            return new Size(Math.Ceiling(w), Math.Ceiling(h));
        }
        var f = Format(Brushes.White);
        return new Size(Math.Ceiling(f.WidthIncludingTrailingWhitespace) + 4, Math.Ceiling(f.Height) + 4);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Text)) return;
        var origin = new Point(2, 2);
        switch (Look)
        {
            case ClockLook.Solid:
                dc.DrawText(Format(Brushes.White), origin);
                break;
            case ClockLook.Outline:
            {
                var line = new SolidColorBrush(Color.FromArgb(0xEE, 255, 255, 255));
                var pen = new Pen(line, Math.Max(0.9, FontSize / 46)) { LineJoin = PenLineJoin.Round };
                var (parts, _, _) = LayoutLock();
                foreach (var (geo, _) in parts) dc.DrawGeometry(null, pen, geo);
                break;
            }
            default: // Glass
            {
                var g = Format(Brushes.White).BuildGeometry(origin);
                var height = Math.Max(1, ActualHeight);
                var fill = new LinearGradientBrush(Color.FromArgb(0x5E, 255, 255, 255), Color.FromArgb(0x1E, 255, 255, 255), 90);
                var rim = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0xFF, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0x70, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0xB0, 255, 255, 255), 1),
                }, 60);
                dc.DrawGeometry(fill, null, g);                       // the translucent body
                dc.PushClip(g);                                        // a soft glow just inside the edge
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), Math.Max(4, FontSize / 7)), g);
                dc.Pop();
                dc.DrawGeometry(null, new Pen(rim, Math.Max(1.2, FontSize / 30)) { LineJoin = PenLineJoin.Round }, g); // the lit rim
                break;
            }
        }
    }
}
