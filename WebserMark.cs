// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DIsland;

/// <summary>
/// Webser W from the cream app icon: black glyph kept, cream plate removed, painted white for dark UI.
/// </summary>
public static class WebserMark
{
    public static readonly Uri Site = new("https://webser.app");

    private static ImageSource? _glyph;

    public static FrameworkElement Create(double size, Brush? fill = null)
    {
        _ = fill;
        return new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Source = Glyph(),
            SnapsToDevicePixels = true,
        };
    }

    private static ImageSource Glyph()
    {
        if (_glyph is not null) return _glyph;

        // Prefer the cream-tile official mark (black W). Dark-on-dark is a fallback source only.
        foreach (var name in new[] { "webser-w-light.png", "webser-w.png", "webser-icon.png" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            if (!File.Exists(path)) continue;
            try
            {
                _glyph = ExtractGlyph(path);
                if (_glyph is not null) return _glyph;
            }
            catch (Exception) { /* next */ }
        }

        var empty = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        empty.Freeze();
        return _glyph = empty;
    }

    /// <summary>
    /// Cream tile → keep dark W pixels, drop the plate, recolor glyph white so it reads on dark About.
    /// Cream-on-black stickers → keep bright pixels as-is.
    /// </summary>
    private static ImageSource? ExtractGlyph(string path)
    {
        var raw = new BitmapImage();
        raw.BeginInit();
        raw.UriSource = new Uri(path);
        raw.CacheOption = BitmapCacheOption.OnLoad;
        raw.EndInit();
        raw.Freeze();

        var converted = new FormatConvertedBitmap(raw, PixelFormats.Bgra32, null, 0);
        var w = converted.PixelWidth;
        var h = converted.PixelHeight;
        var stride = w * 4;
        var pixels = new byte[stride * h];
        converted.CopyPixels(pixels, stride, 0);

        // Sample corners to decide: light plate (cream icon) vs dark plate (sticker).
        double CornerLum(int x, int y)
        {
            var i = y * stride + x * 4;
            return 0.299 * pixels[i + 2] + 0.587 * pixels[i + 1] + 0.114 * pixels[i];
        }
        var plate = (CornerLum(2, 2) + CornerLum(w - 3, 2) + CornerLum(2, h - 3) + CornerLum(w - 3, h - 3)) / 4;
        var lightPlate = plate > 160; // cream app icon

        var minX = w; var minY = h; var maxX = 0; var maxY = 0;
        var has = false;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = y * stride + x * 4;
            var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
            var lum = 0.299 * r + 0.587 * g + 0.114 * b;
            var keep = lightPlate ? lum < 80 : lum > 100;
            if (keep)
            {
                has = true;
                if (x < minX) minX = x; if (y < minY) minY = y;
                if (x > maxX) maxX = x; if (y > maxY) maxY = y;
                // Always white on dark Settings chrome
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
                pixels[i + 3] = 255;
            }
            else
            {
                pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;
            }
        }
        if (!has) return null;

        const int pad = 2;
        minX = Math.Max(0, minX - pad); minY = Math.Max(0, minY - pad);
        maxX = Math.Min(w - 1, maxX + pad); maxY = Math.Min(h - 1, maxY + pad);
        var cw = maxX - minX + 1;
        var ch = maxY - minY + 1;
        var cropStride = cw * 4;
        var crop = new byte[cropStride * ch];
        for (var y = 0; y < ch; y++)
            Buffer.BlockCopy(pixels, (minY + y) * stride + minX * 4, crop, y * cropStride, cropStride);

        var bmp = BitmapSource.Create(cw, ch, 96, 96, PixelFormats.Bgra32, null, crop, cropStride);
        bmp.Freeze();
        return bmp;
    }
}
