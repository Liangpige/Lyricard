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

        NotifyIcon _icon;
        CardWindow _win;
        SmtcService _smtc;

        public Tray(CardWindow win, SmtcService smtc)
        {
            _win = win;
            _smtc = smtc;

            _icon = new NotifyIcon();
            _icon.Icon = LoadIcon();
            _icon.Text = Strings.T("app.tip");
            _icon.Visible = true;
            // Double-click is the second entry point next to the card itself: open settings
            // instead of toggling visibility (the card is normally always on screen anyway).
            _icon.DoubleClick += delegate { try { _win.OpenMainPage(); } catch { } };
            _icon.ContextMenuStrip = BuildMenu();
        }

        /// <summary>Rebuild the tray menu after an interface-language change.</summary>
        public void Relang()
        {
            _icon.Text = Strings.T("app.tip");
            var old = _icon.ContextMenuStrip;
            _icon.ContextMenuStrip = BuildMenu();
            if (old != null) old.Dispose();   // NotifyIcon does not own it; leaks one menu per relang
        }

        public void Balloon(string title, string text)
        {
            try { _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info); } catch { }
        }

        void Toggle()
        {
            _win.UserToggle();
        }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add(Item(Strings.T("menu.showHide"), delegate { Toggle(); }));
            m.Items.Add(Item(Strings.T("menu.main"), delegate { _win.OpenMainPage(); }));
            m.Items.Add(Item(Strings.T("menu.settings"), delegate { _win.OpenSettings(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item(Strings.T("menu.refetch"), delegate { _win.Refetch(); }));
            m.Items.Add(Item(Strings.T("menu.export"), delegate { _win.ExportLyrics(); }));
            m.Items.Add(Item(Strings.T("menu.lyricEarly"), delegate { _win.NudgeOffset(0.1); }));
            m.Items.Add(Item(Strings.T("menu.lyricLate"), delegate { _win.NudgeOffset(-0.1); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item(Strings.T("menu.bigger"), delegate { _win.NudgeScale(0.08); }));
            m.Items.Add(Item(Strings.T("menu.smaller"), delegate { _win.NudgeScale(-0.08); }));
            m.Items.Add(Item(Strings.T("menu.fadeMore"), delegate { _win.NudgeOpacity(-0.06); }));
            m.Items.Add(Item(Strings.T("menu.fadeLess"), delegate { _win.NudgeOpacity(0.06); }));

            var lockItem = new ToolStripMenuItem(Strings.T("menu.lock"));
            lockItem.CheckOnClick = true;
            lockItem.Checked = _win.Cfg.Locked;
            lockItem.Click += delegate { _win.ToggleLock(); lockItem.Checked = _win.Cfg.Locked; };
            m.Items.Add(lockItem);


            var clearItem = new ToolStripMenuItem(Strings.T("menu.clearBgShort"));
            clearItem.CheckOnClick = true;
            clearItem.Checked = _win.Cfg.ClearBg;
            clearItem.Click += delegate { _win.SetClearBg(clearItem.Checked); };
            m.Items.Add(clearItem);

            var spinItem = new ToolStripMenuItem(Strings.T("menu.spin"));
            spinItem.CheckOnClick = true;
            spinItem.Checked = _win.Cfg.ArtSpin;
            spinItem.Click += delegate { _win.SetArtSpin(spinItem.Checked); };
            m.Items.Add(spinItem);

            var allItem = new ToolStripMenuItem(Strings.T("menu.listenAll"));
            allItem.CheckOnClick = true;
            allItem.Checked = _win.Cfg.ListenAll;
            allItem.Click += delegate
            {
                _win.Cfg.ListenAll = allItem.Checked;
                _smtc.ListenAll = allItem.Checked;
                _win.Cfg.Save();
            };
            m.Items.Add(allItem);

            m.Items.Add(new ToolStripSeparator());
            var autoItem = new ToolStripMenuItem(Strings.T("menu.autostart"));
            autoItem.CheckOnClick = true;
            autoItem.Checked = AutoStart.IsEnabled();
            autoItem.Click += delegate
            {
                AutoStart.Set(autoItem.Checked);
                _win.Cfg.AutoStart = autoItem.Checked;
                _win.Cfg.Save();
            };
            m.Items.Add(autoItem);

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item(Strings.T("menu.quit"), delegate { _win.Quit(); }));
            // Last entry, after the normal quit: it wipes config/cache and deletes the exe.
            m.Items.Add(Item(Strings.T("menu.uninstall"), delegate { Uninstall.Run(true, false); }));

            m.Opening += delegate
            {
                lockItem.Checked = _win.Cfg.Locked;
                clearItem.Checked = _win.Cfg.ClearBg;
                spinItem.Checked = _win.Cfg.ArtSpin;
                allItem.Checked = _win.Cfg.ListenAll;
                autoItem.Checked = AutoStart.IsEnabled();
            };
            return m;
        }

        static ToolStripMenuItem Item(string text, Action act)
        {
            var i = new ToolStripMenuItem(text);
            i.Click += delegate { act(); };
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
