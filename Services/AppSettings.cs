// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DIsland.Services;

public enum CloudProvider { Groq, Gemini, OpenRouter }

public enum IslandMode
{
    /// <summary>Floating pill just below the top edge.</summary>
    DynamicIsland,
    /// <summary>Attached to the top edge with flared shoulders, like a laptop notch.</summary>
    Notch,
    /// <summary>Only a thin glass line near the top; hover it and the island appears, then it folds back into the line.</summary>
    Bar,
}

public enum IslandMaterial
{
    Opaque,
    Translucent,
    /// <summary>Blurred backdrop with a light tint, specular rim and sheen.</summary>
    LiquidGlass,
}

public sealed class QuickLink
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Optional browser id (e.g. "comet") so this link skips the Windows default browser.</summary>
    public string? Browser { get; set; }
}

public enum PeekKey { Alt, Ctrl, Shift }

public enum EqualizerLook { Aura, Silver, Song, Mint, Sunset }

public enum CompactTime { Off, Elapsed, Remaining }

public enum GlassEngine
{
    /// <summary>GPU when it has been verified on this Windows build, otherwise screen capture.</summary>
    Auto,
    /// <summary>DWM + DirectComposition only; no blur if unavailable. Island stays capturable.</summary>
    Gpu,
    /// <summary>Screen copy + blur; hides the island from screenshots while blurring.</summary>
    ScreenCapture,
    /// <summary>Screen copy bent through the island's shape by a pixel shader (true refraction).</summary>
    Refraction,
    /// <summary>GPU blur plus a Direct3D lens ring that bends the background at the rim (needs the GPU engine).</summary>
    Lens,
}

public enum AccentSource
{
    /// <summary>Wallpaper Engine's theme when available; otherwise the media app's color for media bits.</summary>
    Auto,
    Wallpaper,
    MediaApp,
    Custom,
    Monochrome,
}

/// <summary>How the current lyric line is coloured (collapsed pill + expanded Home + lyrics page highlight).</summary>
public enum LyricsColorMode
{
    /// <summary>The classic mint green (#30D158).</summary>
    Green,
    /// <summary>Vibrant colour pulled from the album / thumbnail art (same idea as Aura / aurora).</summary>
    Cover,
    /// <summary>Follows the island accent (wallpaper / media app / custom).</summary>
    Accent,
    White,
    /// <summary>A single colour from <see cref="AppSettings.LyricsCustomColor"/>.</summary>
    Custom,
    /// <summary>Gradient across two colours from the cover palette.</summary>
    CoverGradient,
    /// <summary>Gradient from <see cref="AppSettings.LyricsCustomColor"/> to <see cref="AppSettings.LyricsCustomColorB"/>.</summary>
    CustomGradient,
}

public enum ClockFormat
{
    System,
    TwelveHour,
    TwentyFourHour,
}

public enum ClockStyle { Classic, TimeOnly, Stacked, Analog, Big }

public enum IdleStyle
{
    Clock,
    /// <summary>A small empty pill when nothing is happening.</summary>
    Minimal,
}

/// <summary>What fills the right side of Home when nothing is playing.</summary>
public enum HomeGlance
{
    Weather,
    NextEvent,
    Vitals,
    Todos,
    Focus,
    NextAlarm,
    Clipboard,
}

/// <summary>Motion of the Home weather sky card.</summary>
public enum WeatherMotion
{
    /// <summary>Dense, vivid rain/clouds — busy and alive.</summary>
    Lively,
    /// <summary>Sparse, slow filmic drift — fewer particles, long soft streaks.</summary>
    Cinematic,
    /// <summary>Same rich look as Lively, frozen (no timer — cheapest on CPU/GPU).</summary>
    Static,
}

public enum PageNavigation
{
    /// <summary>Labeled tabs across the top of the expanded island.</summary>
    Tabs,
    /// <summary>Page dots (no labels), like the iPhone home screen.</summary>
    Dots,
}

