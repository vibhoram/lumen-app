// Lumen (personal fork) - Ctrl+Alt + first free letter of each page name, with conflict resolution.

using System.Windows.Input;

namespace DIsland.Services;

/// <summary>
/// Default page shortcuts: Ctrl+Alt plus the first free letter of the page title.
/// Clara / hide / quick-paste keep their own settings; those letters are reserved here.
/// </summary>
public static class PageHotkeyMap
{
    /// <summary>Pages that get a dedicated open-shortcut (Clara uses <see cref="AppSettings.AssistantHotkey"/> instead).</summary>
    public static readonly string[] PageNames =
    {
        "Home", "Notifications", "Calendar", "Todo", "Notes", "Clipboard",
        "Focus", "Lyrics", "Vitals", "Shelf", "Weather", "Apps",
    };

    public static string TitleOf(string pageName) => pageName switch
    {
        "Todo" => "To-Do",
        "Vitals" => "System",
        "Apps" => "Apps",
        _ => pageName,
    };

    /// <summary>Fills missing keys in <paramref name="map"/> from the letter formula without overwriting customs.</summary>
    public static void EnsureDefaults(Dictionary<string, string> map, string? assistantHotkey, string? toggleHotkey, string? pasteHotkey)
    {
        var reserved = ReservedLetters(assistantHotkey, toggleHotkey, pasteHotkey);
        foreach (var (page, combo) in map)
        {
            if (Hotkey.TryParse(combo, out var mod, out var key) && IsCtrlAltLetter(mod, key))
                reserved.Add(char.ToUpperInvariant((char)KeyInterop.VirtualKeyFromKey(key)));
        }

        foreach (var page in PageNames)
        {
            if (map.ContainsKey(page)) continue;
            var letter = PickLetter(TitleOf(page), reserved);
            if (letter is null) continue;
            reserved.Add(letter.Value);
            map[page] = $"Ctrl+Alt+{letter}";
        }
    }

    /// <summary>Fresh map from the formula (for Reset shortcuts).</summary>
    public static Dictionary<string, string> BuildDefaults(string? assistantHotkey, string? toggleHotkey, string? pasteHotkey)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        EnsureDefaults(map, assistantHotkey, toggleHotkey, pasteHotkey);
        return map;
    }

    private static HashSet<char> ReservedLetters(string? assistant, string? toggle, string? paste)
    {
        var set = new HashSet<char>();
        foreach (var text in new[] { assistant, toggle, paste })
        {
            if (text is null || !Hotkey.TryParse(text, out var mod, out var key) || !IsCtrlAltLetter(mod, key)) continue;
            set.Add(char.ToUpperInvariant((char)KeyInterop.VirtualKeyFromKey(key)));
        }
        return set;
    }

    private static bool IsCtrlAltLetter(ModifierKeys mod, Key key) =>
        mod == (ModifierKeys.Control | ModifierKeys.Alt)
        && key is >= Key.A and <= Key.Z;

    private static char? PickLetter(string title, HashSet<char> used)
    {
        foreach (var c in title.Where(char.IsLetter).Select(char.ToUpperInvariant))
            if (used.Add(c)) return c;
        for (var c = 'A'; c <= 'Z'; c++)
            if (used.Add(c)) return c;
        return null;
    }
}
