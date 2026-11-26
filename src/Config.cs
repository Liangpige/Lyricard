// ---------------------------------------------------------------------------
// Config.cs
//
/// Every persisted setting, in one flat INI-style file next to the exe (data\config.ini).
/// Loading never fails: unknown keys are ignored and out-of-range values are clamped, so
/// a hand-edited or half-written file can always boot.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Amc
{
    /// <summary>
    /// All persisted settings, read from / written to <c>data\config.ini</c> next to the exe.
    /// The field initialisers double as the factory defaults - they are the look the author tuned,
    /// so a fresh install looks exactly like the original.
    /// </summary>
    public class Config
    {
        public double Left = double.NaN;
        public double Top = double.NaN;
        // ---- defaults: the owner's tuned look (2026-11-22), used for a fresh install
        //      and by the "restore defaults" button ----
        public double Scale = 0.85;
        public double Opacity = 0.94;
        // Click-through: the card ignores the mouse entirely and clicks fall through to
        // whatever is underneath it. Replaces the old "lock position" flag (which only
        // stopped dragging) -- being able to use the card as a transparent overlay beats
        // accidentally dragging it.
        public bool ClickThrough = false;

        /// <summary>What "click-through" means: "off", "full" (nothing on the card answers the
        /// mouse) or "transport" (the card is see-through except for the transport row, so the
        /// play button keeps working while the rest of the desktop does).</summary>
        public string ClickThroughMode = "off";

        // Hide the card while a window is fullscreen on its monitor. Off by default: a
        // mis-detection here looks exactly like "the card vanished for no reason".
        public bool HideFullscreen = false;
        /// <summary>What a mouse-wheel notch over the card does: scale | volume | off.</summary>
        public string WheelAction = "scale";
        // Default off: a fresh copy should never touch somebody else's Run key on first launch.
        public bool AutoStart = false;
        // Update policy: one silent check at startup (never a poller), and a version the user
        // asked us to stop nagging about.
        public bool AutoCheckUpdate = true;
        public string SkippedVersion = "";
        // Default 0: a first-time install must be neutral -- we tuned +1.6s by ear against
        // Apple Music's own reporting lag during development, which is not something a new
        // user should inherit. Existing configs keep whatever they already saved.
        public double LyricOffset = 0.0;
        // Housekeeping: lyric cache is cheap to rebuild, so let the user throw it away on a
        // schedule instead of watching data\ grow forever.
        public bool AutoCleanCache = false;
        public int CacheCleanDays = 30;
        public string LastCacheClean = "";
        public bool ListenAll = false;
        public bool ClearBg = true;
        // Cover treatment: saturation kept (0..1) and the lowest opacity that the
        // darkest pixels of the cover may fall to (0..1). 1.0 / 1.0 = untouched.
        public double ArtSat = 0.1;
        public double ArtBlackAlpha = 1.0;
        public double ArtHiCut = 0.06;
        public bool ArtSpin = true;

        /// <summary>"circle" (the disc) or "square" (a plain sleeve). A square sleeve does not turn:
        /// rotating a rectangle reads as a wobbling tile, not as a record.</summary>
        public string ArtShape = "circle";
        public bool ArtShadow = false;   // removed: kept only so old configs still parse
        /// <summary>Soft halo behind the text. Off by default (the card looks clean on dark
        /// wallpapers); turn it on when the desktop is light and white text disappears.</summary>
        public bool TextShadow;
        /// <summary>Colour of the bottom transport buttons ("" = built-in white).</summary>
        public string ControlsColor = "";

        /// <summary>
        /// Media apps allowed to drive the card (PlayerInfo.Key, or "aumid:..." / "exe:..." for
        /// players outside the built-in list). Empty = legacy behaviour: Apple Music first,
        /// other players only when "listen to all players" is on.
        /// </summary>
        public List<string> BoundPlayers = new List<string>();
        // False until a BoundPlayers line is seen; an explicitly empty list means
        // "bind nothing" (no card at all), while a missing line keeps the legacy
        // Apple-Music-first behaviour.
        /// <summary>
        /// True once the user has explicitly saved the player list. Needed because an empty
        /// <c>BoundPlayers=</c> used to be written on every save, which then read back as
        /// "configured, but nothing is bound" -- i.e. the card silently never appeared again.
        /// </summary>
        public bool BoundConfigured = false;

        // Interface language: 0 = follow the system, 1 = 中文, 2 = English.
        public int Lang = 0;
        // ---- personalisation (all defaults reproduce the original look) ----
        public double TitleScale = 1.0;
        public double ArtistScale = 1.0;
        public double LyricScale = 1.0;
        public string TitleColor = "";
        public string ArtistColor = "";
        public string LyricColor = "";
        public double BarThickness = 3.0;

        // ---- music bars (音乐律动条) ---------------------------------------
        // Bar count is derived from the module's width unless the user pins it; attack/release are
        // the per-frame easing factors, thickness is the bar width in DIP, and BarsColor is "" when
        // the row should follow the progress-bar colours instead.
        public int BarsCount = 0;              // 0 = as many as the width allows
        public double BarsAttack = 0.55;       // 0..1, higher = snappier rise
        public double BarsRelease = 0.14;      // 0..1, higher = faster fall
        public double BarsThickness = 4.0;     // bar width in DIP
        public double BarsLength = 170.0;      // module width in DIP
        public double BarsMaxHeight = 26.0;    // module height in DIP
        public string BarsColor = "";          // "" = follow BarFillColor / BarColor
        public double BarScale = 1.0;
        public string BarColor = "";
        public string BarFillColor = "";
        public double ArtSize = 96.0;

        // Cover grading. Everything is computed once per cover (192x192), never per frame, so these
        // cost exactly nothing while the card is running.
        public double ArtBrightness = 1.0;
        public double ArtContrast = 1.0;
        public double ArtWarmth = 0.0;   // -1 cool .. +1 warm
        public double ArtHue = 0.0;      // -180 .. +180 degrees
        public double ArtVignette = 0.0; // 0 .. 0.8

        // Duotone: map luminance onto a dark->light ramp. Off by default; while it is on the
        // saturation slider is disabled because the ramp already defines the hue.
        public bool ArtTint = false;
        public string ArtTintDark = "#3A0A0A";
        public string ArtTintLight = "#FFB4B4";
        public double ArtTintMix = 1.0;
        /// <summary>Legacy: superseded by ModuleConfig.MaxChars. Parsed so old files still load, never applied.</summary>
        public int ArtistMaxChars = 0;

        // user saved looks (settings panel -> 预设). Data is a "k=v;k=v;" blob of the
        // look fields, so adding a field later does not need a new file format.
        public class LookPreset
        {
            public string Name = "";
            public string Data = "";
        }

        public System.Collections.Generic.List<LookPreset> Presets =
            new System.Collections.Generic.List<LookPreset>();

        // Two presets the author ships with the app. They are seeded only on a first run
        // (no config.ini yet), so deleting them later stays deleted.
        static readonly string[] BuiltInPresets =
        {
            "\u9ED8\u8BA42|Scale=0.73;Opacity=0.94;ClearBg=True;ArtSat=0.2;ArtBlackAlpha=1;ArtHiCut=0.08;ArtSpin=True;ArtShape=circle;ArtShadow=True;TextShadow=False;ControlsColor=;TitleScale=1;ArtistScale=1;LyricScale=1;TitleColor=;ArtistColor=;LyricColor=;BarThickness=3;BarScale=1;BarColor=;BarFillColor=;ArtSize=100;ArtBrightness=1;ArtContrast=1;ArtWarmth=0;ArtHue=0;ArtVignette=0;ArtTint=False;ArtTintDark=#3A0A0A;ArtTintLight=#FFB4B4;ArtTintMix=1;BarsCount=0;BarsAttack=0.6;BarsRelease=0.4;BarsThickness=5;BarsLength=300;BarsMaxHeight=18;BarsColor=;ArtistMaxChars=15;Template=IyBMeXJpY2FyZCBjYXJkIGxheW91dA0KU2NoZW1hVmVyc2lvbj0yDQpDYXJkVz0wDQpUaXRsZUFydGlzdElubGluZT1UcnVlDQpDYXJkSD0wDQoNCltDb3Zlcl0NCkVuYWJsZWQ9VHJ1ZQ0KWD0zNTYNClk9MjgNClc9OTYNCkg9OTYNCkZvbnRTaXplPTANCkNvbG9yPQ0KQWxpZ249QXV0bw0KWkluZGV4PTANCk1heENoYXJzPTANCg0KW1RpdGxlXQ0KRW5hYmxlZD1UcnVlDQpYPTM2DQpZPTI4DQpXPTMyMA0KSD0tMQ0KRm9udFNpemU9MA0KQ29sb3I9DQpBbGlnbj1SaWdodA0KWkluZGV4PTENCk1heENoYXJzPTM0DQoNCltBcnRpc3RdDQpFbmFibGVkPVRydWUNClg9MzINClk9MTAwDQpXPTIwMA0KSD0tMQ0KRm9udFNpemU9MA0KQ29sb3I9DQpBbGlnbj1SaWdodA0KWkluZGV4PTINCk1heENoYXJzPTE2DQoNCltMeXJpY3NdDQpFbmFibGVkPVRydWUNClg9MjQNClk9NjANClc9MzIwDQpIPS0xDQpGb250U2l6ZT0wDQpDb2xvcj0NCkFsaWduPVJpZ2h0DQpaSW5kZXg9Mw0KTWF4Q2hhcnM9NDINCg0KW1Byb2dyZXNzXQ0KRW5hYmxlZD1UcnVlDQpYPTE2DQpZPTkyDQpXPTMyMA0KSD0zDQpGb250U2l6ZT0wDQpDb2xvcj0NCkFsaWduPUF1dG8NClpJbmRleD00DQpNYXhDaGFycz0wDQoNCltDb250cm9sc10NCkVuYWJsZWQ9VHJ1ZQ0KWD0yMzINClk9OTYNClc9MTAyDQpIPTMwDQpGb250U2l6ZT0wDQpDb2xvcj0NCkFsaWduPUF1dG8NClpJbmRleD01DQpNYXhDaGFycz0wDQoNCltNdXNpY0JhcnNdDQpFbmFibGVkPUZhbHNlDQpYPTEzMA0KWT0xMDANClc9MzAwDQpIPTE4DQpGb250U2l6ZT0wDQpDb2xvcj0NCkFsaWduPUF1dG8NClpJbmRleD02DQpNYXhDaGFycz0wDQo=",
            "\u9ED8\u8BA41|Scale=0.85;Opacity=0.94;ClearBg=True;ArtSat=0.2;ArtBlackAlpha=1;ArtHiCut=0.08;ArtSpin=True;ArtShape=square;ArtShadow=True;TextShadow=False;ControlsColor=;TitleScale=1;ArtistScale=1;LyricScale=1;TitleColor=;ArtistColor=;LyricColor=;BarThickness=3;BarScale=1;BarColor=;BarFillColor=;ArtSize=84;ArtBrightness=1;ArtContrast=1;ArtWarmth=0;ArtHue=0;ArtVignette=0;ArtTint=False;ArtTintDark=#3A0A0A;ArtTintLight=#FFB4B4;ArtTintMix=1;BarsCount=0;BarsAttack=0.6;BarsRelease=0.4;BarsThickness=5;BarsLength=290;BarsMaxHeight=16;BarsColor=;ArtistMaxChars=15;Template=IyBMeXJpY2FyZCBjYXJkIGxheW91dA0KU2NoZW1hVmVyc2lvbj0yDQpDYXJkVz0wDQpUaXRsZUFydGlzdElubGluZT1GYWxzZQ0KQ2FyZEg9MA0KDQpbQ292ZXJdDQpFbmFibGVkPVRydWUNClg9MzQwDQpZPTIwDQpXPTg0DQpIPTg0DQpGb250U2l6ZT0wDQpDb2xvcj0NCkFsaWduPUF1dG8NClpJbmRleD0wDQpNYXhDaGFycz0wDQoNCltUaXRsZV0NCkVuYWJsZWQ9VHJ1ZQ0KWD0xNg0KWT0yMA0KVz0zMjANCkg9LTENCkZvbnRTaXplPTANCkNvbG9yPQ0KQWxpZ249TGVmdA0KWkluZGV4PTENCk1heENoYXJzPTM0DQoNCltBcnRpc3RdDQpFbmFibGVkPVRydWUNClg9MTYNClk9NDgNClc9MjAwDQpIPS0xDQpGb250U2l6ZT0wDQpDb2xvcj0NCkFsaWduPUxlZnQNClpJbmRleD0yDQpNYXhDaGFycz00Mg0KDQpbTHlyaWNzXQ0KRW5hYmxlZD1UcnVlDQpYPTE2DQpZPTY4DQpXPTMyMA0KSD0tMQ0KRm9udFNpemU9MA0KQ29sb3I9DQpBbGlnbj1MZWZ0DQpaSW5kZXg9Mw0KTWF4Q2hhcnM9NDINCg0KW1Byb2dyZXNzXQ0KRW5hYmxlZD1UcnVlDQpYPTE2DQpZPTkyDQpXPTMyMA0KSD0zDQpGb250U2l6ZT0wDQpDb2xvcj0NCkFsaWduPUF1dG8NClpJbmRleD00DQpNYXhDaGFycz0wDQoNCltDb250cm9sc10NCkVuYWJsZWQ9VHJ1ZQ0KWD0xNg0KWT0xMDANClc9MTAyDQpIPTMwDQpGb250U2l6ZT0wDQpDb2xvcj0NCkFsaWduPUF1dG8NClpJbmRleD01DQpNYXhDaGFycz0wDQoNCltNdXNpY0JhcnNdDQpFbmFibGVkPVRydWUNClg9MTIwDQpZPTEwMA0KVz0zMDANCkg9MTgNCkZvbnRTaXplPTANCkNvbG9yPQ0KQWxpZ249QXV0bw0KWkluZGV4PTYNCk1heENoYXJzPTANCg=="
        };

        // Give a brand-new install the two presets above. Only called when there is no
        // config.ini at all, so someone who clears the list keeps it cleared.
        void SeedBuiltInPresets()
        {
            foreach (string line in BuiltInPresets)
            {
                int bar = line.IndexOf('|');
                if (bar <= 0) continue;
                var p = new LookPreset();
                p.Name = line.Substring(0, bar);
                p.Data = line.Substring(bar + 1);
                Presets.Add(p);
            }
        }

        // restore every appearance / behaviour value to the tuned defaults (position is kept)
        public void ResetLookDefaults()
        {
            Scale = 0.85;
            Opacity = 0.94;
            LyricOffset = 0.0;
            ClearBg = true;
            ArtSpin = true;
            ArtShape = "circle";
            ArtShadow = false;
            TextShadow = false;
            ControlsColor = "";
            ArtSat = 0.1;
            ArtBlackAlpha = 1.0;
            ArtHiCut = 0.06;
            TitleScale = 1.0;
            ArtistScale = 1.0;
            LyricScale = 1.0;
            TitleColor = "";
            ArtistColor = "";
            LyricColor = "";
            BarThickness = 3.0;
            BarScale = 1.0;
            BarColor = "";
            BarFillColor = "";
            ArtSize = 96.0;
            ArtBrightness = 1.0;
            ArtContrast = 1.0;
            ArtWarmth = 0.0;
            ArtHue = 0.0;
            ArtVignette = 0.0;
            ArtTint = false;
            ArtTintDark = "#3A0A0A";
            ArtTintLight = "#FFB4B4";
            ArtTintMix = 1.0;
            ArtistMaxChars = 15;
        }

        static string _dir;

        public static string Dir
        {
            get
            {
                if (_dir == null)
                {
                    _dir = EnsurePortable();
                    if (_dir == null) _dir = Ensure(Environment.SpecialFolder.ApplicationData);
                    if (_dir == null) _dir = Ensure(Environment.SpecialFolder.LocalApplicationData);
                    if (_dir == null) _dir = Path.Combine(Path.GetTempPath(), "AppleMusicCard");
                    try { if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir); } catch { }
                }
                return _dir;
            }
        }

        // Portable first: keep config + lyric cache next to the exe.
        static string EnsurePortable()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exe)) return null;
                string root = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(root)) return null;
                string d = Path.Combine(root, "data");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                string probe = Path.Combine(d, ".write-test");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return d;
            }
            catch { return null; }
        }

        static string Ensure(Environment.SpecialFolder kind)
        {
            try
            {
                string root = Environment.GetFolderPath(kind);
                if (string.IsNullOrEmpty(root)) return null;
                string d = Path.Combine(root, "AppleMusicCard");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
            catch { return null; }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(Dir, "config.ini"); }
        }

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    c.SeedBuiltInPresets();
                    c.Save();          // write the seeds out, so the presets exist before the first edit
                    return c;
                }
                foreach (string raw in File.ReadAllLines(ConfigPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k.StartsWith("Preset"))
                    {
                        int bar = v.IndexOf('|');
                        var p = new LookPreset();
                        if (bar > 0) { p.Name = v.Substring(0, bar).Trim(); p.Data = v.Substring(bar + 1); }
                        else { p.Name = v.Trim(); }
                        if (p.Name.Length > 0) c.Presets.Add(p);
                        continue;
                    }
                    c.ApplyKey(k, v);
                    continue;
                }
            }
            catch { }
            c.ClampAll();
            return c;
        }

        void ApplyKey(string k, string v)
        {
                    double d;
                    bool b;
                    int n;
                    switch (k)
                    {
                        case "Left": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) Left = d; break;
                        case "Top": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) Top = d; break;
                        case "Scale": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) Scale = d; break;
                        case "Opacity": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) Opacity = d; break;
                        case "LyricOffset": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) LyricOffset = d; break;
                        case "ClickThrough":
                            if (bool.TryParse(v, out b)) { ClickThrough = b; if (b && ClickThroughMode == "off") ClickThroughMode = "full"; }
                            break;
                        case "HideFullscreen":
                            if (bool.TryParse(v, out b)) HideFullscreen = b;
                            break;
                        case "ClickThroughMode":
                            if (v == "off" || v == "full" || v == "transport") ClickThroughMode = v;
                            break;
                        case "WheelAction":
                            if (v == "scale" || v == "volume" || v == "off") WheelAction = v;
                            break;
                        // legacy: "Locked=..." only disabled dragging; ignore it so old files
                        // do not silently turn the card into a non-interactive overlay.
                        case "Locked": break;
                        case "AutoStart": if (bool.TryParse(v, out b)) AutoStart = b; break;
                        case "AutoCheckUpdate": if (bool.TryParse(v, out b)) AutoCheckUpdate = b; break;
                        case "SkippedVersion": SkippedVersion = v; break;
                        case "AutoCleanCache": if (bool.TryParse(v, out b)) AutoCleanCache = b; break;
                        case "CacheCleanDays": if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) CacheCleanDays = n; break;
                        case "LastCacheClean": LastCacheClean = v; break;
                        case "ListenAll": if (bool.TryParse(v, out b)) ListenAll = b; break;
                        case "BoundPlayers":
                            BoundConfigured = true;
                            BoundPlayers.Clear();
                            if (!string.IsNullOrEmpty(v))
                            {
                                string[] parts = v.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                for (int i = 0; i < parts.Length; i++)
                                {
                                    string one = parts[i].Trim();
                                    if (one.Length > 0) BoundPlayers.Add(one);
                                }
                            }
                            break;
                        case "ClearBg": if (bool.TryParse(v, out b)) ClearBg = b; break;
                        case "ArtSat": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtSat = d; break;
                        case "ArtBlackAlpha": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtBlackAlpha = d; break;
                        case "ArtHiCut": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtHiCut = d; break;
                        case "ArtSpin": if (bool.TryParse(v, out b)) ArtSpin = b; break;
                        case "ArtShape":
                            ArtShape = string.Equals(v, "square", StringComparison.OrdinalIgnoreCase) ? "square" : "circle";
                            break;
                        case "ArtShadow": if (bool.TryParse(v, out b)) ArtShadow = b; break;
                        case "TextShadow": if (bool.TryParse(v, out b)) TextShadow = b; break;
                        case "ControlsColor": ControlsColor = v; break;
                        case "Lang":
                            {
                                string lv = (v ?? "").Trim().ToLowerInvariant();
                                if (lv == "zh" || lv == "cn" || lv == "1") Lang = 1;
                                else if (lv == "en" || lv == "2") Lang = 2;
                                else Lang = 0;
                            }
                            break;
                        case "TitleScale": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) TitleScale = d; break;
                        case "ArtistScale": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtistScale = d; break;
                        case "LyricScale": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) LyricScale = d; break;
                        case "TitleColor": TitleColor = v; break;
                        case "ArtistColor": ArtistColor = v; break;
                        case "LyricColor": LyricColor = v; break;
                        case "BarsCount": { int bn; if (int.TryParse(v, out bn)) BarsCount = bn; } break;
                        case "BarsAttack": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) BarsAttack = d; break;
                        case "BarsRelease": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) BarsRelease = d; break;
                        case "BarsThickness": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) BarsThickness = d; break;
                        case "BarsLength": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) BarsLength = d; break;
                        case "BarsMaxHeight": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) BarsMaxHeight = d; break;
                        case "BarsColor": BarsColor = v; break;
                case "BarThickness": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) BarThickness = d; break;
                        case "BarScale": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) BarScale = d; break;
                        case "BarColor": BarColor = v; break;
                        case "BarFillColor": BarFillColor = v; break;
                        case "ArtSize": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtSize = d; break;
                        case "ArtBrightness": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtBrightness = d; break;
                        case "ArtContrast": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtContrast = d; break;
                        case "ArtWarmth": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtWarmth = d; break;
                        case "ArtHue": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtHue = d; break;
                        case "ArtVignette": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtVignette = d; break;
                        case "ArtTint": if (bool.TryParse(v, out b)) ArtTint = b; break;
                        case "ArtTintDark": ArtTintDark = v; break;
                        case "ArtTintLight": ArtTintLight = v; break;
                        case "ArtTintMix": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtTintMix = d; break;
                        case "ArtistMaxChars": if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) ArtistMaxChars = n; break;

                    }
        }

        // ---- user presets -------------------------------------------------

        public System.Collections.Generic.List<string> LookPairs()
        {
            var l = new System.Collections.Generic.List<string>();
            l.Add("Scale=" + Scale.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("Opacity=" + Opacity.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ClearBg=" + ClearBg);
            l.Add("ArtSat=" + ArtSat.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ArtBlackAlpha=" + ArtBlackAlpha.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ArtHiCut=" + ArtHiCut.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ArtSpin=" + ArtSpin);
            l.Add("ArtShape=" + ArtShape);
            l.Add("ArtShadow=" + ArtShadow);
            l.Add("TextShadow=" + TextShadow);
            l.Add("ControlsColor=" + ControlsColor);
            l.Add("TitleScale=" + TitleScale.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ArtistScale=" + ArtistScale.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("LyricScale=" + LyricScale.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("TitleColor=" + TitleColor);
            l.Add("ArtistColor=" + ArtistColor);
            l.Add("LyricColor=" + LyricColor);
            l.Add("BarThickness=" + BarThickness.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("BarScale=" + BarScale.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("BarColor=" + BarColor);
            l.Add("BarFillColor=" + BarFillColor);
            l.Add("ArtSize=" + ArtSize.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ArtBrightness=" + ArtBrightness.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ArtContrast=" + ArtContrast.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ArtWarmth=" + ArtWarmth.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ArtHue=" + ArtHue.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ArtVignette=" + ArtVignette.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("ArtTint=" + ArtTint);
            l.Add("ArtTintDark=" + ArtTintDark);
            l.Add("ArtTintLight=" + ArtTintLight);
            l.Add("ArtTintMix=" + ArtTintMix.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("BarsCount=" + BarsCount);
            l.Add("BarsAttack=" + BarsAttack.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("BarsRelease=" + BarsRelease.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("BarsThickness=" + BarsThickness.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("BarsLength=" + BarsLength.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("BarsMaxHeight=" + BarsMaxHeight.ToString("0.###", CultureInfo.InvariantCulture));
            l.Add("BarsColor=" + BarsColor);
            l.Add("ArtistMaxChars=" + ArtistMaxChars);
            return l;
        }

        public LookPreset CaptureLook(string name)
        {
            var p = new LookPreset();
            p.Name = name;
            p.Data = string.Join(";", LookPairs().ToArray());
            return p;
        }

        public void ApplyLookPreset(LookPreset pres)
        {
            if (pres == null || pres.Data == null) return;
            foreach (string pair in pres.Data.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                ApplyKey(pair.Substring(0, eq).Trim(), pair.Substring(eq + 1).Trim());
            }
            ClampAll();
        }

        void ClampAll()
        {
            Scale = Clamp(Scale, 0.55, 2.2);
            Opacity = Clamp(Opacity, 0.25, 1.0);
            ArtSat = Clamp(ArtSat, 0.0, 1.0);
            ArtBlackAlpha = Clamp(ArtBlackAlpha, 0.0, 1.0);
            ArtHiCut = Clamp(ArtHiCut, 0.0, 0.6);
            TitleScale = Clamp(TitleScale, 0.60, 1.60);
            ArtistScale = Clamp(ArtistScale, 0.60, 1.60);
            LyricScale = Clamp(LyricScale, 0.60, 1.60);
            BarThickness = Clamp(BarThickness, 2.0, 8.0);
            BarScale = Clamp(BarScale, 0.5, 3.0);
            ArtSize = Clamp(ArtSize, 56.0, 160.0);
            ArtistMaxChars = (int)Clamp(ArtistMaxChars, 4, 40);
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Apple Music 桌面卡片配置");
                sb.AppendLine("Left=" + Left.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("Top=" + Top.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("Scale=" + Scale.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("Opacity=" + Opacity.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("LyricOffset=" + LyricOffset.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ClickThrough=" + ClickThrough);
                sb.AppendLine("ClickThroughMode=" + ClickThroughMode);
                sb.AppendLine("HideFullscreen=" + HideFullscreen);
                sb.AppendLine("WheelAction=" + WheelAction);
                sb.AppendLine("AutoStart=" + AutoStart);
                sb.AppendLine("AutoCheckUpdate=" + AutoCheckUpdate);
                sb.AppendLine("SkippedVersion=" + SkippedVersion);
                sb.AppendLine("AutoCleanCache=" + AutoCleanCache);
                sb.AppendLine("CacheCleanDays=" + CacheCleanDays);
                sb.AppendLine("LastCacheClean=" + LastCacheClean);
                sb.AppendLine("ListenAll=" + ListenAll);
                sb.AppendLine("ClearBg=" + ClearBg);
                sb.AppendLine("ArtSat=" + ArtSat.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ArtBlackAlpha=" + ArtBlackAlpha.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ArtHiCut=" + ArtHiCut.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ArtSpin=" + ArtSpin);
                sb.AppendLine("ArtShape=" + ArtShape);
                sb.AppendLine("ArtShadow=" + ArtShadow);
                sb.AppendLine("TextShadow=" + TextShadow);
                sb.AppendLine("ControlsColor=" + ControlsColor);
                sb.AppendLine("Lang=" + (Lang == 1 ? "zh" : Lang == 2 ? "en" : "auto"));
                // Only persist the key when the user has actually made a choice. Writing an empty
                // list would read back as "configured, nobody bound", permanently killing the card.
                if (BoundConfigured || BoundPlayers.Count > 0)
                    sb.AppendLine("BoundPlayers=" + string.Join(",", BoundPlayers.ToArray()));
                sb.AppendLine("TitleScale=" + TitleScale.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ArtistScale=" + ArtistScale.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("LyricScale=" + LyricScale.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("TitleColor=" + TitleColor);
                sb.AppendLine("ArtistColor=" + ArtistColor);
                sb.AppendLine("LyricColor=" + LyricColor);
                sb.AppendLine("BarThickness=" + BarThickness.ToString("0.###", CultureInfo.InvariantCulture));
            sb.AppendLine("BarsCount=" + BarsCount.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("BarsAttack=" + BarsAttack.ToString("0.###", CultureInfo.InvariantCulture));
            sb.AppendLine("BarsRelease=" + BarsRelease.ToString("0.###", CultureInfo.InvariantCulture));
            sb.AppendLine("BarsThickness=" + BarsThickness.ToString("0.##", CultureInfo.InvariantCulture));
            sb.AppendLine("BarsLength=" + BarsLength.ToString("0.#", CultureInfo.InvariantCulture));
            sb.AppendLine("BarsMaxHeight=" + BarsMaxHeight.ToString("0.#", CultureInfo.InvariantCulture));
            sb.AppendLine("BarsColor=" + (BarsColor ?? ""));
                sb.AppendLine("BarScale=" + BarScale.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("BarColor=" + BarColor);
                sb.AppendLine("BarFillColor=" + BarFillColor);
                sb.AppendLine("ArtSize=" + ArtSize.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ArtistMaxChars=" + ArtistMaxChars);
                for (int i = 0; i < Presets.Count; i++)
                    sb.AppendLine("Preset" + i + "=" + Presets[i].Name + "|" + Presets[i].Data);
                File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