public sealed class AppSettings
{
    // ---- shape & material
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandMode Mode { get; set; } = IslandMode.DynamicIsland;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandMaterial Material { get; set; } = IslandMaterial.LiquidGlass;

    /// <summary>Background opacity in Translucent mode (content stays opaque), 0.15..1.</summary>
    public double BackgroundOpacity { get; set; } = 0.7;

    /// <summary>Tint strength in Liquid Glass mode, 0..0.9.</summary>
    public double GlassTint { get; set; } = 0.30;

    /// <summary>How much of the frosted blur is mixed over the sharp background: 0 = clear glass, 1 = fully frosted.</summary>
    public double GlassBlur { get; set; } = 0.85;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GlassEngine GlassEngine { get; set; } = GlassEngine.Lens;

    /// <summary>Cached GPU blur probe result, "&lt;windows build&gt;:ok|fail"; re-probed after Windows updates.</summary>
    public string? GpuBlurProbe { get; set; }

    /// <summary>Liquid Glass edge highlight and top sheen, 0 = matte, 1 = very glossy.</summary>
    public double GlassShine { get; set; } = 0.7;

    /// <summary>The bright line along the glass edge and the soft light just inside it (0 = none).</summary>
    public double GlassRim { get; set; } = 0.08;

    /// <summary>How vivid the background looks through the glass (0 = as it is, 1 = very rich colour).</summary>
    public double GlassVibrancy { get; set; } = 0.55;

    /// <summary>How strongly the Refraction engine bends the background at the island's rim.</summary>
    public double GlassRefraction { get; set; } = 0.4;

    /// <summary>Soft shadow behind text and icons so they stay readable over see-through materials.</summary>
    public bool TextShadow { get; set; } = true;

    public double IslandScale { get; set; } = 1.0;

    /// <summary>Multiplier on corner radius; 1 = fully rounded pill.</summary>
    public double CornerRoundness { get; set; } = 0.9;

    public double OutlineWidth { get; set; }
    public double OutlineOpacity { get; set; } = 0.25;
    public double ShadowStrength { get; set; } = 0.0;

    // ---- adaptive
    /// <summary>Fade the material over the bare desktop; use it fully when an app window is behind.</summary>
    public bool AdaptiveBackground { get; set; } = true;

    /// <summary>Material strength while only the desktop/wallpaper is behind the island, 0..1.</summary>
    public double DesktopOpacity { get; set; } = 0.15;

    public bool SolidWhenExpanded { get; set; } = true;

    // ---- colors
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AccentSource AccentSource { get; set; } = AccentSource.Wallpaper;

    public string CustomAccent { get; set; } = "#0A84FF";

    /// <summary>How much of the accent is mixed into the dark background, 0..0.5.</summary>
    public double BackgroundTint { get; set; } = 0.0;

    // ---- text & clock
    public string FontFamily { get; set; } = "Segoe UI Variable Text";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClockFormat ClockFormat { get; set; } = ClockFormat.TwelveHour;

