// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DIsland.Interop;
using DIsland.Services;

namespace DIsland;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;

    public TrayIcon(AppSettings settings, Action onOpenSettings, Func<bool> isHidden, Action<bool> setHidden, Action onExit, Action<string>? command = null)
    {
        _icon = CreateIcon();

        var menu = new ContextMenuStrip();

        var open = new ToolStripMenuItem("Settings…") { Font = new Font(menu.Font, FontStyle.Bold) };
        open.Click += (_, _) => onOpenSettings();

        var hide = new ToolStripMenuItem("Hide island") { CheckOnClick = true };
        hide.Click += (_, _) => setHidden(hide.Checked);

        var hideFlyout = new ToolStripMenuItem("Hide Windows volume flyout") { CheckOnClick = true };
        hideFlyout.Click += (_, _) =>
        {
            settings.HideVolumeFlyout = hideFlyout.Checked;
            settings.NotifyChanged();
            settings.Save();
        };

        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        startup.Click += (_, _) =>
        {
            try { StartupManager.SetEnabled(startup.Checked); }
            catch (Exception) { /* registry write blocked by policy */ }
            startup.Checked = StartupManager.IsEnabled;
        };

        ToolStripMenuItem Cmd(string text, string id, string? shortcut = null)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcut };
            item.Click += (_, _) => command?.Invoke(id);
            return item;
        }
        var quiet = new ToolStripMenuItem("Quiet mode");
        quiet.DropDownItems.AddRange(new ToolStripItem[] { Cmd("30 minutes", "quiet30"), Cmd("1 hour", "quiet60"), Cmd("Until I turn it off", "quietforever"), Cmd("Off", "quietoff") });
        var pet = new ToolStripMenuItem("Pet") { CheckOnClick = true };
        pet.Click += (_, _) => { settings.PetEnabled = pet.Checked; settings.NotifyChanged(); settings.Save(); };

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => onExit();

        menu.Items.AddRange(new ToolStripItem[]
        {
            Cmd("Ask Clara", "clara", settings.AssistantHotkey), Cmd("Quick paste", "paste", settings.QuickPasteHotkey), Cmd("Start a 25 minute focus session", "focus"), quiet,
            new ToolStripSeparator(), hide, pet, new ToolStripSeparator(), open, hideFlyout, startup, new ToolStripSeparator(), exit,
        });

        // Settings can also change from the settings window; sync checkmarks each time the menu opens.
        menu.Opening += (_, _) =>
        {
            hide.Checked = isHidden();
            pet.Checked = settings.PetEnabled;
            hide.ShortcutKeyDisplayString = settings.ToggleHotkey;
            hideFlyout.Checked = settings.HideVolumeFlyout;
            startup.Checked = StartupManager.IsEnabled;
        };

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Lumen",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseDoubleClick += (_, _) => onOpenSettings();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    /// <summary>The Lumen mark: a violet rounded square with a glass pill and a cyan dot (same as the app icon).</summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var square = new GraphicsPath();
            const float r = 7;
            square.AddArc(0, 0, 2 * r, 2 * r, 180, 90); square.AddArc(32 - 2 * r, 0, 2 * r, 2 * r, 270, 90);
            square.AddArc(32 - 2 * r, 32 - 2 * r, 2 * r, 2 * r, 0, 90); square.AddArc(0, 32 - 2 * r, 2 * r, 2 * r, 90, 90); square.CloseFigure();
            using var bg = new LinearGradientBrush(new RectangleF(0, 0, 32, 32), Color.FromArgb(255, 32, 24, 86), Color.FromArgb(255, 124, 92, 255), 55f);
            g.FillPath(bg, square);
            using var pill = new GraphicsPath();
            pill.AddArc(5.5f, 11.5f, 9.6f, 9.6f, 90, 180); pill.AddArc(16.9f, 11.5f, 9.6f, 9.6f, 270, 180); pill.CloseFigure();
            using var pf = new LinearGradientBrush(new RectangleF(5.5f, 11.5f, 21, 9.6f), Color.FromArgb(235, 255, 255, 255), Color.FromArgb(150, 214, 224, 255), 90f);
            g.FillPath(pf, pill);
            using var dot = new SolidBrush(Color.FromArgb(255, 45, 212, 255));
            g.FillEllipse(dot, 19.3f, 13.6f, 4.2f, 4.2f);
        }
        var handle = bmp.GetHicon();
        using var temp = Icon.FromHandle(handle);
        var icon = (Icon)temp.Clone();
        NativeMethods.DestroyIcon(handle);
        return icon;
    }
}
