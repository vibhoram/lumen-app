// D-Island (personal fork) - mouse shake detection.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DIsland.Interop;

namespace DIsland.Services;

public enum ShakeSensitivity { Low, Medium, High }

/// <summary>
/// Detects a quick left-right shake of the mouse through a low-level mouse hook, so the island can
/// hide or show itself ("Magic Shake"). The hook only reads the X coordinate and returns at once.
/// </summary>
public sealed class MouseShakeService : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;

    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly NativeMethods.LowLevelKeyboardProc _proc; // same signature as the mouse hook callback
    private IntPtr _hook;

    // Shake state (touched only on the hook thread = the UI thread, since the hook is installed there).
    private readonly Queue<long> _reversals = new();
    private int _lastX, _segStartX, _dir;
    private bool _haveLast;
    private long _lastFire;

    private int _minTravel = 90, _needed = 4, _windowMs = 450;

    public MouseShakeService() => _proc = HookProc;

    /// <summary>Raised on the UI thread when a shake is detected.</summary>
    public event Action? Shaken;

    public ShakeSensitivity Sensitivity
    {
        set
        {
            (_minTravel, _needed, _windowMs) = value switch
            {
                ShakeSensitivity.Low => (130, 5, 500),
                ShakeSensitivity.High => (55, 4, 400),
                _ => (90, 4, 450),
            };
        }
    }

    public bool Enabled
    {
        get => _hook != IntPtr.Zero;
        set
        {
            if (value == Enabled) return;
            if (value)
            {
                _haveLast = false;
                _reversals.Clear();
                _hook = NativeMethods.SetWindowsHookEx(WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
            }
            else
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    public void Dispose() => Enabled = false;

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_MOUSEMOVE)
        {
            var x = Marshal.ReadInt32(lParam); // MSLLHOOKSTRUCT.pt.x
            OnMove(x);
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnMove(int x)
    {
        if (!_haveLast)
        {
            _haveLast = true;
            _lastX = _segStartX = x;
            return;
        }
        var dx = x - _lastX;
        if (Math.Abs(dx) < 2) return; // jitter
        var dir = Math.Sign(dx);
        if (_dir == 0)
        {
            _dir = dir;
            _segStartX = _lastX;
        }
        else if (dir != _dir)
        {
            // Direction flipped at _lastX: count it only if the stroke just finished was long enough.
            var now = Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
            if (Math.Abs(_lastX - _segStartX) >= _minTravel)
            {
                _reversals.Enqueue(now);
                while (_reversals.Count > 0 && now - _reversals.Peek() > _windowMs) _reversals.Dequeue();
                if (_reversals.Count >= _needed && now - _lastFire > 900)
                {
                    _lastFire = now;
                    _reversals.Clear();
                    _ui.InvokeAsync(() => Shaken?.Invoke()); // return fast; hooks have a timeout
                }
            }
            else if (_reversals.Count > 0 && now - _reversals.Peek() > _windowMs)
            {
                _reversals.Clear(); // slow wiggling shouldn't accumulate
            }
            _dir = dir;
            _segStartX = _lastX;
        }
        _lastX = x;
    }
}
