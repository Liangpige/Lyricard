using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            Make("foobar", "foobar2000", new string[] { "foobar" }, new string[] { "foobar2000.exe" }),
            Make("musicbee", "MusicBee", new string[] { "musicbee" }, new string[] { "musicbee.exe" }),
            Make("aimp", "AIMP", new string[] { "aimp" }, new string[] { "aimp.exe" }),
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
        /// Last-resort install probe. The Start-menu scan used to be the only fallback, but the
        /// late-bound <c>WScript.Shell</c> COM object returns nothing in this process, so every
        /// Chinese player (QQ音乐 / 酷狗 / 酷我 / AIMP) stayed invisible even when installed.
        /// The uninstall registry always lists them; <c>DisplayIcon</c> points at the real exe,
        /// except for AIMP which points at its uninstaller -- then we scan that folder instead.
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
                                // DisplayIcon often points at uninstall.exe (AIMP does), which tells us
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
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            string[] files;
            try { files = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories); }
            catch { return; }
            object shell = null;
            try { shell = Type.GetTypeFromProgID("WScript.Shell"); } catch { shell = null; }
            if (shell == null)
            {
                // Loud, not silent: this used to fail invisibly and make half the player list vanish.
                LyricsService.Log("players scan: WScript.Shell unavailable, shortcuts skipped (" + files.Length + " lnk)");
            }
            object sc = null;
            try { if (shell != null) sc = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { "" }); }
            catch { sc = null; }
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    if (shell == null) return;
                    object lnk = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                        null, shell, new object[] { files[i] });
                    string target = (string)lnk.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, lnk, null);
                    if (!string.IsNullOrEmpty(target))
                    {
                        string exe = Path.GetFileName(target).ToLowerInvariant();
                        if (exe.Length > 4) set[exe] = true;
                    }
                }
                catch { }
            }
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
