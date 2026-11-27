// ---------------------------------------------------------------------------
// Players.cs
//
/// Which players we can see, and which of them lie about the timeline. Detection is
/// two-pronged (SMTC app id first, then App Paths / Start-menu shortcuts), and the
/// 'laggy' set marks the clients whose reported position only moves every few seconds.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Amc
{
    /// <summary>One entry of the built-in "known players" list.</summary>
    public class PlayerInfo
    {
        public string Key;          // stable id used in config.ini ("apple-music")
        public string Name;         // display name ("Apple Music")
        public string[] AumidParts; // lowercase substrings of SourceAppUserModelId
        public string[] Exes;       // lowercase exe names, used for install detection
    }

    /// <summary>A known player plus what we could see of it right now.</summary>
    public class DetectedPlayer
    {
        public PlayerInfo Info;
        public bool Running;    // a media session with a matching AUMID exists right now
        public bool Installed;  // found in the start menu / App Paths
        public string Aumid;    // the AUMID we matched while running ("" otherwise)
    }

    /// <summary>
    /// Player binding: which media apps are allowed to drive the card.
    ///
    /// Windows exposes no "list of installed media apps", so detection is a best-effort
    /// mixture of two cheap probes: (a) the live media sessions, which tell us what is
    /// running right now, and (b) the start menu plus the App Paths registry key, which
    /// tell us what looks installed. Anything that is neither known nor bound is ignored,
    /// which is also what keeps browsers (which happily report a "Music" session for a
    /// video page) out of the card.
    /// </summary>
    public static class Players
    {
        /// <summary>Bound entry that matches a raw AUMID (player not in the known list).</summary>
        public const string AumidPrefix = "aumid:";
        /// <summary>Bound entry that matches an executable name.</summary>
        public const string ExePrefix = "exe:";

        static PlayerInfo Make(string key, string name, string[] aumids, string[] exes)
        {
            var p = new PlayerInfo();
            p.Key = key;
            p.Name = name;
            p.AumidParts = aumids;
            p.Exes = exes;
            return p;
        }

        // Browsers publish a session for whatever tab is playing audio/video and report it as
        // "Music", so they are never part of the built-in list and never matched implicitly.
        static readonly string[] BrowserParts = new string[] {
            "msedge", "chrome", "firefox", "brave", "opera", "vivaldi", "iexplore", "360se", "qqbrowser"
        };

        public static readonly PlayerInfo[] Known = new PlayerInfo[] {
            Make("apple-music", "Apple Music", new string[] { "applemusic" }, new string[] { "applemusic.exe" }),
            Make("spotify", "Spotify", new string[] { "spotify" }, new string[] { "spotify.exe" }),
            Make("netease", "网易云音乐", new string[] { "cloudmusic", "netease" }, new string[] { "cloudmusic.exe" }),
            Make("qqmusic", "QQ 音乐", new string[] { "qqmusic", "qq music" }, new string[] { "qqmusic.exe" }),
            Make("kugou", "酷狗音乐", new string[] { "kugou" }, new string[] { "kugou.exe", "kgmusic.exe" }),
            // 酷我音乐 deliberately absent: its client never registers an SMTC session
            // (no SystemMediaTransportControls string anywhere in its install tree), so a
            // binding could never light the card up. Listing it only produced dead switches.
            // MD3Music is a third-party KuGou client (Flutter + Rust). Unlike the official
            // KuGou app it publishes a complete session -- AUMID com.md3music.md3music with
            // real seek/rate support and an end time -- so it binds like any other player.
            Make("md3music", "MD3Music", new string[] { "md3music", "com.md3music" }, new string[] { "md3music.exe" }),
            // Salt Player for Windows (SPW). The Microsoft Store build is an MSIX, so its session
            // AUMID carries the package family name (Sakawish.SaltPlayerforWindows_...); the Steam
            // build is a plain executable. It publishes a full SMTC session -- title, artist, album,
            // cover and progress -- since 1.3.x, so it binds like any other player.
            Make("saltplayer", "Salt Player", new string[] { "sakawish", "saltplayer" }, new string[] { "saltplayer.exe" }),
            Make("musicbee", "MusicBee", new string[] { "musicbee" }, new string[] { "musicbee.exe" }),
            Make("wmp", "Windows 媒体播放器", new string[] { "zunemusic", "wmplayer", "media player" }, new string[] { "wmplayer.exe", "music.ui.exe" })
        };

        public static bool IsBrowser(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return false;
            string s = aumid.ToLowerInvariant();
            for (int i = 0; i < BrowserParts.Length; i++)
                if (s.IndexOf(BrowserParts[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        public static PlayerInfo ByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < Known.Length; i++)
                if (Known[i].Key == key) return Known[i];
            return null;
        }

        /// <summary>Known player whose AUMID pattern matches, or null.</summary>
        public static PlayerInfo MatchAumid(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return null;
            string s = aumid.ToLowerInvariant();
            for (int i = 0; i < Known.Length; i++)
            {
                var p = Known[i];
                for (int j = 0; j < p.AumidParts.Length; j++)
                    if (s.IndexOf(p.AumidParts[j], StringComparison.Ordinal) >= 0) return p;
            }
            return null;
        }

        /// <summary>Executable that a known player runs as (used for per-process audio metering).</summary>
        public static string ExeFor(string aumid)
        {
            var info = MatchAumid(aumid);
            if (info != null && info.Exes.Length > 0) return info.Exes[0];
            if (!string.IsNullOrEmpty(aumid) && aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return aumid;
            return null;
        }

        public static bool IsNetEase(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return false;
            string s = aumid.ToLowerInvariant();
            return s.IndexOf("cloudmusic", StringComparison.Ordinal) >= 0 || s.IndexOf("netease", StringComparison.Ordinal) >= 0;
        }

        // Players whose playback position cannot be trusted: they hand out a frozen number
        // (or zero) while playing, and jump back to zero when paused. NetEase is the classic
        // case; KuGou behaves the same way. Everything else reports a coarse but honest value.
        public static bool IsLaggyTimeline(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return false;
            if (IsNetEase(aumid)) return true;
            string s = aumid.ToLowerInvariant();
            // MD3Music reports a position that only moves every 10-30 s (and jitters by a few
            // tenths while "stuck"), so it belongs to the same "don't trust the number, run
            // our own clock" family as NetEase and KuGou.
            return s.IndexOf("kugou", StringComparison.Ordinal) >= 0
                || s.IndexOf("kgmusic", StringComparison.Ordinal) >= 0
                || s.IndexOf("md3music", StringComparison.Ordinal) >= 0;
        }

        public static PlayerInfo MatchExe(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return null;
            string s = exe.ToLowerInvariant();
            for (int i = 0; i < Known.Length; i++)
            {
                var p = Known[i];
                for (int j = 0; j < p.Exes.Length; j++)
                    if (s == p.Exes[j]) return p;
            }
            return null;
        }

        /// <summary>Friendly name for an AUMID that is not in the known list.</summary>
        public static string DisplayName(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return "?";
            var p = MatchAumid(aumid);
            if (p != null) return p.Name;
            // UWP ids look like "Family_publisher!Entry" - show the family without the publisher.
            int bang = aumid.IndexOf('!');
            int us = aumid.IndexOf('_');
            if (bang > 0 && us > 0 && us < bang) return aumid.Substring(0, us);
            // Win32 players usually register their exe path; show just the file name.
            try
            {
                string f = Path.GetFileName(aumid);
                if (!string.IsNullOrEmpty(f)) return f;
            }
            catch { }
            return aumid;
        }

        // ---- binding helpers -------------------------------------------------

        /// <summary>True when this AUMID is covered by the user's binding list.</summary>
        public static bool IsBound(List<string> bound, string aumid)
        {
            if (bound == null || bound.Count == 0) return false;
            string s = (aumid ?? "").ToLowerInvariant();
            for (int i = 0; i < bound.Count; i++)
            {
                string b = bound[i];
                if (string.IsNullOrEmpty(b)) continue;
                if (b.StartsWith(AumidPrefix, StringComparison.Ordinal))
                {
                    string want = b.Substring(AumidPrefix.Length).ToLowerInvariant();
                    if (want.Length > 0 && s == want) return true;
                }
                else if (b.StartsWith(ExePrefix, StringComparison.Ordinal))
                {
                    string want = b.Substring(ExePrefix.Length).ToLowerInvariant();
                    string got = "";
                    try { got = Path.GetFileName(s); } catch { }
                    if (want.Length > 0 && (got == want || s.IndexOf(want, StringComparison.Ordinal) >= 0)) return true;
                }
                else
                {
                    var p = ByKey(b);
                    if (p == null) continue;
                    for (int j = 0; j < p.AumidParts.Length; j++)
                        if (s.IndexOf(p.AumidParts[j], StringComparison.Ordinal) >= 0) return true;
                    for (int j = 0; j < p.Exes.Length; j++)
                        if (s.IndexOf(p.Exes[j], StringComparison.Ordinal) >= 0) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the "waiting for music" idle card may be shown. It is an Apple Music
        /// affordance, so it only appears when Apple Music is (implicitly) allowed.
        /// </summary>
        public static bool AppleMusicWanted(List<string> bound)
        {
            if (bound == null || bound.Count == 0) return true;
            return bound.Contains("apple-music");
        }

        /// <summary>Push the current binding list into the SMTC poller.</summary>
        public static void ApplyBindings(Config cfg)
        {
            if (cfg != null && cfg.BoundConfigured)
            {
                // Explicit binding list (possibly empty): match exactly what is listed.
                // An empty list therefore means "no player drives the card".
                SmtcService.BoundMatch = delegate(string aumid) { return IsBound(cfg.BoundPlayers, aumid); };
                SmtcService.AppleMusicWanted = AppleMusicWanted(cfg.BoundPlayers);
            }
            else
            {
                SmtcService.BoundMatch = null;
                SmtcService.AppleMusicWanted = true;
            }
        }

        /// <summary>Forget the install-scan cache so the next Detect() rescans the machine.</summary>
        public static void Refresh()
        {
            _exeCache = null;
            _exeCacheAt = DateTime.MinValue;
        }

        // ---- detection -------------------------------------------------------

        static Dictionary<string, bool> _exeCache;
        static DateTime _exeCacheAt = DateTime.MinValue;

        /// <summary>Exe names (lowercase) we could find in the start menu or App Paths.</summary>
        static Dictionary<string, bool> InstalledExes()
        {
            if (_exeCache != null && (DateTime.Now - _exeCacheAt).TotalSeconds < 60) return _exeCache;
            var set = new Dictionary<string, bool>();

            // App Paths: one key per registered exe, cheap and tells us the app is real.
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                {
                    AddKeys(set, k);
                }
            }
            catch { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"))
                {
                    AddKeys(set, k);
                }
            }
            catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                {
                    AddKeys(set, k);
                }
            }
            catch { }

            // Start menu shortcuts: covers installers that never touch App Paths.
            int apCount = set.Count;
            ScanShortcuts(set, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
            ScanShortcuts(set, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            int smCount = set.Count;

            // Uninstall entries: plenty of players (QQ音乐 / 酷狗 / 酷我 …) register themselves
            // here and nowhere else -- no App Paths key, and the shortcut may live in a folder
            // whose name we never guess. DisplayIcon usually points straight at the exe.
            ScanUninstall(set, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanUninstall(set, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanUninstall(set, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            LyricsService.Log("players scan: apppaths=" + apCount + " shortcuts=+" + (smCount - apCount) + " uninstall=+" + (set.Count - smCount));

            _exeCache = set;
            _exeCacheAt = DateTime.Now;
            return set;
        }

        /// <summary>
        /// Last-resort install probe. The Start-menu scan is the cheapest fallback but it misses
        /// plenty: a per-user install with no shortcut, a player registered only for "open with".
        /// The uninstall registry always lists them; <c>DisplayIcon</c> points at the real exe,
        /// except when it points at the uninstaller -- then we scan that folder instead.
        /// </summary>
        static void ScanUninstall(Dictionary<string, bool> set, RegistryKey root, string path)
        {
            try
            {
                using (var u = root.OpenSubKey(path))
                {
                    if (u == null) return;
                    string[] names = u.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        try
                        {
                            using (var e = u.OpenSubKey(names[i]))
                            {
                                if (e == null) continue;
                                string icon = e.GetValue("DisplayIcon") as string;
                                AddExeFromPath(set, icon);
                                // DisplayIcon often points at uninstall.exe, which tells us
                                // the install folder but not the player exe -- so scan that folder too.
                                string dir = null;
                                try
                                {
                                    string ip = CleanExePath(icon);
                                    if (!string.IsNullOrEmpty(ip)) dir = Path.GetDirectoryName(ip);
                                }
                                catch { }
                                string loc = e.GetValue("InstallLocation") as string;
                                if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc)) dir = loc;
                                if (string.IsNullOrEmpty(dir)) continue;
                                try
                                {
                                    string[] exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                                    for (int k = 0; k < exes.Length && k < 40; k++) AddExeFromPath(set, exes[k]);
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>Turns an uninstall DisplayIcon value ("C:\app\x.exe,0") into a bare path.</summary>
        static string CleanExePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            p = p.Trim().Trim('"');
            int comma = p.IndexOf(',');
            if (comma > 0) p = p.Substring(0, comma).Trim().Trim('"');
            if (!p.ToLowerInvariant().EndsWith(".exe", StringComparison.Ordinal)) return null;
            return p;
        }

        static void AddExeFromPath(Dictionary<string, bool> set, string p)
        {
            p = CleanExePath(p);
            if (p == null) return;
            try
            {
                string exe = Path.GetFileName(p).ToLowerInvariant();
                if (exe.Length > 4) set[exe] = true;
            }
            catch { }
        }

        static void AddKeys(Dictionary<string, bool> set, RegistryKey k)
        {
            if (k == null) return;
            string[] names = k.GetSubKeyNames();
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                if (n.Length > 4 && n.ToLowerInvariant().EndsWith(".exe", StringComparison.Ordinal))
                    set[n.ToLowerInvariant()] = true;
            }
        }

        static void ScanShortcuts(Dictionary<string, bool> set, string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                LyricsService.Log("players scan: no start-menu path for this user");
                return;
            }
            if (!Directory.Exists(root))
            {
                LyricsService.Log("players scan: missing " + root);
                return;
            }
            // Enumerating recursively in one call throws UnauthorizedAccessException as soon as a
            // single subfolder is locked down (the Start Menu contains a few), which is what kept
            // this scan at zero for months. Walk it by hand and skip what we cannot read.
            var files = new List<string>();
            WalkLnks(root, files, 0);
            if (files.Count == 0)
            {
                LyricsService.Log("players scan: no lnk under " + root);
                return;
            }
            int hit = 0;
            for (int i = 0; i < files.Count; i++)
            {
                string exe = ShortcutTarget(files[i]);
                if (string.IsNullOrEmpty(exe)) continue;
                hit++;
                set[exe] = true;
            }
            // Loud on purpose: for months this reported shortcuts=+0 and quietly dropped
            // installed players out of the list.
            LyricsService.Log("players scan: lnk " + files.Count + " -> " + hit + " with an exe in " + root);
        }

        /// <summary>Collects every .lnk below <paramref name="dir"/>, tolerating locked folders.</summary>
        static void WalkLnks(string dir, List<string> into, int depth)
        {
            if (depth > 8) return;
            try
            {
                string[] here = Directory.GetFiles(dir, "*.lnk");
                for (int i = 0; i < here.Length; i++) into.Add(here[i]);
            }
            catch { }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { return; }
            for (int i = 0; i < subs.Length; i++)
            {
                // Junctions and symlinks can point back up the tree (and are rarely interesting).
                try
                {
                    var fi = new DirectoryInfo(subs[i]);
                    if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }
                WalkLnks(subs[i], into, depth + 1);
            }
        }

        // ---- shell link parsing ------------------------------------------------------------
        // WScript.Shell was late-bound through reflection and returned nothing in this process,
        // so every start-menu scan came back empty. The .lnk format is a small, stable binary
        // structure (MS-SHLLINK), and all we need out of it is the target executable's name, so
        // read the file directly: LinkInfo first (the normal case), then the string table, then
        // a byte-level sweep for any "*.exe" the link happens to carry in some other field.
        static string ShortcutTarget(string path)
        {
            byte[] b;
            try { b = File.ReadAllBytes(path); }
            catch { return null; }
            if (b.Length < 0x4C) return null;
            if (BitConverter.ToInt32(b, 0) != 0x4C) return null;   // HeaderSize
            int flags = BitConverter.ToInt32(b, 0x14);
            bool wide = (flags & 0x80) != 0;                       // IsUnicode
            int p = 0x4C;
            if ((flags & 0x01) != 0)                               // HasLinkTargetIDList
            {
                if (p + 2 > b.Length) return null;
                int idl = BitConverter.ToUInt16(b, p);
                p += 2 + idl;
            }
            string best = null;
            if ((flags & 0x02) != 0 && p + 0x1C <= b.Length)       // HasLinkInfo
            {
                int size = BitConverter.ToInt32(b, p);
                if (size >= 0x1C && p + size <= b.Length)
                {
                    int baseA = SafeU32(b, p + 0x10), suffixA = SafeU32(b, p + 0x18);
                    int baseW = size >= 0x24 ? SafeU32(b, p + 0x1C) : 0;
                    int suffixW = size >= 0x24 ? SafeU32(b, p + 0x20) : 0;
                    string bs = baseW > 0 ? WideZ(b, p + baseW) : (baseA > 0 ? AnsiZ(b, p + baseA) : null);
                    string sf = suffixW > 0 ? WideZ(b, p + suffixW) : (suffixA > 0 ? AnsiZ(b, p + suffixA) : null);
                    if (!string.IsNullOrEmpty(bs) && EndsExe(bs)) best = bs;
                    else if (!string.IsNullOrEmpty(bs) && !string.IsNullOrEmpty(sf)) best = bs + sf;
                }
                p += Math.Max(size, 0);
            }
            // StringData: NAME, RELATIVE_PATH, WORKING_DIR, COMMAND_LINE_ARGUMENTS, ICON_LOCATION.
            for (int k = 0; k < 5 && p + 2 <= b.Length; k++)
            {
                if ((flags & (1 << (2 + k))) == 0) continue;
                int chars = BitConverter.ToUInt16(b, p);
                p += 2;
                int bytes = wide ? chars * 2 : chars;
                if (p + bytes > b.Length) break;
                string v = wide ? Encoding.Unicode.GetString(b, p, bytes)
                                : Encoding.Default.GetString(b, p, bytes);
                p += bytes;
                if (EndsExe(v)) { best = v; break; }
            }
            string exe = ExeName(best);
            if (exe != null) return exe;
            // Last resort: the link may only carry a relative target. Sweep the raw bytes for an
            // "*.exe" in ANSI or UTF-16 and keep the first plausible name.
            return SweepExe(b);
        }

        static bool EndsExe(string s)
        {
            return !string.IsNullOrEmpty(s) && s.Length > 4 &&
                   s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        static string ExeName(string path)
        {
            if (!EndsExe(path)) return null;
            int cut = path.LastIndexOfAny(new char[] { '\\', '/' });
            string name = cut >= 0 ? path.Substring(cut + 1) : path;
            if (name.Length < 5) return null;
            return name.ToLowerInvariant();
        }

        static int SafeU32(byte[] b, int off)
        {
            if (off < 0 || off + 4 > b.Length) return 0;
            return (int)BitConverter.ToUInt32(b, off);
        }

        static string AnsiZ(byte[] b, int off)
        {
            if (off <= 0 || off >= b.Length) return null;
            int i = off;
            while (i < b.Length && b[i] != 0) i++;
            if (i <= off) return null;
            return Encoding.Default.GetString(b, off, i - off);
        }

        static string WideZ(byte[] b, int off)
        {
            if (off <= 0 || off + 1 >= b.Length) return null;
            int i = off;
            while (i + 1 < b.Length && (b[i] != 0 || b[i + 1] != 0)) i += 2;
            if (i <= off) return null;
            return Encoding.Unicode.GetString(b, off, i - off);
        }

        /// <summary>Finds the first "something.exe" in the file, ANSI or UTF-16 encoded.</summary>
        static string SweepExe(byte[] b)
        {
            for (int i = 0; i + 4 <= b.Length; i++)
            {
                bool ansi = (b[i] == (byte)'.' && i + 4 <= b.Length &&
                             (b[i + 1] | 0x20) == (byte)'e' && (b[i + 2] | 0x20) == (byte)'x' && (b[i + 3] | 0x20) == (byte)'e');
                bool utf16 = i + 8 <= b.Length && b[i] == (byte)'.' && b[i + 1] == 0 &&
                             (b[i + 2] | 0x20) == (byte)'e' && b[i + 3] == 0 &&
                             (b[i + 4] | 0x20) == (byte)'x' && b[i + 5] == 0;
                if (!ansi && !utf16) continue;
                int back = i;
                if (utf16)
                {
                    while (back - 2 >= 0 && b[back - 1] == 0 &&
                           b[back - 2] >= 0x20 && b[back - 2] < 0x7F) back -= 2;
                    if (i - back < 2) continue;
                    string w = Encoding.Unicode.GetString(b, back, i + 8 - back);
                    string n = ExeName(w);
                    if (n != null) return n;
                }
                else
                {
                    while (back - 1 >= 0 && b[back - 1] >= 0x20 && b[back - 1] < 0x7F) back--;
                    if (i - back < 2) continue;
                    string a = Encoding.Default.GetString(b, back, i + 4 - back);
                    string n = ExeName(a);
                    if (n != null) return n;
                }
            }
            return null;
        }

        /// <summary>
        /// Known players that are installed and/or running, plus any extra session that the
        /// user could bind manually (returned separately so the UI can offer "add other").
        /// </summary>
        public static List<DetectedPlayer> Detect(string[] runningAumids, out List<string> unknownAumids)
        {
            unknownAumids = new List<string>();
            var installed = InstalledExes();
            var list = new List<DetectedPlayer>();

            var running = new List<string>();
            if (runningAumids != null)
                for (int i = 0; i < runningAumids.Length; i++)
                    if (!string.IsNullOrEmpty(runningAumids[i])) running.Add(runningAumids[i]);

            for (int i = 0; i < Known.Length; i++)
            {
                var p = Known[i];
                var d = new DetectedPlayer();
                d.Info = p;
                for (int j = 0; j < p.Exes.Length; j++)
                    if (installed.ContainsKey(p.Exes[j])) { d.Installed = true; break; }
                for (int j = 0; j < running.Count; j++)
                {
                    if (MatchAumid(running[j]) == p)
                    {
                        d.Running = true;
                        d.Aumid = running[j];
                        running.RemoveAt(j);
                        j--;
                    }
                }
                // UWP apps are often "installed" without any start-menu exe we can see, so a
                // running session also counts as installed.
                if (d.Running) d.Installed = true;
                list.Add(d);
            }

            for (int i = 0; i < running.Count; i++)
                if (!IsBrowser(running[i])) unknownAumids.Add(running[i]);

            var seen = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (!d.Installed && !d.Running) continue;
                seen.Append(d.Info.Key);
                seen.Append(d.Running ? "(running)" : "(installed)");
                seen.Append(' ');
            }
            LyricsService.Log("players detected: " + seen);

            return list;
        }
    }
}
