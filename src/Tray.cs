// ---------------------------------------------------------------------------
// Tray.cs
//
/// The notification-area icon and its menu, including the icon fallback for systems
/// whose shell refuses to hand back the embedded one.
// ---------------------------------------------------------------------------
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Amc
{
    /// <summary>
    /// Notification-area icon: the second way to reach every command (and the only way back if the
    /// card is hidden). Its menu is rebuilt on language changes.
    /// </summary>
    public class Tray : IDisposable
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        NotifyIcon _icon;
        CardWindow _win;

        public Tray(CardWindow win)
        {
            _win = win;

            _icon = new NotifyIcon();
            _icon.Icon = LoadIcon();
            _icon.Text = Strings.T("app.tip");
            _icon.Visible = true;
            // Double-click is the second entry point next to the card itself: open settings
            // instead of toggling visibility (the card is normally always on screen anyway).
            _icon.DoubleClick += delegate { try { _win.OpenMainPage(); } catch { } };
            // Right-click opens a WPF menu (see ShowMenu) so the tray and the card speak the
            // same visual language: same theme, same row height, same check marks.
            _icon.MouseUp += delegate(object s2, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                try { ShowMenu(); } catch { }
            };
        }

        /// <summary>The menu is rebuilt on every open, so a language change only needs the tooltip.</summary>
        public void Relang()
        {
            _icon.Text = Strings.T("app.tip");
        }

        public void Balloon(string title, string text)
        {
            try { _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info); } catch { }
        }

        void Toggle()
        {
            _win.UserToggle();
        }

        /// <summary>
        /// The tray menu, built as a WPF ContextMenu and dressed by MenuTheme so it is visually
        /// identical to the card's right-click menu. It is created fresh on each open: the menu is
        /// small, and this way every check mark reflects the state of the moment without an
        /// Opening hook to keep in sync.
        ///
        /// Deliberately absent here: 透明背景 / 封面旋转 / 监听所有播放器 (they live on the 外观 and
        /// 主页 pages, where you can see what you are changing) and 卸载 (destructive, so it sits at
        /// the bottom of 关于 next to the version you are uninstalling).
        /// </summary>
        public void ShowMenu()
        {
            var m = new System.Windows.Controls.ContextMenu();
            m.Items.Add(Item(Strings.T("menu.showHide"), delegate { Toggle(); }));
            m.Items.Add(Item(Strings.T("menu.main"), delegate { _win.OpenMainPage(); }));
            m.Items.Add(Item(Strings.T("menu.settings"), delegate { _win.OpenSettings(); }));
            m.Items.Add(new System.Windows.Controls.Separator());
            m.Items.Add(Item(Strings.T("menu.refetch"), delegate { _win.Refetch(); }));
            m.Items.Add(Item(Strings.T("menu.export"), delegate { _win.ExportLyrics(); }));
            m.Items.Add(Item(Strings.T("menu.lyricEarly"), delegate { _win.NudgeOffset(0.1); }));
            m.Items.Add(Item(Strings.T("menu.lyricLate"), delegate { _win.NudgeOffset(-0.1); }));
            m.Items.Add(new System.Windows.Controls.Separator());
            m.Items.Add(Item(Strings.T("menu.bigger"), delegate { _win.NudgeScale(0.08); }));
            m.Items.Add(Item(Strings.T("menu.smaller"), delegate { _win.NudgeScale(-0.08); }));
            m.Items.Add(Item(Strings.T("menu.fadeMore"), delegate { _win.NudgeOpacity(-0.06); }));
            m.Items.Add(Item(Strings.T("menu.fadeLess"), delegate { _win.NudgeOpacity(0.06); }));
            m.Items.Add(Check(Strings.T("menu.lock"), _win.Cfg.Locked, delegate(bool on)
            {
                if (_win.Cfg.Locked != on) _win.ToggleLock();
            }));
            m.Items.Add(new System.Windows.Controls.Separator());
            m.Items.Add(Check(Strings.T("menu.autostart"), AutoStart.IsEnabled(), delegate(bool on)
            {
                AutoStart.Set(on);
                _win.Cfg.AutoStart = on;
                _win.Cfg.Save();
            }));
            m.Items.Add(new System.Windows.Controls.Separator());
            m.Items.Add(Item(Strings.T("menu.quit"), delegate { _win.Quit(); }));

            MenuTheme.Apply(m);
            // MousePoint keeps the popup at the cursor without any coordinate maths, and the card
            // window doubles as the placement target (it exists for the whole lifetime of the app,
            // even when it is hidden because no player is running).
            m.PlacementTarget = _win;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.StaysOpen = false;
            // A popup opened from a tray click is not inside our own input loop, so WPF never
            // takes the mouse capture and the menu would hang around forever after a click
            // elsewhere. Handing the popup's HWND the foreground role (the classic WinForms tray
            // trick) makes it behave: click away / Esc both dismiss it.
            m.Opened += delegate
            {
                try
                {
                    var src = System.Windows.PresentationSource.FromVisual(m) as System.Windows.Interop.HwndSource;
                    if (src != null) SetForegroundWindow(src.Handle);
                }
                catch { }
            };
            m.IsOpen = true;
        }

        static System.Windows.Controls.MenuItem Item(string text, Action act)
        {
            var i = new System.Windows.Controls.MenuItem { Header = text };
            i.Click += delegate { act(); };
            return i;
        }

        static System.Windows.Controls.MenuItem Check(string text, bool on, Action<bool> act)
        {
            var i = new System.Windows.Controls.MenuItem { Header = text, IsCheckable = true, IsChecked = on };
            i.Click += delegate { act(i.IsChecked); };
            return i;
        }

        /// <summary>Prefers the icon embedded in the exe (assets/lyricard.ico, compiled in with
        /// /win32icon); falls back to drawing one if the resource is missing.</summary>
        static Icon LoadIcon()
        {
            try
            {
                string exe = System.Windows.Forms.Application.ExecutablePath;
                var ico = Icon.ExtractAssociatedIcon(exe);
                if (ico != null) return ico;
            }
            catch { }
            return MakeIcon();
        }

        static Icon MakeIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                using (var path = RoundRect(1, 1, 30, 30, 8))
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(255, 52, 52, 60), Color.FromArgb(255, 16, 16, 20), 45f))
                    g.FillPath(b, path);

                using (var f = new Font("Segoe UI Symbol", 17f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var br = new SolidBrush(Color.FromArgb(255, 250, 250, 252)))
                {
                    var sz = g.MeasureString("\u266A", f);
                    g.DrawString("\u266A", f, br, (32 - sz.Width) / 2f, (32 - sz.Height) / 2f + 1);
                }
            }
            // Icon.FromHandle does not own the handle: clone it and release the raw HICON, or every
            // call leaks a GDI/user handle for the life of the process.
            IntPtr h = bmp.GetHicon();
            try
            {
                using (var raw = Icon.FromHandle(h)) return (Icon)raw.Clone();
            }
            finally
            {
                DestroyIcon(h);
                bmp.Dispose();
            }
        }

        static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, r * 2, r * 2, 180, 90);
            p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void Dispose()
        {
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
        }
    }
}
