// ---------------------------------------------------------------------------
// AmProbe.cs
//
/// Developer probe (--smtcdump): prints every SMTC session the machine currently has,
/// with app id, playback status, position and end time.
// ---------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Amc
{
    // Cheap "is Apple Music running?" probe with a 2 second cache.
    // The card stays on screen while the app exists, because Apple Music does not
    // publish a media session until it actually plays something.
    /// <summary>
    /// Answers "is the Apple Music app running?" - used to tell "Apple Music is closed" apart from
    /// "it is open but nothing is playing yet". Results are cached briefly because this runs on the
    /// 33ms UI tick path.
    /// </summary>
    static class AmProbe
    {
        static bool _last;
        static DateTime _at = DateTime.MinValue;

        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc cb, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextW(IntPtr hWnd, StringBuilder sb, int max);

        public static bool IsRunning()
        {
            // Debug hooks used by support / testing: force "running" or "not running".
            if (Environment.GetEnvironmentVariable("AMC_FORCE_AM") == "1") return true;
            if (Environment.GetEnvironmentVariable("AMC_FORCE_NOAM") == "1") return false;

            var now = DateTime.UtcNow;
            if ((now - _at).TotalMilliseconds < 2000) return _last;
            _at = now;

            bool r = false;
            try
            {
                var ps = System.Diagnostics.Process.GetProcessesByName("AppleMusic");
                r = ps != null && ps.Length > 0;
            }
            catch
            {
                r = false;
            }

            if (!r) r = HasAppleMusicWindow();

            if (r != _last) LyricsService.Log("am running=" + r);
            _last = r;
            return r;
        }

        // Fallback for tokens that cannot enumerate processes: look for the top level
        // window the app calls "Apple Music".
        static bool HasAppleMusicWindow()
        {
            bool found = false;
            try
            {
                EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    var sb = new StringBuilder(256);
                    int n = GetWindowTextW(hWnd, sb, 256);
                    if (n > 0 && sb.ToString() == "Apple Music")
                    {
                        found = true;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }
            return found;
        }
    }
}
