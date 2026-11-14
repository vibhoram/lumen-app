// D-Island (personal fork) - Groq chat client (OpenAI-compatible API): streaming, images and tool calls.

using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace DIsland.Services;

/// <summary>One turn in the conversation. Tool turns carry the call id; an assistant turn that asked for tools carries them as JSON.</summary>
public sealed record ChatMessage(string Role, string Text, string? ImageDataUrl = null, string? ToolCallId = null, string? ToolCallsJson = null);

public sealed record ToolCall(string Id, string Name, string Arguments);

/// <summary>Either a piece of reply text, or (at the end of a turn) the tools the model wants run.</summary>
public sealed record GroqEvent(string? Text, IReadOnlyList<ToolCall>? Calls);

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

    /// <summary>Streams one model turn: reply text as it arrives, then any tool calls the model made.</summary>
    public static async IAsyncEnumerable<GroqEvent> StreamAsync(
        string? apiKey, string model, string system, IReadOnlyList<ChatMessage> history, JsonArray? tools,
        [EnumeratorCancellation] CancellationToken cancel, string? endpoint = null, int maxTokens = 1400)
    {
        // Only the newest image goes out: older screenshots would burn tokens and the model's image limit.
        var lastImage = -1;
        for (var i = 0; i < history.Count; i++) if (history[i].ImageDataUrl is not null) lastImage = i;

        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        for (var i = 0; i < history.Count; i++)
        {
            var m = history[i];
            if (m.Role == "tool")
            {
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = m.ToolCallId, ["content"] = m.Text });
            }
            else if (m.ToolCallsJson is not null)
            {
                messages.Add(new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = string.IsNullOrEmpty(m.Text) ? null : m.Text,
                    ["tool_calls"] = JsonNode.Parse(m.ToolCallsJson),
                });
            }
            else if (m.ImageDataUrl is not null && i == lastImage)
            {
                messages.Add(new JsonObject
                {
                    ["role"] = m.Role,
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = m.Text },
                        new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = m.ImageDataUrl } },
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
            ["temperature"] = 0.7,
            ["max_tokens"] = maxTokens,
        };
        if (tools is not null)
        {
            body["tools"] = JsonNode.Parse(tools.ToJsonString());
            body["tool_choice"] = "auto";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint ?? Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.UserAgent.ParseAdd("D-Island");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancel);
            throw new GroqException((int)response.StatusCode, ExtractError(text, response.ReasonPhrase));
        }

        var calls = new SortedDictionary<int, (string Id, string Name, StringBuilder Args)>();
        using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancel) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line[5..].Trim();
            if (payload == "[DONE]") break;
            string? chunk = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() > 0 && choices[0].TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        chunk = content.GetString();
                    if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcs.EnumerateArray())
                        {
                            var index = tc.TryGetProperty("index", out var ix) ? ix.GetInt32() : 0;
                            if (!calls.TryGetValue(index, out var entry)) entry = ("", "", new StringBuilder());
                            var id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? entry.Id : entry.Id;
                            var name = entry.Name;
                            if (tc.TryGetProperty("function", out var fn))
                            {
                                if (fn.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nm) name = nm;
                                if (fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String) entry.Args.Append(a.GetString());
                            }
                            calls[index] = (id, name, entry.Args);
                        }
                    }
                }
            }
            catch (JsonException) { /* keep-alive or partial line */ }
            if (!string.IsNullOrEmpty(chunk)) yield return new GroqEvent(chunk, null);
        }

        if (calls.Count > 0)
            yield return new GroqEvent(null, calls.Values
                .Select((c, i) => new ToolCall(string.IsNullOrEmpty(c.Id) ? $"call_{i}" : c.Id, c.Name, c.Args.Length == 0 ? "{}" : c.Args.ToString()))
                .ToList());
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
