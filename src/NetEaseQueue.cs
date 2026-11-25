// ---------------------------------------------------------------------------
// NetEaseQueue.cs
//
/// Reads NetEase Cloud Music's local play queue, purely to label the 'next track' in
/// places where SMTC gives us nothing.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Amc
{
    /// <summary>
    /// NetEase Cloud Music never reports a track duration over SMTC (EndTime is 0), which left
    /// the progress bar without a scale and made a looping song walk past its own end. The client
    /// keeps its play queue in a small readable JSON file, and every entry carries the real
    /// duration in milliseconds - that is all we take from it.
    /// </summary>
    public static class NetEaseQueue
    {
        class Item
        {
            public string Title;
            public string Artist;
            public double Dur;
        }

        static readonly List<Item> _items = new List<Item>();
        static DateTime _stamp = DateTime.MinValue;
        static readonly object _gate = new object();

        static string FilePath()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(Path.Combine(Path.Combine(Path.Combine(dir, "NetEase"), "CloudMusic"), "webdata"), Path.Combine("file", "playingList"));
        }

        static void Refresh()
        {
            string p = FilePath();
            if (!File.Exists(p)) return;
            DateTime st = File.GetLastWriteTime(p);
            if (st == _stamp) return;

            string text = File.ReadAllText(p, Encoding.UTF8);
            J root = J.Parse(text);
            List<J> list = root == null ? null : root.Arr("list");
            if (list == null) return;

            var fresh = new List<Item>();
            for (int i = 0; i < list.Count; i++)
            {
                J tr = list[i]["track"];
                if (tr == null) continue;
                var it = new Item();
                it.Title = tr.Str("name");
                it.Dur = tr.Num("duration", 0) / 1000.0;
                List<J> artists = tr.Arr("artists");
                var names = new List<string>();
                if (artists != null)
                    for (int k = 0; k < artists.Count; k++)
                    {
                        string n = artists[k].Str("name");
                        if (!string.IsNullOrEmpty(n)) names.Add(n);
                    }
                it.Artist = string.Join(" / ", names.ToArray());
                if (it.Dur > 1 && !string.IsNullOrEmpty(it.Title)) fresh.Add(it);
            }

            lock (_gate)
            {
                _items.Clear();
                _items.AddRange(fresh);
                _stamp = st;
            }
            LyricsService.Log("netease queue n=" + fresh.Count);
        }

        static string Norm(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = char.ToLowerInvariant(s[i]);
                if (char.IsWhiteSpace(c)) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Real duration in seconds for the given song, or 0 when the queue does not know it.</summary>
        public static double LookupDuration(string title, string artist)
        {
            if (string.IsNullOrEmpty(title)) return 0;
            try
            {
                Refresh();
                Item best = null;
                int bestScore = 0;
                string t = Norm(title);
                string a = Norm(artist);
                lock (_gate)
                {
                    for (int i = 0; i < _items.Count; i++)
                    {
                        Item it = _items[i];
                        string nt = Norm(it.Title);
                        int score = 0;
                        if (nt == t) score = 4;
                        else if (nt.Length > 3 && t.Length > 3 && (nt.IndexOf(t, StringComparison.Ordinal) >= 0 || t.IndexOf(nt, StringComparison.Ordinal) >= 0)) score = 2;
                        if (score == 0) continue;
                        string na = Norm(it.Artist);
                        if (a.Length > 0 && na.Length > 0 && (na.IndexOf(a, StringComparison.Ordinal) >= 0 || a.IndexOf(na, StringComparison.Ordinal) >= 0)) score += 3;
                        if (score > bestScore) { bestScore = score; best = it; }
                    }
                }
                if (best == null) return 0;
                return best.Dur;
            }
            catch (Exception ex)
            {
                LyricsService.Log("netease queue lookup failed: " + ex.Message);
                return 0;
            }
        }
    }
}
