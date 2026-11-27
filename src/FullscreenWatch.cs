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
        static readonly bool Trace = Environment.GetEnvironmentVariable("AMC_FS_TRACE") == "1";

        static void T(string s) { if (Trace) Amc.LyricsService.Log("fs " + s); }

        public static bool IsActiveOnCardMonitor(IntPtr card)
        {
            try
            {
                T("enter card=" + card.ToInt64().ToString("x"));
                IntPtr fg = FullscreenNative.GetForegroundWindow();
                T("fg=" + fg.ToInt64().ToString("x"));
                if (IsDesktopOrShell(fg))
                {
                    // The desktop itself is a "window covering the monitor" whenever it has focus
                    // (clicking the wallpaper, closing the last window, Alt+Tab to it). It is not an
                    // app going fullscreen, so never let it hide the card.
                    T("fg is shell/desktop");
                    return false;
                }
                if (fg != IntPtr.Zero && fg != card && FullscreenNative.IsWindowVisible(fg)
                    && CoversCardMonitor(fg, card))
                    return true;

                // Exclusive fullscreen hides the window from ordinary hit-testing but the shell still
                // reports it; treat it as fullscreen no matter which monitor it grabbed.
                T("shqns enter");
                int state;
                int hr = FullscreenNative.SHQueryUserNotificationState(out state);
                T("shqns hr=" + hr + " state=" + state);
                if (hr != 0) return false;
                return state == 2 /* QUNS_BUSY */ || state == 3 /* QUNS_RUNNING_D3D_FULL_SCREEN */;
            }
            catch (Exception ex) { Amc.LyricsService.Log("fullscreen check failed (" + ex.GetType().Name + ")"); return false; }
        }

        /// <summary>Desktop / taskbar / shell surfaces: visible, monitor-sized, but not fullscreen apps.</summary>
        static bool IsDesktopOrShell(IntPtr h)
        {
            if (h == IntPtr.Zero) return true;
            var sb = new System.Text.StringBuilder(64);
            if (FullscreenNative.GetClassNameW(h, sb, 64) == 0) return false;
            switch (sb.ToString())
            {
                case "Progman":
                case "WorkerW":
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                case "MultitaskingViewFrame":
                case "XamlExplorerHostIslandWindow":
                case "Windows.UI.Core.CoreWindow":
                    return true;
                default:
                    return false;
            }
        }

        static bool CoversCardMonitor(IntPtr fg, IntPtr card)
        {
            FullscreenNative.RECT cr;
            T("rect card");
            if (!FullscreenNative.GetWindowRect(card, out cr)) return false;
            T("rect card ok");
            IntPtr mon = FullscreenNative.MonitorFromWindow(card, FullscreenNative.MONITOR_DEFAULTTONEAREST);
            T("mon card=" + mon.ToInt64().ToString("x"));
            var mi = new FullscreenNative.MONITORINFO();
            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(FullscreenNative.MONITORINFO));
            T("moninfo enter");
            if (!FullscreenNative.GetMonitorInfo(mon, ref mi)) return false;
            T("moninfo ok");

            // The fullscreen window must sit on that same monitor, not on a second screen.
            IntPtr fgMon = FullscreenNative.MonitorFromWindow(fg, FullscreenNative.MONITOR_DEFAULTTONEAREST);
            if (fgMon != mon) return false;

            FullscreenNative.RECT wr;
            T("rect fg");
            if (!FullscreenNative.GetWindowRect(fg, out wr)) return false;
            T("rect fg ok " + wr.Left + "," + wr.Top + "," + wr.Right + "," + wr.Bottom);
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

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder sb, int max);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);

        // HRESULT SHQueryUserNotificationState(QUERY_USER_NOTIFICATION_STATE *pquns) -- it takes an
        // OUT pointer, it does not return the state. Declaring it with no argument makes the shell
        // write four bytes through whatever happens to be in RCX, which corrupts the CLR heap and
        // kills the process a few ticks later (0xC0000005 in clr.dll, exit 0x80131506).
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

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