    public bool ShowSeconds { get; set; }
    public bool ShowDateWhenIdle { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IdleStyle IdleStyle { get; set; } = IdleStyle.Clock;

    /// <summary>Home, no music: glance card on the right (weather by default).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HomeGlance HomeGlance { get; set; } = HomeGlance.Weather;

    // ---- interaction & animation
    public bool ExpandOnHover { get; set; } = true;
    public int HoverDelayMs { get; set; } = 120;
    public int CollapseDelayMs { get; set; } = 250;

    /// <summary>Spring speed multiplier; 1 = default, 2 = twice as fast.</summary>
    public double AnimationSpeed { get; set; } = 1.0;

    /// <summary>0 = no overshoot, 1 = very bouncy.</summary>
    public double Bounciness { get; set; } = 0.8;

    // ---- media & Wallpaper Engine
    public bool ShowArtBanner { get; set; } = true;

    /// <summary>
    /// Fetch Apple Music motion artwork (looping album videos) for the playing album, from any player.
    /// Opt-in: uses Apple's unofficial web-player token and private catalog endpoint.
    /// </summary>
    public bool MotionArtwork { get; set; }

    /// <summary>Show pause / mute / next wallpaper controls when Wallpaper Engine is running.</summary>
    public bool WallpaperEngineControls { get; set; } = true;

    /// <summary>Mute Wallpaper Engine's audio while a media app is playing.</summary>
    public bool MuteWallpaperDuringMedia { get; set; } = true;

    /// <summary>Superseded by <see cref="AccentSource"/>; read once for migration, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ThemeFromWallpaper { get; set; }

    // ---- alerts, position, system
    public double OsdSeconds { get; set; } = 2;
    public double AlertSeconds { get; set; } = 3;

    /// <summary>Extra distance from the top of the screen, in DIPs (e.g. for a top-docked taskbar).</summary>
    public double TopOffset { get; set; }

    public bool HideVolumeFlyout { get; set; }

    /// <summary>Shake the mouse quickly left and right to hide / show the island.</summary>
    public bool MagicShake { get; set; } = true;

    /// <summary>Hold Ctrl over the island and it fades and lets clicks through to the window behind (e.g. browser tabs).</summary>
    public bool CtrlPeek { get; set; } = true;
    /// <summary>The key held to make the island see-through and click-through.</summary>
    public PeekKey PeekKey { get; set; } = PeekKey.Alt;

    public bool QuickPasteEnabled { get; set; } = true;
    public string QuickPasteHotkey { get; set; } = "Ctrl+Shift+V";

    /// <summary>After the island has been collapsed this many seconds, it reopens on the first page (0 = never).</summary>
    public int ReturnHomeSeconds { get; set; } = 5;
    /// <summary>Show a Copy button when a notification contains a verification code.</summary>
    public bool CopyCodes { get; set; } = true;

    /// <summary>Pet look: 0 none, then hat, bow, glasses, scarf.</summary>
    public PetOutfit PetOutfit { get; set; } = PetOutfit.Scarf;

    /// <summary>Minutes a snoozed notification or alarm waits before it comes back.</summary>
    public int SnoozeMinutes { get; set; } = 10;

    /// <summary>Hovering the collapsed pill shows a quick tray (music controls, recent copies) instead of opening the whole island.</summary>
    public bool HoverQuickTray { get; set; }
    public int TrayLingerMs { get; set; } = 1500;

    /// <summary>Collapsed music: the album art becomes a spinning disc and the equalizer a pulse bubble, separate from the title pill.</summary>
    public bool MusicSplit { get; set; } = true;
    /// <summary>A flowing wave ribbon along the bottom of the collapsed music pill (replaces the pulse bubble).</summary>
    public bool MusicWave { get; set; } = true;
    public bool ClockOnMusicPill { get; set; } = true;
    /// <summary>How the audio-reactive bars look.</summary>
    public EqualizerLook EqualizerLook { get; set; } = EqualizerLook.Aura;

    /// <summary>Your own saved looks (Settings > Look presets). Built-in ones live in code.</summary>
    public List<LookPreset> CustomPresets { get; set; } = new();

    /// <summary>With several screens, the island moves to the one the cursor stays on.</summary>
    public bool FollowCursorMonitor { get; set; } = true;

    /// <summary>Legacy flat set-counts (migrated into AlarmHabits on load).</summary>
    public Dictionary<string, int> AlarmUse { get; set; } = new();

    /// <summary>Learned wake-time habits: distinct days, rings, deletes, weekday lean — not one-off sets.</summary>
    public List<AlarmHabit> AlarmHabits { get; set; } = new();

    /// <summary>How often each timer length (minutes) was started.</summary>
    public Dictionary<string, int> TimerUse { get; set; } = new();

    /// <summary>Bar mode: how long the island stays a pill after you leave it, before folding back into the thin bar.</summary>
    public double BarRevealSeconds { get; set; } = 6;

    /// <summary>Plain text chats with Clara use a model on this PC (Ollama) when one is running; pictures still go to Groq.</summary>
    public bool AssistantUseLocalAi { get; set; } = true;

    /// <summary>Which Ollama model to use offline. Empty = pick the fastest installed.</summary>
    public string LocalModel { get; set; } = "";

    /// <summary>The pet nudges you now and then: water, stretch, rest your eyes.</summary>
    public bool PetReminders { get; set; } = true;

    /// <summary>Minutes between the pet's nudges (10..120).</summary>
    public int PetReminderMinutes { get; set; } = 40;

    /// <summary>How the big clock on the Home page is drawn: plain, outlined or glass (like the iOS 26 lock screen).</summary>
    public ClockLook ClockLook { get; set; } = ClockLook.Glass;

    /// <summary>Set once the thin outlined lock-screen clock became the default for existing settings files too.</summary>
    public bool ClockDefaultsV2 { get; set; }

    /// <summary>The name Clara uses for you. It is sent to the AI provider as part of her instructions (only the name).</summary>
    public string UserName { get; set; } = "Vibhor";

    /// <summary>What Clara should know about you: your field, vocabulary, goals. Shapes how she reads your abbreviations.</summary>
    public string UserBio { get; set; } =
        "Studying aircraft maintenance engineering (AME). Everything technical should be read in an aviation and aircraft maintenance sense first: " +
        "GTE = gas turbine engine, VOR = VHF omnidirectional range, DME = distance measuring equipment, ILS = instrument landing system, ADF = automatic direction finder, " +
        "FADEC = full authority digital engine control, APU = auxiliary power unit, EGT = exhaust gas temperature, N1/N2 = engine spool speeds, TCAS, MEL/MMEL, AMM, IPC, SRM, NDT, CAMO, AD/SB (airworthiness directives and service bulletins), " +
        "torque values, safety wire, tooling, human factors, and regulations (ICAO, EASA, FAA, Transport Canada). Never reinterpret these as software, medical or other meanings unless he says so. " +
        "If a term really is ambiguous, ask once which he means.";

    /// <summary>Clipboard items older than this many hours are removed (0 = keep until the 100-item limit). Pinned items stay.</summary>
    public int ClipboardKeepHours { get; set; }

    /// <summary>Preferred cloud brain label in Settings. Runtime still picks Gemini first when its key is set.</summary>
    public CloudProvider CloudProvider { get; set; } = CloudProvider.Gemini;

    /// <summary>Google AI Studio key (free, no card): aistudio.google.com/apikey.</summary>
    public string GeminiApiKey { get; set; } = "";

    /// <summary>Gemini model used when the provider is Gemini.</summary>
    public string GeminiModel { get; set; } = "gemini-2.5-flash";

    public const string GeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
    public const string OpenRouterEndpoint = "https://openrouter.ai/api/v1/chat/completions";

    /// <summary>OpenRouter key (openrouter.ai/keys) — one key, many models.</summary>
    public string OpenRouterApiKey { get; set; } = "";
    /// <summary>OpenRouter model id, e.g. google/gemini-2.5-flash.</summary>
    public string OpenRouterModel { get; set; } = "google/gemini-2.5-flash";

    public string CloudKey => CloudProvider switch
    {
        CloudProvider.Gemini => GeminiApiKey,
        CloudProvider.OpenRouter => OpenRouterApiKey,
        _ => GroqApiKey,
    };
    public string CloudModel => CloudProvider switch
    {
        CloudProvider.Gemini => string.IsNullOrWhiteSpace(GeminiModel) ? "gemini-2.5-flash" : GeminiModel,
        CloudProvider.OpenRouter => string.IsNullOrWhiteSpace(OpenRouterModel) ? "google/gemini-2.5-flash" : OpenRouterModel,
        _ => GroqModel,
    };
    public string CloudEndpoint => CloudProvider switch
    {
        CloudProvider.Gemini => GeminiEndpoint,
        CloudProvider.OpenRouter => OpenRouterEndpoint,
        _ => "",
    };

    /// <summary>Show every slider (otherwise just presets and the essentials).</summary>
    public bool ShowAdvanced { get; set; }

    /// <summary>Shape of the animated line along the bottom of the music pill. Ribbon is the original.</summary>
    public WaveStyle WaveStyle { get; set; } = WaveStyle.Aurora;
    /// <summary>A notification or copy leaves a small bubble beside the pill for this many seconds (0 = off).</summary>
    public int CarrySeconds { get; set; } = 8;

    /// <summary>A little pet that wanders beneath the collapsed island.</summary>
    public bool PetEnabled { get; set; } = true;
    public PetKind PetKind { get; set; } = PetKind.Dog;
    /// <summary>The pet speaks (little speech bubbles).</summary>
    public bool PetTalks { get; set; } = true;

    /// <summary>Page icons swell under the cursor like the macOS Dock.</summary>
    public bool DockMagnify { get; set; } = true;
    /// <summary>Resting the cursor on a page icon opens that page.</summary>
    public bool HoverSwitchPages { get; set; } = true;

    /// <summary>Hide the island by itself while a full-screen game, video or presentation is running.</summary>
    public bool HideInFullscreen { get; set; } = true;

    // ---- Music
    public ProgressStyle ProgressStyle { get; set; } = ProgressStyle.Wavy;
    public CompactTime CompactMediaTime { get; set; } = CompactTime.Remaining;
    /// <summary>A thin progress line along the bottom of the collapsed music pill.</summary>
    public bool CompactProgressLine { get; set; } = true;
    /// <summary>Look up real album art (iTunes) when the player only gives a browser icon.</summary>
    public bool AutoAlbumArt { get; set; } = true;
    public bool LyricsEnabled { get; set; } = true;
    /// <summary>Show Hindi (Devanagari) lyrics in English letters, the way Hindi is typed ("main yeh kar raha hoon").</summary>
    public bool RomanizeHindi { get; set; } = true;
    public bool LyricsPageEnabled { get; set; }
    /// <summary>Handwriting-style font for all lyrics.</summary>
    public string LyricsFont { get; set; } = "Segoe Print";
    public double LyricsFontSize { get; set; } = 16;
    /// <summary>Colour of the current lyric line.</summary>
    public LyricsColorMode LyricsColorMode { get; set; } = LyricsColorMode.Cover;
    /// <summary>Used when <see cref="LyricsColorMode"/> is Custom or CustomGradient (start).</summary>
    public string LyricsCustomColor { get; set; } = "#FFFFD60A";
    /// <summary>Used when <see cref="LyricsColorMode"/> is CustomGradient (end).</summary>
    public string LyricsCustomColorB { get; set; } = "#FF64D2FF";
    /// <summary>Artist name under the title on the collapsed music pill.</summary>
    public bool ShowArtistOnPill { get; set; } = true;
    public bool LyricsInCompact { get; set; }
    public bool LyricsInExpanded { get; set; } = true;

    // ---- Focus sessions (Pomodoro)
    public bool FocusEnabled { get; set; } = true;

    /// <summary>The System page (RAM / CPU / GPU / network, top apps, free-RAM button).</summary>
    public bool VitalsEnabled { get; set; } = true;

    /// <summary>Show an alert (and let the pet worry) when RAM use stays above the limit below.</summary>
    public bool VitalsAlertEnabled { get; set; } = true;

    /// <summary>RAM percentage that raises the alert (80..99).</summary>
    public int VitalsAlertPercent { get; set; } = 95;
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int FocusCyclesBeforeLong { get; set; } = 4;
    /// <summary>Start the next phase by itself when one finishes.</summary>
    public bool FocusAutoStart { get; set; } = true;

    /// <summary>Order of the island's pages after Home (page names); anything missing follows in the default order.</summary>
    public List<string> PageOrder { get; set; } = new()
    {
        "Lyrics", "Assistant", "Clipboard", "Focus", "Calendar", "Todo", "Notes",
        "Shelf", "Weather", "Apps", "Notifications",
    };

    /// <summary>Keep a history of what you copy (text, links, images, files), on this PC only.</summary>
    public bool ClipboardHistoryEnabled { get; set; } = true;
    public bool ClipboardPageEnabled { get; set; } = true;

    // ---- AI assistant (Groq). The key lives only in this settings file, outside the source tree.
    public bool AssistantEnabled { get; set; } = true;
    public string GroqApiKey { get; set; } = "";
    public string GroqModel { get; set; } = "qwen/qwen3.8-27b";
    /// <summary>Attach a fresh screenshot to every message automatically.</summary>
    public bool AssistantLiveScreen { get; set; }
    /// <summary>Tell Clara the time, what's playing, battery and weather with each message.</summary>
    public bool ClaraContext { get; set; } = true;
    /// <summary>System-wide shortcut: grabs the screen, opens the assistant and focuses the input.</summary>
    public string AssistantHotkey { get; set; } = "Ctrl+Alt+J";

    /// <summary>
    /// Per-page open shortcuts (page enum name → "Ctrl+Alt+N"). Defaults use the first free letter of
    /// the page title; empty string = off. Clara stays on <see cref="AssistantHotkey"/>.
    /// </summary>
    public Dictionary<string, string> PageHotkeys { get; set; } = new(StringComparer.Ordinal);

    public ShakeSensitivity ShakeSensitivity { get; set; } = ShakeSensitivity.High;

    /// <summary>System-wide shortcut that hides / shows the island, e.g. "Ctrl+Alt+I"; empty = none.</summary>
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+I";

    /// <summary>Why <see cref="ToggleHotkey"/> couldn't be registered (runtime only, shown in Settings).</summary>
    [JsonIgnore]
    public string? HotkeyProblem { get; set; }

    /// <summary>Alert when a USB / Bluetooth device or monitor connects or disconnects.</summary>
    public bool DeviceAlerts { get; set; } = true;

    public bool LockKeyAlerts { get; set; } = true;

    /// <summary>Do Not Disturb (Focus) turning on or off.</summary>
    public bool FocusAlerts { get; set; } = true;

    public bool CopyAlerts { get; set; } = true;

    /// <summary>On Ctrl+V / Shift+Insert (Windows has no paste event; the shortcut is the signal).</summary>
    public bool PasteAlerts { get; set; } = true;

    /// <summary>Show the first line of copied text in the alert.</summary>
    public bool ClipboardPreview { get; set; } = true;

    // ---- quick apps
    /// <summary>Launcher row in the expanded island: Start menu app ids (AppUserModelID or known-folder path).</summary>
    public List<string> QuickApps { get; set; } = new()
    {
        "Microsoft.Windows.Explorer",
        "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel",
    };

    public const int MaxQuickApps = 16;

    /// <summary>Web links shown on the Apps page after the apps (YouTube, Gmail accounts...).</summary>
    public List<QuickLink> QuickLinks { get; set; } = new()
    {
        // ChatGPT opens in Perplexity Comet when installed (not the Windows default browser).
        new QuickLink { Name = "ChatGPT", Url = "https://chatgpt.com", Browser = BrowserLaunch.Comet },
    };

    // ---- updates
    /// <summary>Check the public GitHub releases once a day.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Download, verify and install updates by themselves (installed copies only).</summary>
    public bool AutoInstallUpdates { get; set; } = true;

    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>The version that last ran, to say "Updated to …" once after an update.</summary>
    public string? LastRunVersion { get; set; }

    // ---- activities
    /// <summary>Clicking the collapsed music activity opens the music app (hover still expands), like tapping on iPhone.</summary>
    public bool ClickOpensMediaApp { get; set; } = true;

    /// <summary>When two activities run (e.g. timer + music), show the second as a small bubble beside the island.</summary>
    public bool ShowSecondActivity { get; set; } = true;

    /// <summary>Equalizer bars follow the actual sound instead of a looping animation.</summary>
    public bool RealWaveform { get; set; } = true;

    public List<Alarm> Alarms { get; set; } = new();

    /// <summary>Microphone muted, Energy Saver, Wi-Fi / VPN connected or disconnected.</summary>
    public bool StatusAlerts { get; set; } = true;

    // ---- notes and to-do
    public bool TodoEnabled { get; set; }
    public bool NotesEnabled { get; set; }

    // ---- file shelf
    public bool ShelfEnabled { get; set; } = true;

    /// <summary>Keep files on the shelf after dragging them out (otherwise a successful drop removes them).</summary>
    public bool ShelfKeepAfterDrag { get; set; } = true;

    // ---- calendar
    public bool CalendarEnabled { get; set; }

    /// <summary>iCal (.ics / webcal) addresses: Google "Secret address in iCal format", Outlook "ICS" link, iCloud public link.</summary>
    public List<string> CalendarIcsUrls { get; set; } = new();

    /// <summary>Calendars added to Windows (Outlook / Calendar app accounts); needs the one-time identity setup.</summary>
    public bool WindowsCalendar { get; set; } = true;

    /// <summary>Alert this many minutes before a timed event; 0 = off.</summary>
    public int CalendarReminderMinutes { get; set; } = 5;

    // ---- notifications
    /// <summary>Show other apps' notifications in the island (needs the one-time identity setup).</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Only the app name in the alert, not the message text.</summary>
    public bool NotificationsHideText { get; set; }

    public double NotificationSeconds { get; set; } = 5;

    // ---- weather
    public bool WeatherEnabled { get; set; }

    /// <summary>Windows location (rounded to ~1 km); otherwise the chosen city below.</summary>
    public bool WeatherUseDeviceLocation { get; set; } = true;

    public string? WeatherPlace { get; set; }

    /// <summary>Town name for "My location" (from OpenStreetMap) and the rounded coordinates it's for.</summary>
    public string? WeatherDevicePlace { get; set; }
    public string? WeatherDevicePlaceKey { get; set; }
    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    /// <summary>Weather icon and temperature next to the clock in the idle pill ("10:58 PM | ☁ 26°").</summary>
    public bool WeatherInIdlePill { get; set; } = true;
    /// <summary>The idle weather sits in its own round bubble beside the clock pill, not inside it.</summary>
    public bool WeatherSeparate { get; set; } = true;

    /// <summary>Home weather card motion. Default is cinematic (slow filmic); Static freezes the rich sky.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WeatherMotion WeatherMotion { get; set; } = WeatherMotion.Cinematic;

    public ClockStyle ClockStyle { get; set; } = ClockStyle.Stacked;
    /// <summary>Font of the idle clock.</summary>
    public string ClockFont { get; set; } = "Segoe UI Variable";

    // ---- expanded pages
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PageNavigation PageNavigation { get; set; } = PageNavigation.Tabs;

    /// <summary>Orange / green dot beside the island (and an alert) while an app uses the mic / camera.</summary>
    public bool PrivacyIndicator { get; set; } = true;

    /// <summary>Quick app icon size in design units (before the island's Size scaling), 16..48.</summary>
    public double QuickAppIconSize { get; set; } = 26;

    /// <summary>Raised (on the UI thread) after any setting is changed through the UI.</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Restores look-and-feel defaults; system integration (volume flyout, wallpaper muting) is left alone.</summary>
    public void ResetToDefaults()
    {
        var d = new AppSettings();
        Mode = d.Mode;
        Material = d.Material;
        BackgroundOpacity = d.BackgroundOpacity;
        GlassTint = d.GlassTint;
        GlassBlur = d.GlassBlur;
        GlassEngine = d.GlassEngine;
        GlassShine = d.GlassShine;
        GlassRim = d.GlassRim;
        GlassVibrancy = d.GlassVibrancy;
        GlassRefraction = d.GlassRefraction;
        TextShadow = d.TextShadow;
        IslandScale = d.IslandScale;
        CornerRoundness = d.CornerRoundness;
        OutlineWidth = d.OutlineWidth;
        OutlineOpacity = d.OutlineOpacity;
        ShadowStrength = d.ShadowStrength;
        AdaptiveBackground = d.AdaptiveBackground;
        DesktopOpacity = d.DesktopOpacity;
        SolidWhenExpanded = d.SolidWhenExpanded;
        AccentSource = d.AccentSource;
        CustomAccent = d.CustomAccent;
        BackgroundTint = d.BackgroundTint;
        FontFamily = d.FontFamily;
        ClockFormat = d.ClockFormat;
        ShowSeconds = d.ShowSeconds;
        ShowDateWhenIdle = d.ShowDateWhenIdle;
        IdleStyle = d.IdleStyle;
        ExpandOnHover = d.ExpandOnHover;
        HoverDelayMs = d.HoverDelayMs;
        CollapseDelayMs = d.CollapseDelayMs;
        AnimationSpeed = d.AnimationSpeed;
        Bounciness = d.Bounciness;
        ShowArtBanner = d.ShowArtBanner;
        WallpaperEngineControls = d.WallpaperEngineControls;
        OsdSeconds = d.OsdSeconds;
        AlertSeconds = d.AlertSeconds;
        TopOffset = d.TopOffset;
        QuickAppIconSize = d.QuickAppIconSize;
        PageNavigation = d.PageNavigation;
    }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DIsland", "settings.json");

    /// <summary>Enums may be written as names or numbers.</summary>
    private static readonly JsonSerializerOptions ReadOptions = new() { Converters = { new JsonStringEnumConverter() } };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions) ?? new AppSettings();

