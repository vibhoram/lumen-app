// D-Island (personal fork) - clipboard history: text, links, images and files you copied, kept on this PC.

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DIsland.Services;

public enum ClipKind { Text, Link, Image, Files }

public sealed class ClipItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ClipKind Kind { get; set; }
    /// <summary>The text or link; for files, one path per line.</summary>
    public string Text { get; set; } = "";
    /// <summary>For images: the PNG's file name inside the history folder.</summary>
    public string? ImageFile { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }
    public string Hash { get; set; } = "";
}

/// <summary>
/// Remembers what was copied. Content that password managers mark private never reaches this class
/// (the ClipboardWatcher drops it first). Everything stays in %LocalAppData%\DIsland\Clipboard.
/// </summary>
public sealed class ClipboardHistory
{
    private const int MaxItems = 100;
    private const int MaxText = 20000;

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIsland", "Clipboard");
    private static readonly string IndexFile = Path.Combine(Folder, "history.json");

    private List<ClipItem> _items = new();

    public ClipboardHistory()
    {
        try
        {
            if (File.Exists(IndexFile))
                _items = JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(IndexFile)) ?? new();
        }
        catch (Exception) { /* damaged index: start empty */ }
    }

    public IReadOnlyList<ClipItem> Items => _items;

    /// <summary>How long items are kept, in hours (0 = until the 100-item limit). Pinned items are never expired.</summary>
    public int KeepHours { get; set; }

    private bool _paused;

    /// <summary>One-time hold: while on, nothing you copy is saved (screenshots included). Not remembered after a restart.</summary>
    public bool Paused
    {
        get => _paused;
        set { if (_paused == value) return; _paused = value; PausedChanged?.Invoke(); }
    }

    public event Action? PausedChanged;

    /// <summary>Removes items older than the keep time (pinned ones stay).</summary>
    public void PurgeExpired()
    {
        if (KeepHours <= 0) return;
        var cutoff = DateTime.Now.AddHours(-KeepHours);
        var old = _items.Where(i => !i.Pinned && i.Time < cutoff).ToList();
        if (old.Count == 0) return;
        foreach (var item in old) { _items.Remove(item); DeleteFile(item); }
        Save();
        Changed?.Invoke();
    }

    public event Action? Changed;

    public string ImagePath(ClipItem item) => Path.Combine(Folder, item.ImageFile ?? "");

    /// <summary>Reads whatever is on the clipboard right now and adds it (or moves an identical item to the top).</summary>
    public void CaptureCurrent()
    {
        if (_paused) return; // on hold: save nothing
        try
        {
            ClipItem? item = null;
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().Take(20).ToList();
                if (files.Count == 0) return;
                var text = string.Join("\n", files);
                item = new ClipItem { Kind = ClipKind.Files, Text = text, Hash = Hash(Encoding.UTF8.GetBytes("f:" + text)) };
            }
            else if (Clipboard.ContainsImage())
            {
                // Reading the image must happen here, but turning a full-screen screenshot into a PNG takes a good fraction of a
                // second: do that elsewhere, or the island freezes (and seems to vanish) every time you take a screenshot.
                if (Clipboard.GetImage() is not { } image) return;
                if (image.CanFreeze) image.Freeze();
                _ = Task.Run(() => AddImage(image));
                return;
            }
            else if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;
                if (text.Length > MaxText) text = text[..MaxText];
                var link = text.Trim().Length < 2048 && !text.Trim().Contains(' ') && !text.Trim().Contains('\n')
                           && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
                           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
                item = new ClipItem { Kind = link ? ClipKind.Link : ClipKind.Text, Text = text, Hash = Hash(Encoding.UTF8.GetBytes("t:" + text)) };
            }
            if (item is null || Bump(item.Hash)) return;

            _items.Insert(0, item);
            Trim();
            Save();
            Changed?.Invoke();
        }
        catch (Exception) { /* the clipboard can be locked by the writer; the next copy will work */ }
    }

    private void AddImage(BitmapSource image)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            var bytes = ms.ToArray();
            var hash = Hash(bytes);
            Directory.CreateDirectory(Folder);
            var name = Guid.NewGuid().ToString("N") + ".png";
            File.WriteAllBytes(Path.Combine(Folder, name), bytes);
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (Bump(hash)) { try { File.Delete(Path.Combine(Folder, name)); } catch (Exception) { } return; }
                _items.Insert(0, new ClipItem { Kind = ClipKind.Image, ImageFile = name, Hash = hash, Text = $"{image.PixelWidth} × {image.PixelHeight}" });
                Trim();
                Save();
                Changed?.Invoke();
            });
        }
        catch (Exception) { /* a screenshot we could not keep is not worth an error */ }
    }

    public void Touch(ClipItem item)
    {
        _items.Remove(item);
        item.Time = DateTime.Now;
        _items.Insert(0, item);
        Save();
        Changed?.Invoke();
    }

    public void SetPinned(ClipItem item, bool pinned)
    {
        item.Pinned = pinned;
        Save();
        Changed?.Invoke();
    }

    public void Delete(ClipItem item)
    {
        _items.Remove(item);
        DeleteFile(item);
        Save();
        Changed?.Invoke();
    }

    public void Clear(bool keepPinned = true)
    {
        foreach (var item in _items.Where(i => !keepPinned || !i.Pinned).ToList())
        {
            _items.Remove(item);
            DeleteFile(item);
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>Puts the item back on the clipboard as the newest copy (the history just moves it to the top, no duplicate).</summary>
    public void Restore(ClipItem item)
    {
        try
        {
            switch (item.Kind)
            {
                case ClipKind.Image:
                    if (LoadImage(item) is { } image) Clipboard.SetImage(image);
                    break;
                case ClipKind.Files:
                    var files = new System.Collections.Specialized.StringCollection();
                    files.AddRange(item.Text.Split('\n'));
                    Clipboard.SetFileDropList(files);
                    break;
                default:
                    Clipboard.SetText(item.Text);
                    break;
            }
        }
        catch (Exception) { /* clipboard busy */ }
    }

    public BitmapSource? LoadImage(ClipItem item, int decodeWidth = 0)
    {
        try
        {
            var path = ImagePath(item);
            if (!File.Exists(path)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }

    private bool Bump(string hash)
    {
        var existing = _items.FirstOrDefault(i => i.Hash == hash);
        if (existing is null) return false;
        if (_items[0] != existing) Touch(existing);
        return true;
    }

    private void Trim()
    {
        while (_items.Count(i => !i.Pinned) > MaxItems)
        {
            var oldest = _items.Last(i => !i.Pinned);
            _items.Remove(oldest);
            DeleteFile(oldest);
        }
    }

    private static void DeleteFile(ClipItem item)
    {
        try
        {
            if (item.ImageFile is not null) File.Delete(Path.Combine(Folder, item.ImageFile));
        }
        catch (Exception) { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(IndexFile, JsonSerializer.Serialize(_items));
        }
        catch (Exception) { }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes));
}
