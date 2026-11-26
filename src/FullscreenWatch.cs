using System;

namespace Amc
{
    /// <summary>
    /// "Is some window fullscreen right now?" — asked once every ~300 ms while the option is on.
    ///
    /// Two tests, in order:
    ///   1. the foreground window's rect covers the whole monitor the card sits on (2 px slack).
    ///      Catches borderless-fullscreen games, F11 browsers, video players, UWP apps.
    ///   2. SHQueryUserNotificationState says BUSY / D3D_FULL_SCREEN — the shell's own verdict,
    ///      for the exclusive-fullscreen cases where a rect test can be fooled.
    ///
    /// Deliberately NOT enumerating every top-level window: we only care about the app the user is
    /// looking at, and an off-screen fullscreen window should not hide the card.
    /// </summary>
    public static class FullscreenWatch
    {
        /// <summary>True when a fullscreen window owns the monitor that holds <paramref name="card"/>.</summary>
        public static bool IsActiveOnCardMonitor(IntPtr card)
        {
            try
            {
                IntPtr fg = FullscreenNative.GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != card && FullscreenNative.IsWindowVisible(fg)
                    && CoversCardMonitor(fg, card))
                    return true;

                // Exclusive fullscreen hides the window from ordinary hit-testing but the shell still
                // reports it; treat it as fullscreen no matter which monitor it grabbed.
                int state = FullscreenNative.SHQueryUserNotificationState();
                return state == 2 /* QUNS_BUSY */ || state == 3 /* QUNS_RUNNING_D3D_FULL_SCREEN */;
            }
            catch (Exception ex) { Amc.LyricsService.Log("fullscreen check failed (" + ex.GetType().Name + ")"); return false; }
        }

        static bool CoversCardMonitor(IntPtr fg, IntPtr card)
        {
            FullscreenNative.RECT cr;
            if (!FullscreenNative.GetWindowRect(card, out cr)) return false;
            IntPtr mon = FullscreenNative.MonitorFromWindow(card, FullscreenNative.MONITOR_DEFAULTTONEAREST);
            var mi = new FullscreenNative.MONITORINFO();
            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(FullscreenNative.MONITORINFO));
            if (!FullscreenNative.GetMonitorInfo(mon, ref mi)) return false;

            // The fullscreen window must sit on that same monitor, not on a second screen.
            IntPtr fgMon = FullscreenNative.MonitorFromWindow(fg, FullscreenNative.MONITOR_DEFAULTTONEAREST);
            if (fgMon != mon) return false;

            FullscreenNative.RECT wr;
            if (!FullscreenNative.GetWindowRect(fg, out wr)) return false;
            var m = mi.rcMonitor;
            const int slack = 2;
            return wr.Left <= m.Left + slack && wr.Top <= m.Top + slack
                && wr.Right >= m.Right - slack && wr.Bottom >= m.Bottom - slack;
        }
    }

    internal static class FullscreenNative
    {
        public const int MONITOR_DEFAULTTONEAREST = 2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hWnd, int flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState();

        public struct RECT { public int Left, Top, Right, Bottom; }

        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
    }
}
