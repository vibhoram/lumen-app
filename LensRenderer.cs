// D-Island (personal fork) - GPU Liquid Glass lens: Desktop Duplication -> Direct3D 11 shader -> DirectComposition.
//
// The island's backdrop engine (GpuBlurBackdrop) already blurs what is behind the pill on the GPU. This adds
// the part a blur can't do: the curved rim bends the background. The screen is duplicated as a GPU texture
// (no CPU copy), a pixel shader draws only the bevel ring of the island's shape, and the result is a
// DirectComposition swap chain layered above the blur. If anything here fails, the island simply keeps the
// plain blur.

using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DIsland.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DIsland;

internal sealed class LensRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Params
    {
        public float RectX, RectY, RectW, RectH;
        public float Radius, Bevel, Strength, Light;
        public float MonW, MonH, OriginX, OriginY;
        public float Blur, Dispersion, Shine, Saturation;
    }

    private readonly object _lock = new();
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swap;
    private ID3D11RenderTargetView _rtv;
    private readonly ID3D11VertexShader _vs;
    private readonly ID3D11PixelShader _ps;
    private readonly ID3D11Buffer _cb;
    private readonly ID3D11SamplerState _sampler;
    private readonly IDXGIOutput1 _output;
    private readonly Thread _thread;
    private int _width, _height;
    private readonly int _monLeft, _monTop, _monW, _monH;

    private IDXGIOutputDuplication? _dup;
    private ID3D11Texture2D? _frame;
    private ID3D11ShaderResourceView? _frameSrv;
    private volatile bool _stop;
    private bool _haveFrame;
    private int _origX, _origY;
    private Params _params;
    private bool _shapeSet;

#if DEBUG
    private static readonly int Stage = int.TryParse(Environment.GetEnvironmentVariable("DISLAND_LENS_STAGE"), out var s) ? s : 9;
    private static void Mark(string what) { lock (TraceLock) { try { if (Environment.GetEnvironmentVariable("DISLAND_LENS_TRACE") is { Length: > 0 } f) File.AppendAllText(f, what + Environment.NewLine); } catch (Exception) { } } }
#else
    private const int Stage = 9;
    private static void Mark(string what) { }
