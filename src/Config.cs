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
        public bool Locked = false;
        // Default off: a fresh copy should never touch somebody else's Run key on first launch.
        public bool AutoStart = false;
        public double LyricOffset = 1.6;
        public bool ListenAll = false;
        public bool ClearBg = true;
        // Cover treatment: saturation kept (0..1) and the lowest opacity that the
        // darkest pixels of the cover may fall to (0..1). 1.0 / 1.0 = untouched.
        public double ArtSat = 0.1;
        public double ArtBlackAlpha = 1.0;
        public double ArtHiCut = 0.06;
        public bool ArtSpin = true;
        public bool ArtShadow = true;
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
        public double BarScale = 1.0;
        public string BarColor = "";
        public string BarFillColor = "";
        public double ArtSize = 96.0;
        public int ArtistMaxChars = 15;
        public string TextAlign = "right";   // right | center | left
        public double ControlsX = 100.0;   // 0..100, percent of the free width left of the cover
        public double TitleIndent = 31.0;
        public double LyricIndent = 0.0;

        // user saved looks (settings panel -> 预设). Data is a "k=v;k=v;" blob of the
        // look fields, so adding a field later does not need a new file format.
        public class LookPreset
        {
            public string Name = "";
            public string Data = "";
        }

        public System.Collections.Generic.List<LookPreset> Presets =
            new System.Collections.Generic.List<LookPreset>();

        // restore every appearance / behaviour value to the tuned defaults (position is kept)
        public void ResetLookDefaults()
        {
            Scale = 0.85;
            Opacity = 0.94;
            LyricOffset = 1.6;
            ClearBg = true;
            ArtSpin = true;
            ArtShadow = true;
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
            ArtistMaxChars = 15;
            TextAlign = "right";
            ControlsX = 100.0;
            TitleIndent = 31.0;
            LyricIndent = 0.0;
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
                if (!File.Exists(ConfigPath)) return c;
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
                        case "Locked": if (bool.TryParse(v, out b)) Locked = b; break;
                        case "AutoStart": if (bool.TryParse(v, out b)) AutoStart = b; break;
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
                        case "BarThickness": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) BarThickness = d; break;
                        case "BarScale": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) BarScale = d; break;
                        case "BarColor": BarColor = v; break;
                        case "BarFillColor": BarFillColor = v; break;
                        case "ArtSize": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ArtSize = d; break;
                        case "ArtistMaxChars": if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) ArtistMaxChars = n; break;
                        case "TextAlign":
                            {
                                string t = (v ?? "").Trim().ToLowerInvariant();
                                if (t == "left" || t == "center" || t == "right") TextAlign = t;
                            }
                            break;
                        case "ControlsX": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ControlsX = d; break;
                        case "TitleIndent": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) TitleIndent = d; break;
                        case "LyricIndent": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) LyricIndent = d; break;
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
            l.Add("ArtistMaxChars=" + ArtistMaxChars);
            l.Add("TextAlign=" + (TextAlign ?? "right"));
            l.Add("ControlsX=" + ControlsX.ToString("0.##", CultureInfo.InvariantCulture));
            l.Add("TitleIndent=" + TitleIndent.ToString("0.##", CultureInfo.InvariantCulture));
            l.Add("LyricIndent=" + LyricIndent.ToString("0.##", CultureInfo.InvariantCulture));
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
            BarScale = Clamp(BarScale, 0.5, 1.6);
            ArtSize = Clamp(ArtSize, 56.0, 160.0);
            ArtistMaxChars = (int)Clamp(ArtistMaxChars, 4, 40);
            ControlsX = Clamp(ControlsX, 0.0, 100.0);
            TitleIndent = Clamp(TitleIndent, 0.0, 200.0);
            LyricIndent = Clamp(LyricIndent, 0.0, 200.0);
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
                sb.AppendLine("Locked=" + Locked);
                sb.AppendLine("AutoStart=" + AutoStart);
                sb.AppendLine("ListenAll=" + ListenAll);
                sb.AppendLine("ClearBg=" + ClearBg);
                sb.AppendLine("ArtSat=" + ArtSat.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ArtBlackAlpha=" + ArtBlackAlpha.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ArtHiCut=" + ArtHiCut.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ArtSpin=" + ArtSpin);
                sb.AppendLine("ArtShadow=" + ArtShadow);
                sb.AppendLine("TextShadow=" + TextShadow);
                sb.AppendLine("ControlsColor=" + ControlsColor);
                sb.AppendLine("Lang=" + (Lang == 1 ? "zh" : Lang == 2 ? "en" : "auto"));
                sb.AppendLine("BoundPlayers=" + string.Join(",", BoundPlayers.ToArray()));
                sb.AppendLine("TitleScale=" + TitleScale.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ArtistScale=" + ArtistScale.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("LyricScale=" + LyricScale.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("TitleColor=" + TitleColor);
                sb.AppendLine("ArtistColor=" + ArtistColor);
                sb.AppendLine("LyricColor=" + LyricColor);
                sb.AppendLine("BarThickness=" + BarThickness.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("BarScale=" + BarScale.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("BarColor=" + BarColor);
                sb.AppendLine("BarFillColor=" + BarFillColor);
                sb.AppendLine("ArtSize=" + ArtSize.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ArtistMaxChars=" + ArtistMaxChars);
                sb.AppendLine("TextAlign=" + (TextAlign ?? "right"));
                sb.AppendLine("ControlsX=" + ControlsX.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("TitleIndent=" + TitleIndent.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("LyricIndent=" + LyricIndent.ToString("0.##", CultureInfo.InvariantCulture));
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
