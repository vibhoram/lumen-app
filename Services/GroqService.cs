// D-Island (personal fork) - Groq chat client (OpenAI-compatible API) with streaming and image input.

using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace DIsland.Services;

public sealed record ChatMessage(string Role, string Text, string? ImageDataUrl = null);

public sealed class GroqException : Exception
{
    public int Status { get; }
    public GroqException(int status, string message) : base(message) => Status = status;
}

public static class GroqService
{
    public const string DefaultModel = "qwen/qwen3.8-27b";
    private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    private const string System =
        "You are the assistant inside D-Island, a small floating overlay on the user's Windows PC. " +
        "Be direct and concise: short paragraphs, no filler. When a screenshot is attached, it is what is " +
        "currently on the user's screen; answer about it specifically. Use light markdown only " +
        "(**bold**, `code`, fenced code blocks, '-' bullets).";

    /// <summary>Downscales and JPEG-encodes a screenshot into a data URL small enough for the API.</summary>
    public static string ToDataUrl(BitmapSource image, int maxEdge = 1400)
    {
        var scale = Math.Min(1.0, maxEdge / (double)Math.Max(image.PixelWidth, image.PixelHeight));
        BitmapSource source = image;
        if (scale < 1) source = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
        var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Streams the assistant's reply, token chunk by token chunk.</summary>
    public static async IAsyncEnumerable<string> StreamAsync(
        string apiKey, string model, IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancel)
    {
        // Only the newest image goes out: older screenshots would burn tokens and the model's image limit.
        var lastImage = -1;
        for (var i = 0; i < history.Count; i++) if (history[i].ImageDataUrl is not null) lastImage = i;

        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = System } };
        for (var i = 0; i < history.Count; i++)
        {
            var m = history[i];
            if (m.ImageDataUrl is not null && i == lastImage)
            {
                messages.Add(new JsonObject
                {
                    ["role"] = m.Role,
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = m.Text },
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject { ["url"] = m.ImageDataUrl },
                        },
                    },
                });
            }
            else
            {
                var text = m.ImageDataUrl is not null ? "[screenshot attached earlier] " + m.Text : m.Text;
                messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = text });
            }
        }

        var body = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(model) ? DefaultModel : model,
            ["messages"] = messages,
            ["stream"] = true,
            ["temperature"] = 0.6,
            ["max_tokens"] = 1200,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.UserAgent.ParseAdd("D-Island");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancel);
            throw new GroqException((int)response.StatusCode, ExtractError(text, response.ReasonPhrase));
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancel) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line[5..].Trim();
            if (payload == "[DONE]") yield break;
            string? chunk = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                    chunk = content.GetString();
            }
            catch (JsonException) { /* keep-alive or partial line */ }
            if (!string.IsNullOrEmpty(chunk)) yield return chunk;
        }
    }

    private static string ExtractError(string body, string? fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
                return m.GetString() ?? fallback ?? "Request failed";
        }
        catch (JsonException) { }
        return fallback ?? "Request failed";
    }
}
