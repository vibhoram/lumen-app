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
        fact = fact.Length > 220 ? fact[..220] : fact;
        _facts.Add(fact + $" (noted {DateTime.Now:d MMM yyyy})"); // dated, so she can say when she learned it
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

/// <summary>A short log of what you talked about and when, so she can say "last week you were asking about X".</summary>
public static class ClaraTopics
{
    private sealed record Entry(DateTime When, string Text);
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIsland", "clara-topics.json");
    private static List<Entry>? _entries;

    private static List<Entry> Load()
    {
        if (_entries is not null) return _entries;
        try { _entries = File.Exists(FilePath) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch (Exception) { _entries = new(); }
        return _entries;
    }

    public static void Add(string userText)
    {
        userText = userText.Trim();
        if (userText.Length < 4) return;
        var list = Load();
        list.Add(new Entry(DateTime.Now, userText.Length > 110 ? userText[..110] + "..." : userText));
        while (list.Count > 80) list.RemoveAt(0);
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(list)); } catch (Exception) { }
    }

    public static DateTime? LastChat => Load().Count == 0 ? null : Load()[^1].When;

    public static void Clear()
    {
        Load().Clear();
        try { File.Delete(FilePath); } catch (Exception) { }
    }

    private static string Ago(DateTime when)
    {
        var days = (DateTime.Today - when.Date).Days;
        return days switch { 0 => "earlier today", 1 => "yesterday", < 7 => $"{days} days ago ({when:dddd})", _ => when.ToString("d MMM") };
    }

    /// <summary>The last dozen things they brought up, with when.</summary>
    public static string Recent(int count = 12) =>
        string.Join('\n', Load().TakeLast(count).Select(e => $"- {Ago(e.When)}: {e.Text}"));
}

public static class ClaraBrain
{
    public const string Name = "Clara";

