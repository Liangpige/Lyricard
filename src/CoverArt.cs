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
            // Only truly broken junk is rejected on size alone. Do NOT raise this to catch small
            // tiles: NetEase serves perfectly good covers at 1.5-5.3 KB (measured live), and a 6 KB
            // gate there swapped real sleeves for an online look-alike of another release. A drawn
            // placeholder is recognised by the pixel tests below, not by its byte count.
            if (bytes != null && bytes.Length > 0 && bytes.Length <= 512)
            {
                note = "junk=" + bytes.Length;
                return true;
            }
            // Kugou's drawn tile really is a four-kilobyte PNG, and the byte count is the cheapest
            // way to recognise it -- but only for PNG. NetEase serves real JPEG sleeves at 1.5-5.3 KB
            // and those must go through the pixel tests below, not be thrown away for being small.
            if (bytes != null && bytes.Length <= 6000 && IsPng(bytes))
            {
                note = "png-tile=" + bytes.Length;
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

        // QQ's song object puts albummid FIRST and songname LAST, so a "songname ... albummid"
        // pattern always reaches into the NEXT hit and pairs this song with that hit's album --
        // which is how a same-titled track came back with another release's sleeve. Split on
        // albummid instead: the span between two albummids is exactly one song object, so every
        // field stays with its own song.
        static readonly Regex AlbumMidRx = new Regex(@"""albummid""\s*:\s*""(?<mid>[0-9A-Za-z]+)""", RegexOptions.Compiled);
        static readonly Regex SongNameRx = new Regex(@"""songname""\s*:\s*""(?<v>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
        static readonly Regex AlbumNameRx = new Regex(@"""albumname""\s*:\s*""(?<v>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
        static readonly Regex SingerBlockRx = new Regex(@"""singer""\s*:\s*\[(?<v>[\s\S]*?)\]", RegexOptions.Compiled);
        static readonly Regex SingerNameRx = new Regex(@"""name""\s*:\s*""(?<v>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

        static readonly Regex DecoRx = new Regex(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);

        /// <summary>
        /// Cover for this track, or null. Blocks on the network -- call from a worker thread.
        /// The album is part of the cache key and of the match: one song can sit on a single, on the
        /// album and on a compilation, and the player tells us which one is playing.
        /// </summary>
        public static byte[] Fetch(string title, string artist, string album)
        {
            string key = (title ?? "") + "\u0001" + (artist ?? "") + "\u0001" + (album ?? "");
            lock (Cache)
            {
                byte[] hit;
                if (Cache.TryGetValue(key, out hit)) return hit;
            }

            byte[] bytes = null;
            try
            {
                string mid = FindAlbumMid(title, artist, album);
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

            if (bytes != null) bytes = StripBrokenExif(bytes);
            // Only cache something that at least looks like an image. A captive portal or an error
            // page used to be cached under this key, which permanently poisoned the track.
            if (bytes != null && !LooksLikeImage(bytes))
            {
                LyricsService.Log("cover fallback dropped (not an image) " + bytes.Length + " bytes");
                bytes = null;
            }
            if (bytes != null)
            {
                lock (Cache) Cache[key] = bytes;
            }
            return bytes;
        }

        /// <summary>
        /// y.gtimg.cn serves some sleeves with a malformed APP1 (EXIF) segment that makes WIC refuse
        /// the whole file -- "IOException: cannot read from the stream" -- while PIL and every other
        /// decoder read it fine. Measured on 《Dissolve》(300x300, 8296 bytes): the same bytes decode
        /// once APP1 is removed, and APP0/APP14 alone are harmless. Strip APP1 from JPEGs before
        /// anyone caches or decodes them.
        /// </summary>
        public static byte[] StripBrokenExif(byte[] b)
        {
            if (b == null || b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return b;
            int i = 2; bool stripped = false;
            var outBuf = new List<byte>(b.Length);
            outBuf.Add(0xFF); outBuf.Add(0xD8);
            while (i + 3 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                int m = b[i + 1];
                if (m == 0xD8 || m == 0xD9 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
                int len = (b[i + 2] << 8) | b[i + 3];
                if (len < 2 || i + 2 + len > b.Length) break;
                if (m == 0xE1) stripped = true;
                else for (int k = 0; k < 2 + len; k++) outBuf.Add(b[i + k]);
                i += 2 + len;
                if (m == 0xDA) { for (int k = i; k < b.Length; k++) outBuf.Add(b[k]); break; }
            }
            return stripped ? outBuf.ToArray() : b;
        }

        /// <summary>Can this payload actually be decoded here? Used to reject a sleeve that every
        /// other decoder reads but WIC will not, and to keep it out of the cache.</summary>
        public static bool Decodes(byte[] bytes)
        {
            try
            {
                var fr = System.Windows.Media.Imaging.BitmapFrame.Create(new MemoryStream(bytes),
                    System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                return fr != null && fr.PixelWidth > 0;
            }
            catch { return false; }
        }

        /// <summary>Drop a cached payload that turned out to be undecodable, so the next attempt
        /// fetches again instead of replaying the same broken bytes.</summary>
        public static void Evict(byte[] bytes)
        {
            if (bytes == null) return;
            lock (Cache)
            {
                var dead = new List<string>();
                foreach (var kv in Cache) if (ReferenceEquals(kv.Value, bytes)) dead.Add(kv.Key);
                foreach (var k in dead) Cache.Remove(k);
                if (dead.Count > 0) LyricsService.Log("cover cache evicted " + dead.Count);
            }
        }

        /// <summary>Magic-number check: JPEG / PNG / GIF / BMP / WEBP. Cheap insurance against
        /// caching an HTML error page.</summary>
        static bool LooksLikeImage(byte[] b)
        {
            if (b == null || b.Length < 12) return false;
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true;
            if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return true;
            if (b[0] == 0x42 && b[1] == 0x4D) return true;
            if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
                && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return true;
            return false;
        }

        /// <summary>
        /// Best album mid among the QQ search hits: the title has to match, then the release we are
        /// actually playing wins over another release of the same song, then the artist.
        /// </summary>
        static string FindAlbumMid(string title, string artist, string album)
        {
            if (string.IsNullOrEmpty(title)) return null;
            string q = string.IsNullOrEmpty(artist) ? title : title + " " + artist;
            string json = Get("https://c.y.qq.com/soso/fcgi-bin/client_search_cp?format=json&n=8&w="
                              + Uri.EscapeDataString(q), "https://y.qq.com/");
            if (string.IsNullOrEmpty(json)) return null;

            string wantTitle = Norm(title);
            string wantArtist = Norm(artist);
            string wantAlbum = Norm(album);
            if (wantTitle.Length == 0) return null;

            MatchCollection mids = AlbumMidRx.Matches(json);
            string bestMid = null;
            double bestScore = double.MinValue;
            for (int i = 0; i < mids.Count; i++)
            {
                int from = mids[i].Index;
                int to = (i + 1 < mids.Count) ? mids[i + 1].Index : json.Length;
                if (to - from < 40 || to - from > 8000) continue;

                string hit = json.Substring(from, to - from);
                string name = Norm(Unescape(Field(SongNameRx, hit)));
                if (name.Length == 0) continue;

                double titleScore = name == wantTitle ? 1.0
                    : (name.Contains(wantTitle) || wantTitle.Contains(name)) ? 0.85
                    : 0.0;
                if (titleScore <= 0) continue;

                string hitAlbum = Norm(Unescape(Field(AlbumNameRx, hit)));
                bool albumKnown = wantAlbum.Length > 0 && hitAlbum.Length > 0;
                bool albumHit = albumKnown
                    && (hitAlbum == wantAlbum || hitAlbum.Contains(wantAlbum) || wantAlbum.Contains(hitAlbum));

                bool artistHit = false;
                Match sb = SingerBlockRx.Match(hit);
                if (sb.Success && wantArtist.Length > 0)
                {
                    string singers = "";
                    foreach (Match nm in SingerNameRx.Matches(sb.Groups["v"].Value))
                        singers += Norm(Unescape(nm.Groups["v"].Value)) + " ";
                    artistHit = singers.Length > 0 && singers.Contains(wantArtist);
                }

                double score = titleScore;
                if (albumHit) score += 2.0;          // the release on screen, not a look-alike
                else if (albumKnown) score -= 0.4;   // a different release of the same song
                if (artistHit) score += 0.6;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMid = mids[i].Groups["mid"].Value;
                }
            }
            return bestMid;
        }

        static string Field(Regex rx, string block)
        {
            Match m = rx.Match(block);
            return m.Success ? m.Groups["v"].Value : "";
        }

        /// <summary>Lower-case, punctuation-free form used for every comparison in this file.</summary>
        static string Norm(string s)
        {
            return TextUtil.Clean(TextUtil.StripBrackets(s ?? ""));
        }

        /// <summary>True when the bytes start with the PNG signature (Kugou draws its tile as PNG).</summary>
        static bool IsPng(byte[] b)
        {
            return b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;
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
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
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
            req.Timeout = 8000;
            req.ReadWriteTimeout = 8000;
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
