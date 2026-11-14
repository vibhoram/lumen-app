// D-Island (personal fork) - small site icons for quick links, cached on this PC. Only the site's domain is requested.

using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace DIsland.Services;

public static class FaviconService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIsland", "Icons");

    public static string? HostOf(string url) =>
        Uri.TryCreate(url.Contains("://") ? url : "https://" + url, UriKind.Absolute, out var uri) ? uri.Host : null;

    /// <summary>The site's icon (from the cache, or fetched once), or null.</summary>
    public static async Task<BitmapImage?> GetAsync(string url)
    {
        var host = HostOf(url);
        if (host is null) return null;
        var file = Path.Combine(Folder, host.Replace(':', '_') + ".png");
        try
        {
            if (!File.Exists(file))
            {
                var bytes = await Http.GetByteArrayAsync("https://www.google.com/s2/favicons?sz=64&domain=" + Uri.EscapeDataString(host));
                if (bytes.Length < 100) return null;
                Directory.CreateDirectory(Folder);
                await File.WriteAllBytesAsync(file, bytes);
            }
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }
}
