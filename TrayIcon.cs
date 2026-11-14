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

        // ---- more quick actions
        var ram = new ToolStripMenuItem("Free up RAM");
        ram.DropDownItems.AddRange(new ToolStripItem[] { Cmd("Tidy (safe)", "ram0"), Cmd("Deeper", "ram1"), Cmd("Maximum (needs admin)", "ram2") });
        var timer = new ToolStripMenuItem("Timer");
        timer.DropDownItems.AddRange(new ToolStripItem[] { Cmd("5 minutes", "timer5"), Cmd("10 minutes", "timer10"), Cmd("25 minutes", "timer25"), Cmd("1 hour", "timer60"), new ToolStripSeparator(), Cmd("Cancel timer", "timercancel") });
        var media = new ToolStripMenuItem("Music");
        media.DropDownItems.AddRange(new ToolStripItem[] { Cmd("Play / pause", "media:playpause"), Cmd("Next", "media:next"), Cmd("Previous", "media:previous") });
        var pages = new ToolStripMenuItem("Open page");
        foreach (var (label, id) in new[] { ("Home", "Home"), ("Clara", "Assistant"), ("System", "Vitals"), ("Clipboard", "Clipboard"), ("Focus", "Focus"), ("To-Do", "Todo"), ("Notes", "Notes"), ("Notifications", "Notifications"), ("Apps", "Apps") })
            pages.DropDownItems.Add(Cmd(label, "page:" + id));

        var shape = new ToolStripMenuItem("Shape");
        var shapeItems = new List<(ToolStripMenuItem Item, IslandMode Mode)>();
        foreach (var (label, mode) in new[] { ("Dynamic Island", IslandMode.DynamicIsland), ("Notch", IslandMode.Notch), ("Thin bar", IslandMode.Bar) })
        {
            var item = new ToolStripMenuItem(label);
            var captured = mode;
            item.Click += (_, _) => { settings.Mode = captured; settings.NotifyChanged(); settings.Save(); };
            shape.DropDownItems.Add(item);
            shapeItems.Add((item, mode));
        }
        var look = new ToolStripMenuItem("Look");
        foreach (var preset in LookPresets.BuiltIn)
        {
            var captured = preset;
            var item = new ToolStripMenuItem(preset.Name);
            item.Click += (_, _) => { LookPresets.Apply(settings, captured); settings.NotifyChanged(); settings.Save(); };
            look.DropDownItems.Add(item);
        }
        var species = new ToolStripMenuItem("Pet species");
        var speciesItems = new List<(ToolStripMenuItem Item, PetKind Kind)>();
        foreach (var kind in Enum.GetValues<PetKind>())
        {
            var captured = kind;
            var item = new ToolStripMenuItem(kind.ToString());
            item.Click += (_, _) => { settings.PetKind = captured; settings.PetEnabled = true; settings.NotifyChanged(); settings.Save(); };
            species.DropDownItems.Add(item);
            speciesItems.Add((item, kind));
        }

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => onExit();

        menu.Items.AddRange(new ToolStripItem[]
        {
            Cmd("Ask Clara", "clara", settings.AssistantHotkey), Cmd("Quick paste", "paste", settings.QuickPasteHotkey), Cmd("Start a 25 minute focus session", "focus"), timer, media, quiet,
            new ToolStripSeparator(), ram, pages,
            new ToolStripSeparator(), shape, look, species, hide, pet,
            new ToolStripSeparator(), open, hideFlyout, startup, new ToolStripSeparator(), exit,
        });

        // Settings can also change from the settings window; sync checkmarks each time the menu opens.
        menu.Opening += (_, _) =>
        {
            hide.Checked = isHidden();
            pet.Checked = settings.PetEnabled;
            hide.ShortcutKeyDisplayString = settings.ToggleHotkey;
            hideFlyout.Checked = settings.HideVolumeFlyout;
            startup.Checked = StartupManager.IsEnabled;
            foreach (var (item, mode) in shapeItems) item.Checked = settings.Mode == mode;
            foreach (var (item, kind) in speciesItems) item.Checked = settings.PetKind == kind;
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

    /// <summary>Hover text of the tray icon (Windows allows 63 characters): live RAM, timer and next alarm.</summary>
    public void SetTooltip(string text)
    {
        try { _notifyIcon.Text = text.Length > 63 ? text[..63] : text; } catch (Exception) { }
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
