using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Amc
{
    /// <summary>
    /// Portable "uninstaller": the app has no registry footprint beyond the Run key and no files
    /// outside its own folder, so uninstalling means - stop every copy, drop the Run value,
    /// delete the data folder (config + lyric cache) and finally delete the exe itself.
    ///
    /// The exe cannot delete itself while it is running, so the last step is handed to a detached
    /// cmd.exe that waits a second and then deletes the file (and the folder, if it is now empty).
    /// </summary>
    public static class Uninstall
    {
        /// <summary>Run the whole routine. With confirm=true the user sees a Yes/No box first.</summary>
        public static void Run(bool confirm, bool silent)
        {
            if (confirm && !silent)
            {
                MessageBoxResult r = MessageBox.Show(
                    Strings.T("uninst.confirm"),
                    Strings.T("uninst.title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            int killed = KillOtherCopies();
            bool regCleared = false;
            try
            {
                AutoStart.Set(false);
                regCleared = !AutoStart.IsEnabled();
            }
            catch { }

            int removed = DeleteDataFolders();

            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            LyricsService.Log("uninstall: killed=" + killed + " regCleared=" + regCleared
                + " removedDirs=" + removed + " exe=" + exe);

            if (!silent)
            {
                MessageBox.Show(
                    Strings.F("uninst.done", killed, regCleared ? 1 : 0, removed),
                    Strings.T("uninst.title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            ScheduleSelfDelete(exe);
            Application.Current.Shutdown();
        }

        /// <summary>Kill every other AppleMusicCard.exe so the files can be removed.</summary>
        static int KillOtherCopies()
        {
            int n = 0;
            try
            {
                int me = Process.GetCurrentProcess().Id;
                string[] names = { "Lyricard", "AppleMusicCard" };
                foreach (string nm in names)
                {
                    Process[] procs = Process.GetProcessesByName(nm);
                    try
                    {
                        foreach (Process p in procs)
                        {
                            if (p.Id == me) continue;
                            try { p.Kill(); p.WaitForExit(3000); n++; }
                            catch { }
                        }
                    }
                    finally { for (int i = 0; i < procs.Length; i++) procs[i].Dispose(); }
                }
            }
            catch { }
            return n;
        }

        /// <summary>Delete the data folder next to the exe plus the two AppData fallbacks.</summary>
        static int DeleteDataFolders()
        {
            var dirs = new List<string>();
            try
            {
                string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir)) dirs.Add(Path.Combine(exeDir, "data"));
            }
            catch { }
            try { dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppleMusicCard")); }
            catch { }
            try { dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppleMusicCard")); }
            catch { }

            int removed = 0;
            foreach (string d in dirs)
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    Directory.Delete(d, true);
                    removed++;
                }
                catch { }
            }
            return removed;
        }

        /// <summary>
        /// Hand the delete to a detached shell: it waits ~1.5s (so this process can exit and the
        /// image lock is released), deletes the exe, then removes the folder when it is empty.
        /// </summary>
        static void ScheduleSelfDelete(string exePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(exePath);
                string cmd = "/c ping -n 2 127.0.0.1 >nul & del /f /q \"" + exePath + "\"";
                if (!string.IsNullOrEmpty(dir)) cmd += " & rmdir \"" + dir + "\" 2>nul";
                var psi = new ProcessStartInfo("cmd.exe", cmd);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
        }
    }
}