#endif
    private static readonly object TraceLock = new();
    public int FramesReceived { get; private set; }
    public string? LastError { get; private set; }

    private volatile bool _paused;

    /// <summary>
    /// While parked the renderer keeps its duplication and last frame (so switching the lens back on is instant and
    /// can't be left waiting for a screen change) but draws nothing.
    /// </summary>
    public void SetPaused(bool paused)
    {
        lock (_lock)
        {
            _paused = paused;
            if (!paused && _haveFrame && _shapeSet && Stage >= 2) RenderLocked();
        }
    }

    /// <summary>The swap chain to attach to a DirectComposition visual.</summary>
    public IDXGISwapChain1 SwapChain => _swap;

    /// <param name="hwnd">The glass window (for its monitor); its size is the swap chain size.</param>
    public LensRenderer(IntPtr hwnd, int width, int height)
    {
        _width = Math.Max(16, width);
        _height = Math.Max(16, height);

        // Find the adapter and output that show this window: duplication only works on the adapter that
        // drives the output (matters on laptops with an integrated + discrete GPU).
        var monitor = NativeMethods.MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? adapter = null;
        IDXGIOutput? found = null;
        for (uint a = 0; factory.EnumAdapters1(a, out var candidate).Success; a++)
        {
            for (uint o = 0; candidate.EnumOutputs(o, out var output).Success; o++)
            {
                if (output.Description.Monitor == monitor) { found = output; break; }
                output.Dispose();
            }
            if (found is not null) { adapter = candidate; break; }
            candidate.Dispose();
        }
        if (adapter is null || found is null) throw new InvalidOperationException("No output found for the island's monitor.");
        _output = found.QueryInterface<IDXGIOutput1>();
        var desc = found.Description.DesktopCoordinates;
        _monLeft = desc.Left;
        _monTop = desc.Top;
        _monW = desc.Right - desc.Left;
        _monH = desc.Bottom - desc.Top;

        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 },
            out _device, out _, out _context).CheckError();

        // The capture thread and the UI thread both touch the device; let D3D serialise them as well as our lock.
        using (var multithread = _device.QueryInterface<ID3D11Multithread>())
            multithread.SetMultithreadProtected(true);

        using (var dxgiDevice = _device.QueryInterface<IDXGIDevice>())
        using (var dxgiFactory = adapter.GetParent<IDXGIFactory2>())
        {
            var swapDesc = new SwapChainDescription1
            {
                Width = (uint)_width,
                Height = (uint)_height,
                Format = Format.B8G8R8A8_UNorm,
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = AlphaMode.Premultiplied,
                Scaling = Scaling.Stretch,
            };
            _swap = dxgiFactory.CreateSwapChainForComposition(_device, swapDesc);
        }
        adapter.Dispose();

        using (var back = _swap.GetBuffer<ID3D11Texture2D>(0))
            _rtv = _device.CreateRenderTargetView(back);

        _vs = _device.CreateVertexShader(Load("LensVS.cso"));
        _ps = _device.CreatePixelShader(Load("LensPS.cso"));
        _cb = _device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<Params>(), BindFlags.ConstantBuffer, ResourceUsage.Default));
        _sampler = _device.CreateSamplerState(new SamplerDescription(Filter.MinMagLinearMipPoint, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp));

        Mark("ctor done");
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "D-Island lens capture" };
        if (Stage >= 1) _thread.Start();
    }

    private static byte[] Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                           ?? throw new FileNotFoundException(name);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>The glass window changed size (the island grew for a bigger page): resize the swap chain to match.</summary>
    public void Resize(int width, int height)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        lock (_lock)
        {
            if (width == _width && height == _height) return;
            _context.ClearState();
            _rtv.Dispose();
            _swap.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
            _width = width;
            _height = height;
            using var back = _swap.GetBuffer<ID3D11Texture2D>(0);
            _rtv = _device.CreateRenderTargetView(back);
        }
    }

    /// <summary>The island's rounded shape in glass-window pixels, plus the window's position on screen. Rendered at once.</summary>
    public void SetShape(float x, float y, float w, float h, float radius, int screenLeft, int screenTop,
        float blur, float strength, float dispersion, float shine)
    {
        lock (_lock)
        {
            _origX = screenLeft - _monLeft;
            _origY = screenTop - _monTop;
            var bevel = Math.Clamp(Math.Min(w, h) * 0.55f, 16f, 64f);
            _params = new Params
            {
                RectX = x + w / 2, RectY = y + h / 2, RectW = w / 2, RectH = h / 2,
                Radius = radius, Bevel = bevel, Strength = strength, Light = -2.35f,
                MonW = _monW, MonH = _monH, OriginX = _origX, OriginY = _origY,
                Blur = blur, Dispersion = dispersion, Shine = shine, Saturation = 1.35f,
            };
            _shapeSet = true;
            if (_haveFrame && Stage >= 2 && !_paused) RenderLocked();
        }
    }

    // ------------------------------------------------------------------ capture thread

    private void CaptureLoop()
    {
        try
        {
            while (!_stop)
            {
                if (_dup is null && !StartDuplication())
                {
                    Thread.Sleep(500);
                    continue;
                }

                Mark("acquire");
                var hr = _dup!.AcquireNextFrame(33, out var info, out var resource);
                Mark("acquired " + hr.Code);
                if (hr.Success)
                {
                    try
                    {
                        // Frames with no presented desktop change (only the mouse moved) carry no new picture.
                        if (info.LastPresentTime == 0) continue;
                        using var texture = resource.QueryInterface<ID3D11Texture2D>();
                        lock (_lock)
                        {
                            EnsureFrameTexture(texture);
                            Mark("copy");
                            _context.CopyResource(_frame, texture);
                            _haveFrame = true;
                            FramesReceived++;
                            if (_shapeSet && Stage >= 2 && !_paused) RenderLocked();
                        }
                    }
                    finally
                    {
                        _dup.ReleaseFrame();
                        resource.Dispose();
                    }
                }
                else if (hr.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code)
                {
                    // nothing changed on screen; the last frame is still right
                }
                else
                {
                    // access lost (resolution change, lock screen, UAC): start duplicating again
                    _dup.Dispose();
                    _dup = null;
                    Thread.Sleep(250);
                }
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            App.LogError(ex);
        }
    }

    private bool StartDuplication()
    {
        try
        {
            _dup = _output.DuplicateOutput(_device);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private void EnsureFrameTexture(ID3D11Texture2D source)
    {
        var d = source.Description;
        if (_frame is not null && _frame.Description.Width == d.Width && _frame.Description.Height == d.Height) return;
        _frameSrv?.Dispose();
        _frame?.Dispose();
        _frame = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = d.Width,
            Height = d.Height,
            Format = d.Format,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
        });
        _frameSrv = _device.CreateShaderResourceView(_frame);
    }

    // ------------------------------------------------------------------ drawing

    private void RenderLocked()
    {
        if (_frameSrv is null) return;
        Mark("render");
        _context.UpdateSubresource(_params, _cb);
        _context.OMSetRenderTargets(_rtv);
        _context.RSSetViewport(0, 0, _width, _height);
        _context.ClearRenderTargetView(_rtv, new Vortice.Mathematics.Color4(0, 0, 0, 0));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vs);
        _context.PSSetShader(_ps);
        _context.PSSetConstantBuffer(0, _cb);
        _context.PSSetShaderResource(0, _frameSrv);
        _context.PSSetSampler(0, _sampler);
        _context.Draw(3, 0);
        Mark("present");
        _swap.Present(0, PresentFlags.None);
        Mark("presented");
    }

