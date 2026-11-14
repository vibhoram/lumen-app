// D-Island (personal fork) - context awareness: step aside in full-screen games and video, stay quiet in calls.

using System.Runtime.InteropServices;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private enum NotificationState
    {
        NotPresent = 0, Busy = 1, RunningD3DFullScreen = 2, PresentationMode = 3, AcceptsNotifications = 4, QuietTime = 5, App = 6,
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out NotificationState state);

    private readonly DispatcherTimer _contextTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _autoHidden;

    private void WireContext()
    {
        _contextTimer.Tick += (_, _) => CheckFullscreen();
        _contextTimer.Start();
    }

    /// <summary>Full-screen games, video and presentations: the island hides itself, then comes back.</summary>
    private void CheckFullscreen()
    {
        if (!_settings.HideInFullscreen)
        {
            if (_autoHidden) { _autoHidden = false; SetIslandHidden(false); }
            return;
        }
        if (SHQueryUserNotificationState(out var state) != 0) return;
        var fullscreen = state is NotificationState.RunningD3DFullScreen or NotificationState.PresentationMode;
        if (fullscreen && !IslandHidden)
        {
            _autoHidden = true;
            SetIslandHidden(true);
        }
        else if (!fullscreen && _autoHidden)
        {
            _autoHidden = false;
            SetIslandHidden(false); // only undo our own hiding, never one the user asked for
        }
    }
}
