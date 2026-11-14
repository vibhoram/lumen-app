// D-Island (personal fork) - hand a copied item to Google's AI Mode in the default browser.
//
// Text and links go in the address (the question is typed for you). A browser can't be handed an image by address, so the
// picture is put on the clipboard, AI Mode opens, and Ctrl+V is pressed for you once the browser is in front.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DIsland.Services;

public static class GoogleAiMode
{
    private const string Base = "https://www.google.com/search?udm=50";
    private static readonly string[] Browsers = { "chrome", "msedge", "firefox", "vivaldi", "brave", "opera", "comet", "arc", "browser" };

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    /// <summary>Opens AI Mode with the item. Returns a short message for the user.</summary>
    public static string Open(ClipItem item, ClipboardHistory history)
    {
        switch (item.Kind)
        {
            case ClipKind.Image:
                history.Restore(item);
                return OpenWithImage();
            case ClipKind.Files:
            {
                var first = item.Text.Split('\n')[0].Trim();
                var ext = System.IO.Path.GetExtension(first).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" && System.IO.File.Exists(first))
                {
                    try
                    {
                        var image = new System.Windows.Media.Imaging.BitmapImage(new Uri(first));
                        image.Freeze();
                        System.Windows.Clipboard.SetImage(image);
                        return OpenWithImage();
                    }
                    catch (Exception) { }
                }
                return Navigate(Base + "&q=" + Uri.EscapeDataString("Tell me about this file: " + System.IO.Path.GetFileName(first)));
            }
            case ClipKind.Link:
                return Navigate(Base + "&q=" + Uri.EscapeDataString("Summarize and explain this page: " + item.Text.Trim()));
            default:
            {
                var text = item.Text.Trim();
                if (text.Length > 1800) text = text[..1800];
                return Navigate(Base + "&q=" + Uri.EscapeDataString(text));
            }
        }
    }

    private static string Navigate(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception) { return "Couldn't open the browser."; }
        return "Opened in AI Mode";
    }

    private static string OpenWithImage()
    {
        var result = Navigate(Base);
        if (result.StartsWith("Couldn't")) return result;
        // Give the browser a moment to come forward and focus its question box, then paste the picture.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (BrowserInFront()) PressCtrlV();
        };
        timer.Start();
        return "Opened in AI Mode, pasting the image";
    }

    private static bool BrowserInFront()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            using var process = Process.GetProcessById((int)pid);
            return Browsers.Any(b => process.ProcessName.Contains(b, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception) { return false; }
    }

    private static void PressCtrlV()
    {
        const byte VK_CONTROL = 0x11, VK_V = 0x56;
        const uint KEYUP = 0x2;
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYUP, UIntPtr.Zero);
    }
}
