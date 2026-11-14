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

    protected override Size MeasureOverride(Size availableSize)
    {
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
                var g = Format(Brushes.White).BuildGeometry(origin);
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0xE6, 255, 255, 255)), Math.Max(1.2, FontSize / 26)) { LineJoin = PenLineJoin.Round }, g);
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
