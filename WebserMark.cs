// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DIsland;

/// <summary>
/// Webser studio mark drawn as vectors (no boxed PNG). The W is two slanted bars + a right triangle.
/// </summary>
public static class WebserMark
{
    public static readonly Uri Site = new("https://webser.app");

    /// <summary>Just the white W — no rounded-square tile behind it.</summary>
    public static FrameworkElement Create(double size, Brush? fill = null)
    {
        fill ??= Brushes.White;
        // Design space 100×100; bars lean down-right, triangle finishes the W on the right.
        var bar1 = Poly(fill, 18, 22, 34, 22, 52, 82, 36, 82);
        var bar2 = Poly(fill, 40, 22, 56, 22, 74, 82, 58, 82);
        var tip = Poly(fill, 66, 22, 86, 22, 66, 52);

        var canvas = new Canvas { Width = 100, Height = 100, Background = Brushes.Transparent, IsHitTestVisible = false };
        canvas.Children.Add(bar1);
        canvas.Children.Add(bar2);
        canvas.Children.Add(tip);

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            SnapsToDevicePixels = true,
        };
    }

    private static Polygon Poly(Brush fill, params double[] xy)
    {
        var pts = new PointCollection();
        for (var i = 0; i + 1 < xy.Length; i += 2)
            pts.Add(new Point(xy[i], xy[i + 1]));
        return new Polygon { Points = pts, Fill = fill, StrokeThickness = 0 };
    }
}
