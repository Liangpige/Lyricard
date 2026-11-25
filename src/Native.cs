// ---------------------------------------------------------------------------
// Native.cs
//
/// Small P/Invoke surface: window styles, DPI, topmost, cursor position, audio session
/// enumeration -- the things WPF does not expose.
// ---------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Amc
{
    /// <summary>Win32 interop: window styles (no-activate tool window), always-on-top, the
    /// outside-click hook used by the context menu, and DPI helpers.</summary>
    public static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public const int WS_EX_TOPMOST = 0x00000008;
        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOACTIVATE = 0x0010;
        public const int SWP_NOOWNERZORDER = 0x0200;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

        public const uint GW_HWNDPREV = 3;

        // Put the window back on top of the topmost band without stealing focus.
        public static void BringToTop(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0,
                         SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        // ---- outside-click watcher -------------------------------------------------
        // The card window is WS_EX_NOACTIVATE, so the ContextMenu popup never gets mouse
        // capture and a click outside it lands on whatever is underneath without closing
        // the menu. A low-level mouse hook installed only while the menu is open fixes that,
        // and swallowing the click keeps it from also dragging the card / pressing a button.
        public delegate IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, MouseHookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT pt);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_MBUTTONDOWN = 0x0207;

        static IntPtr _mouseHook;
        static MouseHookProc _mouseProc;
        static Func<int, int, bool> _inside;
        static Action _onOutside;

        public static void BeginOutsideClickWatch(Func<int, int, bool> insideMenu, Action onOutside)
        {
            EndOutsideClickWatch();
            _inside = insideMenu;
            _onOutside = onOutside;
            _mouseProc = delegate(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                    {
                        POINT pt;
                        if (GetCursorPos(out pt))
                        {
                            bool inside = _inside != null && _inside(pt.X, pt.Y);
                            if (!inside)
                            {
                                if (_onOutside != null) _onOutside();
                                return new IntPtr(1);   // swallow the click, like a real menu
                            }
                        }
                    }
                }
                return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            };
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
        }

        public static void EndOutsideClickWatch()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            _mouseProc = null;
            _inside = null;
            _onOutside = null;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassNameW(IntPtr hWnd, StringBuilder sb, int n);

        // Start menu / taskbar flyouts are topmost too. Re-asserting there would make the card
        // jump on top of the Start menu, which reads as a bug, so skip those moments.
        public static bool ForegroundIsShell()
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            var sb = new StringBuilder(256);
            if (GetClassNameW(fg, sb, 256) == 0) return false;
            var c = sb.ToString();
            return c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd"
                || c == "Windows.UI.Core.CoreWindow" || c == "XamlExplorerHostIslandWindow"
                || c == "MultitaskingViewFrame";
        }

        public static bool IsTopmost(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            return (GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        }
    }

    public static class TokenInfo
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr buf, int len, out int needed);

        [DllImport("advapi32.dll")]
        static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

        [DllImport("advapi32.dll")]
        static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        public static string IntegrityLevel()
        {
            IntPtr tok = IntPtr.Zero;
            IntPtr buf = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out tok)) return "openfail:" + Marshal.GetLastWin32Error();
                int need;
                GetTokenInformation(tok, 25, IntPtr.Zero, 0, out need);
                if (need <= 0) return "need0";
                buf = Marshal.AllocHGlobal(need);
                int got;
                if (!GetTokenInformation(tok, 25, buf, need, out got)) return "getfail:" + Marshal.GetLastWin32Error();
                IntPtr sid = Marshal.ReadIntPtr(buf);
                IntPtr cnt = GetSidSubAuthorityCount(sid);
                byte n = Marshal.ReadByte(cnt);
                IntPtr rid = GetSidSubAuthority(sid, (uint)(n - 1));
                int v = Marshal.ReadInt32(rid);
                string name = v >= 0x4000 ? "SYSTEM" : v >= 0x3000 ? "HIGH" : v >= 0x2000 ? "MEDIUM" : v >= 0x1000 ? "LOW" : "UNTRUSTED";
                return name + "(" + v + ")";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                if (tok != IntPtr.Zero) CloseHandle(tok);
            }
        }
    }

    /// <summary>Reads / writes the per-user Run key entry ("start with Windows").</summary>
    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Lyricard";
        const string LegacyName = "AppleMusicCard";   // pre-rename value, removed on start

        public static string LastError = "";

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return k != null && k.GetValue(Name) != null;
            }
            catch (Exception ex) { LastError = "is:" + ex.GetType().Name + ":" + ex.Message; return false; }
        }

        /// <summary>Drop the Run value written by builds before the product was renamed.</summary>
        public static void RemoveLegacy()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k != null) k.DeleteValue(LegacyName, false);
                }
            }
            catch { }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) { LastError = "set:nullkey"; return; }
                    if (on)
                    {
                        string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        // --background: logon launches go straight to the tray + card, without
                        // popping the main page in the user's face every boot.
                        k.SetValue(Name, "\"" + exe + "\" --background");
                    }
                    else
                    {
                        k.DeleteValue(Name, false);
                    }
                    LastError = "ok";
                }
            }
            catch (Exception ex) { LastError = "set:" + ex.GetType().Name + ":" + ex.Message; }
        }
    }
}
