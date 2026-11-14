// D-Island (personal fork) - synced lyrics (LRCLIB) and real album art (iTunes Search) for the current track.
//
// Both look the track up by its title and artist. Nothing else is sent. Results are cached for the session.

using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DIsland.Services;

public sealed record LyricLine(TimeSpan Time, string Text);

public static class MusicLookup
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Dictionary<string, List<LyricLine>?> LyricsCache = new();
    private static readonly Dictionary<string, byte[]?> ArtCache = new();

    static MusicLookup() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("D-Island/1.0 (personal)");

    /// <summary>Browser titles carry noise: "(Official Video)", "[Lyrics]", "ft. X", "- Topic", "VEVO".</summary>
    public static (string Title, string Artist) Clean(string title, string artist)
    {
        // "Song | Film | Singers | Label": the song is the first part.
        var t = title.Split('|')[0];
        t = Regex.Replace(t, @"[\(\[][^\)\]]*(official|lyric|audio|video|visuali[sz]er|hd|4k|8k|remaster|live|full song)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s*[-–—]\s*(official|lyrics?|audio|video|youtube).*$", "", RegexOptions.IgnoreCase);
        // trailing noise words: "Qaafirana 8K Video", "Song Lyrical Video"
        t = Regex.Replace(t, @"(\s+(official|full|lyrical|lyric|lyrics|music|video|song|audio|hd|4k|8k|1080p|mv))+\s*$", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s+(ft\.?|feat\.?|featuring)\s+.*$", "", RegexOptions.IgnoreCase).Trim();
        var a = Regex.Replace(artist, @"\s*-\s*Topic$|VEVO$", "", RegexOptions.IgnoreCase).Trim();
        // "Artist - Song" in the title of a YouTube video
        var split = Regex.Match(t, @"^(?<a>.+?)\s[-–—]\s(?<s>.+)$");
        if (split.Success && (a.Length == 0 || split.Groups["a"].Value.Length < 40))
        {
            a = split.Groups["a"].Value.Trim();
            t = split.Groups["s"].Value.Trim();
        }
        return (t.Trim(), a);
    }

    /// <summary>Channels that are labels or networks say nothing about who sings: leave them out of the search.</summary>
    private static bool LooksLikeLabel(string artist) =>
        Regex.IsMatch(artist, @"(music|records|entertainment|company|official|network|studios?|films?|t-series|vevo|channel)", RegexOptions.IgnoreCase);

    private static IEnumerable<string> Queries(string title, string artist)
    {
        if (artist.Length > 0 && !LooksLikeLabel(artist)) yield return $"{title} {artist}";
        yield return title;
    }

    // ------------------------------------------------------------------ lyrics

    /// <summary>Time-synced lyrics from lrclib.net, or null when there are none.</summary>
    public static async Task<List<LyricLine>?> LyricsAsync(string title, string artist, TimeSpan duration, CancellationToken cancel)
    {
        var (t, a) = Clean(title, artist);
        if (t.Length == 0) return null;
        var key = $"{a}|{t}|{(int)duration.TotalSeconds / 4}";
        if (LyricsCache.TryGetValue(key, out var cached)) return cached;

        List<LyricLine>? lines = null;
        try
        {
            foreach (var query in Queries(t, a))
            {
                var url = "https://lrclib.net/api/search?q=" + Uri.EscapeDataString(query);
                using var response = await Http.GetAsync(url, cancel);
                if (!response.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
                JsonElement? best = null;
                var bestGap = double.MaxValue;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("syncedLyrics", out var synced) || synced.ValueKind != JsonValueKind.String) continue;
                    var gap = item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && duration > TimeSpan.Zero
                        ? Math.Abs(d.GetDouble() - duration.TotalSeconds) : 30;
                    if (gap < bestGap) { bestGap = gap; best = item; }
                }
                if (best is { } found && bestGap <= 20)
                {
                    lines = Parse(found.GetProperty("syncedLyrics").GetString() ?? "");
                    if (lines is not null) break;
                }
            }
        }
        catch (Exception) when (!cancel.IsCancellationRequested) { /* offline or no match */ }

        if (!cancel.IsCancellationRequested) LyricsCache[key] = lines;
        return lines;
    }

    private static List<LyricLine>? Parse(string lrc)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var m = Regex.Match(raw, @"^\[(\d+):(\d+(?:\.\d+)?)\]\s*(.*)$");
            if (!m.Success) continue;
            var time = TimeSpan.FromSeconds(int.Parse(m.Groups[1].Value) * 60 + double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            lines.Add(new LyricLine(time, m.Groups[3].Value.Trim()));
        }
        return lines.Count > 0 ? lines : null;
    }

    /// <summary>The line being sung at <paramref name="position"/>, and the next one.</summary>
    public static (string Current, string Next) LineAt(List<LyricLine> lines, TimeSpan position)
    {
        var index = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Time <= position + TimeSpan.FromMilliseconds(150)) index = i;
            else break;
        }
        var current = index >= 0 ? lines[index].Text : "";
        var next = index + 1 < lines.Count ? lines[index + 1].Text : "";
        return (current, next);
    }

    // ------------------------------------------------------------------ artwork

    /// <summary>Album art (about 600 px) from the iTunes Search API, or null.</summary>
    public static async Task<byte[]?> ArtAsync(string title, string artist, CancellationToken cancel)
    {
        var (t, a) = Clean(title, artist);
        if (t.Length == 0) return null;
        var key = $"{a}|{t}";
        if (ArtCache.TryGetValue(key, out var cached)) return cached;

        byte[]? bytes = null;
        try
        {
            var url = "https://itunes.apple.com/search?media=music&entity=song&limit=1&term=" + Uri.EscapeDataString(Queries(t, a).First());
            using var response = await Http.GetAsync(url, cancel);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
                var results = doc.RootElement.GetProperty("results");
                if (results.GetArrayLength() > 0 && results[0].TryGetProperty("artworkUrl100", out var art) && art.GetString() is { Length: > 0 } small)
                    bytes = await Http.GetByteArrayAsync(small.Replace("100x100bb", "600x600bb"), cancel);
            }
        }
        catch (Exception) when (!cancel.IsCancellationRequested) { }

        if (!cancel.IsCancellationRequested) ArtCache[key] = bytes;
        return bytes;
    }
}
