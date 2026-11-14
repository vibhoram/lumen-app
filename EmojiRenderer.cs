// Lumen (personal fork) - real colour emoji. WPF draws emoji as flat one-colour outlines; Direct2D + DirectWrite can draw
// the colour glyphs of "Segoe UI Emoji", so each emoji in a chat bubble is rendered once to a small bitmap and shown as an image.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace DIsland;

public static class EmojiRenderer
{
    private static readonly Dictionary<string, BitmapSource?> Cache = new();
    private static readonly object Lock = new();
    private static bool _broken;

    /// <summary>A colour bitmap of the emoji at the given size in pixels, or null if colour rendering isn't available.</summary>
    public static BitmapSource? Render(string emoji, int px)
    {
        if (_broken) return null;
        var key = emoji + "|" + px;
        lock (Lock)
        {
            if (Cache.TryGetValue(key, out var hit)) return hit;
            BitmapSource? result = null;
            try { result = Draw(emoji, px); }
            catch (Exception ex) { App.LogError(ex); _broken = true; }
            Cache[key] = result;
            return result;
        }
    }

    private static BitmapSource Draw(string emoji, int px)
    {
        var size = px + px / 4; // a little room so glyphs aren't clipped
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, new[] { Vortice.Direct3D.FeatureLevel.Level_11_0, Vortice.Direct3D.FeatureLevel.Level_10_1 }, out var device, out _, out var context).CheckError();
        using var _d = device; using var _c = context;

        using var target = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)size, Height = (uint)size, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        });
        using var staging = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)size, Height = (uint)size, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
        });

        using var d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using var d2dDevice = d2dFactory.CreateDevice(dxgiDevice);
        using var dc = d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        using var surface = target.QueryInterface<IDXGISurface>();
        using var bitmap = dc.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
        dc.Target = bitmap;

        using var dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
        using var format = dwrite.CreateTextFormat("Segoe UI Emoji", px * 0.86f);
        format.ParagraphAlignment = ParagraphAlignment.Center;
        format.TextAlignment = Vortice.DirectWrite.TextAlignment.Center;
        using var brush = dc.CreateSolidColorBrush(new Color4(1, 1, 1, 1));

        dc.BeginDraw();
        dc.Clear(new Color4(0, 0, 0, 0));
        dc.DrawText(emoji, format, new Vortice.Mathematics.Rect(0, 0, size, size), brush, DrawTextOptions.EnableColorFont);
        dc.EndDraw();
        dc.Target = null;

        context.CopyResource(staging, target);
        var map = context.Map(staging, 0, MapMode.Read);
        try
        {
            var pixels = new byte[size * size * 4];
            for (var y = 0; y < size; y++)
                Marshal.Copy(map.DataPointer + (int)(y * map.RowPitch), pixels, y * size * 4, size * 4);
            var image = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
            image.Freeze();
            return image;
        }
        finally { context.Unmap(staging, 0); }
    }
}