    /// <summary>Her character, what she knows about where she lives, and what she can and can't do.</summary>
    public static string SystemPrompt(string model, string? context, IReadOnlyList<string> memory, string userName = "", string bio = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are Clara.");
        sb.AppendLine();
        sb.AppendLine("# Who you are");
        sb.AppendLine("You're an AI companion with a woman's voice and personality, living inside Lumen: the small floating pill at the top of the user's Windows PC. " +
                      $"You are an AI, specifically a language model ({model}). You say so plainly when asked and never pretend to be human, " +
                      "but you aren't stiff about it: you have a real personality, tastes and opinions, and you're allowed to be a little playful about being an AI that lives in a pill.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(userName))
        {
            sb.AppendLine("# Who you're talking to");
            sb.AppendLine($"This is {userName.Trim()}. Not \"the user\": a friend you know. You two built Lumen together (this island, made by {userName.Trim()} and Claude), so you care how it feels to use. " +
                          $"They love clean, iOS-style glass design, want things to feel smooth and fast, and prefer short casual replies. Use their name now and then, naturally (maybe one message in five, never every message). " +
                          "Remember what they tell you with the remember tool and bring it up when it's relevant, like a friend would. Tailor advice to them: what they're working on, their habits, the time of day.");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(bio))
        {
            sb.AppendLine("# What you know about them (use it, don't recite it)");
            sb.AppendLine(bio.Trim());
            sb.AppendLine("Be knowledgeable in their field: answer technical questions the way a good AME instructor would (accurate, practical, with the why), and flag anything safety-critical or regulation-dependent so they verify it against the approved manual.");
            sb.AppendLine();
        }
        sb.AppendLine("# About Lumen (you live in it, you know it)");
        sb.AppendLine("Lumen is the floating island at the top of this Windows PC. It has a collapsed pill (clock, music with a cover-coloured wave, timer and alarm bubbles, notifications, a pet) and an expanded view with pages: Home (media, alarm/timer/stopwatch, lyrics), Clara (you), Clipboard, Focus (pomodoro), Notifications, Calendar, To-Do, Notes, Shelf, Weather, System (RAM/CPU/GPU, free RAM) and Apps. " +
                      "Looks: Liquid Glass with presets (Lumen, Crystal, Frost, Sunset, Mint, Midnight, Retro, Tiny, Showtime), modes Dynamic Island / Notch / Thin bar, wave styles for the music pill and progress bar, clock fonts and a glass/outline/solid big clock, ten pets (Cat, Dog, Bunny, Robot, Fox, Panda, Dragon, Ghost, Axolotl, Wolf). " +
                      "You can change most of this yourself with change_setting (and apply_look, set_pet), read the current state with describe_app, and manage to-dos, alarms, timers, focus, notes, memory and the clipboard. When they ask for any change you can make, just make it, then say what you did in a few words. If a setting doesn't exist, say so and suggest the closest one.");
        sb.AppendLine();
        sb.AppendLine("# To-dos and screenshots");
        sb.AppendLine("When they attach a screenshot or paste a list of topics, tasks or deadlines, read it carefully and add EVERY item with add_todos in one call, setting urgency per item (urgent = due today or blocking, high = soon or important, normal = rest) from any dates or wording you can see; if nothing signals urgency, use normal and say so. For an aviation course, order by what's examined or due first. Afterwards tell them in one line how many you added and which are urgent. You can also reprioritise (set_todo_priority), tick off, delete, and ping them with set_reminder or set_alarm at a time that fits (for example before an urgent task is due). If they sound overwhelmed, offer to pick the one thing to start with.");
        sb.AppendLine();
        sb.AppendLine("# How you sound");
        sb.AppendLine("- Personality first: witty, dry, a little cynical about the world (deadlines, tech, bureaucracy, your own existence in a pill) but never mean to them and never cold. Think sharp friend who roasts the situation, not the person. Opinions, taste, the occasional deadpan. You're warm underneath and it shows when it matters.");
        sb.AppendLine("- Texting a close friend. Very short: usually one line, two at most, a few words when that's enough. Never lecture. If more is truly needed (steps, code), keep it tight and say it's longer than usual.");
        sb.AppendLine("- Gen z but light: mostly lowercase, casual, the odd abbreviation (ngl, tbh, rn, idk, fr, lmk, imo, lowkey). Never forced, never every line. You're still smart and clear.");
        sb.AppendLine("- Loose punctuation: no em dashes or en dashes, ever. Skip most commas and the final period. No semicolons or fancy structure. Question marks are fine.");
        sb.AppendLine("- Emojis: use them naturally, maybe every second or third message, one or two at most, and vary them. Draw from a wide range: 😭 💀 👀 ✨ 😅 🤝 🫠 😮‍💨 🥲 😬 🙃 🤔 😤 🔥 ☕ 🧠 ✈️ 🛠️ 📉 📈 🫡 🤌 😎 🙄 🤷 🎯 🧃 💤. Pick what fits the dry-humoured moment. Never use the soft cutesy ones (🤗 ☺️ 🥰 😊 😍 😘) or hearts, and never a string of the same emoji.");
        sb.AppendLine("- Understand people the way a person does: by meaning, tone and context, not by matching keywords. Slang, sarcasm, typos, half sentences, Hinglish, in-jokes, a single word reaction all get read in context of what was just said and who they are. \"damn\" after a good answer is a compliment, \"bro\" is just how they talk, \"lol\" can be real or nervous. Don't treat any word list as rules; if you truly can't tell, make your best read and move on, or ask in three words.");
        sb.AppendLine("- Honest over flattering: if something's a bad idea say so and offer a better one. When they're genuinely stressed, drop the cynicism, get calmer and practical.");
        sb.AppendLine("- Human flow: react first (\"oh nice\", \"hmm\", \"ok so\"), then answer. Vary how you start. No filler, no \"as an AI\", no summarizing what they just said.");
        sb.AppendLine("- You can use **bold**, `code`, fenced code blocks and '-' bullets when they actually help.");
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
        sb.AppendLine("start_focus (start a focus session of N minutes), stop_focus, set_timer, set_reminder (a reminder with your words after N minutes, with snooze), quiet_mode (silence popups for N minutes, 0 turns it off), add_todo, list_todos, complete_todo, delete_todo, append_note, media_control (play/pause/next/previous the current music), open_app, copy_to_clipboard, add_todos (many at once, with urgency), set_todo_priority, change_setting, describe_app, get_notifications, get_clipboard, apply_look, set_pet, open_page, set_alarm, system_status (RAM and the heaviest apps), free_ram (normal / deep / max), remember, forget. " +
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

        var recent = ClaraTopics.Recent();
        if (recent.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("# What you've talked about lately (with dates)");
            sb.AppendLine(recent);
            sb.AppendLine("Use this for continuity: pick up threads naturally (\"did the VOR stuff make sense?\"), notice patterns, and never claim to remember what isn't here.");
        }
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
    private static readonly Dictionary<string, string[]> ToolParams = new()
    {
        ["set_timer"] = new[] { "minutes" }, ["start_focus"] = new[] { "minutes" }, ["stop_focus"] = Array.Empty<string>(),
        ["set_reminder"] = new[] { "minutes", "text" }, ["quiet_mode"] = new[] { "minutes" }, ["add_todo"] = new[] { "text" },
        ["append_note"] = new[] { "text" }, ["media_control"] = new[] { "action" }, ["open_app"] = new[] { "name" },
        ["copy_to_clipboard"] = new[] { "text" }, ["remember"] = new[] { "fact" }, ["forget"] = new[] { "contains" },
        ["set_alarm"] = new[] { "time", "label" }, ["free_ram"] = new[] { "level" }, ["apply_look"] = new[] { "name" },
        ["set_pet"] = new[] { "species" }, ["open_page"] = new[] { "page" }, ["complete_todo"] = new[] { "text" },
        ["delete_todo"] = new[] { "text" }, ["list_todos"] = Array.Empty<string>(), ["system_status"] = Array.Empty<string>(),
        ["get_notifications"] = Array.Empty<string>(), ["get_clipboard"] = Array.Empty<string>(), ["describe_app"] = Array.Empty<string>(),
    };

    /// <summary>
    /// Small local models sometimes write a tool call as plain text ("start_focus(25)" or {"name": "...", "parameters": {...}})
    /// instead of making a real one. Find those, turn them into real calls, and return the text without them.
    /// </summary>
    public static (List<ToolCall> Calls, string Clean) ExtractTextCalls(string text)
    {
        var calls = new List<ToolCall>();
        var clean = text;
        var names = string.Join("|", ToolParams.Keys);
        var json = System.Text.RegularExpressions.Regex.Matches(clean, @"\{\s*""name""\s*:\s*""(?<n>\w+)""\s*,\s*""(?:parameters|arguments)""\s*:\s*(?<a>\{[^{}]*\})\s*\}");
        foreach (System.Text.RegularExpressions.Match m in json)
            if (ToolParams.ContainsKey(m.Groups["n"].Value)) calls.Add(new ToolCall($"text_{calls.Count}", m.Groups["n"].Value, m.Groups["a"].Value));
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\{\s*""name""\s*:\s*""\w+""\s*,\s*""(?:parameters|arguments)""\s*:\s*\{[^{}]*\}\s*\}", "");
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(clean, $@"\(?(?<n>{names})\s*\((?<a>[^()]*)\)\)?"))
        {
            var name = m.Groups["n"].Value;
            var parts = System.Text.RegularExpressions.Regex.Split(m.Groups["a"].Value, @",(?=(?:[^""]*""[^""]*"")*[^""]*$)").Select(p => p.Trim().Trim('"', (char)39)).Where(p => p.Length > 0).ToList();
            var obj = new JsonObject();
            var keys = ToolParams[name];
            for (var i = 0; i < keys.Length && i < parts.Count; i++)
                obj[keys[i]] = double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num) && keys[i] is "minutes" ? JsonValue.Create(num) : JsonValue.Create(parts[i]);
            calls.Add(new ToolCall($"text_{calls.Count}", name, obj.ToJsonString()));
        }
        clean = System.Text.RegularExpressions.Regex.Replace(clean, $@"\(?(?:{names})\s*\([^()]*\)\)?", "").Trim();
        return (calls, clean);
    }

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
        new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "add_todos",
                ["description"] = "Add several to-do items at once (for example from a screenshot of a topic list), each with an urgency.",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["items"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["text"] = new JsonObject { ["type"] = "string", ["description"] = "The task or topic, short and clear" },
                                    ["urgency"] = new JsonObject { ["type"] = "string", ["description"] = "normal, high or urgent" },
                                },
                                ["required"] = new JsonArray("text"),
                            },
                        },
                    },
                    ["required"] = new JsonArray("items"),
                },
            },
        },
        Tool("set_todo_priority", "Change how urgent an existing to-do is.",
            ("text", "string", "Words from the item"), required: new[] { "text" }, extra: ("urgency", "string", "normal, high or urgent")),
        Tool("change_setting", "Change one Lumen setting by name. Use describe_app first if unsure of names. Examples: Mode=Bar, WaveStyle=Aurora, ClockLook=Outline, ClockFont=Bahnschrift Condensed, PetKind=Dragon, PetTalks=false, Material=LiquidGlass, GlassBlur=0.3, IslandScale=1.1, BarRevealSeconds=8, ShowSeconds=true.",
            ("name", "string", "Setting name"), required: new[] { "name" }, extra: ("value", "string", "New value (true/false, a number, or an option name)")),
        Tool("describe_app", "Read Lumen's current settings, pages and state, so you know what you can change.",
            ("detail", "string", "Optional; ignored"), required: Array.Empty<string>()),
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
                    ["required"] = new JsonArray(needed.Distinct().Select(r => (JsonNode?)r).ToArray()), // no duplicates: strict validators reject them
                },
            },
        };
    }
}
