// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DIsland;

/// <summary>
/// Webser cream-tile mark: knock out the black export frame (no dog-ear corners),
/// keep the cream plate, crush the W to pure #000.
/// </summary>
public static class WebserMark
{
    public static readonly Uri Site = new("https://webser.app");

    private static ImageSource? _mark;

    public static FrameworkElement Create(double size, Brush? fill = null)
    {
        _ = fill;
        var img = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Source = Mark(),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    private static ImageSource Mark()
    {
        if (_mark is not null) return _mark;

        foreach (var name in new[] { "webser-w-light.png", "webser-w.png", "webser-icon.png" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            if (!File.Exists(path)) continue;
            try
            {
                _mark = BuildMark(path);
                if (_mark is not null) return _mark;
            }
            catch (Exception) { /* next */ }
        }

        var empty = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        empty.Freeze();
        return _mark = empty;
    }

    private static ImageSource? BuildMark(string path)
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

        // Flood-fill from every border pixel: anything dark/transparent connected to the
        // export frame becomes clear — that's what left the black dog-ears on the cream tile.
        var drop = new bool[w * h];
        var stack = new Stack<int>();
        void Seed(int x, int y)
        {
            var i = y * w + x;
            if (drop[i]) return;
            var p = y * stride + x * 4;
            var a = pixels[p + 3];
            var lum = 0.299 * pixels[p + 2] + 0.587 * pixels[p + 1] + 0.114 * pixels[p];
            // Frame is black (or already clear). Cream and the W stay.
            if (a < 40 || lum < 55)
            {
                drop[i] = true;
                stack.Push(i);
            }
        }
        for (var x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (var y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }

        while (stack.Count > 0)
        {
            var i = stack.Pop();
            var x = i % w;
            var y = i / w;
            void Try(int nx, int ny)
            {
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) return;
                var ni = ny * w + nx;
                if (drop[ni]) return;
                var p = ny * stride + nx * 4;
                var a = pixels[p + 3];
                var lum = 0.299 * pixels[p + 2] + 0.587 * pixels[p + 1] + 0.114 * pixels[p];
                // Stay on the black frame / soft AA into it — don't eat into cream or the W.
                if (a < 40 || lum < 55)
                {
                    drop[ni] = true;
                    stack.Push(ni);
                }
            }
            Try(x - 1, y); Try(x + 1, y); Try(x, y - 1); Try(x, y + 1);
        }

        // Representative cream from remaining light pixels.
        byte creamB = 0xEB, creamG = 0xF3, creamR = 0xFA;
        for (var y = h / 5; y < h - h / 5; y += 4)
        for (var x = w / 5; x < w - w / 5; x += 4)
        {
            var i = y * w + x;
            if (drop[i]) continue;
            var p = y * stride + x * 4;
            if (pixels[p + 3] < 200) continue;
            var lum = 0.299 * pixels[p + 2] + 0.587 * pixels[p + 1] + 0.114 * pixels[p];
            if (lum < 200) continue;
            creamB = pixels[p]; creamG = pixels[p + 1]; creamR = pixels[p + 2];
            goto GotCream;
        }
        GotCream:

        var minX = w; var minY = h; var maxX = 0; var maxY = 0;
        var has = false;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = y * w + x;
            var p = y * stride + x * 4;
            if (drop[i])
            {
                pixels[p] = pixels[p + 1] = pixels[p + 2] = pixels[p + 3] = 0;
                continue;
            }

            var lum = 0.299 * pixels[p + 2] + 0.587 * pixels[p + 1] + 0.114 * pixels[p];
            // Hard split so small sizes don't wash the W into grey.
            if (lum < 140)
            {
                pixels[p] = pixels[p + 1] = pixels[p + 2] = 0;
                pixels[p + 3] = 255;
            }
            else
            {
                pixels[p] = creamB; pixels[p + 1] = creamG; pixels[p + 2] = creamR;
                pixels[p + 3] = 255;
            }

            has = true;
            if (x < minX) minX = x; if (y < minY) minY = y;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y;
        }
        if (!has) return null;

        const int pad = 1;
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
