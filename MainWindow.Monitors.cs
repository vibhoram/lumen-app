// Lumen (personal fork) - multi-monitor: the island follows the cursor to another screen after a short pause.

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DIsland.Interop;

namespace DIsland;

public partial class MainWindow
{
    private readonly DispatcherTimer _monitorWatch = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private System.Windows.Forms.Screen? _homeScreen;
    private int _awayTicks;

    private void WireMonitors()
    {
        _monitorWatch.Tick += (_, _) => FollowCursorMonitor();
        _monitorWatch.Start();
    }

    private static System.Windows.Forms.Screen ScreenAtCursor()
    {
        NativeMethods.GetCursorPos(out var c);
        return System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(c.X, c.Y));
    }

    /// <summary>Where the island lives now: the screen its window is on.</summary>
    private System.Windows.Forms.Screen CurrentScreen()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        return hwnd == IntPtr.Zero ? System.Windows.Forms.Screen.PrimaryScreen! : System.Windows.Forms.Screen.FromHandle(hwnd);
    }

    private void FollowCursorMonitor()
    {
        if (!_settings.FollowCursorMonitor || System.Windows.Forms.Screen.AllScreens.Length < 2 || IslandHidden || _state.IsExpanded)
        {
            _awayTicks = 0;
            return;
        }
        _homeScreen = CurrentScreen();
        var cursorScreen = ScreenAtCursor();
        if (cursorScreen.DeviceName == _homeScreen.DeviceName) { _awayTicks = 0; return; }
        if (++_awayTicks < 4) return; // about 1.6 s on the other screen
        _awayTicks = 0;
        MoveToScreen(cursorScreen);
    }

    private void MoveToScreen(System.Windows.Forms.Screen screen)
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var b = screen.Bounds;
        Left = (b.Left + (b.Width - Width * dpi) / 2) / dpi;
        Top = b.Top / dpi + _settings.TopOffset;
        Dispatcher.InvokeAsync(Reposition); // fine-tune: centre the island line exactly on that screen
        // the lens reads one monitor through Desktop Duplication: rebuild it for the new one
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(250);
            _gpuBlur?.DisposeLens();
            _lensFailed = false;
            UpdateGlass();
            KeepOnTop(force: true);
        });
    }
}
