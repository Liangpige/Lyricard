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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;

namespace Amc
{
    public static class CoverArt
    {
        /// <summary>
        /// Some players answer "this song has no artwork" with a grey tile carrying their own logo
        /// (Kugou's big K). That is not a cover, but it is a thumbnail, so the card dutifully shows a
        /// grey square and the user reports "no cover". Treat a near-uniform light grey picture as
        /// "nothing" so the network fallback can step in.
        /// </summary>
        public static bool LooksLikePlaceholder(byte[] bytes, out string note)
        {
            note = "";
            // Kugou's tile is a four-and-a-bit kilobyte PNG; a real cover at this size never is.
            if (bytes != null && bytes.Length > 0 && bytes.Length <= 6000)
            {
                note = "small=" + bytes.Length;
                return true;
            }
            try
            {
                var frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                int w = frame.PixelWidth, h = frame.PixelHeight;
                if (w <= 0 || h <= 0) return false;
                double k = 24.0 / Math.Max(w, h);
                var small = new FormatConvertedBitmap(
                    new TransformedBitmap(frame, new ScaleTransform(k, k)), PixelFormats.Bgra32, null, 0);
                int tw = small.PixelWidth, th = small.PixelHeight, stride = tw * 4;
                var px = new byte[stride * th];
                small.CopyPixels(px, stride, 0);

                int n = tw * th, grey = 0, tinted = 0, sum = 0; long sum2 = 0;
                for (int i = 0; i < n; i++)
                {
                    int b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2];
                    int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                    int lum = (r * 299 + g * 587 + b * 114) / 1000;
                    sum += lum; sum2 += (long)lum * lum;
                    if (mx - mn <= 26 && lum >= 160) grey++;
                    // Same test with a looser chroma limit: catches tiles that are flat *and* tinted.
                    if (mx - mn <= 60 && lum >= 150) tinted++;
                }
                // Rotation test: the "no cover" tile is a synthetic disc, so turning it by 90 degrees
                // changes almost nothing, while any photograph or sleeve design changes a lot.
                double rot = 0;
                for (int y = 0; y < th; y++)
                    for (int x = 0; x < tw; x++)
                    {
                        int i = (y * tw + x) * 4, j = (x * tw + (th - 1 - y)) * 4;
                        int la = (px[i + 2] * 299 + px[i + 1] * 587 + px[i] * 114) / 1000;
                        int lb = (px[j + 2] * 299 + px[j + 1] * 587 + px[j] * 114) / 1000;
                        rot += Math.Abs(la - lb);
                    }
                rot /= n;

                // Local detail: a drawn gradient tile has almost no high-frequency energy, while any
                // photograph or sleeve design has plenty. Colour-agnostic, unlike the chroma tests.
                double edge = 0;
                int ec = 0;
                for (int y = 1; y < th - 1; y++)
                    for (int x = 1; x < tw - 1; x++)
                    {
                        int c = ((y * tw) + x) * 4;
                        int lc = (px[c + 2] * 299 + px[c + 1] * 587 + px[c] * 114) / 1000;
                        int acc = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int o = (((y + dy) * tw) + (x + dx)) * 4;
                                acc += (px[o + 2] * 299 + px[o + 1] * 587 + px[o] * 114) / 1000;
                            }
                        edge += Math.Abs(lc - acc / 8.0);
                        ec++;
                    }
                edge = ec > 0 ? edge / ec : 0;

                double share = (double)grey / n;
                double lowChroma = (double)tinted / n;
                double mean = (double)sum / n;
                double std = Math.Sqrt(Math.Max(0.0, (double)sum2 / n - mean * mean));
                note = "grey=" + share.ToString("0.00", CultureInfo.InvariantCulture)
                     + " tinted=" + lowChroma.ToString("0.00", CultureInfo.InvariantCulture)
                     + " std=" + std.ToString("0.0", CultureInfo.InvariantCulture)
                     + " mean=" + mean.ToString("0.0", CultureInfo.InvariantCulture)
                     + " rot=" + rot.ToString("0.0", CultureInfo.InvariantCulture)
                     + " edge=" + edge.ToString("0.0", CultureInfo.InvariantCulture);
                // Two tiles to catch: the neutral grey one (Kugou's "K" logo), and the pink/blue
                // blurred CD that is flat and bright but decidedly not neutral -- the grey test alone
                // scored it 0.00 and let it through as a real cover.
                bool neutralTile = share >= 0.70 && std <= 34.0 && mean >= 150.0;
                bool flatTintTile = lowChroma >= 0.60 && std <= 22.0 && mean >= 150.0;
                // Third variant: the same disc wearing a colour wash (purple here), which keeps the
                // chroma stats out of reach of both rules above. Flat + rotation-invariant is the
                // signature of a drawn graphic rather than a photograph.
                bool discTile = rot <= 8.0 && std <= 26.0 && mean >= 120.0;
                bool smoothTile = edge <= 4.5 && std <= 30.0 && mean >= 110.0;
                return neutralTile || flatTintTile || discTile || smoothTile;
            }
            catch (Exception ex)
            {
                note = "decode failed (" + ex.GetType().Name + ")";
                return false;
            }
        }

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
