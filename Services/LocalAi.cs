// Lumen (personal fork) - offline chat through Ollama: plain text conversations run on this PC (no internet, no key);
// anything with a picture goes to Groq, which can see images. Ollama unloads a model after about five minutes idle,
// so the first message after a break loads it (a few seconds) and the next ones are quick.

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DIsland.Services;

public static class LocalAi
{
    public const string BaseUrl = "http://localhost:11434";
    public const string ChatEndpoint = BaseUrl + "/v1/chat/completions";

    // Fastest first. llama3.2:3b is small and quick and handles tools; the others are fallbacks.
    private static readonly string[] Preferred = { "llama3.2:3b", "llama3.1:8b", "qwen3.5:4b", "gemma3:4b" };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static DateTime _checkedAt = DateTime.MinValue;
    private static List<string> _installed = new();

    private static async Task<List<string>> InstalledAsync()
    {
        if ((DateTime.UtcNow - _checkedAt).TotalSeconds < 20) return _installed;
        try
        {
            using var response = await Http.GetAsync(BaseUrl + "/api/tags");
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                _installed = doc.RootElement.GetProperty("models").EnumerateArray()
                    .Select(m => m.GetProperty("name").GetString() ?? "").Where(n => n.Length > 0).ToList();
            }
            else _installed = new();
        }
        catch (Exception) { _installed = new(); } // Ollama isn't running
        _checkedAt = DateTime.UtcNow;
        return _installed;
    }

    /// <summary>The model to chat with offline: your choice if installed, otherwise the best fast one found; null when Ollama isn't there.</summary>
    public static async Task<string?> PickModelAsync(AppSettings settings)
    {
        var installed = await InstalledAsync();
        if (installed.Count == 0) return null;
        var wanted = settings.LocalModel.Trim();
        if (wanted.Length > 0)
        {
            var match = installed.FirstOrDefault(n => n.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                        ?? installed.FirstOrDefault(n => n.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        foreach (var p in Preferred)
            if (installed.Contains(p, StringComparer.OrdinalIgnoreCase)) return installed.First(n => n.Equals(p, StringComparison.OrdinalIgnoreCase));
        return installed.FirstOrDefault(n => !n.Contains("embed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Loads the model into memory ahead of the first message (Ollama keeps it for 5 minutes).</summary>
    public static async Task WarmUpAsync(AppSettings settings)
    {
        try
        {
            if (!settings.AssistantUseLocalAi) return;
            var model = await PickModelAsync(settings);
            if (model is null) return;
            using var warm = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var body = new StringContent(JsonSerializer.Serialize(new { model, keep_alive = "5m" }), Encoding.UTF8, "application/json");
            using var _ = await warm.PostAsync(BaseUrl + "/api/generate", body); // an empty prompt just loads the model
        }
        catch (Exception) { /* best effort */ }
    }

    private static readonly Regex Actionable = new(
        @"\b(timer|alarm|remind|reminder|to-?do|todo|task|tasks|topic|topics|list|add|change|switch|set|make|turn|enable|disable|delete|remove|focus|pomodoro|study|open|launch|play|pause|next|previous|skip|copy|clipboard|remember|forget|note|notes|look|theme|pet|page|ram|memory|cpu|slow|notification|notifications|quiet|silence|snooze|volume)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Small models are better chatters than tool users: only offer tools when the message sounds like a command.</summary>
    public static bool LooksActionable(string text) => Actionable.IsMatch(text);
}
