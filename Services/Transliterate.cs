// D-Island (personal fork) - Hindi (Devanagari) lyrics written the way people type Hindi in English letters ("Hinglish").

using System.Text;

namespace DIsland.Services;

public static class Transliterate
{
    private static readonly Dictionary<char, string> Consonants = new()
    {
        ['क'] = "k", ['ख'] = "kh", ['ग'] = "g", ['घ'] = "gh", ['ङ'] = "ng",
        ['च'] = "ch", ['छ'] = "chh", ['ज'] = "j", ['झ'] = "jh", ['ञ'] = "ny",
        ['ट'] = "t", ['ठ'] = "th", ['ड'] = "d", ['ढ'] = "dh", ['ण'] = "n",
        ['त'] = "t", ['थ'] = "th", ['द'] = "d", ['ध'] = "dh", ['न'] = "n",
        ['प'] = "p", ['फ'] = "ph", ['ब'] = "b", ['भ'] = "bh", ['म'] = "m",
        ['य'] = "y", ['र'] = "r", ['ल'] = "l", ['व'] = "v", ['श'] = "sh", ['ष'] = "sh", ['स'] = "s", ['ह'] = "h",
        ['ळ'] = "l",
        // precomposed nukta letters (U+0958..U+095F)
        ['क़'] = "q", ['ख़'] = "kh", ['ग़'] = "g", ['ज़'] = "z", ['ड़'] = "r", ['ढ़'] = "rh", ['फ़'] = "f", ['य़'] = "y",
    };

    private static readonly Dictionary<char, string> Vowels = new()
    {
        ['अ'] = "a", ['आ'] = "aa", ['इ'] = "i", ['ई'] = "ee", ['उ'] = "u", ['ऊ'] = "oo", ['ऋ'] = "ri",
        ['ए'] = "e", ['ऐ'] = "ai", ['ओ'] = "o", ['औ'] = "au", ['ऑ'] = "o", ['ऍ'] = "e",
    };

    private static readonly Dictionary<char, string> Matras = new()
    {
        ['ा'] = "a", ['ि'] = "i", ['ी'] = "i", ['ु'] = "u", ['ू'] = "u", ['ृ'] = "ri",
        ['े'] = "e", ['ै'] = "ai", ['ो'] = "o", ['ौ'] = "au", ['ॉ'] = "o", ['ॅ'] = "e",
    };

    public static bool HasDevanagari(string text) => text.Any(c => c >= '\u0900' && c <= '\u097F');

    /// <summary>Rewrites any Devanagari in <paramref name="text"/> in Latin letters; other text is kept as it is.</summary>
    public static string ToLatin(string text)
    {
        if (!HasDevanagari(text)) return text;
        var result = new StringBuilder();
        var word = new StringBuilder();
        void Flush()
        {
            if (word.Length == 0) return;
            result.Append(Word(word.ToString()));
            word.Clear();
        }
        foreach (var c in text)
        {
            if (c >= '\u0900' && c <= '\u097F' && c != '\u0964' && c != '\u0965') word.Append(c);
            else
            {
                Flush();
                result.Append(c is '\u0964' or '\u0965' ? '.' : c);
            }
        }
        Flush();
        var s = result.ToString();
        // sentence-style capital at the start of the line
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }

    private sealed class Unit
    {
        public string Consonant = "";   // Latin letters of the consonant (empty for a bare vowel)
        public string? Vowel;           // explicit vowel; null = inherent "a"; "" = none (halant / end)
        public bool KeepsVowel => Vowel is null or { Length: > 0 };
        public string Extra = "";       // anusvara / visarga that follows
    }

    /// <summary>Very common words people spell a fixed way when typing Hindi in English.</summary>
    private static readonly Dictionary<string, string> Common = new()
    {
        ["में"] = "mein", ["मैं"] = "main", ["यह"] = "yeh", ["वह"] = "woh", ["वो"] = "woh", ["ये"] = "ye", ["नहीं"] = "nahi",
        ["हूँ"] = "hoon", ["हूं"] = "hoon", ["है"] = "hai", ["हैं"] = "hain", ["और"] = "aur", ["मैंने"] = "maine", ["कि"] = "ki",
        ["क्या"] = "kya", ["तेरे"] = "tere", ["मेरे"] = "mere", ["तेरा"] = "tera", ["मेरा"] = "mera", ["तेरी"] = "teri", ["मेरी"] = "meri",
    };

    private static string Word(string w)
    {
        if (Common.TryGetValue(w, out var known)) return known;
        var units = new List<Unit>();
        for (var i = 0; i < w.Length; i++)
        {
            var c = w[i];
            // क्ष and ज्ञ read as single sounds
            if ((c == 'क' && i + 2 < w.Length && w[i + 1] == '्' && w[i + 2] == 'ष') || (c == 'ज' && i + 2 < w.Length && w[i + 1] == '्' && w[i + 2] == 'ञ'))
            {
                units.Add(new Unit { Consonant = c == 'क' ? "ksh" : "gy" });
                i += 2;
                continue;
            }
            if (Consonants.TryGetValue(c, out var latin))
            {
                if (i + 1 < w.Length && w[i + 1] == '़') // nukta after a plain consonant
                {
                    latin = c switch { 'ज' => "z", 'फ' => "f", 'क' => "q", 'ख' => "kh", 'ग' => "g", 'ड' => "r", 'ढ' => "rh", _ => latin };
                    i++;
                }
                units.Add(new Unit { Consonant = latin });
            }
            else if (Vowels.TryGetValue(c, out var vowel))
            {
                units.Add(new Unit { Vowel = vowel });
            }
            else if (units.Count > 0)
            {
                var last = units[^1];
                if (Matras.TryGetValue(c, out var matra)) last.Vowel = matra;
                else if (c == '्') last.Vowel = "";
                else if (c == 'ं' || c == 'ँ') last.Extra = "n";
                else if (c == 'ः') last.Extra = "h";
            }
        }

        // Hindi drops the inherent "a" at the end of a word and in the middle of VC_CV patterns:
        // करता -> karta, but समझ -> samajh.
        if (units.Count > 0 && units[^1].Vowel is null && units[^1].Consonant.Length > 0 && units.Count > 1) units[^1].Vowel = "";
        for (var i = 1; i < units.Count - 1; i++)
        {
            var u = units[i];
            if (u.Vowel is not null || u.Consonant.Length == 0) continue;
            if (units[i - 1].KeepsVowel && units[i + 1].KeepsVowel) u.Vowel = "";
        }

        var sb = new StringBuilder();
        for (var i = 0; i < units.Count; i++)
        {
            var u = units[i];
            sb.Append(u.Consonant);
            sb.Append(u.Vowel ?? (u.Consonant.Length > 0 ? "a" : ""));
            if (u.Extra.Length > 0)
            {
                // "m" before p/b/m sounds, otherwise "n"
                var nextStarts = i + 1 < units.Count ? units[i + 1].Consonant : "";
                sb.Append(u.Extra == "n" && nextStarts.Length > 0 && "pbm".Contains(nextStarts[0]) ? "m" : u.Extra);
            }
        }
        return sb.ToString();
    }
}
