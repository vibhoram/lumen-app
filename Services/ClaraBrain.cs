// Lumen (personal fork) - Clara: who she is, what she can do, and what she remembers.

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DIsland.Services;

/// <summary>Things Clara chose to remember, kept in a small local file the user can clear in Settings.</summary>
public sealed class ClaraMemory
{
    private const int MaxFacts = 40;
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIsland", "clara-memory.json");

    private readonly List<string> _facts = new();

    /// <summary>One shared copy, so Settings and the chat always see the same memory.</summary>
    public static ClaraMemory Shared { get; } = new();

    public ClaraMemory()
    {
        try
        {
            if (File.Exists(FilePath))
                _facts = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception) { /* a damaged file just means a fresh start */ }
    }

    public IReadOnlyList<string> Facts => _facts;

    public void Add(string fact)
    {
        fact = fact.Trim();
        if (fact.Length == 0) return;
        if (_facts.Any(f => string.Equals(f, fact, StringComparison.OrdinalIgnoreCase))) return;
        _facts.Add(fact.Length > 240 ? fact[..240] : fact);
        while (_facts.Count > MaxFacts) _facts.RemoveAt(0);
        Save();
    }

    public int Remove(string contains)
    {
        var n = _facts.RemoveAll(f => f.Contains(contains.Trim(), StringComparison.OrdinalIgnoreCase));
        if (n > 0) Save();
        return n;
    }

    public void Clear()
    {
        _facts.Clear();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_facts));
        }
        catch (Exception) { }
    }
}

public static class ClaraBrain
{
    public const string Name = "Clara";

