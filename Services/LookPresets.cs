// Lumen (personal fork) - look presets: one click sets a whole appearance (material, glass, shape, wave, bars) instead of
// fiddling with a dozen sliders. Built-in ones ship here; "Save current look" stores your own in the settings file.

using System.Globalization;
using System.Reflection;

namespace DIsland.Services;

public sealed class LookPreset
{
    public string Name { get; set; } = "";
    /// <summary>Setting name → value as text (invariant culture).</summary>
    public Dictionary<string, string> Values { get; set; } = new();
}

public static class LookPresets
{
    /// <summary>The settings a preset is allowed to change. Anything else (Clara, pages, alarms, keys) is never touched.</summary>
    public static readonly string[] Keys =
    {
        "Material", "BackgroundOpacity", "GlassBlur", "GlassShine", "GlassRim", "GlassVibrancy", "GlassRefraction", "GlassTint",
        "IslandScale", "CornerRoundness", "OutlineWidth", "OutlineOpacity", "ShadowStrength", "WaveStyle", "EqualizerLook",
    };

    private static LookPreset Make(string name, params (string Key, object Value)[] values) => new()
    {
        Name = name,
        Values = values.ToDictionary(v => v.Key, v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? ""),
    };

    private static (string, object)[] Glass(double blur, double shine, double rim, double vib, double bend, double tint) => new (string, object)[]
    {
        ("Material", IslandMaterial.LiquidGlass), ("GlassBlur", blur), ("GlassShine", shine), ("GlassRim", rim),
        ("GlassVibrancy", vib), ("GlassRefraction", bend), ("GlassTint", tint),
    };

    public static readonly LookPreset[] BuiltIn =
    {
        Make("Lumen", Glass(0.45, 0.9, 0.1, 0.65, 0.6, 0.30).Concat(new (string, object)[] { ("IslandScale", 1.0), ("CornerRoundness", 1.0), ("OutlineWidth", 0.0), ("ShadowStrength", 0.0), ("WaveStyle", WaveStyle.Ribbon), ("EqualizerLook", EqualizerLook.Aura) }).ToArray()),
        Make("Crystal", Glass(0.30, 0.95, 0.15, 0.75, 0.70, 0.18).Concat(new (string, object)[] { ("CornerRoundness", 1.0), ("OutlineWidth", 0.0), ("WaveStyle", WaveStyle.Ribbon), ("EqualizerLook", EqualizerLook.Aura) }).ToArray()),
        Make("Frost", Glass(0.85, 0.7, 0.08, 0.55, 0.4, 0.30).Concat(new (string, object)[] { ("CornerRoundness", 0.9), ("WaveStyle", WaveStyle.Tide), ("EqualizerLook", EqualizerLook.Silver) }).ToArray()),
        Make("Sunset", Glass(0.40, 0.9, 0.18, 1.0, 0.65, 0.20).Concat(new (string, object)[] { ("WaveStyle", WaveStyle.Silk), ("EqualizerLook", EqualizerLook.Sunset) }).ToArray()),
        Make("Mint", Glass(0.05, 1.0, 0.20, 0.5, 0.85, 0.06).Concat(new (string, object)[] { ("WaveStyle", WaveStyle.Beads), ("EqualizerLook", EqualizerLook.Mint) }).ToArray()),
        Make("Midnight", ("Material", IslandMaterial.Opaque), ("CornerRoundness", 1.0), ("OutlineWidth", 0.0), ("ShadowStrength", 0.0), ("WaveStyle", WaveStyle.Tide), ("EqualizerLook", EqualizerLook.Silver)),
        Make("Retro", ("Material", IslandMaterial.Translucent), ("BackgroundOpacity", 0.7), ("CornerRoundness", 0.55), ("OutlineWidth", 1.0), ("OutlineOpacity", 0.35), ("WaveStyle", WaveStyle.Pulse), ("EqualizerLook", EqualizerLook.Song)),
        Make("Tiny", Glass(0.45, 0.8, 0.1, 0.6, 0.5, 0.3).Concat(new (string, object)[] { ("IslandScale", 0.85), ("WaveStyle", WaveStyle.Bars), ("EqualizerLook", EqualizerLook.Silver) }).ToArray()),
        Make("Showtime", Glass(0.35, 1.0, 0.2, 0.95, 0.75, 0.15).Concat(new (string, object)[] { ("IslandScale", 1.2), ("CornerRoundness", 1.0), ("WaveStyle", WaveStyle.Tide), ("EqualizerLook", EqualizerLook.Aura) }).ToArray()),
    };

    public static void Apply(AppSettings settings, LookPreset preset)
    {
        foreach (var (key, text) in preset.Values)
        {
            if (!Keys.Contains(key)) continue;
            var property = typeof(AppSettings).GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanWrite) continue;
            try
            {
                var type = property.PropertyType;
                object value = type.IsEnum ? Enum.Parse(type, text, true)
                    : type == typeof(bool) ? bool.Parse(text)
                    : type == typeof(int) ? int.Parse(text, CultureInfo.InvariantCulture)
                    : double.Parse(text, CultureInfo.InvariantCulture);
                property.SetValue(settings, value);
            }
            catch (Exception) { /* a value from an older version: skip it */ }
        }
    }

    public static LookPreset Capture(AppSettings settings, string name)
    {
        var preset = new LookPreset { Name = name };
        foreach (var key in Keys)
        {
            var value = typeof(AppSettings).GetProperty(key)?.GetValue(settings);
            if (value is not null) preset.Values[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
        return preset;
    }
}
