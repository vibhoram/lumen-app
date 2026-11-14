// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DIsland.Services;

public static class ColorTools
{
    /// <summary>True when the color has enough saturation and brightness to act as an accent.</summary>
    public static bool IsVivid(Color c)
    {
        var (_, s, l) = ToHsl(c);
        return s > 0.15 && l > 0.08 && l < 0.95;
    }

    /// <summary>Nudges an accent so it reads well on a near-black island.</summary>
    public static Color Readable(Color c)
    {
        var (h, s, l) = ToHsl(c);
        return FromHsl(h, Math.Clamp(s, 0.35, 0.9), Math.Clamp(l, 0.55, 0.72));
    }

    public static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount),
        (byte)Math.Round(a.G + (b.G - a.G) * amount),
        (byte)Math.Round(a.B + (b.B - a.B) * amount));

    /// <summary>
    /// Dominant vibrant color: pixels are bucketed by hue (weighted toward saturated mid-tones),
    /// and the heaviest bucket is averaged. Averaging all hues would produce mud.
    /// </summary>
    public static Color? Vibrant(BitmapSource source)
    {
        try
        {
            const int size = 32;
            var scaled = new TransformedBitmap(source,
                new ScaleTransform((double)size / source.PixelWidth, (double)size / source.PixelHeight));
            var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);

            const int buckets = 12;
            var weight = new double[buckets];
            var r = new double[buckets];
            var g = new double[buckets];
            var b = new double[buckets];

            for (var i = 0; i < pixels.Length; i += 4)
            {
                var c = Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
                var (hue, sat, light) = ToHsl(c);
                var wgt = sat * sat * (1 - Math.Abs(light - 0.5) * 2);
                if (wgt <= 0.01) continue;
                var k = (int)(hue / 360 * buckets) % buckets;
                weight[k] += wgt;
                r[k] += c.R * wgt;
                g[k] += c.G * wgt;
                b[k] += c.B * wgt;
            }

            var best = Array.IndexOf(weight, weight.Max());
            if (weight[best] <= 0) return null;
            return Color.FromRgb(
                (byte)(r[best] / weight[best]),
                (byte)(g[best] / weight[best]),
                (byte)(b[best] / weight[best]));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A small palette of distinct vivid colours from the artwork (the way a Now Bar tints itself from the cover):
    /// hue buckets weighted toward saturated mid-tones, the heaviest few kept apart in hue, then made lively.
    /// </summary>
    public static List<Color> Palette(BitmapSource source, int count)
    {
        var result = new List<Color>();
        try
        {
            const int size = 40;
            var scaled = new TransformedBitmap(source, new ScaleTransform((double)size / source.PixelWidth, (double)size / source.PixelHeight));
            var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);

            const int buckets = 18;
            var weight = new double[buckets];
            var r = new double[buckets];
            var g = new double[buckets];
            var b = new double[buckets];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var c = Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
                var (hue, sat, light) = ToHsl(c);
                var wgt = sat * (1 - Math.Abs(light - 0.5) * 1.6);
                if (wgt <= 0.02) continue;
                var k = (int)(hue / 360 * buckets) % buckets;
                weight[k] += wgt;
                r[k] += c.R * wgt;
                g[k] += c.G * wgt;
                b[k] += c.B * wgt;
            }

            var picked = new List<double>(); // hues already used
            foreach (var k in Enumerable.Range(0, buckets).OrderByDescending(i => weight[i]))
            {
                if (weight[k] <= 0 || result.Count >= count) break;
                var avg = Color.FromRgb((byte)(r[k] / weight[k]), (byte)(g[k] / weight[k]), (byte)(b[k] / weight[k]));
                var (hue, sat, _) = ToHsl(avg);
                if (picked.Any(p => Math.Min(Math.Abs(p - hue), 360 - Math.Abs(p - hue)) < 38)) continue;
                picked.Add(hue);
                result.Add(FromHsl(hue, Math.Clamp(Math.Max(sat, 0.6), 0, 1), 0.6)); // lively, readable on dark glass
            }
            // Mostly grey artwork: fall back to cool light tones so the bars still glow.
            if (result.Count == 0) result.Add(Color.FromRgb(0xCF, 0xD6, 0xFF));
            while (result.Count < count)
            {
                var (hue, sat, _) = ToHsl(result[0]);
                result.Add(FromHsl((hue + 26 * result.Count) % 360, Math.Max(sat, 0.5), 0.64));
            }
        }
        catch (Exception)
        {
            result.Clear();
            result.AddRange(new[] { Color.FromRgb(0x9F, 0xB4, 0xFF), Color.FromRgb(0xFF, 0x9F, 0xD0), Color.FromRgb(0x9F, 0xF0, 0xFF), Color.FromRgb(0xFF, 0xD8, 0x9F) }.Take(count));
        }
        return result;
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-6) return (0, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h * 60, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        var hk = h / 360;
        var q2 = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p2 = 2 * l - q2;
        return Color.FromRgb(
            (byte)Math.Round(Channel(p2, q2, hk + 1.0 / 3) * 255),
            (byte)Math.Round(Channel(p2, q2, hk) * 255),
            (byte)Math.Round(Channel(p2, q2, hk - 1.0 / 3) * 255));
    }
}