                // Before materials existed, a lowered opacity meant "translucent"; keep that look.
                if (!json.Contains("\"Material\"") && json.Contains("\"BackgroundOpacity\"") && settings.BackgroundOpacity < 1)
                    settings.Material = IslandMaterial.Translucent;

                // The first Liquid Glass default tint (30%) made the glass look solid; move it to the new default.
                if (!json.Contains("\"GlassBlur\"") && Math.Abs(settings.GlassTint - 0.3) < 0.001)
                    settings.GlassTint = new AppSettings().GlassTint;

                if (settings.ThemeFromWallpaper == false && !json.Contains("\"AccentSource\""))
                    settings.AccentSource = AccentSource.MediaApp;
                settings.ThemeFromWallpaper = null;

                SmartTime.MigrateAlarmUse(settings);
                settings.PageHotkeys = new Dictionary<string, string>(settings.PageHotkeys ?? new(), StringComparer.Ordinal);
                PageHotkeyMap.EnsureDefaults(settings.PageHotkeys, settings.AssistantHotkey, settings.ToggleHotkey, settings.QuickPasteHotkey);

                // Clara: Gemini is the cloud main; keep Groq only as failsafe when a Gemini key is present.
                if (!string.IsNullOrWhiteSpace(settings.GeminiApiKey))
                    settings.CloudProvider = CloudProvider.Gemini;