#if DEBUG
    /// <summary>Debug aid: renders the lens ring over the captured pixels into a PNG, since the island hides from screen capture.</summary>
    public bool DumpPng(string path)
    {
        lock (_lock)
        {
            if (_frame is null || !_shapeSet) return false;
            var td = new Texture2DDescription
            {
                Width = (uint)_width, Height = (uint)_height, Format = Format.B8G8R8A8_UNorm, MipLevels = 1, ArraySize = 1,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            };
            Mark("dump:start");
            using var ring = _device.CreateTexture2D(td);
            using var ringRtv = _device.CreateRenderTargetView(ring);
            _context.UpdateSubresource(_params, _cb);
            _context.OMSetRenderTargets(ringRtv);
            _context.RSSetViewport(0, 0, _width, _height);
            _context.ClearRenderTargetView(ringRtv, new Vortice.Mathematics.Color4(0, 0, 0, 0));
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.VSSetShader(_vs);
            _context.PSSetShader(_ps);
            _context.PSSetConstantBuffer(0, _cb);
            _context.PSSetShaderResource(0, _frameSrv);
            _context.PSSetSampler(0, _sampler);
            _context.Draw(3, 0);

            Texture2DDescription Staging(uint w, uint h, Format f) => new()
            {
                Width = w, Height = h, Format = f, MipLevels = 1, ArraySize = 1, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
            };
            var fd = _frame.Description;
            using var stR = _device.CreateTexture2D(Staging((uint)_width, (uint)_height, Format.B8G8R8A8_UNorm));
            using var stF = _device.CreateTexture2D(Staging(fd.Width, fd.Height, fd.Format));
            Mark("dump:draw");
            _context.CopyResource(stR, ring);
            _context.CopyResource(stF, _frame);
            Mark("dump:copied");
            var mr = _context.Map(stR, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            var mf = _context.Map(stF, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            Mark("dump:mapped");
            var pixels = new byte[_width * _height * 4];
            for (var y = 0; y < _height; y++)
            {
                for (var x = 0; x < _width; x++)
                {
                    var rp = Marshal.ReadInt32(mr.DataPointer, (int)(y * mr.RowPitch + x * 4));
                    int fx = Math.Clamp(x + _origX, 0, (int)fd.Width - 1), fy = Math.Clamp(y + _origY, 0, (int)fd.Height - 1);
                    var fp = Marshal.ReadInt32(mf.DataPointer, (int)(fy * mf.RowPitch + fx * 4));
                    var ra = Environment.GetEnvironmentVariable("DISLAND_LENS_NORING") == "1" ? 0 : (rp >> 24) & 255;
                    for (var c = 0; c < 3; c++)
                    {
                        var rc = (rp >> (8 * c)) & 255;
                        var fc = (fp >> (8 * c)) & 255;
                        pixels[(y * _width + x) * 4 + c] = (byte)Math.Min(255, rc + fc * (255 - ra) / 255);
                    }
                    pixels[(y * _width + x) * 4 + 3] = 255;
                }
            }
            Mark("dump:read");
            _context.Unmap(stR, 0);
            _context.Unmap(stF, 0);
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(_width, _height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, _width * 4);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
            return true;
        }
    }
#endif

    public void Dispose()
    {
        _stop = true;
        _thread.Join(500);
        lock (_lock)
        {
            _dup?.Dispose();
            _frameSrv?.Dispose();
            _frame?.Dispose();
            _sampler.Dispose();
            _cb.Dispose();
            _ps.Dispose();
            _vs.Dispose();
            _rtv.Dispose();
            _swap.Dispose();
            _output.Dispose();
            _context.Dispose();
            _device.Dispose();
        }
    }
}
