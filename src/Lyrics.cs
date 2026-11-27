// ---------------------------------------------------------------------------
// Lyrics.cs
//
/// Lyric fetching, parsing and matching: local .lrc first, then QQ / NetEase / lrclib
/// in parallel. Also holds the scoring rules that decide whether a candidate is good
/// enough to show (a wrong lyric is worse than no lyric).
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Amc
{
    /// <summary>One timed lyric line (row-level LRC, no per-word timing).</summary>
    public class LyricLine
    {
        public double Time;
        public string Text = "";
    }

    public class LyricDoc
    {
        public List<LyricLine> Lines = new List<LyricLine>();
        public string Source = "";
        public string Matched = "";
        public bool Instrumental;
    }

    /// <summary>
    /// String helpers shared by the matcher: fold case / width / punctuation, strip bracketed
    /// suffixes like "(Live)" or "(From \"…\")", split Apple's "artist - album" artist field, and
    /// score two titles by longest-common-substring similarity.
    /// </summary>
    public static class TextUtil
    {
        static readonly Regex BracketRx = new Regex(@"[\(\[（【][^)\]）】]*[\)\]）】]", RegexOptions.Compiled);

        public static string StripBrackets(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string r = BracketRx.Replace(s, " ");
            return r;
        }

        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('®', ' ').Replace('™', ' ').Replace('©', ' ');
            s = s.Replace('“', ' ').Replace('”', ' ').Replace('’', ' ');
            s = s.ToLowerInvariant();
            var sb = new StringBuilder(s.Length);
            bool space = true;
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    space = false;
                }
                else if (c > 0x2E80)
                {
                    sb.Append(c);
                    space = false;
                }
                else if (!space)
                {
                    sb.Append(' ');
                    space = true;
                }
            }
            return sb.ToString().Trim();
        }

        public static string SearchTitle(string title)
        {
            string t = StripBrackets(title);
            t = t.Replace('®', ' ').Replace('™', ' ');
            t = Regex.Replace(t, @"\s+", " ");
            return t.Trim();
        }

        // "Artist — Album" is how the Apple Music app fills the Artist field.
        public static void SplitArtistAlbum(string raw, out string artist, out string album)
        {
            artist = raw ?? "";
            album = "";
            if (string.IsNullOrEmpty(artist)) return;
            string[] seps = new string[] { " — ", " – ", " - " };
            foreach (string sep in seps)
            {
                int i = artist.IndexOf(sep, StringComparison.Ordinal);
                if (i > 0)
                {
                    album = artist.Substring(i + sep.Length).Trim();
                    artist = artist.Substring(0, i).Trim();
                    return;
                }
            }
        }

        public static double Similar(string a, string b)
        {
            a = Clean(a);
            b = Clean(b);
            if (a.Length == 0 || b.Length == 0) return 0;
            if (a == b) return 1;
            int max = Math.Max(a.Length, b.Length);
            double lev = 1.0 - (double)Levenshtein(a, b) / max;
            // Levenshtein punishes insertions twice (feat. lines, "(抖音版)" tails that survive
            // Clean), while LCS tolerates them. Lyricify's helper scores short titles with LCS
            // for exactly this reason, so take the friendlier of the two.
            double lcs = (double)LcsLength(a, b) / max;
            return lev > lcs ? lev : lcs;
        }

        static int LcsLength(string a, string b)
        {
            int n = a.Length, m = b.Length;
            if (n == 0 || m == 0) return 0;
            var prev = new int[m + 1];
            var cur = new int[m + 1];
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                    cur[j] = a[i - 1] == b[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
                var tmp = prev; prev = cur; cur = tmp;
                Array.Clear(cur, 0, cur.Length);
            }
            return prev[m];
        }

        /// <summary>
        /// Drop the decorative tails that online catalogues add and that make a perfect title
        /// look like a different song: "- Remastered 2011", "(Explicit)", "feat. Someone",
        /// "- Single", "OST", trailing "/xxx version". Only outside-vs-inside brackets matter,
        /// so this runs on the already stripped text.
        /// </summary>
        public static string StripDecorations(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\s*[-–—]\s*(remaster(ed)?|digital remaster|explicit|single|ep|ost|soundtrack|live|acoustic|deluxe)(\s*\d{4})?\s*$",
                " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+(feat\.?|ft\.?|featuring)\s+[^,;]+$", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*[-–—]\s*$", " ");
            s = Regex.Replace(s, @"\s+", " ");
            return s.Trim();
        }

        public static double ArtistSimilar(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            string[] pa = Regex.Split(a, @"\s*(?:&|,|、|/|feat\.?|ft\.?|x)\s*", RegexOptions.IgnoreCase);
            string[] pb = Regex.Split(b, @"\s*(?:&|,|、|/|feat\.?|ft\.?|x)\s*", RegexOptions.IgnoreCase);
            double best = Similar(a, b);
            foreach (string x in pa)
            {
                if (x.Trim().Length == 0) continue;
                foreach (string y in pb)
                {
                    if (y.Trim().Length == 0) continue;
                    double s = Similar(x, y);
                    if (s > best) best = s;
                }
            }
            return best;
        }

        static int Levenshtein(string s, string t)
        {
            int n = s.Length, m = t.Length;
            if (n == 0) return m;
            if (m == 0) return n;
            var prev = new int[m + 1];
            var cur = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    int a = cur[j - 1] + 1;
                    int b = prev[j] + 1;
                    int c = prev[j - 1] + cost;
                    cur[j] = Math.Min(a, Math.Min(b, c));
                }
                var tmp = prev; prev = cur; cur = tmp;
            }
            return prev[m];
        }
    }

    /// <summary>One search hit before the lyric body is fetched, with its match components.</summary>
    public class Cand
    {
        public string Src = "";
        public string Id = "";
        public string Name = "";
        public string Artist = "";
        public double Dur;
        public double Score;
        public double TitleSim;
        public double ArtistSim;
        public double DurSim;    // -1 when either side has no running time
        public bool VersionDiff; // same base title but different version tag (live/remix/伴奏…)
        public bool InstrName;   // provider says "纯音乐 / 伴奏 / instrumental" in the title
    }

    /// <summary>
    /// The lyric pipeline: local .lrc first, then QQ / NetEase / lrclib queried in parallel, the
    /// first acceptable result wins (with a short grace window for a better one), everything is
    /// sanitised and cached under <c>data\lyrics</c>.
    /// </summary>
    /// <summary>One row in the manual lyric picker.</summary>
    public class LyricChoice
    {
        public string Source = "";
        public string Id = "";
        public string Title = "";
        public string Artist = "";
        public double Dur;
        public double Score;
    }

    public class LyricsService
    {
        static readonly object LogLock = new object();

        public static void Log(string s)
        {
            try
            {
                string f = Path.Combine(Config.Dir, "amc.log");
                lock (LogLock)
                {
                    var fi = new FileInfo(f);
                    if (fi.Exists && fi.Length > 300000) File.Delete(f);
                    File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\r\n", Encoding.UTF8);
                }
            }
            catch { }
        }

        static readonly Regex TimeRx = new Regex(@"\[(\d{1,3}):(\d{1,2})(?:[\\.:](\d{1,3}))?\]", RegexOptions.Compiled);
        static readonly Regex MetaRx = new Regex("^[\\w\\s\\.\\-]{0,20}?(作词|作曲|编曲|制作人|制作|混音|母带|录音|监制|出品|词|曲|演唱|原唱|和声|吉他|贝斯|鼓|键盘|弦乐|OP|SP|Lyrics?|Song|Composed|Arranged|Produced|Producer|Written|Mixed|Mastered|Performed|Vocals|Guitar|Bass|Drums|Piano|Strings)\\s*[:：]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // trailing credit blocks that show up as the last "line" of an LRC file
        static readonly Regex CreditRx = new Regex("(recorded (at|by)|engineered|published by|under license|courtesy of|released on|label\\s*[:：]|℗|©)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex CreditWordRx = new Regex("(作词|作曲|编曲|制作|混音|母带|录音|监制|出品|演唱|和声|吉他|贝斯|弦乐|鼓|键盘|词|曲|Vocals|Guitar|Bass|Drums|Timpani|Gong|Piano|Strings|Percussion|Engineer|Producer|Composed|Arranged|Written|Performed|Mixed|Mastered|Lyrics)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "Operatic Vocals : Freddie Mercury" style credit lines
        static bool IsCredit(string body)
        {
            int i = body.IndexOf(':');
            int j = body.IndexOf('：');
            int idx = i < 0 ? j : (j < 0 ? i : Math.Min(i, j));
            if (idx <= 0 || idx > 40) return false;
            string head = body.Substring(0, idx).Trim();
            if (head.Length == 0) return false;
            return CreditWordRx.IsMatch(head);
        }

        // Placeholder lines that providers return for tracks without lyrics.
        // Per-line filter: only wording that can never be a real lyric line.
        static readonly Regex PlaceholderRx = new Regex(
            "纯音乐|纯音|没有填词|没有歌词|暂无歌词|无歌词|歌词未|敬请欣赏|请欣赏|此歌曲|该歌曲|"
            + "instrumental|no\\s*lyri|lyric.{0,8}not|not available",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Whole-document check: broader, only applied when the doc is 1~2 short lines.
        static readonly Regex NoticeRx = new Regex(
            "暂无|未有|未提供|还没有|暂时没有|karaoke|off[\\s\\-]?vocal|纯音乐|instrumental|no\\s*lyri",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Delete every cached .lrc and return how many files went away. Used by both
        /// the manual button on the Lyrics page and the scheduled cleanup below.</summary>
        public static int ClearCache()
        {
            int n = 0;
            try
            {
                string dir = CacheDir;
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        try { File.Delete(f); n++; } catch { }
                    }
                }
            }
            catch (Exception ex) { Log("cache clear failed: " + ex.Message); }
            return n;
        }

        /// <summary>Scheduled housekeeping: only runs when the user turned it on and the interval
        /// has actually elapsed. Returns true when it deleted something.</summary>
        public static bool AutoCleanIfDue(Config cfg)
        {
            if (cfg == null || !cfg.AutoCleanCache || cfg.CacheCleanDays <= 0) return false;
            DateTime last;
            bool haveLast = DateTime.TryParse(cfg.LastCacheClean, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out last);
            DateTime now = DateTime.Now;
            if (!haveLast)
            {
                // First time we see the option enabled: stamp today instead of wiping the cache the
                // user accumulated so far. The interval starts counting from here.
                cfg.LastCacheClean = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                cfg.Save();
                Log("cache autoclean armed (every " + cfg.CacheCleanDays + "d, starting today)");
                return false;
            }
            if ((now - last).TotalDays < cfg.CacheCleanDays) return false;
            int n = ClearCache();
            cfg.LastCacheClean = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            cfg.Save();
            Log("cache autoclean removed " + n + " files (interval " + cfg.CacheCleanDays + "d)");
            return true;
        }

        public static string CacheDir
        {
            get
            {
                string d = Path.Combine(Config.Dir, "lyrics");
                try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); }
                catch { return Config.Dir; }
                return d;
            }
        }

        static string KeyFor(string title, string artist, double dur)
        {
            string k = TextUtil.Clean(title) + "|" + TextUtil.Clean(artist) + "|" + ((int)(dur / 5)).ToString(CultureInfo.InvariantCulture);
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(k));
                var sb = new StringBuilder();
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // Set by the "换一个歌词源" menu item: one-shot, ignored after this call.
        public static string SkipSource;

        /// <summary>
        /// Search every source for the current track and hand back the ranked candidates instead
        /// of silently picking one. The card only shows a single line, so when the guess is wrong
        /// the user has no way to tell which song we actually matched - this list is that view.
        /// </summary>
        public List<LyricChoice> Candidates(string title, string artist, double duration)
        {
            // Both sources are queried in parallel: each one may need a second, looser attempt,
            // and doing them one after another kept the picker on "searching" for ~8 seconds.
            var results = new List<Cand>[2];
            var threads = new System.Threading.Thread[2];
            string[] sources = new string[] { "qq", "netease", "kugou" };
            for (int i = 0; i < sources.Length; i++)
            {
                int slot = i;
                string src = sources[i];
                threads[i] = new System.Threading.Thread(delegate()
                {
                    try { results[slot] = Search(src, title, artist, duration); }
                    catch (Exception ex) { Log("  " + src + " search fail " + ex.GetType().Name); results[slot] = new List<Cand>(); }
                });
                threads[i].IsBackground = true;
                threads[i].Start();
            }
            var outp = new List<LyricChoice>();
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Join(3500);
                List<Cand> list = results[i];
                if (list == null) continue;
                foreach (Cand c in list)
                {
                    // Below this the entry is noise (same word in an unrelated title); keeping it
                    // would only make the list longer, not more useful.
                    if (c.Score < 0.35) continue;
                    outp.Add(new LyricChoice
                    {
                        Source = sources[i],
                        Id = c.Id,
                        Title = c.Name,
                        Artist = c.Artist,
                        Dur = c.Dur,
                        Score = c.Score
                    });
                }
            }
            outp.Sort(delegate (LyricChoice x, LyricChoice y) { return y.Score.CompareTo(x.Score); });
            // The same recording is usually offered by both sources; keep the higher score once.
            var seen = new Dictionary<string, bool>();
            var dedup = new List<LyricChoice>();
            foreach (LyricChoice c in outp)
            {
                string k = TextUtil.Clean(c.Title) + "|" + TextUtil.Clean(c.Artist);
                if (seen.ContainsKey(k)) continue;
                seen[k] = true;
                dedup.Add(c);
            }
            if (dedup.Count > 12) dedup.RemoveRange(12, dedup.Count - 12);
            Log("  candidates " + dedup.Count + " for " + title);
            return dedup;
        }

        /// <summary>Fetch the body for one explicit candidate (the user picked it).</summary>
        public LyricDoc Fetch(LyricChoice ch, string title, string artist, double duration)
        {
            if (ch == null) return null;
            var c = new Cand { Src = ch.Source, Id = ch.Id, Name = ch.Title, Artist = ch.Artist, Dur = ch.Dur };
            LyricDoc doc = FetchLyric(c);
            if (doc == null) return null;
            doc = Sanitize(doc, title, artist, duration);
            doc.Matched = ch.Title + " - " + ch.Artist;
            Log("  manual pick " + ch.Source + " " + doc.Matched + " lines=" + doc.Lines.Count);
            return doc;
        }

        public LyricDoc Get(string title, string artist, double duration, bool force)
        {
            string skipSrc = SkipSource;
            SkipSource = null;
            // "another source" while the local folder was serving: don't persist the result,
            // otherwise the network copy would shadow the user's own file forever.
            bool skipLocal = (skipSrc == "local");
            string key = KeyFor(title, artist, duration);
            string file = Path.Combine(CacheDir, key + ".lrc");
            string miss = Path.Combine(CacheDir, key + ".miss");

            if (!force)
            {
                if (File.Exists(file))
                {
                    try
                    {
                        var doc = Parse(File.ReadAllText(file, Encoding.UTF8), Path.GetFileNameWithoutExtension(file));
                        if (doc.Lines.Count > 0 || doc.Instrumental) return doc;
                    }
                    catch { }
                }
                if (File.Exists(miss))
                {
                    try
                    {
                        var age = DateTime.Now - File.GetLastWriteTime(miss);
                        if (age.TotalHours < 6) return null;
                    }
                    catch { }
                }
            }

            var found = Pick(title, artist, duration, skipSrc);

            Log("get[" + key + "] t=" + title + " | a=" + artist + " | d=" + ((int)duration) +
                " => " + (found == null ? "NULL" : found.Source + " lines=" + found.Lines.Count + " instr=" + found.Instrumental + " matched=" + found.Matched));

            try
            {
                if (found != null && (found.Lines.Count > 0 || found.Instrumental))
                {
                    if (!skipLocal) SaveLrc(file, found);
                    if (File.Exists(miss)) File.Delete(miss);
                    return found;
                }
                File.WriteAllText(miss, DateTime.Now.ToString("o"));
            }
            catch { }
            return null;
        }

        // Copy every cached lyric into data\lrc so the user can keep / share them.
        // Returns the number written; never overwrites an existing file.
        public static string ExportToLocal(out int written, out int existed, out int skipped)
        {
            written = 0; existed = 0; skipped = 0;
            try
            {
                string cache = CacheDir;
                string dest = ExportDir;
                foreach (string f in Directory.GetFiles(cache, "*.lrc"))
                {
                    string text;
                    try { text = File.ReadAllText(f, Encoding.UTF8); }
                    catch { skipped++; continue; }
                    string name = "";
                    bool instr = false;
                    var body = new StringBuilder();
                    foreach (string raw in text.Replace("\r", "").Split('\n'))
                    {
                        string line = raw;
                        if (line.StartsWith("#matched=")) { name = line.Substring(9).Trim(); continue; }
                        if (line.StartsWith("#instr=1")) { instr = true; continue; }
                        if (line.StartsWith("#src=")) continue;
                        if (line.Trim().Length == 0) continue;
                        body.AppendLine(line);
                    }
                    if (instr || body.Length == 0) { skipped++; continue; }
                    if (name.Length == 0) name = Path.GetFileNameWithoutExtension(f);
                    name = SafeFileName(name);
                    string target = Path.Combine(dest, name + ".lrc");
                    if (File.Exists(target)) { existed++; continue; }
                    try
                    {
                        File.WriteAllText(target, body.ToString(), Encoding.UTF8);
                        written++;
                    }
                    catch { skipped++; }
                }
            }
            catch { }
            return ExportDir;
        }

        // Export target: a separate folder on purpose, so a bulk export never changes
        // what the card plays back. Move files into data\lrc by hand to use them.
        public static string ExportDir
        {
            get
            {
                string d = Path.Combine(Config.Dir, "lrc-export");
                try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); }
                catch { return Config.Dir; }
                return d;
            }
        }

        static string SafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "lyrics";
            foreach (char c in "\\/:*?\"<>|")
                s = s.Replace(c, ' ');
            s = s.Replace('\u2014', '-').Replace('\u2013', '-');
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length > 110) s = s.Substring(0, 110).Trim();
            if (s.Length == 0) return "lyrics";
            return s;
        }

        static void SaveLrc(string file, LyricDoc doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#src=" + doc.Source);
            sb.AppendLine("#matched=" + doc.Matched);
            if (doc.Instrumental) sb.AppendLine("#instr=1");
            foreach (var l in doc.Lines)
            {
                int min = (int)(l.Time / 60);
                double sec = l.Time - min * 60;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "[{0:00}:{1:00.00}]{2}", min, sec, l.Text));
            }
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        }

        public static LyricDoc Parse(string text, string source)
        {
            var doc = new LyricDoc();
            doc.Source = source ?? "";
            if (string.IsNullOrEmpty(text)) return doc;
            double offset = 0;
            var list = new List<LyricLine>();
            foreach (string rawLine in text.Replace("\r", "").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#"))
                {
                    if (line.StartsWith("#src=")) doc.Source = line.Substring(5);
                    else if (line.StartsWith("#matched=")) doc.Matched = line.Substring(9);
                    else if (line.StartsWith("#instr=1")) doc.Instrumental = true;
                    continue;
                }
                var off = Regex.Match(line, @"^\[offset:\s*([+-]?\d+)\]\s*$", RegexOptions.IgnoreCase);
                if (off.Success)
                {
                    double o;
                    if (double.TryParse(off.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out o)) offset = o / 1000.0;
                    continue;
                }
                var ms = TimeRx.Matches(line);
                if (ms.Count == 0) continue;
                string body = TimeRx.Replace(line, "").Trim();
                if (body.Length == 0) continue;
                if (MetaRx.IsMatch(body))
                {
                    continue;
                }
                if (CreditRx.IsMatch(body))
                {
                    continue;
                }
                if (IsCredit(body))
                {
                    continue;
                }
                // credit blocks at the head of a lyric file ("Album : ...", "合声：...")
                double t0 = Stamp(ms[0]);
                if (t0 <= 45 && body.Length <= 40 && (body.IndexOf('：') >= 0 || body.IndexOf(':') >= 0))
                {
                    continue;
                }
                foreach (Match m in ms)
                {
                    double t = Stamp(m);
                    list.Add(new LyricLine { Time = t + offset, Text = body });
                }
            }
            list.Sort(delegate (LyricLine a, LyricLine b) { return a.Time.CompareTo(b.Time); });
            // Drop duplicate timestamps (karaoke files often repeat a line) so the display
            // cannot flip between two entries that share a time stamp.
            var uniq = new List<LyricLine>();
            for (int i = 0; i < list.Count; i++)
            {
                if (uniq.Count > 0 && Math.Abs(uniq[uniq.Count - 1].Time - list[i].Time) < 0.001) continue;
                uniq.Add(list[i]);
            }
            doc.Lines = uniq;
            return doc;
        }

        static double Stamp(Match m)
        {
            int min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double frac = 0;
            if (m.Groups[3].Success)
            {
                string f = m.Groups[3].Value;
                double v;
                if (double.TryParse("0." + f, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) frac = v;
            }
            return min * 60 + sec + frac;
        }

        // ---------------- selection ----------------

        static LyricDoc Sanitize(LyricDoc doc, string title, string artist, double duration)
        {
            if (doc == null) return null;
            var keep = new List<LyricLine>();
            bool instr = doc.Instrumental;
            string ct = TextUtil.Clean(title);
            string ca = TextUtil.Clean(artist);
            foreach (var l in doc.Lines)
            {
                string t = l.Text.Trim();
                if (t.Length == 0) continue;
                if (PlaceholderRx.IsMatch(t)) { instr = true; continue; }
                string ct2 = TextUtil.Clean(t);
                // provider credit lines ("晴天 - 周杰伦 (Jay Chou)", "QQ音乐" etc.)
                if (t.IndexOf("QQ音乐", StringComparison.Ordinal) >= 0 ||
                    t.IndexOf("网易云音乐", StringComparison.Ordinal) >= 0 ||
                    t.IndexOf("酷狗", StringComparison.Ordinal) >= 0) continue;
                if (l.Time <= 1.5 && t.IndexOf(" - ", StringComparison.Ordinal) > 0)
                {
                    if ((ct.Length > 0 && ct2.IndexOf(ct, StringComparison.Ordinal) >= 0) ||
                        (ca.Length > 0 && ct2.IndexOf(ca, StringComparison.Ordinal) >= 0)) continue;
                }
                keep.Add(l);
            }
            // Some providers answer with a one-or-two-line notice in wording nobody
            // enumerated ("这首歌还没有歌词哦"). If the whole document is tiny and looks
            // like a notice, normalise it to the instrumental state so that every source
            // ends up rendering the same line.
            if (keep.Count > 0 && keep.Count <= 2)
            {
                string joined = keep[0].Text.Trim();
                if (keep.Count > 1) joined = joined + " " + keep[1].Text.Trim();
                if (joined.Length <= 80 && NoticeRx.IsMatch(joined))
                {
                    Log("  notice-only doc -> instrumental: " + joined);
                    keep.Clear();
                    instr = true;
                }
            }
            doc.Lines = keep;
            doc.Instrumental = instr && keep.Count < 8;

            // A "synced" file whose lines all share one timestamp (or are all zero) would play
            // as a single frozen line. Providers do ship those, so refuse them: an empty lyric
            // line is honest, a stuck first sentence is not.
            if (keep.Count >= 3)
            {
                int distinct = 1;
                for (int i = 1; i < keep.Count; i++)
                    if (Math.Abs(keep[i].Time - keep[0].Time) > 0.05) { distinct = 2; break; }
                if (distinct < 2)
                {
                    Log("  unsynced doc (one timestamp for " + keep.Count + " lines)");
                    doc.Lines = new List<LyricLine>();
                    doc.Instrumental = false;
                    return doc;
                }
            }

            if (duration > 30 && keep.Count > 0)
            {
                double last = keep[keep.Count - 1].Time;
                if (last > duration + 45 || last < duration * 0.4)
                {
                    Log("  timing mismatch last=" + ((int)last) + " dur=" + ((int)duration));
                    doc.Lines = new List<LyricLine>();
                    doc.Instrumental = false;
                    return doc;
                }
            }
            return doc;
        }

        static string F2(double v)
        {
            return v.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // One source's attempt: the winner doc (>=3 lines) or an "instrumental" marker.
        class SrcResult
        {
            public string Src = "";
            public LyricDoc Doc;
            public double Score;
            public LyricDoc Instr;
            public bool Passed { get { return Doc != null; } }
        }

        // Sources are queried in parallel; the first one that clears the match threshold wins,
        // but we wait a short grace window so a later (better scoring) hit can still take it.
        const int SourceTimeoutMs = 6000;
        const int GraceMs = 700;

        static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                if (c >= 0x3040 && c <= 0x30FF) return true;   // kana
                if (c >= 0x3400 && c <= 0x9FFF) return true;   // CJK ideographs
                if (c >= 0xAC00 && c <= 0xD7AF) return true;   // hangul
            }
            return false;
        }

        // CJK titles are best served by the Chinese services, latin titles by lrclib.
        static string[] PreferredSources(string title)
        {
            if (HasCjk(title)) return new string[] { "qq", "netease", "kugou", "lrclib" };
            return new string[] { "lrclib", "qq", "netease", "kugou" };
        }

        // A user-supplied folder of .lrc files: offline, instant, and always wins.
        public static string LocalDir
        {
            get
            {
                string d = Path.Combine(Config.Dir, "lrc");
                try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); }
                catch { return Config.Dir; }
                return d;
            }
        }

        static LyricDoc TryLocal(string title, string artist, double duration)
        {
            try
            {
                string dir = LocalDir;
                if (!Directory.Exists(dir)) return null;
                string want = TextUtil.Clean(TextUtil.StripBrackets(title));
                if (want.Length == 0) return null;
                string bestPath = null;
                double bestSim = 0;
                foreach (string f in Directory.GetFiles(dir, "*.lrc"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    string n = TextUtil.Clean(name);
                    double sim = TextUtil.Similar(want, n);
                    // "Title - Artist.lrc" or "Artist - Title.lrc"
                    int dash = n.IndexOf(" - ", StringComparison.Ordinal);
                    if (dash > 0)
                    {
                        sim = Math.Max(sim, TextUtil.Similar(want, n.Substring(0, dash)));
                        sim = Math.Max(sim, TextUtil.Similar(want, n.Substring(dash + 3)));
                    }
                    if (n.StartsWith(want)) sim = Math.Max(sim, 0.95);
                    if (sim > bestSim) { bestSim = sim; bestPath = f; }
                }
                if (bestPath == null || bestSim < 0.80) return null;
                var doc = Parse(File.ReadAllText(bestPath, Encoding.UTF8), "local");
                doc.Matched = Path.GetFileNameWithoutExtension(bestPath);
                // A user-supplied file is trusted: no duration sanity check (pass 0 to skip it).
                doc = Sanitize(doc, title, artist, 0);
                Log("  local candidate " + Path.GetFileName(bestPath) + " sim=" + F2(bestSim) + " lines=" + doc.Lines.Count);
                return doc;
            }
            catch { return null; }
        }

        static LyricDoc Pick(string title, string artist, double duration, string skipSrc)
        {
            var local = (skipSrc == "local") ? null : TryLocal(title, artist, duration);
            if (local != null && local.Lines.Count >= 3)
            {
                Log("  take local " + local.Matched + " lines=" + local.Lines.Count);
                return local;
            }
            // A local file that only contains a notice ("纯音乐，请欣赏") is authoritative:
            // the user put it there, so nobody else should be asked.
            LyricDoc localInstr = (local != null && local.Instrumental) ? local : null;

            var prefs = PreferredSources(title);
            var results = new List<SrcResult>();
            var gate = new object();
            var threads = new List<Thread>();
            foreach (string src in prefs)
            {
                if (src == skipSrc) { Log("  skip source " + src + " (user asked for another one)"); continue; }
                string s2 = src;
                var t = new Thread(delegate()
                {
                    SrcResult r = TrySource(s2, title, artist, duration);
                    lock (gate) { results.Add(r); }
                });
                t.IsBackground = true;
                t.Start();
                threads.Add(t);
            }
            if (threads.Count == 0)
            {
                // the user asked for "another source" on every source: start over from scratch
                return Pick(title, artist, duration, null);
            }

            DateTime started = DateTime.Now;
            SrcResult best = null;
            while (best == null)
            {
                lock (gate) { best = BestOf(results); }
                if (best != null) break;
                if ((DateTime.Now - started).TotalMilliseconds > SourceTimeoutMs) break;
                bool alive = false;
                for (int i = 0; i < threads.Count; i++) if (threads[i].IsAlive) alive = true;
                if (!alive) break;
                Thread.Sleep(35);
            }

            if (best != null)
            {
                DateTime until = DateTime.Now.AddMilliseconds(GraceMs);
                while (DateTime.Now < until)
                {
                    SrcResult better;
                    lock (gate) { better = BestOf(results); }
                    if (better != null && better.Score > best.Score + 0.02) best = better;
                    bool alive = false;
                    for (int i = 0; i < threads.Count; i++) if (threads[i].IsAlive) alive = true;
                    if (!alive) break;
                    Thread.Sleep(30);
                }
                Log("  take " + best.Doc.Source + " " + best.Doc.Matched + " score=" + F2(best.Score) + " lines=" + best.Doc.Lines.Count);
                return best.Doc;
            }

            if (localInstr != null)
            {
                Log("  take local " + localInstr.Matched + " instr");
                return localInstr;
            }

            lock (gate)
            {
                LyricDoc instr = null;
                for (int i = 0; i < results.Count; i++) if (results[i].Instr != null) { instr = results[i].Instr; break; }
                return instr;
            }
        }

        static SrcResult BestOf(List<SrcResult> results)
        {
            SrcResult best = null;
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (!r.Passed) continue;
                if (best == null || r.Score > best.Score) best = r;
            }
            return best;
        }

        // Search + fetch inside one source. Stops at the first candidate that survives the
        // "same song" gate AND yields >= 3 lyric lines.
        static SrcResult TrySource(string src, string title, string artist, double duration)
        {
            var res = new SrcResult();
            res.Src = src;
            if (src == "lrclib")
            {
                var lr = TryLrclib(title, artist, duration);
                if (lr != null)
                {
                    lr = Sanitize(lr, title, artist, duration);
                    if (lr.Lines.Count >= 3) { lr.Matched = title + " - " + artist; res.Doc = lr; res.Score = 0.90; }
                    else if (lr.Instrumental) res.Instr = lr;
                }
                return res;
            }

            List<Cand> cands = Search(src, title, artist, duration);
            Cand fallback = null;
            int tried = 0;
            foreach (var c in cands)
            {
                if (tried >= 4) break;
                tried++;
                // One weighted verdict instead of three separate booleans: the old code let a
                // strong title paper over a wrong artist as long as *either* artist or length
                // looked plausible, which is how a same-name song ended up on screen.
                // Tight on purpose: 0.80 of the available signal, and the title itself must be close.
                bool accept = c.Score >= 0.80 && c.TitleSim >= 0.72;
                if (!accept)
                {
                    Log("  skip " + c.Src + " " + c.Name + " - " + c.Artist
                        + " ts=" + F2(c.TitleSim) + " as=" + F2(c.ArtistSim)
                        + " dd=" + (c.DurSim < 0 ? "na" : F2(c.DurSim))
                        + (c.VersionDiff ? " ver" : "") + " norm=" + F2(c.Score));
                    // Covers and obscure uploads often have no candidate with a matching
                    // artist at all. Remember the best exact-title one so that a total miss
                    // can still fall back to the original song's lyrics instead of nothing.
                    if (c.TitleSim >= 0.98 && (fallback == null || c.Score > fallback.Score))
                        fallback = c;
                    continue;
                }
                var doc = FetchLyric(c);
                if (doc == null) { Log("  nolyr " + c.Src + " " + c.Name); continue; }
                doc = Sanitize(doc, title, artist, duration);
                doc.Matched = c.Name + " - " + c.Artist;
                if (c.InstrName && doc.Lines.Count < 3) doc.Instrumental = true;
                if (doc.Lines.Count >= 3) { res.Doc = doc; res.Score = c.Score; return res; }
                if (doc.Instrumental && res.Instr == null) res.Instr = doc;
                Log("  reject " + c.Src + " " + c.Name + " lines=" + doc.Lines.Count + " instr=" + doc.Instrumental);
            }

            // Nothing looked like the same recording. A cover or a niche upload still plays
            // the same song, so show the exact-title candidate we set aside instead of an
            // empty lyric line. Logged, because this is the one path that can pair a song
            // with a different artist's lyrics.
            if (fallback != null)
            {
                var fb = FetchLyric(fallback);
                if (fb != null)
                {
                    fb = Sanitize(fb, title, artist, duration);
                    fb.Matched = fallback.Name + " - " + fallback.Artist;
                    if (fb.Lines.Count >= 3)
                    {
                        Log("  fallback exact-title " + fallback.Name + " - " + fallback.Artist
                            + " (no artist match)");
                        res.Doc = fb;
                        res.Score = fallback.Score - 0.15;
                        return res;
                    }
                }
            }
            return res;
        }

        // ---------------- providers: search ----------------

        static List<Cand> SearchRaw(string src, string q, string title, string artist, double duration)
        {
            var outp = new List<Cand>();
            if (src == "kugou")
            {
                // Kugou's search answer mixes encodings per field, and no single reading of it
                // recovers Chinese titles cleanly. Rather than show a lyric whose title is
                // 铏氭嫙, Kugou is only used for titles we can read: Latin/digit queries.
                if (HasCjk(title) || HasCjk(artist))
                {
                    Log("  kugou skip cjk t=" + title);
                    return outp;
                }
                string url = "http://mobiles.kugou.com/api/v3/search/song?format=json&showtype=1&page=1&pagesize=12&keyword="
                             + Uri.EscapeDataString(q);
                byte[] bytes = GetBytes(url, "http://www.kugou.com/", 6);
                if (bytes == null || bytes.Length == 0) return outp;
                // The same endpoint answers UTF-8 for some songs and GB18030 for others, and a
                // wrong guess turns every title into mojibake that scores 0. Decode both ways and
                // keep whichever reading actually matches what we asked for.
                string asUtf8 = Encoding.UTF8.GetString(bytes);
                string asGbk = null;
                try { asGbk = Encoding.GetEncoding(936).GetString(bytes); }
                catch { }
                AddKugou(outp, asUtf8, title, artist, duration);
                if (outp.Count == 0 && asGbk != null) AddKugou(outp, asGbk, title, artist, duration);
                return outp;
            }

            if (src == "netease")
            {
                string url = "https://music.163.com/api/search/get/web?type=1&limit=12&s=" + Uri.EscapeDataString(q);
                string body = Get(url, null);
                if (string.IsNullOrEmpty(body)) return outp;
                var root = J.Parse(body);
                var songs = root["result"] != null ? root["result"].Arr("songs") : null;
                if (songs == null) return outp;
                foreach (var s in songs)
                {
                    string nm = s.Str("name");
                    var al = s.Arr("artists");
                    string ar = "";
                    if (al != null)
                    {
                        var names = new List<string>();
                        foreach (var a in al) names.Add(a.Str("name"));
                        ar = string.Join(" / ", names.ToArray());
                    }
                    double durMs = s.Num("duration", 0);
                    var c = new Cand();
                    c.Src = "netease";
                    c.Id = ((long)s.Num("id", 0)).ToString(CultureInfo.InvariantCulture);
                    c.Name = nm;
                    c.Artist = ar;
                    c.InstrName = LooksInstrumental(nm);
                    c.Dur = durMs / 1000.0;
                    Score(title, artist, duration, c);
                    if (c.Score >= 0.5) outp.Add(c);
                }
                return outp;
            }

            string qurl = "https://c.y.qq.com/soso/fcgi-bin/client_search_cp?format=json&n=10&w=" + Uri.EscapeDataString(q);
            string qbody = Get(qurl, "https://y.qq.com/");
            if (string.IsNullOrEmpty(qbody)) return outp;
            int kb = qbody.IndexOf('{');
            if (kb < 0) return outp;
            var qroot = J.Parse(qbody.Substring(kb));
            var qsongs = (qroot["data"] != null && qroot["data"]["song"] != null) ? qroot["data"]["song"].Arr("list") : null;
            if (qsongs == null) return outp;
            foreach (var s in qsongs)
            {
                string nm = s.Str("songname");
                var singer = s.Arr("singer");
                string ar = "";
                if (singer != null)
                {
                    var names = new List<string>();
                    foreach (var a in singer) names.Add(a.Str("name"));
                    ar = string.Join(" / ", names.ToArray());
                }
                var c = new Cand();
                c.Src = "qq";
                c.Id = s.Str("songmid");
                c.Name = nm;
                c.Artist = ar;
                c.InstrName = LooksInstrumental(nm);
                c.Dur = s.Num("interval", 0);
                Score(title, artist, duration, c);
                if (c.Score >= 0.5 && !string.IsNullOrEmpty(c.Id)) outp.Add(c);
            }
            return outp;
        }

        static void AddKugou(List<Cand> outp, string body, string title, string artist, double duration)
        {
            if (string.IsNullOrEmpty(body)) return;
            int b = body.IndexOf('{');
            if (b < 0) return;
            J parsed;
            try { parsed = J.Parse(body.Substring(b)); }
            catch { return; }
            if (parsed == null) return;
            var root = parsed;
            var info = (root["data"] != null) ? root["data"].Arr("info") : null;
            if (info == null || info.Count == 0) return;
            foreach (var s in info)
            {
                string nm = s.Str("songname");
                var c = new Cand();
                c.Src = "kugou";
                c.Id = s.Str("hash");
                c.Name = nm;
                c.Artist = s.Str("singername");
                c.InstrName = LooksInstrumental(nm);
                c.Dur = s.Num("duration", 0);
                Score(title, artist, duration, c);
                if (c.Score >= 0.5 && !string.IsNullOrEmpty(c.Id)) outp.Add(c);
            }
            Log("  kugou parse -> " + outp.Count.ToString(CultureInfo.InvariantCulture) +
                (outp.Count > 0 ? " first=" + outp[0].Name + " - " + outp[0].Artist + " d=" + F2(outp[0].Dur) : ""));
        }

        static void SortCands(List<Cand> list)
        {
            list.Sort(delegate (Cand x, Cand y) { return y.Score.CompareTo(x.Score); });
        }

        static List<Cand> Search(string src, string title, string artist, double duration)
        {
            string t = TextUtil.SearchTitle(title);
            var list = new List<Cand>();
            try
            {
                list = SearchRaw(src, (t + " " + artist).Trim(), title, artist, duration);
                SortCands(list);
                if (list.Count == 0 || list[0].Score < 0.85)
                {
                    var more = SearchRaw(src, t, title, artist, duration);
                    foreach (var c in more)
                    {
                        bool dup = false;
                        foreach (var x in list) if (x.Id == c.Id) { dup = true; break; }
                        if (!dup) list.Add(c);
                    }
                    SortCands(list);
                }
                if (list.Count > 0) Log("  " + src + " top " + list[0].Score.ToString("0.00", CultureInfo.InvariantCulture) + " " + list[0].Name + " - " + list[0].Artist + " [" + list.Count + "]");
                else Log("  " + src + " no candidate");
            }
            catch (Exception ex) { Log("  " + src + " search fail " + ex.GetType().Name); }
            return list;
        }

        // ---------------- providers: lyric body ----------------

        static LyricDoc FetchLyric(Cand c)
        {
            try
            {
                if (c.Src == "netease")
                {
                    string url = "https://music.163.com/api/song/lyric?id=" + c.Id + "&lv=1&tv=-1";
                    string body = Get(url, null);
                    if (string.IsNullOrEmpty(body)) return null;
                    var lj = J.Parse(body);
                    if (body.IndexOf("\"pureMusic\":true", StringComparison.Ordinal) >= 0)
                    {
                        var pm = new LyricDoc();
                        pm.Source = "netease";
                        pm.Instrumental = true;
                        return pm;
                    }
                    string lrc = lj["lrc"] != null ? lj["lrc"].Str("lyric") : null;
                    if (string.IsNullOrEmpty(lrc)) return null;
                    return Parse(lrc, "netease");
                }
                if (c.Src == "kugou")
                {
                    // Two hops: the search endpoint hands out id+accesskey, the download endpoint
                    // returns the LRC as base64 inside JSON.
                    string kw = Uri.EscapeDataString(c.Name + " - " + c.Artist);   // UTF-8, the GBK answer is on the way back
                    string ms = ((int)Math.Round(c.Dur * 1000)).ToString(CultureInfo.InvariantCulture);
                    string s1 = "http://krcs.kugou.com/search?ver=1&man=yes&client=pc&keyword=" + kw +
                                "&duration=" + ms + "&hash=" + Uri.EscapeDataString(c.Id);
                    string b1 = GetFlexible(s1, "http://www.kugou.com/", 6);
                    if (string.IsNullOrEmpty(b1)) return null;
                    var r1 = J.Parse(b1);
                    var cands = r1.Arr("candidates");
                    if (cands == null || cands.Count == 0) return null;
                    string kid = cands[0].Str("id");
                    string key = cands[0].Str("accesskey");
                    if (string.IsNullOrEmpty(kid) || string.IsNullOrEmpty(key)) return null;
                    string s2 = "http://lyrics.kugou.com/download?ver=1&client=pc&fmt=lrc&charset=utf8&id=" +
                                Uri.EscapeDataString(kid) + "&accesskey=" + Uri.EscapeDataString(key);
                    string b2 = GetFlexible(s2, "http://www.kugou.com/", 6);
                    if (string.IsNullOrEmpty(b2)) return null;
                    var r2 = J.Parse(b2);
                    string b64 = r2.Str("content");
                    if (string.IsNullOrEmpty(b64)) return null;
                    string lrc = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                    if (string.IsNullOrEmpty(lrc)) return null;
                    return Parse(lrc, "kugou");
                }
                if (c.Src == "qq")
                {
                    string url = "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid=" + Uri.EscapeDataString(c.Id) + "&format=json&nobase64=1";
                    string body = Get(url, "https://y.qq.com/");
                    if (string.IsNullOrEmpty(body)) return null;
                    int b = body.IndexOf('{');
                    if (b < 0) return null;
                    var lj = J.Parse(body.Substring(b));
                    string lrc = lj.Str("lyric");
                    if (string.IsNullOrEmpty(lrc)) return null;
                    return Parse(lrc, "qq");
                }
            }
            catch (Exception ex) { Log("  lyric fail " + c.Src + " " + ex.GetType().Name); }
            return null;
        }

        static LyricDoc TryLrclib(string title, string artist, double duration)
        {
            try
            {
                string url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(TextUtil.SearchTitle(title)) +
                             "&artist_name=" + Uri.EscapeDataString(artist);
                if (duration > 20) url += "&duration=" + ((int)Math.Round(duration)).ToString(CultureInfo.InvariantCulture);
                string body = Get(url, null, 4);
                if (string.IsNullOrEmpty(body)) return null;
                var root = J.Parse(body);
                string synced = root.Str("syncedLyrics");
                if (string.IsNullOrEmpty(synced)) synced = root.Str("plainLyrics");
                if (string.IsNullOrEmpty(synced)) return null;
                var doc = Parse(synced, "lrclib");
                doc.Matched = root.Str("trackName") + " - " + root.Str("artistName");
                return doc;
            }
            catch { return null; }
        }

        static bool LooksInstrumental(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string s = name.ToLowerInvariant();
            return s.Contains("纯音乐") || s.Contains("纯音") || s.Contains("伴奏")
                || s.Contains("instrumental") || s.Contains("karaoke") || s.Contains("off vocal");
        }

        /// <summary>Version tags that make "the same song" a different recording.</summary>
        static readonly Regex VersionTagRx = new Regex(
            @"\b(live|remix|dj|acoustic|instrumental|karaoke|off vocal|cover|slowed|reverb|nightcore|sped up)\b|(?:伴奏|纯音乐|纯音|铃声|抖音版|女声版|男声版|粤语版|钢琴版|指弹版)",
            RegexOptions.IgnoreCase);

        static string VersionTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var found = new List<string>();
            foreach (Match m in VersionTagRx.Matches(s))
            {
                string t = m.Value.ToLowerInvariant();
                if (!found.Contains(t)) found.Add(t);
            }
            found.Sort(StringComparer.Ordinal);
            return string.Join(",", found.ToArray());
        }

        /// <summary>
        /// Weighted verdict, Lyricify style: title 1, artist 1, duration 1, but only over the
        /// parts both sides actually provide. Duration is a gate, not a tie-breaker: more than
        /// 3.5 s apart is a different recording, unless the title and artist are both exact.
        /// </summary>
        static double Score(string title, string artist, double duration, Cand c)
        {
            // Compare every reasonable form of the two titles and keep the best one.
            double ts = TextUtil.Similar(TextUtil.StripBrackets(title), c.Name);
            double t;
            t = TextUtil.Similar(title, c.Name); if (t > ts) ts = t;
            t = TextUtil.Similar(TextUtil.StripBrackets(title), TextUtil.StripBrackets(c.Name)); if (t > ts) ts = t;
            t = TextUtil.Similar(TextUtil.StripDecorations(TextUtil.StripBrackets(title)),
                                 TextUtil.StripDecorations(TextUtil.StripBrackets(c.Name))); if (t > ts) ts = t;

            // (Live) / (DJ版) / (伴奏) are the usual way a wrong lyric slips in: the base title is
            // identical, so the letters agree, but the recording is a minute longer or shorter.
            string vt = VersionTags(title), vc = VersionTags(c.Name);
            bool verDiff = vt != vc;
            if (verDiff && ts > 0.90) ts *= 0.92;

            double asim = TextUtil.ArtistSimilar(artist, c.Artist);

            bool bothKnown = duration > 20 && c.Dur > 20;
            double dsim = -1;
            double gap = 0;
            if (bothKnown)
            {
                gap = Math.Abs(duration - c.Dur);
                if (gap <= 0.3) dsim = 1.0;
                else if (gap < 0.7) dsim = 0.9;
                else if (gap < 1.5) dsim = 0.75;
                else if (gap < 3.5) dsim = 0.45;
                else dsim = 0.0;
            }

            c.TitleSim = ts;
            c.ArtistSim = asim;
            c.DurSim = dsim;
            c.VersionDiff = verDiff;

            double wsum = 2.0 + (bothKnown ? 1.0 : 0.0);
            double raw = ts + asim + (bothKnown ? dsim : 0.0);
            c.Score = raw / wsum;

            // A cover or a live cut is often a minute away from the studio take. Refuse it
            // outright rather than show lyrics that drift, unless both title and artist are
            // essentially identical (a re-upload of the same recording).
            if (bothKnown && dsim == 0.0 && !(ts >= 0.98 && asim >= 0.85)) c.Score = 0;
            // "晴天" vs "晴天 (Live)": letters agree, the recording does not. Length decides;
            // when no length is known, at least stay under the accept line.
            if (verDiff)
            {
                if (bothKnown && gap > 1.5) c.Score = 0;
                else if (!bothKnown && c.Score > 0.78) c.Score = 0.78;
            }
            return c.Score;
        }

        static string Get(string url, string referer)
        {
            return Get(url, referer, 4);
        }

        /// <summary>
        /// Kugou's JSON is GB18030, not UTF-8: read as UTF-8 and every Chinese name turns into
        /// U+FFFD, which is why its candidates looked unusable at first. Decode both ways and keep
        /// whichever produced fewer replacement characters.
        /// </summary>
        static string GetFlexible(string url, string referer, int timeoutSeconds)
        {
            try
            {
                Net.EnsureTls();
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
                req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
                if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
                req.Timeout = timeoutSeconds * 1000;
                req.ReadWriteTimeout = timeoutSeconds * 1000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var ms = new System.IO.MemoryStream())
                {
                    using (var st = resp.GetResponseStream()) st.CopyTo(ms);
                    byte[] bytes = ms.ToArray();
                    string gb;
                    try { gb = Encoding.GetEncoding(936).GetString(bytes); }
                    catch { gb = null; }
                    string utf = Encoding.UTF8.GetString(bytes);
                    if (gb == null) return utf;
                    return CountBroken(gb) <= CountBroken(utf) ? gb : utf;
                }
            }
            catch (Exception ex) { Log("  http fail " + ex.GetType().Name + " " + url); return null; }
        }

        /// <summary>Same request, but hand back the bytes: Kugou's answer is not uniformly encoded.</summary>
        static byte[] GetBytes(string url, string referer, int timeoutSeconds)
        {
            try
            {
                Net.EnsureTls();
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
                req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
                if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
                req.Timeout = timeoutSeconds * 1000;
                req.ReadWriteTimeout = timeoutSeconds * 1000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var ms = new System.IO.MemoryStream())
                {
                    using (var st = resp.GetResponseStream()) st.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch (Exception ex) { Log("  http fail " + ex.GetType().Name + " " + url); return null; }
        }

        /// <summary>Percent-encode as GB18030: Kugou decodes its query as GBK, not UTF-8.</summary>
        static string EscapeGbk(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            byte[] bytes = Encoding.GetEncoding(936).GetBytes(text);
            var sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                if ((b >= '0' && b <= '9') || (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z')
                    || b == '-' || b == '_' || b == '.' || b == '~')
                    sb.Append((char)b);
                else
                    sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        static int CountBroken(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            foreach (char c in s) if (c == '\uFFFD') n++;
            return n;
        }

        static string Get(string url, string referer, int timeoutSeconds)
        {
            try
            {
                Net.EnsureTls();
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
                req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
                if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
                req.Timeout = timeoutSeconds * 1000;
                req.ReadWriteTimeout = timeoutSeconds * 1000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception ex) { Log("  http fail " + ex.GetType().Name + " " + url); return null; }
        }
    }
}
