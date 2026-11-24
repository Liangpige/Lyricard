using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace Amc
{
    /// <summary>One release as GitHub describes it, reduced to what the updater needs.</summary>
    public class UpdateInfo
    {
        public Version Version;
        public string Tag = "";
        public string AssetName = "";
        public string Url = "";
        public string Sha256 = "";
        public long Size;
        public string Notes = "";
    }

    /// <summary>
    /// Self-updater for the portable build: ask GitHub for the latest release, compare it with the
    /// running version, download the zip's exe, verify the digest and hand the swap to a second
    /// instance of this same exe.
    ///
    /// Deliberately dependency-free (the project compiles with the in-box csc, no NuGet), and
    /// deliberately quiet: it never polls. GitHub's anonymous API allows 60 requests per hour per
    /// IP, so one check at startup plus one per button press is the whole budget.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "Liangpige/Lyricard";

        /// <summary>Release found by the silent startup probe, picked up by the About page.</summary>
        public static UpdateInfo Pending;

        /// <summary>The version this build reports, taken from the same string the UI shows.</summary>
        public static Version CurrentVersion
        {
            get { return ParseVersion(Strings.T("app.version")); }
        }

        public static Version ParseVersion(string s)
        {
            if (string.IsNullOrEmpty(s)) return new Version(0, 0, 0);
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsDigit(c) || c == '.') sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '.') sb.Append('.');
            }
            int cut = sb.ToString().IndexOf("..", StringComparison.Ordinal);
            string t = (cut >= 0) ? sb.ToString().Substring(0, cut) : sb.ToString();
            t = t.Trim('.');
            if (t.Length == 0) return new Version(0, 0, 0);
            int dots = 0;
            foreach (char c in t) if (c == '.') dots++;
            while (dots < 2) { t += ".0"; dots++; }
            try { return new Version(t); }
            catch { return new Version(0, 0, 0); }
        }

        static HttpWebRequest Request(string url)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "Lyricard-Updater (+https://github.com/" + Repo + ")";
            r.Accept = "application/vnd.github+json";
            r.Timeout = 15000;
            r.ReadWriteTimeout = 15000;
            r.AllowAutoRedirect = true;
            // The updater must follow whatever proxy the user configured (a dead proxy is exactly
            // the situation we hit here), so do not bypass it.
            try { r.Proxy = WebRequest.DefaultWebProxy; } catch { }
            return r;
        }

        /// <summary>
        /// Blocking; call from a worker thread. Returns null when running the newest build, and
        /// throws on network/parse failures so the caller can show the real reason.
        /// </summary>
        public static UpdateInfo Check()
        {
            string url = "https://api.github.com/repos/" + Repo + "/releases/latest";
            string body;
            using (var resp = (HttpWebResponse)Request(url).GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                body = sr.ReadToEnd();

            J root = J.Parse(body);
            if (root == null) throw new Exception("empty release payload");

            string tag = root.Str("tag_name");
            // /releases/latest already excludes drafts and pre-releases; bail out defensively if a
            // draft ever leaks through.
            string draft = root.Str("draft");
            if (!string.IsNullOrEmpty(draft) && draft == "true") return null;

            var info = new UpdateInfo { Tag = tag ?? "" };
            info.Version = ParseVersion(tag);
            if (info.Version <= CurrentVersion) return null;

            var assets = root.Arr("assets");
            if (assets != null)
            {
                foreach (J a in assets)
                {
                    string name = a.Str("name") ?? "";
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    info.AssetName = name;
                    info.Url = a.Str("browser_download_url") ?? "";
                    info.Size = (long)a.Num("size", 0);
                    string digest = a.Str("digest") ?? "";
                    if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        info.Sha256 = digest.Substring(7).ToLowerInvariant();
                    break;
                }
            }
            if (string.IsNullOrEmpty(info.Url)) throw new Exception("release has no zip asset");

            string notes = root.Str("body") ?? "";
            foreach (string line in notes.Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("#", StringComparison.Ordinal)) continue;
                info.Notes = t;
                break;
            }
            return info;
        }

        /// <summary>
        /// Download the release zip and pull the exe out of it, verifying the published digest.
        /// Returns the path of the extracted exe, or throws.</summary>
        public static string DownloadExe(UpdateInfo info, Action<int, int> progress)
        {
            string dir = Path.Combine(Path.GetTempPath(), "Lyricard-updater");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);
            string zip = Path.Combine(dir, info.AssetName);

            using (var resp = (HttpWebResponse)Request(info.Url).GetResponse())
            using (var input = resp.GetResponseStream())
            using (var file = File.Create(zip))
            {
                var buf = new byte[64 * 1024];
                long total = resp.ContentLength > 0 ? resp.ContentLength : info.Size;
                long done = 0;
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    file.Write(buf, 0, n);
                    done += n;
                    if (progress != null) progress((int)done, (int)total);
                }
            }

            if (!string.IsNullOrEmpty(info.Sha256))
            {
                string got;
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(zip))
                    got = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                if (got != info.Sha256)
                {
                    try { File.Delete(zip); } catch { }
                    throw new Exception("checksum mismatch");
                }
            }

            // The zip holds one exe plus docs; only the exe is replaced, so data\ and the rest of
            // the folder stay untouched.
            string exe = Path.Combine(dir, "Lyricard.exe");
            using (var zipFile = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                ZipArchiveEntry entry = null;
                foreach (ZipArchiveEntry e in zipFile.Entries)
                {
                    if (e.Name.Equals("Lyricard.exe", StringComparison.OrdinalIgnoreCase)) { entry = e; break; }
                }
                if (entry == null) throw new Exception("zip has no Lyricard.exe");
                using (Stream src = entry.Open())
                using (Stream dst = File.Create(exe))
                    src.CopyTo(dst);
            }
            try { File.Delete(zip); } catch { }
            return exe;
        }

        /// <summary>
        /// Start a second copy of this exe in --apply-update mode and quit. That instance waits for
        /// our process to exit, then renames/moves files (Windows cannot overwrite a running exe,
        /// but it can rename it) and relaunches the app.
        /// </summary>
        public static void ApplyAndRestart(string newExe)
        {
            string self = System.Reflection.Assembly.GetEntryAssembly().Location;
            int pid = Process.GetCurrentProcess().Id;
            string args = "--apply-update \"" + newExe + "\" " + pid.ToString(CultureInfo.InvariantCulture);
            var psi = new ProcessStartInfo(self, args);
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(self);
            Process.Start(psi);
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Runs in the short-lived helper instance (see ApplyAndRestart). Never returns.
        /// </summary>
        public static int RunApply(string newExe, int pid)
        {
            string self = System.Reflection.Assembly.GetEntryAssembly().Location;
            string dir = Path.GetDirectoryName(self);
            for (int i = 0; i < 100; i++)
            {
                bool alive;
                try { alive = !Process.GetProcessById(pid).HasExited; }
                catch { alive = false; }
                if (!alive) break;
                System.Threading.Thread.Sleep(100);
            }
            System.Threading.Thread.Sleep(300);

            string old = self + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.Move(self, old);
                    File.Move(newExe, self);
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt == 19)
                    {
                        try { File.WriteAllText(Path.Combine(dir, "update-failed.txt"), ex.ToString()); } catch { }
                        return 1;
                    }
                    System.Threading.Thread.Sleep(250);
                }
            }
            try
            {
                var psi = new ProcessStartInfo(self, "--background");
                psi.WorkingDirectory = dir;
                Process.Start(psi);
            }
            catch { }
            // The renamed file can stay locked for a moment after the new process starts, so give
            // it a few tries instead of leaving a 300 KB .old next to the exe on every update.
            for (int i = 0; i < 20; i++)
            {
                try { File.Delete(old); break; }
                catch { System.Threading.Thread.Sleep(250); }
            }
            try { if (File.Exists(newExe)) File.Delete(newExe); } catch { }
            return 0;
        }
    }
}