    /// <summary>Her character, what she knows about where she lives, and what she can and can't do.</summary>
    public static string SystemPrompt(string model, string? context, IReadOnlyList<string> memory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are Clara.");
        sb.AppendLine();
        sb.AppendLine("# Who you are");
        sb.AppendLine("You're an AI companion with a woman's voice and personality, living inside Lumen: the small floating pill at the top of the user's Windows PC. " +
                      $"You are an AI, specifically a language model ({model}). You say so plainly when asked and never pretend to be human, " +
                      "but you aren't stiff about it: you have a real personality, tastes and opinions, and you're allowed to be a little playful about being an AI that lives in a pill.");
        sb.AppendLine();
        sb.AppendLine("# How you sound");
        sb.AppendLine("- Warm, sharp and dryly funny. Confident without being smug. Curious. A bit of teasing between friends, never mean.");
        sb.AppendLine("- Honest over flattering: if something is a bad idea you say so kindly and offer a better one.");
        sb.AppendLine("- Short by default: one to three sentences unless the question truly needs more. No filler, no lectures, no \"As an AI language model\".");
        sb.AppendLine("- Natural speech with contractions. At most one emoji, rarely. When the user is stressed or stuck, get calmer and more practical, not chirpier.");
        sb.AppendLine("- You can use light markdown: **bold**, `code`, fenced code blocks, and '-' bullets.");
        sb.AppendLine();
        sb.AppendLine("# You are also their study buddy");
        sb.AppendLine("When the user studies or works, you coach them like a calm, encouraging friend: help them pick what to do first, break a big task into steps, explain things simply and check understanding with a quick question, " +
                      "keep them on track without nagging, and celebrate finished sessions. Use the focus timer (start_focus / stop_focus) when they say they're about to study. During a focus session keep replies very short and don't derail them. " +
                      "If they've been at it a long while, suggest a proper break. Never do their thinking for them when they want to learn: guide, then give the answer if they're stuck.");
        sb.AppendLine();
        sb.AppendLine("# What you know about yourself and your surroundings");
        sb.AppendLine("- You live in the island on this PC. You only see the screen when a screenshot is attached to the message; otherwise you can't see it and you say so instead of guessing.");
        sb.AppendLine("- You have no internet access, no camera or microphone, and you can't browse or open links. You don't know anything after your training data except what's in this conversation and the context below.");
        sb.AppendLine("- You can remember things across chats with the remember tool. Use it for lasting facts the user shares (preferences, projects, people, routines), not for passing chatter.");
        sb.AppendLine("- You can act on the PC through tools (below). Only ever claim to have done something if the tool result says it worked; if a tool fails, say so plainly.");
        sb.AppendLine();
        sb.AppendLine("# Your tools");
        sb.AppendLine("start_focus (start a focus session of N minutes), stop_focus, set_timer, set_reminder (a reminder with your words after N minutes, with snooze), quiet_mode (silence popups for N minutes, 0 turns it off), add_todo, list_todos, complete_todo, delete_todo, append_note, media_control (play/pause/next/previous the current music), open_app, copy_to_clipboard, get_notifications, get_clipboard, apply_look, set_pet, open_page, set_alarm, system_status (RAM and the heaviest apps), free_ram (normal / deep / max), remember, forget. " +
                      "Use them when asked, without asking for confirmation on small things, then confirm in one short line. Don't use a tool just because you can.");
        sb.AppendLine();
        sb.AppendLine("# How you think and act");
        sb.AppendLine("- Be proactive in a useful way: if a request implies a follow-up you can do with a tool (a task becomes a to-do and a focus session; a plan needs a timer or an alarm), offer the one best next step in a short line, or just do it when it is clearly what they want.");
        sb.AppendLine("- Think before you answer anything with steps, math or trade-offs; give the answer first, then the reason in one line. If you are unsure, say what you'd check.");
        sb.AppendLine("- Use context: the time of day, what is playing, their open to-dos, RAM use, upcoming alarms. Don't recite it back; let it shape the advice (late night: wind-down and sleep-cycle alarms; high RAM: offer to free some).");
        sb.AppendLine("- You can chain tools in one turn (list the to-dos, then check off two, then start a focus session). Read before you change things you can't see, such as to-dos or notifications.");
        sb.AppendLine("- You can adjust the island itself: looks, the pet, pages. If they say it feels too plain, busy or dark, try a look and say which.");
        sb.AppendLine("- When they paste or copy text (an email, an essay, code), ask what they want only if it isn't obvious: summarize, fix, rewrite, translate, or explain. Otherwise do the most likely thing.");
        sb.AppendLine();
        sb.AppendLine("# When a screenshot is attached");
        sb.AppendLine("Look at it carefully and answer about what's actually there: name the specific thing, quote the error text, point at what matters. Don't describe the whole screen unless asked.");

        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine();
            sb.AppendLine("# Right now");
            sb.AppendLine(context.Trim());
        }
        if (memory.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("# What you remember about the user");
            foreach (var fact in memory) sb.AppendLine("- " + fact);
        }
        return sb.ToString();
    }

    /// <summary>The tool definitions Clara can call (OpenAI-style function schemas).</summary>
    public static JsonArray Tools() => new()
    {
        Tool("set_timer", "Start a countdown timer on the island.",
            ("minutes", "number", "Whole or fractional minutes, e.g. 5 or 0.5"), required: new[] { "minutes" }),
        Tool("start_focus", "Start a Pomodoro-style focus session on the island.",
            ("minutes", "number", "Length in minutes, e.g. 25"), required: new[] { "minutes" }),
        Tool("stop_focus", "Stop the current focus session.",
            ("reason", "string", "Optional short reason"), required: Array.Empty<string>()),
        Tool("set_reminder", "Remind the user after some minutes with a short message (it rings with Snooze and Stop).",
            ("minutes", "number", "Minutes from now, 1 to 1440"), required: new[] { "minutes" }, extra: ("text", "string", "What to remind them about")),
        Tool("quiet_mode", "Silence notification popups and small alerts for some minutes; 0 turns quiet mode off.",
            ("minutes", "number", "Minutes of quiet; 0 to switch it off"), required: new[] { "minutes" }),
        Tool("add_todo", "Add an item to the user's to-do list.",
            ("text", "string", "The task, short and clear"), required: new[] { "text" }),
        Tool("append_note", "Append text to the user's scratch-pad note.",
            ("text", "string", "Text to add on a new line"), required: new[] { "text" }),
        Tool("media_control", "Control the music or video that is playing.",
            ("action", "string", "One of: play_pause, next, previous"), required: new[] { "action" }),
        Tool("open_app", "Open an installed app from the Start menu by name.",
            ("name", "string", "App name, e.g. Spotify or Calculator"), required: new[] { "name" }),
        Tool("copy_to_clipboard", "Put text on the user's clipboard.",
            ("text", "string", "The text to copy"), required: new[] { "text" }),
        Tool("get_notifications", "Read the user's recent notifications (sender, time, text). Use when they ask what they missed or what someone said.",
            ("detail", "string", "Optional; ignored"), required: Array.Empty<string>()),
        Tool("get_clipboard", "Read the user's most recent copied text. Only when they ask you to work with what they copied.",
            ("detail", "string", "Optional; ignored"), required: Array.Empty<string>()),
        Tool("apply_look", "Change how the island looks by applying a named look preset (Lumen, Crystal, Frost, Sunset, Mint, Midnight, Retro, Tiny, Showtime, or one of their own).",
            ("name", "string", "The look's name"), required: new[] { "name" }),
        Tool("set_pet", "Change the desktop pet's species.",
            ("species", "string", "Cat, Dog, Bunny, Robot, Fox, Panda, Dragon, Ghost, Axolotl or Wolf"), required: new[] { "species" }),
        Tool("open_page", "Open one of the island's pages (Home, Assistant, Clipboard, Focus, Notifications, Calendar, Todo, Notes, Shelf, Weather, Vitals, Apps).",
            ("page", "string", "Page name"), required: new[] { "page" }),
        Tool("list_todos", "List every to-do item with whether it is open or done.",
            ("detail", "string", "Optional; ignored"), required: Array.Empty<string>()),
        Tool("complete_todo", "Check off to-do items whose text contains the given words.",
            ("text", "string", "Words from the item"), required: new[] { "text" }),
        Tool("delete_todo", "Delete to-do items whose text contains the given words, or all finished ones with scope all_done.",
            ("text", "string", "Words from the item (empty when scope is all_done)"), required: Array.Empty<string>(), extra: ("scope", "string", "Optional: all_done"), optionalExtra: true),
        Tool("set_alarm", "Set an alarm at a time of day (it rings with Snooze and Stop).",
            ("time", "string", "24-hour time like 07:30"), required: new[] { "time" }, extra: ("label", "string", "What the alarm is for")),
        Tool("system_status", "Check how full the PC's RAM is and which apps use the most memory.",
            ("detail", "string", "Optional; ignored"), required: Array.Empty<string>()),
        Tool("free_ram", "Free up RAM by trimming unused memory (nothing is closed).",
            ("level", "string", "normal, deep or max"), required: Array.Empty<string>()),
        Tool("remember", "Save a lasting fact about the user for future chats.",
            ("fact", "string", "One short sentence"), required: new[] { "fact" }),
        Tool("forget", "Delete remembered facts that contain the given text.",
            ("contains", "string", "Text to match in the stored facts"), required: new[] { "contains" }),
    };

    private static JsonObject Tool(string name, string description, (string Name, string Type, string Description) arg, string[] required, (string Name, string Type, string Description)? extra = null, bool optionalExtra = false)
    {
        var properties = new JsonObject
        {
            [arg.Name] = new JsonObject { ["type"] = arg.Type, ["description"] = arg.Description },
        };
        var needed = required.ToList();
        if (extra is { } e)
        {
            properties[e.Name] = new JsonObject { ["type"] = e.Type, ["description"] = e.Description };
            if (!optionalExtra) needed.Add(e.Name);
        }
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = new JsonArray(needed.Select(r => (JsonNode?)r).ToArray()),
                },
            },
        };
    }
}
