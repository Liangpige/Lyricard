// ---------------------------------------------------------------------------
// Shortcut.cs
//
/// Creates the desktop shortcut on demand (--shortcut desktop) instead of shipping a
/// prebuilt .lnk, which used to leak the builder's absolute paths.
// ---------------------------------------------------------------------------
using System;
using System.IO;
using System.Reflection;

namespace Amc
{
    /// <summary>
    /// Creates the .lnk that ships with the app's look (same icon as the exe).
    /// A shortcut baked at build time would point at the build machine, so the
    /// shortcut is always generated on the machine that runs the program:
    /// once next to the exe on first start, and on demand for Desktop / Start Menu.
    /// </summary>
    public static class Shortcut
    {
        static string ExePath { get { return System.Windows.Forms.Application.ExecutablePath; } }
        static string ExeDir { get { return Path.GetDirectoryName(ExePath); } }

        /// <summary>Shortcut file that lives beside the exe (portable "click me" entry).</summary>
        public static string FolderLink
        {
            get { return Path.Combine(ExeDir, "Lyricard.lnk"); }
        }

        /// <summary>
        /// Make sure the folder shortcut exists and points at THIS exe. A shortcut shipped in the
        /// zip carries the path of whoever packed it, so a stale target is rewritten on first run.
        /// Never throws.
        /// </summary>
        public static void EnsureFolderShortcut()
        {
            try
            {
                if (File.Exists(FolderLink) && PointsToSelf(FolderLink)) return;
                if (Write(FolderLink, ExePath, ExeDir)) LyricsService.Log("shortcut: (re)created " + FolderLink);
            }
            catch { }
        }

        /// <summary>True when the .lnk resolves to an existing file, ideally our own exe.</summary>
        static bool PointsToSelf(string linkPath)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return true;              // cannot verify: leave the file alone
                object shell = Activator.CreateInstance(t);
                try
                {
                    object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                    if (lnk == null) return false;
                    object target = lnk.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null);
                    string ts = target as string;
                    if (string.IsNullOrEmpty(ts)) return false;
                    return ts.Equals(ExePath, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
                }
            }
            catch { return false; }
        }

        /// <summary>Desktop shortcut. Returns the path, or null when creation failed.</summary>
        public static string CreateDesktop()
        {
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string p = Path.Combine(dir, "Lyricard.lnk");
                return Write(p, ExePath, ExeDir) ? p : null;
            }
            catch { return null; }
        }

        /// <summary>Shortcut in the per-user Start Menu programs folder.</summary>
        public static string CreateStartMenu()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lyricard");
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, "Lyricard.lnk");
                return Write(p, ExePath, ExeDir) ? p : null;
            }
            catch { return null; }
        }

        /// <summary>WScript.Shell late binding: no COM reference needed under csc.</summary>
        static bool Write(string linkPath, string target, string workDir)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return false;
            object shell = Activator.CreateInstance(t);
            try
            {
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                if (lnk == null) return false;
                Type lt = lnk.GetType();
                Action<string, object> set = delegate(string name, object v)
                {
                    lt.InvokeMember(name, BindingFlags.SetProperty, null, lnk, new object[] { v });
                };
                set("TargetPath", target);
                set("WorkingDirectory", workDir);
                set("IconLocation", target + ",0");
                set("Description", "Lyricard - 浮声");
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, new object[0]);
                return File.Exists(linkPath);
            }
            finally
            {
                if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }
    }
}