                // Seed ChatGPT → Comet once if the user never added it.
                settings.QuickLinks ??= new List<QuickLink>();
                var seeded = false;
                if (!settings.QuickLinks.Any(l =>
                        l.Url.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase)
                        || l.Name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)))
                {
                    settings.QuickLinks.Insert(0, new QuickLink
                    {
                        Name = "ChatGPT",
                        Url = "https://chatgpt.com",
                        Browser = BrowserLaunch.Comet,
                    });
                    seeded = true;
                }
                else
                {
                    foreach (var l in settings.QuickLinks)
                    {
                        if (l.Url.Contains("chatgpt.com", StringComparison.OrdinalIgnoreCase)
                            && string.IsNullOrWhiteSpace(l.Browser))
                        {
                            l.Browser = BrowserLaunch.Comet;
                            seeded = true;
                        }
                    }
                }
                if (seeded) settings.Save();

                return settings;
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable settings fall back to defaults, but the old file is kept (once) so nothing is lost
            // when the defaults are saved over it.
            try { File.Copy(FilePath, FilePath + ".unreadable", overwrite: false); } catch (Exception) { }
        }
        var fresh = new AppSettings();
        PageHotkeyMap.EnsureDefaults(fresh.PageHotkeys, fresh.AssistantHotkey, fresh.ToggleHotkey, fresh.QuickPasteHotkey);
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Non-fatal: the setting just won't persist.
        }
    }
}
