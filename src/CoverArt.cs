// ---------------------------------------------------------------------------
// CoverArt.cs
//
// Album art fallback. Some players (Kugou is one) publish a track but never hand
// out a thumbnail -- the media session simply has no Thumbnail for that song, and
// the card is left with an empty disc. When that happens we ask QQ Music's public
// search for the release and pull the album cover from y.gtimg.cn.
//
// Only used when the player gave us nothing: a real thumbnail always wins.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Amc
{
    public static class CoverArt
    {
        /// <summary>Track key -> cover bytes. Small (a few dozen KB), so it lives for the session.</summary>
        static readonly Dictionary<string, byte[]> Cache = new Dictionary<string, byte[]>();

        static readonly Regex EntryRx = new Regex(
            "\"songname\"\\s*:\\s*\"(?<name>(?:[^\"\\\\]|\\\\.)*)\"[\\s\\S]{0,4000}?\"albummid\"\\s*:\\s*\"(?<mid>[0-9A-Za-z]+)\"",
            RegexOptions.Compiled);

        static readonly Regex DecoRx = new Regex(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);

        /// <summary>
        /// Cover for this track, or null. Blocks on the network -- call from a worker thread.
        /// </summary>
        public static byte[] Fetch(string title, string artist)
        {
            string key = (title ?? "") + "\u0001" + (artist ?? "");
            lock (Cache)
            {
                byte[] hit;
                if (Cache.TryGetValue(key, out hit)) return hit;
            }

            byte[] bytes = null;
            try
            {
                string mid = FindAlbumMid(title, artist);
                if (!string.IsNullOrEmpty(mid))
                {
                    string url = "https://y.gtimg.cn/music/photo_new/T002R300x300M000" + mid + ".jpg";
                    bytes = GetBytes(url, "https://y.qq.com/");
                    if (bytes != null && bytes.Length < 1024) bytes = null; // error page, not art
                    if (bytes != null) LyricsService.Log("cover fallback ok " + bytes.Length + " bytes mid=" + mid);
                }
                else LyricsService.Log("cover fallback none t=" + title);
            }
            catch (Exception ex)
            {
                LyricsService.Log("cover fallback fail " + ex.GetType().Name);
            }

            if (bytes != null)
            {
                lock (Cache) Cache[key] = bytes;
            }
            return bytes;
        }

        /// <summary>Best album mid from the QQ search hits, matched on title+artist.</summary>
        static string FindAlbumMid(string title, string artist)
        {
            if (string.IsNullOrEmpty(title)) return null;
            string q = string.IsNullOrEmpty(artist) ? title : title + " " + artist;
            string json = Get("https://c.y.qq.com/soso/fcgi-bin/client_search_cp?format=json&n=8&w="
                              + Uri.EscapeDataString(q), "https://y.qq.com/");
            if (string.IsNullOrEmpty(json)) return null;

            string wantTitle = TextUtil.Clean(TextUtil.StripBrackets(title));
            string wantArtist = TextUtil.Clean(artist);
            string bestMid = null;
            double bestScore = 0;
            foreach (Match m in EntryRx.Matches(json))
            {
                string name = TextUtil.Clean(TextUtil.StripBrackets(Unescape(m.Groups["name"].Value)));
                string mid = m.Groups["mid"].Value;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(mid)) continue;

                double score = 0;
                if (name == wantTitle) score = 1.0;
                else if (name.Length > 0 && wantTitle.Length > 0
                         && (name.Contains(wantTitle) || wantTitle.Contains(name))) score = 0.85;
                if (score <= 0) continue;
                // The artist rarely appears inside the hit object itself; a title match plus a
                // sane position wins. Anything but an exact title match has to be clearly better
                // than what we already have.
                if (score > bestScore) { bestScore = score; bestMid = mid; }
                if (bestScore >= 1.0) break;
            }
            return bestMid;
        }

        static string Unescape(string s)
        {
            // QQ returns \uXXXX escapes inside the JSON strings; fold them back to real chars.
            return DecoRx.Replace(s, delegate(Match m)
            {
                int code;
                if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber,
                                 System.Globalization.CultureInfo.InvariantCulture, out code))
                    return ((char)code).ToString();
                return m.Value;
            });
        }

        static string Get(string url, string referer)
        {
            try
            {
                Net.EnsureTls();
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = UserAgent;
                req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
                if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
                req.Timeout = 4000;
                req.ReadWriteTimeout = 4000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                LyricsService.Log("cover search fail " + ex.GetType().Name);
                return null;
            }
        }

        static byte[] GetBytes(string url, string referer)
        {
            Net.EnsureTls();
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
            req.Timeout = 5000;
            req.ReadWriteTimeout = 5000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var ms = new MemoryStream())
            {
                using (var src = resp.GetResponseStream())
                {
                    var buf = new byte[16384];
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        ms.Write(buf, 0, n);
                        if (ms.Length > 4 * 1024 * 1024) return null; // not album art
                    }
                }
                return ms.ToArray();
            }
        }

        const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
    }
}
