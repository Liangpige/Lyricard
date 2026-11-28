// ---------------------------------------------------------------------------
// MainWindow.cs
//
/// The control panel (640x480, four pages: home / look / lyrics / about) plus the
/// hand-rolled WPF widgets and motion primitives the rest of the UI shares. No XAML,
/// no third-party toolkit -- every control here is built in code.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Shapes;

namespace Amc
{
    /// <summary>
    /// The application window ("main page"). A fixed 4:3 shell (640x480 DIP) with a self-drawn
    /// title strip and three real pages: look, lyrics and about. The desktop card stays the
    /// always-on-top widget; this window is the place you configure it.
    ///
    /// Closing only hides the window -- the card and the tray keep running.
    /// </summary>
    public partial class MainWindow : Window
    {
        public const double WinW = 800;
        public const double WinH = 540;
        const double NavH = 58;

        // Monochrome ramp: window, nav strip, hairline, then three text steps.
        static readonly Color Bg = Color.FromRgb(14, 14, 16);
        static readonly Color NavBg = Color.FromRgb(20, 20, 23);
        static readonly Color Hair = Color.FromArgb(30, 255, 255, 255);
        static readonly Color TextMain = Color.FromArgb(246, 246, 247, 249);
        static readonly Color TextDim = Color.FromArgb(150, 224, 226, 230);
        static readonly Color TextFaint = Color.FromArgb(96, 224, 226, 230);

        static MainWindow _open;

        readonly Config _cfg;
        readonly CardWindow _card;

        /// <summary>The UI kit, mapped onto this window's monochrome palette. Every page builds its
        /// controls from it now; the older inline primitives stay behind as a fallback.</summary>
        UiTheme _kit;

        ContentControl _host;
        readonly Dictionary<string, TextBlock> _tabText = new Dictionary<string, TextBlock>();
        readonly Dictionary<string, Rectangle> _tabLine = new Dictionary<string, Rectangle>();
        readonly List<FrameworkElement> _tabBoxes = new List<FrameworkElement>();
        readonly List<FrameworkElement> _noDragZones = new List<FrameworkElement>();
        string _page = "home";

        public MainWindow(Config cfg, CardWindow card)
        {
            _cfg = cfg;
            _card = card;
            _kit = KitTheme();

            Title = Strings.T("app.title");
            Width = WinW;
            Height = WinH;
            MinWidth = WinW;
            MinHeight = WinH;
            MaxWidth = WinW;
            MaxHeight = WinH;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Bg);
            ShowInTaskbar = true;
            UseLayoutRounding = true;
            try { Icon = AppIcon(); } catch (Exception ex) { LyricsService.Log("thin scrollbar skipped: " + ex.Message); }

            // Deliberately NOT topmost: the desktop card is the always-on-top thing, this window
            // behaves like any other app window.
            Topmost = false;

            Content = BuildUi();
            SetPage("home");

            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
            };
        }

        static ImageSource AppIcon()
        {
            using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(
                System.Reflection.Assembly.GetEntryAssembly().Location))
            {
                if (ico == null) return null;
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
        }

        /// <summary>Show the single main window (creating it once); safe to call repeatedly.</summary>
        public static MainWindow Open(Config cfg, CardWindow card)
        {
            if (_open == null)
            {
                _open = new MainWindow(cfg, card);
                _open.Closed += delegate { _open = null; };
            }
            if (!_open.IsVisible) _open.Show();
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
            return _open;
        }

        /// <summary>The tray's "settings" entry lands on the appearance page, which replaced
        /// the old standalone panel.</summary>
        /// <summary>
        /// Opens the window straight on one page ("home" / "look" / "lyrics" / "settings" / "about").
        /// Used by the card's "打开主页面" and by <c>--settings</c>, which has to land on
        /// 「外观」 rather than on the home page -- otherwise the flag looks like it did nothing.
        /// </summary>
        public void ShowPage(string key)
        {
            SetPage(key);
        }

        /// <summary>Closing the main page must not kill the card, so it hides instead.</summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }

        // ---- small building blocks ---------------------------------------------------------

        static TextBlock Label(string text, double size, Color color, FontWeight weight)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = new SolidColorBrush(color),
                FontWeight = weight,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI")
            };
        }

        TextBlock Body(string text, double size)
        {
            var t = Label(text, size, TextDim, FontWeights.Normal);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 0, 0, 14);
            return t;
        }

        int _sectionCount;

        /// <summary>
        /// Group heading. Every heading but the first on a page is preceded by a hairline, which is
        /// what actually separates "groups" visually on a dark surface -- spacing alone was mushy.
        /// </summary>
        /// <summary>Bare 1px divider, same weight as the one <see cref="Section"/> draws, for
        /// cases where two unrelated controls sit close together without a group heading.</summary>
        UIElement Rule()
        {
            var r = new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                Margin = new Thickness(0, 16, 0, 12)
            };
            r.Tag = "rule";
            return r;
        }

        UIElement Section(string text)
        {
            var t = Label(text, 11, TextFaint, FontWeights.Normal);
            t.Margin = new Thickness(0, 12, 0, 6);
            // Tagged so PageFrame can regroup the page into cards without every call site
            // knowing about it.
            t.Tag = "section";
            if (_sectionCount++ == 0) return t;
            var box = new StackPanel();
            var rule = new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                Margin = new Thickness(0, 14, 0, 0)
            };
            rule.Tag = "rule";
            box.Children.Add(rule);
            box.Children.Add(t);
            // Tag the wrapper too: the heading is what the card grouper keys on, and for every
            // heading but the first that wrapper is the element the page actually receives.
            box.Tag = "section";
            return box;
        }

        /// <summary>Page shell: big title, then the content. Gives every page a top edge.</summary>
        /// <summary>
        /// Re-groups a page into the kit's big frames: everything from one section heading up to
        /// the next becomes a rounded panel, so the page is a stack of cards instead of a run of
        /// hairlines. Preamble items (the board, its button row) stay outside, and pairing inside
        /// a card is still handled by <see cref="TwoColumn"/>.
        /// </summary>
        UIElement Cards(UIElement body)
        {
            StackPanel src = body as StackPanel;
            if (src == null) return body;

            var items = new System.Collections.Generic.List<UIElement>();
            foreach (UIElement child in src.Children) items.Add(child);
            src.Children.Clear();

            var dst = new StackPanel();
            var loose = new System.Collections.Generic.List<UIElement>();
            string title = null;
            var rows = new System.Collections.Generic.List<UIElement>();

            Action flush = delegate
            {
                if (title == null)
                {
                    for (int i = 0; i < loose.Count; i++) dst.Children.Add(loose[i]);
                    loose.Clear();
                    rows.Clear();
                    return;
                }

                if (rows.Count == 0) { title = null; return; }   // a heading with nothing under it
                var card = new Border();
                card.CornerRadius = new CornerRadius(10);
                card.Background = new SolidColorBrush(Color.FromArgb(255, 23, 23, 27));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
                card.BorderThickness = new Thickness(1);
                card.Padding = new Thickness(14, 12, 14, 8);
                card.Margin = new Thickness(0, 0, 0, 12);

                var inner = new StackPanel();
                var cap = Label(title, 11.5, TextDim, FontWeights.Normal);
                cap.Margin = new Thickness(0, 0, 0, 10);
                inner.Children.Add(cap);

                var box = new StackPanel();
                for (int i = 0; i < rows.Count; i++) box.Children.Add(rows[i]);
                rows.Clear();
                inner.Children.Add(TwoColumn(box));

                card.Child = inner;
                dst.Children.Add(card);
                title = null;
            };

            foreach (UIElement child in items)
            {
                FrameworkElement fe = child as FrameworkElement;
                string tag = fe == null ? null : fe.Tag as string;
                if (tag == "section")
                {
                    flush();
                    TextBlock head = child as TextBlock;
                    if (head == null)
                    {
                        StackPanel sp = child as StackPanel;
                        if (sp != null)
                            foreach (UIElement ic in sp.Children)
                            {
                                TextBlock tb = ic as TextBlock;
                                if (tb != null) { head = tb; break; }
                            }
                    }
                    title = head == null ? "" : head.Text;
                    continue;
                }
                if (tag == "rule") continue;        // the card edge does that job now
                if (title == null) loose.Add(child);
                else rows.Add(child);
            }
            flush();
            return dst;
        }

        UIElement PageFrame(string key, UIElement body)
        {
            var box = new StackPanel();
            // The appearance tab is a shell around ten pages: its heading names the stop on
            // screen, not the tab, or every stop would look like the same page.
            string navKey = key == "look" ? _lookPage : "nav." + key;
            var title = Label(Strings.T(navKey), 17, TextMain, FontWeights.SemiBold);
            title.Margin = new Thickness(0, 0, 0, 12);
            box.Children.Add(title);
            box.Children.Add(Cards(body));
            return box;
        }

        /// <summary>
        /// Two controls per line wherever the section has room for it. Rows are tagged at creation and
        /// paired here in one pass, so every page gets the same rhythm without touching each call site.
        /// Wide rows (colour palettes) and anything that is not a row -- headings, hairlines, buttons,
        /// prose -- stay alone, which also means a pair never straddles a divider.
        /// </summary>
        UIElement TwoColumn(UIElement body)
        {
            StackPanel src = body as StackPanel;
            if (src == null) return body;
            var items = new System.Collections.Generic.List<UIElement>();
            foreach (UIElement child in src.Children) items.Add(child);
            src.Children.Clear();          // detach first: a child cannot have two logical parents
            var dst = new StackPanel();
            var pending = new System.Collections.Generic.List<FrameworkElement>();
            foreach (UIElement child in items)
            {
                FrameworkElement fe = child as FrameworkElement;
                bool pair = fe != null && (fe.Tag as string) == "row";
                if (pair) { pending.Add(fe); continue; }
                FlushPairs(dst, pending);
                StackPanel inner = child as StackPanel;
                bool innerHasRows = false;
                if (inner != null && inner.Tag == null)
                {
                    foreach (UIElement ic in inner.Children)
                    {
                        FrameworkElement ife = ic as FrameworkElement;
                        if (ife != null && (ife.Tag as string) == "row") { innerHasRows = true; break; }
                    }
                }
                dst.Children.Add(innerHasRows ? TwoColumn(inner) : child);
            }
            FlushPairs(dst, pending);
            return dst;
        }

        static void FlushPairs(StackPanel target, System.Collections.Generic.List<FrameworkElement> pending)
        {
            for (int i = 0; i < pending.Count; i += 2)
            {
                if (i + 1 >= pending.Count) { target.Children.Add(pending[i]); break; }
                var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition());
                FrameworkElement a = pending[i], b = pending[i + 1];
                a.Margin = new Thickness(0, 0, 10, 0);
                b.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(a, 0);
                Grid.SetColumn(b, 1);
                g.Children.Add(a);
                g.Children.Add(b);
                target.Children.Add(g);
            }
            pending.Clear();
        }

        /// <summary>Label above its control, matching the settings panel's stacked layout.</summary>
        FrameworkElement Row(string label, UIElement control)
        {
            return Row(label, control, false);
        }

        /// <summary>A row whose label is dimmer -- used for players that are merely placeholders.</summary>
        FrameworkElement Row(string label, UIElement control, bool muted)
        {
            var box = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Tag = "row" };
            var l = Label(label, 11.5, muted ? TextFaint : TextDim, FontWeights.Normal);
            l.Margin = new Thickness(0, 0, 0, 3);
            box.Children.Add(l);
            if (control is FrameworkElement) ((FrameworkElement)control).HorizontalAlignment = HorizontalAlignment.Left;
            box.Children.Add(control);
            return box;
        }

        /// <summary>Thin dark scrollbar used by the page body (see BuildUi).</summary>
        // palette shared by every colour row: neutral first, then accents
        static readonly string[] Swatches = new string[]
        {
            "", "#FFFFFFFF", "#FFC9CCD2", "#FF8D939C", "#FF4A4F57",
            "#FFE5484D", "#FFFFB224", "#FF3FB950", "#FF3B82F6"
        };

        /// <summary>
        /// The kit ships its own palette; the look page has to keep this window's ramp, so the
        /// semantic tokens are re-pointed at the same colours the rest of the UI uses. Nothing but
        /// the control shapes should change when a page moves onto the kit.
        /// </summary>
        static UiTheme KitTheme()
        {
            UiTheme t = UiTheme.Dark();
            t.Window = Bg;
            t.Surface = NavBg;
            t.SurfaceAlt = Color.FromArgb(255, 28, 28, 32);
            t.Hover = Color.FromArgb(255, 36, 36, 41);
            t.Pressed = Color.FromArgb(255, 46, 46, 52);
            t.Border = Hair;
            t.BorderStrong = Color.FromArgb(52, 255, 255, 255);
            t.Text = TextMain;
            t.TextSecondary = Color.FromArgb(210, 224, 226, 230);
            t.TextTertiary = TextDim;
            t.Solid = Color.FromArgb(255, 236, 238, 242);
            t.SolidHover = Color.FromArgb(255, 255, 255, 255);
            t.SolidPressed = Color.FromArgb(255, 214, 217, 223);
            t.OnSolid = Color.FromRgb(20, 20, 22);
            t.Accent = t.Solid;
            t.AccentHover = t.SolidHover;
            t.OnAccent = t.OnSolid;
            t.AccentSoft = Color.FromArgb(26, 255, 255, 255);
            t.Danger = Color.FromArgb(255, 229, 72, 77);
            t.DangerInk = t.Danger;
            t.Track = Color.FromArgb(255, 62, 62, 70);
            t.TrackOn = t.Solid;
            return t;
        }

        static bool TryHex(string hex, out Color c)
        {
            c = Colors.White;
            if (string.IsNullOrEmpty(hex)) return false;
            string h = hex.TrimStart('#');
            if (h.Length == 6) h = "FF" + h;
            if (h.Length != 8) return false;
            try
            {
                c = Color.FromArgb(
                    Convert.ToByte(h.Substring(0, 2), 16),
                    Convert.ToByte(h.Substring(2, 2), 16),
                    Convert.ToByte(h.Substring(4, 2), 16),
                    Convert.ToByte(h.Substring(6, 2), 16));
                return true;
            }
            catch { return false; }
        }

        /// <summary>One row of colour dots; the first dot means "use the built-in colour".</summary>
        /// <summary>Repaints the selection ring on a swatch row without rebuilding the page.</summary>
        static void RepaintDots(WrapPanel dots, string current)
        {
            foreach (UIElement child in dots.Children)
            {
                Border d = child as Border;
                if (d == null || d.Tag == null) continue;
                string v = d.Tag as string;
                bool on = string.Equals(v ?? "", current ?? "", StringComparison.OrdinalIgnoreCase);
                d.BorderThickness = new Thickness(on ? 2 : 1);
                d.BorderBrush = new SolidColorBrush(on
                    ? Color.FromArgb(255, 236, 238, 242)
                    : Color.FromArgb(40, 255, 255, 255));
            }
        }

        /// <summary>Nine quick swatches plus a "…" that opens the full colour library
        /// (see ColorLibraryWindow) for anything the quick row does not cover.</summary>
        UIElement ColorRow(string label, string current, Action<string> apply)
        {
            string sel0 = current ?? "";
            Color here;
            Color? picked = (sel0.Length > 0 && TryHex(sel0, out here)) ? (Color?)here : null;
            var palette = new List<Color>();
            for (int i = 1; i < Swatches.Length; i++)   // [0] is the reset slot, the kit draws it
            {
                Color one;
                if (TryHex(Swatches[i], out one)) palette.Add(one);
            }
            WrapPanel dots = UiKit.ColorSwatches(_kit, picked, palette.ToArray(),
                delegate(Color c) { apply("#" + c.ToString().Substring(3)); },
                delegate { apply(""); },
                delegate { ColorLibraryWindow.Open(label, sel0, delegate(string v) { apply(v ?? ""); }); });
            return Row(label, dots);
        }

        /// <summary>− value + stepper; every look value in this app uses the same one.</summary>
        UIElement Stepper(double value, double min, double max, double step, string fmt, Action<double> apply)
        {
            return UiKit.Stepper(_kit, value, min, max, step, fmt, apply);
        }



        Border Pill(string text, Action act, bool primary)
        {
            var kb = UiKit.Button(_kit, text, primary ? UiButtonKind.Primary : UiButtonKind.Ghost,
                                  delegate { try { act(); } catch { } });
            kb.Margin = new Thickness(0, 0, 8, 0);
            return kb;
        }

        /// <summary>A 42x22 switch, same shape as the settings panel uses.</summary>
        UIElement Toggle(bool on, Action<bool> apply)
        {
            return Toggle(on, apply, true);
        }

        UIElement Toggle(bool on, Action<bool> apply, bool enabled)
        {
            return UiKit.Switch(_kit, on, enabled, apply);
        }

        // ---- shell -------------------------------------------------------------------------

        UIElement BuildUi()
        {
            // RebuildAll() re-runs this method on every language/binding change, so the
            // per-tab bookkeeping has to start empty -- otherwise the old (detached) tabs
            // stay referenced and the drag hit-test keeps scanning them forever.
            _tabBoxes.Clear();
            _tabText.Clear();
            _tabLine.Clear();
            _noDragZones.Clear();

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(NavH) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var nav = new Border
            {
                Background = new SolidColorBrush(NavBg),
                BorderBrush = new SolidColorBrush(Hair),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 0, 12, 0)
            };
            nav.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                // DragMove captures the mouse until the button goes up, which would swallow the
                // MouseLeftButtonUp that our tabs rely on -- so ignore presses that start on a tab.
                if (e.ButtonState != MouseButtonState.Pressed) return;
                DependencyObject hit = e.OriginalSource as DependencyObject;
                while (hit != null && hit != nav)
                {
                    // tabs and the window buttons both live in the strip; a drag started on them
                    // would swallow their mouse-up and make them feel dead.
                    if (_tabBoxes.Contains(hit as FrameworkElement)) return;
                    if (_noDragZones.Contains(hit as FrameworkElement)) return;
                    hit = VisualTreeHelper.GetParent(hit);
                }
                try { DragMove(); } catch { }
            };

            var navRow = new Grid();
            navRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            navRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(Label("Lyricard", 15.5, TextMain, FontWeights.SemiBold));
            var sub = Label(Strings.T("app.version"), 11, TextFaint, FontWeights.Normal);
            sub.Margin = new Thickness(7, 1, 0, 0);
            brand.Children.Add(sub);
            Grid.SetColumn(brand, 0);
            navRow.Children.Add(brand);

            var tabs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(38, 0, 0, 0)
            };
            tabs.Children.Add(Tab("home", Strings.T("nav.home")));
            tabs.Children.Add(Tab("look", Strings.T("nav.appearance")));
            tabs.Children.Add(Tab("lyrics", Strings.T("nav.lyrics")));
            tabs.Children.Add(Tab("settings", Strings.T("nav.settings")));
            tabs.Children.Add(Tab("about", Strings.T("nav.about")));
            Grid.SetColumn(tabs, 1);
            navRow.Children.Add(tabs);

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            controls.Children.Add(Chrome("min", delegate { WindowState = WindowState.Minimized; }));
            controls.Children.Add(Chrome("close", delegate { Hide(); }));
            Grid.SetColumn(controls, 3);
            navRow.Children.Add(controls);
            _noDragZones.Add(controls);

            nav.Child = navRow;
            Grid.SetRow(nav, 0);
            root.Children.Add(nav);

            _host = new ContentControl();
            // The kit draws the bar, so the page and the preview window cannot drift apart
            // (the XAML style that used to live here silently never applied).
            // The kit template does not forward Padding to its presenter, so the gutter is a
            // wrapper instead of a ScrollViewer property.
            var gutter = new Border { Padding = new Thickness(24, 20, 24, 20), Child = _host };

            // The rail is auto-width, so every page but the appearance one leaves the column at
            // zero and nothing shifts.
            _railHost = new ContentControl
            {
                Margin = new Thickness(24, 24, 4, 20),
                VerticalAlignment = VerticalAlignment.Top
            };
            FrameworkElement scrollHost;
            var body = UiKit.Scroll(_kit, gutter, Bg, out scrollHost);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_railHost, 0);
            columns.Children.Add(_railHost);
            Grid.SetColumn(scrollHost, 1);
            columns.Children.Add(scrollHost);
            Grid.SetRow(columns, 1);
            root.Children.Add(columns);
            _scroll = body;

            return new Border
            {
                BorderBrush = new SolidColorBrush(Hair),
                BorderThickness = new Thickness(1),
                Child = root
            };
        }

        /// <summary>A nav tab: text plus a 2px underline when active.</summary>
        UIElement Tab(string key, string text)
        {
            // padding + transparent background: without them the only clickable pixels are the
            // glyphs themselves, which makes the strip feel dead between words.
            // Grid, not StackPanel: the label is centred in the 34 px strip and the underline
            // is pinned to the bottom edge. A vertical stack would have shifted the text up by
            // half the underline's height, which made the tabs sit visibly higher than the
            // "Lyricard v0.x" lockup on the left.
            var inner = new Grid();
            var box = new Border
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(9, 0, 9, 0),
                CornerRadius = new CornerRadius(7),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Child = inner
            };
            var t = Label(text, 12.5, TextDim, FontWeights.Normal);
            t.VerticalAlignment = VerticalAlignment.Center;
            // Optical alignment with the "Lyricard v0.x" lockup: the 12.5 px tab text sits
            // ~1.5 px higher than the 15.5 px wordmark when both are mathematically centred,
            // so nudge it down by 2 DIP (a centred element shifts by half the top margin).
            t.Margin = new Thickness(0, 2, 0, 0);
            inner.Children.Add(t);

            var line = new Rectangle
            {
                Height = 2,
                RadiusX = 1,
                RadiusY = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Fill = Brushes.Transparent,
                // Starts invisible: SetPage paints every line white before it fades the inactive
                // ones out, and on a fresh tab that white used to be visible for a frame.
                Opacity = 0
            };
            inner.Children.Add(line);
            box.Height = 34;
            // The active tab already carries the underline, so it does not light up on
            // hover -- only the inactive ones do.
            var hoverBrush = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            box.Background = hoverBrush;
            box.MouseEnter += delegate
            {
                UiMotion.Color(hoverBrush, Color.FromArgb((byte)(key == _page ? 0 : 18), 255, 255, 255), _kit.MotionFast);
                // The word lifts towards full white too: a background wash alone reads as a
                // tooltip, the text shift is what makes the strip feel alive.
                UiMotion.Color((SolidColorBrush)t.Foreground, TextMain, _kit.MotionFast);
            };
            box.MouseLeave += delegate
            {
                UiMotion.Color(hoverBrush, Color.FromArgb(0, 255, 255, 255), _kit.MotionBase);
                UiMotion.Color((SolidColorBrush)t.Foreground, key == _page ? TextMain : TextDim, _kit.MotionBase);
            };

            // Same rubber as a kit button: quick dip on press, slower spring back.
            var tabScale = new ScaleTransform(1, 1);
            box.RenderTransformOrigin = new Point(0.5, 0.5);
            box.RenderTransform = tabScale;
            box.PreviewMouseLeftButtonDown += delegate { UiMotion.Scale(tabScale, 0.96, 90); };
            box.PreviewMouseLeftButtonUp += delegate { UiMotion.Scale(tabScale, 1.0, 200); };

            box.MouseLeftButtonUp += delegate { SetPage(key); };
            _tabBoxes.Add(box);
            _tabText[key] = t;
            _tabLine[key] = line;
            return box;
        }

        void SetPage(string key)
        {
            // A re-render of the page we are already on (toggling an option, applying a preset,
            // clearing the cache) must not replay the page-in animation -- that read as a twitch.
            bool samePage = (key == _page) && (_host != null);
            double keepScroll = _pendingScroll > 0.5
                ? _pendingScroll
                : (_scroll == null ? 0.0 : _scroll.VerticalOffset);
            _pendingScroll = 0.0;
            LyricsService.Log("main page -> " + key + (samePage ? " (refresh)" : ""));
            _page = key;
            // Opening the appearance tab from another page lays the rail down once; switching
            // stops inside it must not replay that (the rail is rebuilt, the motion is not).
            if (!samePage && key == "look") _railFresh = true;
            foreach (var kv in _tabText)
            {
                bool on = (kv.Key == key);
                kv.Value.Foreground = new SolidColorBrush(on ? TextMain : TextDim);
                kv.Value.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                // The underline fades rather than pops: the bar stays white and only its opacity
                // changes, so switching tabs reads as one continuous 140 ms move.
                var line = _tabLine[kv.Key];
                line.Fill = new SolidColorBrush(TextMain);
                if (!(line.RenderTransform is ScaleTransform))
                {
                    // The underline grows out of its own centre as the tab takes focus and
                    // shrinks back when it loses it. The bar keeps its full width, so only the
                    // visible span changes and the row never looks like it jumped.
                    line.RenderTransformOrigin = new Point(0.5, 1.0);
                    line.RenderTransform = new ScaleTransform(1.0, 1.0);
                }
                var stx = (ScaleTransform)line.RenderTransform;
                if (samePage)
                {
                    // Re-rendering the page we are already on must not touch the strip: snapping
                    // here keeps the underline exactly where it was instead of re-fading it.
                    line.BeginAnimation(UIElement.OpacityProperty, null);
                    stx.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    line.Opacity = on ? 1.0 : 0.0;
                    stx.ScaleX = on ? 1.0 : 0.55;
                    continue;
                }
                var lo = new DoubleAnimation(on ? 1.0 : 0.0, TimeSpan.FromMilliseconds(on ? 150 : 90));
                lo.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                line.BeginAnimation(UIElement.OpacityProperty, lo);
                var lx = new DoubleAnimation(on ? 1.0 : 0.55, TimeSpan.FromMilliseconds(on ? 200 : 90));
                lx.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                stx.BeginAnimation(ScaleTransform.ScaleXProperty, lx);
            }

            UIElement body;
            if (key == "look") body = LookPage();
            else if (key == "lyrics") body = LyricsPage();
            else if (key == "settings") body = SettingsPage();
            else if (key == "about") body = AboutPage();
            else body = HomePage();
            _sectionCount = 0;
            // The rail belongs to the appearance page alone; the other pages leave the column empty.
            if (_railHost != null)
            {
                // The rail's column is auto-width, so an empty host still costs its own margin
                // and every other page would start 28 px further right than it ends. Collapsing
                // the host takes the whole column away instead.
                if (key != "look") _railHost.Content = null;
                _railHost.Visibility = key == "look" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
            _host.Content = PageFrame(key, body);

            if (samePage)
            {
                // A rail stop switch is a real page change even though the tab stays: give the
                // new stop the same fade-and-rise every other page entrance gets. Refreshes of
                // the page we are already looking at stay still on purpose.
                if (_pageSwitch)
                {
                    _pageSwitch = false;
                    UiMotion.Enter(_host, _kit.MotionSlow, 8);
                }
                // Picking another rail stop opens that stop at its top: the offset on screen
                // belonged to the page we just left and means nothing here.
                if (_scrollTopOnce)
                {
                    _scrollTopOnce = false;
                    if (_scroll != null) Dispatcher.BeginInvoke(new Action(delegate { _scroll.ScrollToVerticalOffset(0); }));
                    return;
                }
                // Content swapped in place: restore where the user was looking instead of jumping
                // back to the top, and skip the fade/lift entirely.
                if (_scroll != null && keepScroll > 0.5)
                {
                    Dispatcher.BeginInvoke(new Action(delegate { _scroll.ScrollToVerticalOffset(keepScroll); }));
                }
                return;
            }

            // Page transition: the kit entrance, so a tab change moves like everything else.
            UiMotion.Enter(_host, _kit.MotionSlow, 8);
        }

        bool _showOthers;
        ScrollViewer _scroll;
        // The appearance page keeps its own section rail in the shell's first column.
        ContentControl _railHost;
        StackPanel _lookBox;
        readonly List<RailRow> _railItems = new List<RailRow>();
        int _railSelected = -1;
        // Set when the appearance page is opened from elsewhere: the rail staggers in once.
        bool _railFresh;
        // Set when a rail stop is picked: the page fades in even though it is the same page.
        bool _pageSwitch;
        string _lookPage = "rail.board";
        double _pendingScroll;
        // Set when a rail stop is picked: the next render of the look page opens at its top
        // instead of keeping the offset of the stop we just left.
        bool _scrollTopOnce;

        // ---- update state (see Updater.cs) ----
        TextBlock _updText;
        Border _updPill;
        UpdateInfo _pending;
        string _updStateText = "";
        bool _updBusy;

        bool IsBoundKey(string key)
        {
            return _cfg.BoundPlayers != null && _cfg.BoundPlayers.Contains(key);
        }

        void SetBound(string key, bool on)
        {
            if (_cfg.BoundPlayers == null) _cfg.BoundPlayers = new System.Collections.Generic.List<string>();
            _cfg.BoundConfigured = true;
            bool has = _cfg.BoundPlayers.Contains(key);
            if (on && !has) _cfg.BoundPlayers.Add(key);
            if (!on && has) _cfg.BoundPlayers.Remove(key);
            _cfg.Save();
            Players.ApplyBindings(_cfg);
            LyricsService.Log("bind " + (on ? "+" : "-") + key + " -> " + _cfg.BoundPlayers.Count);
            RebuildAll();
        }

        static string DisplayBound(string b)
        {
            if (b.StartsWith(Players.AumidPrefix, StringComparison.Ordinal))
                return Players.DisplayName(b.Substring(Players.AumidPrefix.Length));
            if (b.StartsWith(Players.ExePrefix, StringComparison.Ordinal))
                return b.Substring(Players.ExePrefix.Length);
            return b;
        }

        // ---- pages -------------------------------------------------------------------------

        static readonly string[] WheelModes = new string[] { "scale", "volume", "off" };

        /// <summary>Index of the stored wheel mode; unknown values fall back to scaling.</summary>
        int WheelIndex()
        {
            int i = Array.IndexOf(WheelModes, _cfg.WheelAction);
            return i < 0 ? 0 : i;
        }

        UIElement HomePage()
        {
            var box = new StackPanel();

            // language: the same three states the tray/menu expose
            box.Children.Add(Section(Strings.T("page.home.language")));
            box.Children.Add(Segmented(new string[]
                {
                    Strings.T("lang.auto"), Strings.T("lang.zh"), Strings.T("lang.en")
                }, _cfg.Lang, delegate(int idx)
                {
                    _cfg.Lang = idx;
                    _cfg.Save();
                    Strings.Resolve(idx);
                    _card.Relang();
                    RebuildAll();
                }));

            // startup
            box.Children.Add(Section(Strings.T("page.home.startup")));
            box.Children.Add(Row(Strings.T("page.home.autostart"),
                Toggle(AutoStart.IsEnabled(), delegate(bool v)
                {
                    _cfg.AutoStart = v;
                    AutoStart.Set(v);
                    _cfg.Save();
                    RebuildAll();
                })));
            box.Children.Add(Hint(Strings.T("page.home.autostart.hint")));

            // players: only what the user ticked here may drive the card
            box.Children.Add(Section(Strings.T("page.home.players")));

            System.Collections.Generic.List<string> unknown;
            var detected = Players.Detect(SmtcService.SessionAumids, out unknown);
            // The list shows every built-in player, installed or not, so it no longer changes
            // shape behind the user's back (a not-installed entry is dimmed and inert). Only
            // detection decides whether the switch works.
            var seen = new System.Collections.Generic.Dictionary<string, DetectedPlayer>();
            for (int i = 0; i < detected.Count; i++)
                seen[detected[i].Info.Key] = detected[i];
            int shown = 0;
            for (int i = 0; i < Players.Known.Length; i++)
            {
                var info = Players.Known[i];
                DetectedPlayer d;
                seen.TryGetValue(info.Key, out d);
                bool usable = d != null && (d.Installed || d.Running);
                shown++;
                string status = !usable ? Strings.T("page.home.players.missing")
                               : d.Running ? Strings.T("page.home.players.running")
                                           : Strings.T("page.home.players.installed");
                string key = info.Key;
                box.Children.Add(Row(info.Name + "  ·  " + status,
                    Toggle(IsBoundKey(key), delegate(bool v) { SetBound(key, v); }, usable),
                    !usable));
            }
            // custom bindings the user added earlier (aumid: / exe:)
            for (int i = 0; i < _cfg.BoundPlayers.Count; i++)
            {
                string b = _cfg.BoundPlayers[i];
                if (Players.ByKey(b) != null) continue;
                shown++;
                box.Children.Add(Row(DisplayBound(b) + "  ·  " + Strings.T("page.home.players.custom"),
                    Toggle(true, delegate(bool v) { SetBound(b, v); })));
            }
            if (shown == 0) box.Children.Add(Hint(Strings.T("page.home.players.none")));

            var pbtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
            pbtns.Children.Add(Pill(Strings.T("page.home.players.rescan"), delegate
            {
                Players.Refresh();
                RebuildAll();
            }, false));
            var addPill = Pill(_showOthers ? Strings.T("page.home.players.hide")
                                           : Strings.T("page.home.players.add"), delegate
            {
                _showOthers = !_showOthers;
                RebuildAll();
            }, false);
            addPill.Margin = new Thickness(6, 0, 0, 0);
            pbtns.Children.Add(addPill);
            box.Children.Add(pbtns);

            if (_showOthers)
            {
                for (int i = 0; i < unknown.Count; i++)
                {
                    string aumid = unknown[i];
                    string key = Players.AumidPrefix + aumid;
                    box.Children.Add(Row(Players.DisplayName(aumid),
                        Toggle(IsBoundKey(key), delegate(bool v) { SetBound(key, v); })));
                }
                var exeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
                var boxExe = new TextBox
                {
                    Width = 190,
                    Height = 26,
                    FontSize = 12,
                    Padding = new Thickness(6, 3, 6, 3),
                    Foreground = new SolidColorBrush(TextMain),
                    Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 32)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CaretBrush = new SolidColorBrush(TextMain),
                    ToolTip = Strings.T("page.home.players.exe")
                };
                exeRow.Children.Add(boxExe);
                var addExe = Pill("+", delegate
                {
                    string v = (boxExe.Text ?? "").Trim().ToLowerInvariant();
                    if (v.Length == 0) return;
                    if (v.IndexOf('.') < 0) v += ".exe";
                    SetBound(Players.ExePrefix + v, true);
                    boxExe.Text = "";
                }, false);
                addExe.Margin = new Thickness(6, 0, 0, 0);
                exeRow.Children.Add(addExe);
                box.Children.Add(exeRow);
            }

            box.Children.Add(Hint(Strings.T("page.home.players.hint")));

            return box;
        }

        /// <summary>Small grey paragraph used for explanations under a control.</summary>
        TextBlock Hint(string text)
        {
            var t = Label(text, 11, TextFaint, FontWeights.Normal);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 0, 0, 12);
            return t;
        }

        static readonly string[] ClickThroughModes = { "off", "full", "transport" };

        /// <summary>Home page order: off, everything, buttons only.</summary>
        int ClickThroughIndex()
        {
            string m = _cfg.ClickThroughMode;
            for (int i = 0; i < ClickThroughModes.Length; i++) if (ClickThroughModes[i] == m) return i;
            return 0;
        }

        /// <summary>Segmented control: one row of pills, exactly one selected.</summary>
        static readonly ModuleAlign[] AlignValues = new ModuleAlign[]
            { ModuleAlign.Left, ModuleAlign.Center, ModuleAlign.Right };

        int AlignIndex(ModuleKind kind)
        {
            CardConfig m = _card.Model;
            ModuleConfig mc = m == null ? null : m.Find(kind);
            if (mc == null) return 0;
            int i = Array.IndexOf(AlignValues, mc.Align);
            return i < 0 ? 0 : i;
        }

        /// <summary>Which preset matches the stored stops; -1 means "custom".</summary>
        int TintPresetIndex()
        {
            if (_cfg.ArtTintDark == "#3A0A0A" && _cfg.ArtTintLight == "#FFB4B4") return 0;
            if (_cfg.ArtTintDark == "#061826" && _cfg.ArtTintLight == "#A9D6FF") return 1;
            if (_cfg.ArtTintDark == "#1A0A2E" && _cfg.ArtTintLight == "#D9B8FF") return 2;
            return 0;
        }

        void SetModuleAlign(ModuleKind kind, int idx)
        {
            if (idx < 0 || idx >= AlignValues.Length) return;
            CardConfig m = _card.Model;
            ModuleConfig mc = m == null ? null : m.Find(kind);
            if (mc == null) return;
            mc.Align = AlignValues[idx];
            // The template is the truth, so write it straight away (no draft round-trip) and let the
            // card re-apply alignment on the next repaint.
            m.Save(CardConfig.DefaultPath);
            _card.InvalidateModel();
            _card.ApplyLook();
            // Repaint the page: a hand-patched pill kept its old text colour, so let the builder draw
            // the row again. RebuildAll keeps the scroll offset, so the user stays where they were.
            RebuildAll();
        }

        /// <summary>Character cap straight from the template (the steppers used to read a draft that
        /// could be null, so a saved cap showed as 0 and looked like the setting did nothing).</summary>
        bool SameLine()
        {
            CardConfig m = _card.Model;
            return m != null && m.TitleArtistInline;
        }

        int CapOf(ModuleKind kind)
        {
            CardConfig m = _card.Model;
            ModuleConfig mc = m == null ? null : m.Find(kind);
            return mc == null ? 0 : mc.MaxChars;
        }


        /// <summary>
        /// Push the bars' geometry into the template. Either dimension can be left alone (-1), and a
        /// missing module is created enabled at the configured size so "length" works from cold.
        /// </summary>
        void BarsModule(double w, double h)
        {
            CardConfig m = _card.Model;
            if (m == null) return;
            ModuleConfig mc = m.Find(ModuleKind.MusicBars);
            if (mc == null)
            {
                mc = new ModuleConfig(ModuleKind.MusicBars);
                mc.Enabled = true;
                mc.W = _cfg.BarsLength;
                mc.H = _cfg.BarsMaxHeight;
                mc.X = 16;
                mc.Y = 100;
                mc.ZIndex = 6;
                m.Modules.Add(mc);
            }
            if (w > 0) mc.W = w;
            if (h > 0) mc.H = h;
            mc.Enabled = true;
            _card.InvalidateModel();
            _card.SaveModel();
            _card.ApplyLook();
            // the board draws the same template, so let it re-read the box we just resized
            if (_preview != null) _preview.RefreshFromCard();
        }

        /// <summary>
        /// The transport row's size is the Controls module's box: 30 units tall is the design size,
        /// so the module height *is* the button hit box. Width is derived from it so the box keeps
        /// hugging the three buttons -- 96 units at 30 plus the 6 units of slack the shipped
        /// template had, so 30 reproduces today's box exactly.
        /// </summary>
        double ControlsSizeOf()
        {
            CardConfig m = _card.Model;
            ModuleConfig mc = m == null ? null : m.Find(ModuleKind.Controls);
            return mc == null || mc.H <= 1 ? 30.0 : mc.H;
        }

        void ControlsModule(double h)
        {
            CardConfig m = _card.Model;
            if (m == null) return;
            ModuleConfig mc = m.Find(ModuleKind.Controls);
            if (mc == null)
            {
                mc = new ModuleConfig(ModuleKind.Controls);
                mc.X = 16;
                mc.Y = 100;
                mc.ZIndex = 5;
                m.Modules.Add(mc);
            }
            mc.Enabled = true;
            // grow around the box's centre, so a bigger button row stays under the text column
            // instead of sliding right into the cover
            double cx = mc.X + mc.W / 2.0;
            mc.H = h;
            mc.W = Math.Round(h * 3.2) + 6.0;
            mc.X = Math.Max(0.0, Math.Round(cx - mc.W / 2.0));
            _card.InvalidateModel();
            _card.SaveModel();
            _card.ApplyLook();
            // the board draws the same template: resize its copy in place so an unapplied drag
            // survives the stepper (RefreshFromCard would start the draft over from the card)
            if (_preview != null) _preview.ResizeModule(ModuleKind.Controls, mc.X, mc.Y, mc.W, mc.H);
        }

        void SetCap(ModuleKind kind, int chars)
        {
            if (chars < 0) chars = 0;
            CardConfig m = _card.Model;
            ModuleConfig mc = m == null ? null : m.Find(kind);
            if (mc == null) return;
            mc.MaxChars = chars;
            if (chars == 0) mc.W = -1;          // no cap: go back to the automatic width
            m.Save(CardConfig.DefaultPath);
            _card.InvalidateModel();
            // Template-size controls live on 画板: refresh the draft first so the page rebuild does
            // not carry the pre-change boxes over.
            if (_preview != null) _preview.RefreshFromCard();
            RebuildAll();
        }

        UIElement Segmented(string[] labels, int selected, Action<int> pick)
        {
            return UiKit.Segmented(_kit, labels, selected, pick);
        }

        /// <summary>Repaint a segmented row so the chosen pill is the lit one.</summary>
        static void SelectInRow(Panel row, int selected)
        {
            if (row == null) return;
            for (int i = 0; i < row.Children.Count; i++)
            {
                Border b = row.Children[i] as Border;
                if (b == null) continue;
                bool on = (i == selected);
                SolidColorBrush sb = b.Background as SolidColorBrush;
                if (sb != null)
                {
                    sb.Color = on ? Color.FromArgb(255, 236, 238, 242)
                                  : Color.FromArgb(20, 255, 255, 255);
                }
                TextBlock tb = b.Child as TextBlock;
                if (tb != null)
                {
                    tb.Foreground = on ? (Brush)new SolidColorBrush(Color.FromRgb(20, 20, 22))
                                       : new SolidColorBrush(TextDim);
                    tb.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }
        }

        /// <summary>Re-read every string (language switch) and rebuild the whole window.</summary>
        public void RebuildAll()
        {
            // The ScrollViewer dies with the old Content, so capture the offset first and let
            // SetPage put the freshly built page back at the same place (and skip the transition).
            _pendingScroll = _scroll == null ? 0.0 : _scroll.VerticalOffset;
            if (_railHost != null) _railHost.Content = null;
            Title = Strings.T("app.title");
            Content = BuildUi();
            SetPage(_page);
        }

        CardPreview _preview;

        /// <summary>Unsaved board edits, kept alive while the look page is rebuilt (stop / tab switches).</summary>
        CardConfig _boardDraft;

        /// <summary>A click on a board module: logged for now, the editing tools arrive in Step 8/9.</summary>
        void OnBoardModuleClicked(ModuleKind kind, FrameworkElement el)
        {
            LyricsService.Log("board hit " + kind + " tag=" + (el == null ? "<null>" : (el.Tag == null ? "?" : el.Tag.ToString())));
        }

        /// <summary>Preview follows the card through its event; nothing here runs on a timer.</summary>
        void OnCardLayoutChanged()
        {
            // The board is a sketch pad with its own draft: it deliberately ignores content ticks.
        }

        void OnCardLookChanged()
        {
            // Font size / cover size changed: the boxes in the board have to grow or shrink with it,
            // otherwise the frame you drag is not the box the card will draw.
            if (_preview != null) _preview.RefreshMetrics();
        }

        /// <summary>
        /// Behaviour switches that are not about how the card looks: how the mouse reaches it, what
        /// the wheel does, and whether a fullscreen window pushes it aside. Moved off the home page
        /// so that page is about the app talking to players, not about tinkering.
        /// </summary>
        UIElement SettingsPage()
        {
            var box = new StackPanel();

            // click-through: how the mouse reaches the card. Turning it on makes the card
            // transparent to the mouse; the tray menu and this page stay clickable to turn it
            // back off.
            box.Children.Add(Section(Strings.T("page.settings.clickthrough")));
            box.Children.Add(Segmented(new string[] { Strings.T("ct.off"), Strings.T("ct.full"), Strings.T("ct.transport") },
                ClickThroughIndex(), delegate(int i)
                {
                    _card.SetClickThrough(ClickThroughModes[i]);
                    RebuildAll();
                }));
            box.Children.Add(Hint(Strings.T("ct.hint")));

            // The wheel is the card's only silent control: without a switch nobody discovers that
            // it scales, and people who expect it to change volume think it is broken.
            box.Children.Add(Section(Strings.T("page.settings.wheel")));
            box.Children.Add(Segmented(new string[]
                {
                    Strings.T("wheel.scale"), Strings.T("wheel.volume"), Strings.T("wheel.off")
                }, WheelIndex(), delegate(int idx)
                {
                    _cfg.WheelAction = WheelModes[idx];
                    _cfg.Save();
                    LyricsService.Log("wheel -> " + _cfg.WheelAction);
                    RebuildAll();
                }));
            box.Children.Add(Hint(Strings.T("page.settings.wheel.hint")));

            box.Children.Add(Section(Strings.T("page.settings.fullscreen")));
            box.Children.Add(Row(Strings.T("lbl.hideFullscreen"),
                Toggle(_cfg.HideFullscreen, delegate(bool v)
                {
                    _cfg.HideFullscreen = v;
                    _card.HideFullscreen = v;
                    _cfg.Save();
                    RebuildAll();
                })));
            box.Children.Add(Hint(Strings.T("page.settings.fullscreen.hint")));

            return box;
        }

        /// <summary>True when the given rail stop is the page being built.</summary>
        bool On(string key) { return _lookPage == key; }

        UIElement LookPage()
        {
            // The board is a draft, not a mirror, and every stop rebuilds this page: create it
            // here, then let BoardStop draw it only while 画板 is the current stop. 预设 still
            // touches it -- applying a preset refreshes the board through _preview.
            // The board is a draft, not a mirror, and every stop rebuilds this page: carry the
            // working draft across the rebuild so switching stops does not bin what was dragged.
            // 应用 / 取消 / 刷新 / 模板尺寸 and presets clear it explicitly.
            if (_preview != null)
            {
                _boardDraft = _preview.DraftSnapshot();
                _card.LayoutChanged -= OnCardLayoutChanged;
                _card.LookChanged -= OnCardLookChanged;
            }
            _preview = new CardPreview();
            _preview.FitWidth = 430;
            _preview.ApplyRequested += delegate
            {
                bool saved = _card.SaveModel();
                _boardDraft = null;
                LyricsService.Log("board apply -> layout.ini saved=" + (saved ? "1" : "0"));
                RebuildAll();
            };
            _preview.CancelRequested += delegate { _boardDraft = null; RebuildAll(); };

            // AMC_APPLY_PRESET=<n> applies preset n once at start-up and logs what it did. Preset
            // chips live at the bottom of a scrolled page, which makes them the one control that is
            // awkward to drive from a script; this hook keeps them testable.
            string presetEnv = Environment.GetEnvironmentVariable("AMC_APPLY_PRESET");
            int presetIdx;
            if (presetEnv != null && int.TryParse(presetEnv, out presetIdx) && presetIdx >= 0 && presetIdx < _cfg.Presets.Count)
            {
                Config.LookPreset target = _cfg.Presets[presetIdx];
                Dispatcher.BeginInvoke(new Action(delegate { ApplyPreset(target); }), System.Windows.Threading.DispatcherPriority.Background);
            }

            _card.LookChanged += OnCardLookChanged;
            _preview.BeginDraft(_card, _boardDraft);

            // One stop at a time: only the current stop's rows are built into the page. Building
            // all ten and slicing the finished panel is what broke the card grouping before.
            var box = new StackPanel();
            if (On("rail.board")) BoardStop(box);
            else if (On("rail.card")) CardStop(box);
            else if (On("rail.title")) TitleStop(box);
            else if (On("rail.artist")) ArtistStop(box);
            else if (On("rail.lyrics")) LyricStop(box);
            else if (On("sec.bar")) BarStop(box);
            else if (On("rail.buttons")) ButtonStop(box);
            else if (On("sec.spectrum")) SpectrumStop(box);
            else if (On("rail.art")) ArtStop(box);
            else PresetStop(box);

            _lookBox = box;
            BuildRail();
            return box;
        }

        // A preset used to be "how the card looks": colours, sizes, cover treatment. Since the
        // template became the only description of where things sit, it also has to carry the
        // template, otherwise applying a preset leaves the layout behind and the chip looks broken.
        static string ReadTemplateText()
        {
            try
            {
                if (!System.IO.File.Exists(CardConfig.DefaultPath)) return null;
                return System.IO.File.ReadAllText(CardConfig.DefaultPath, System.Text.Encoding.UTF8);
            }
            catch { return null; }
        }

        void ApplyPreset(Config.LookPreset p)
        {
            if (p == null) return;
            string template = null;
            foreach (string pair in (p.Data ?? "").Split(';'))
            {
                if (!pair.StartsWith("Template=")) continue;
                try { template = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pair.Substring(9))); }
                catch { template = null; }
            }
            _cfg.ApplyLookPreset(p);
            _cfg.Save();
            // The template has to land before the look is re-applied: alignment, cover shape and
            // module caps all read the model, so applying the look against the previous template
            // made the first click look half-applied and the second click finish the job.
            if (template != null)
            {
                try { System.IO.File.WriteAllText(CardConfig.DefaultPath, template, System.Text.Encoding.UTF8); }
                catch { }
                _card.ReloadModel();
            }
            _card.ApplyLook();
            _card.ApplyCurrentScale();
            _card.ApplyCurrentOpacity();
            if (template != null) _preview.RefreshFromCard();
            LyricsService.Log("preset apply name=" + p.Name + " template=" + (template != null));
            SetPage("look");
        }

        Border PresetChip(Config.LookPreset p)
        {
            var kc = UiKit.Chip(_kit, p.Name, delegate
            {
                _cfg.Presets.Remove(p);
                _cfg.Save();
                SetPage("look");
            });
            // Chip's delete callback fires on the cross only, so the chip body is free to apply.
            kc.MouseLeftButtonUp += delegate { ApplyPreset(p); };
            kc.Margin = new Thickness(0, 0, 6, 6);
            return kc;
        }

        UIElement LyricsPage()
        {
            var box = new StackPanel();
            box.Children.Add(Section(Strings.T("page.lyr.title")));

            box.Children.Add(Body(Strings.T("page.lyr.order"), 12));

            // Manual sync nudges. The card menu only offers 0.1 s steps and is easy to miss, so
            // the same operation is exposed here with a coarser 0.5 s pair for badly timed files.
            box.Children.Add(Section(Strings.T("page.lyr.timing")));
            var offsetText = Body(Strings.F("lyr.offset", _cfg.LyricOffset.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)), 12);
            Action<double> nudge = delegate(double d)
            {
                try { _card.NudgeOffset(d); } catch { }
                offsetText.Text = Strings.F("lyr.offset", _cfg.LyricOffset.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            };
            var timingRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            // The line is picked by `t = position + offset`, so a *larger* offset shows the text
            // sooner. These two rows had the signs the wrong way round: pressing 提前 pushed the
            // lyric later, which is exactly "the buttons do nothing" from the user's side.
            timingRow.Children.Add(Pill(Strings.T("lyr.early05"), delegate { nudge(0.5); }, false));
            timingRow.Children.Add(Pill(Strings.T("lyr.early01"), delegate { nudge(0.1); }, false));
            timingRow.Children.Add(Pill(Strings.T("lyr.late01"), delegate { nudge(-0.1); }, false));
            timingRow.Children.Add(Pill(Strings.T("lyr.late05"), delegate { nudge(-0.5); }, false));
            box.Children.Add(timingRow);
            box.Children.Add(offsetText);

            // cache stats: count the .lrc files and add up their bytes
            int count = 0;
            long bytes = 0;
            try
            {
                string dir = LyricsService.CacheDir;
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, "*.lrc"))
                    {
                        count++;
                        try { bytes += new FileInfo(f).Length; } catch { }
                    }
                }
            }
            catch { }

            box.Children.Add(Section(Strings.T("page.lyr.cache")));
            var stats = Body(count + " " + Strings.T("page.lyr.cacheval") + " " + (bytes / 1024.0).ToString("0") + " KB", 12);
            box.Children.Add(stats);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            buttons.Children.Add(Pill(Strings.T("page.lyr.export"), delegate
            {
                int w, e, s;
                LyricsService.ExportToLocal(out w, out e, out s);
                SetPage("lyrics");        // refresh the counters
            }, true));
            buttons.Children.Add(Pill(Strings.T("page.lyr.opendir"), delegate
            {
                try { Process.Start("explorer.exe", "\"" + Config.Dir + "\""); } catch { }
            }, false));
            // Same picker as the card's context menu, surfaced here because the menu is a
            // right-click away and easy to miss when the lyrics are simply wrong.
            buttons.Children.Add(Pill(Strings.T("page.lyr.pick"), delegate
            {
                try { _card.OpenCandidates(); } catch { }
            }, false));
            box.Children.Add(buttons);

            box.Children.Add(Section(Strings.T("page.lyr.autoclean")));
            box.Children.Add(Row(Strings.T("page.lyr.autoclean.on"), Toggle(_cfg.AutoCleanCache, delegate(bool on)
            {
                _cfg.AutoCleanCache = on;
                _cfg.Save();
                // Run immediately when the user flips it on, so the interval starts from today.
                if (on) { try { LyricsService.AutoCleanIfDue(_cfg); } catch { } }
                SetPage("lyrics");
            })));
            box.Children.Add(Row(Strings.T("page.lyr.autoclean.days"), Stepper(_cfg.CacheCleanDays, 1, 90, 1, "0", delegate(double v)
            {
                _cfg.CacheCleanDays = (int)Math.Round(v);
                _cfg.Save();
            })));
            string lastText = string.IsNullOrEmpty(_cfg.LastCacheClean)
                ? Strings.T("page.lyr.autoclean.never")
                : _cfg.LastCacheClean;
            box.Children.Add(Body(Strings.F("page.lyr.autoclean.last", lastText), 12));
            box.Children.Add(Body(Strings.T("page.lyr.autoclean.hint"), 11));

            var clear = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            clear.Children.Add(Pill(Strings.T("page.lyr.clearcache"), delegate
            {
                var r = MessageBox.Show(this, Strings.T("page.lyr.confirm"), "Lyricard",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (r != MessageBoxResult.OK) return;
                LyricsService.ClearCache();
                _cfg.LastCacheClean = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                _cfg.Save();
                SetPage("lyrics");
            }, false));
            box.Children.Add(clear);
            return box;
        }

        void UpdateStatus(string text)
        {
            _updStateText = text;
            if (_updText != null) _updText.Text = text;
        }

        /// <summary>One click: check, then download+install once a release is known.</summary>
        void UpdateButton()
        {
            if (_updBusy) return;
            if (_pending != null)
            {
                StartDownload();
                return;
            }
            _updBusy = true;
            UpdateStatus(Strings.T("upd.checking"));
            var th = new System.Threading.Thread(delegate()
            {
                UpdateInfo info = null;
                string err = null;
                try { info = Updater.Check(); }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    _updBusy = false;
                    if (err != null) { UpdateStatus(Strings.F("upd.fail", err)); return; }
                    if (info == null) { UpdateStatus(Strings.F("upd.latest", Strings.T("app.version"))); return; }
                    _pending = info;
                    UpdateStatus(Strings.F("upd.found", info.Version.ToString(),
                        (info.Size / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    SetPage("about");
                }));
            });
            th.IsBackground = true;
            th.Start();
        }

        void StartDownload()
        {
            UpdateInfo info = _pending;
            if (info == null) return;
            _updBusy = true;
            UpdateStatus(Strings.F("upd.downloading", "0"));
            var th = new System.Threading.Thread(delegate()
            {
                string path = null;
                string err = null;
                try
                {
                    path = Updater.DownloadExe(info, delegate(int done, int total)
                    {
                        int pct = (total > 0) ? (int)(done * 100L / total) : 0;
                        Dispatcher.BeginInvoke(new Action(delegate()
                        {
                            UpdateStatus(Strings.F("upd.downloading", pct.ToString(
                                System.Globalization.CultureInfo.InvariantCulture)));
                        }));
                    });
                }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    _updBusy = false;
                    if (err != null || path == null)
                    {
                        UpdateStatus(Strings.F("upd.fail", err ?? "download failed"));
                        return;
                    }
                    UpdateStatus(Strings.T("upd.ready"));
                    Updater.ApplyAndRestart(path);
                }));
            });
            th.IsBackground = true;
            th.Start();
        }

        /// <summary>
        /// The silent startup pass: one request, no UI unless the About page is open later. Skipped
        /// versions stay quiet until a newer one appears.
        /// </summary>
        public void SilentUpdateCheck()
        {
            if (!_cfg.AutoCheckUpdate) return;
            var th = new System.Threading.Thread(delegate()
            {
                UpdateInfo info = null;
                try { info = Updater.Check(); } catch (Exception ex) { LyricsService.Log("update check failed: " + ex.Message); }
                if (info == null) return;
                if (info.Version.ToString() == _cfg.SkippedVersion) return;
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    _pending = info;
                    _updStateText = Strings.F("upd.found", info.Version.ToString(),
                        (info.Size / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (_updText != null) _updText.Text = _updStateText;
                    LyricsService.Log("update available " + info.Version + " (" + info.AssetName + ")");
                }));
            });
            th.IsBackground = true;
            th.Start();
        }

        UIElement AboutPage()
        {
            var box = new StackPanel();
            var name = Label("Lyricard " + Strings.T("app.version"), 18, TextMain, FontWeights.SemiBold);
            name.Margin = new Thickness(0, 4, 0, 6);
            box.Children.Add(name);

            box.Children.Add(Body(Strings.T("page.about.blurb"), 12));
            box.Children.Add(Section(Strings.T("page.about.license")));
            box.Children.Add(Body(Strings.T("page.about.credits"), 11.5));
            box.Children.Add(Section(Strings.T("sec.entry")));
            box.Children.Add(Body(Strings.T("page.about.shortcuts"), 11.5));

            box.Children.Add(Section(Strings.T("sec.update")));
            if (_pending == null) _pending = Updater.Pending;
            if (_pending != null && string.IsNullOrEmpty(_updStateText))
            {
                _updStateText = Strings.F("upd.found", _pending.Version.ToString(),
                    (_pending.Size / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            _updPill = Pill(_pending == null ? Strings.T("upd.check") : Strings.T("upd.download"),
                delegate { UpdateButton(); }, true);
            row.Children.Add(_updPill);
            if (_pending != null)
            {
                row.Children.Add(Pill(Strings.T("upd.skip"), delegate
                {
                    _cfg.SkippedVersion = _pending.Version.ToString();
                    _cfg.Save();
                    _pending = null;
                    _updStateText = Strings.F("upd.skipped", _cfg.SkippedVersion);
                    SetPage("about");
                }, false));
            }
            box.Children.Add(row);

            _updText = Body(_updStateText, 11.5);
            _updText.Margin = new Thickness(0, 8, 0, 2);
            box.Children.Add(_updText);

            box.Children.Add(Row(Strings.T("upd.auto"), Toggle(_cfg.AutoCheckUpdate, delegate(bool on)
            {
                _cfg.AutoCheckUpdate = on;
                _cfg.Save();
            })));

            // Uninstall lives down here, below everything else, because it is the one action in
            // this panel that cannot be undone by toggling it back.
            box.Children.Add(Section(Strings.T("sec.uninstall")));
            box.Children.Add(Body(Strings.T("page.about.uninstall"), 11.5));
            var unRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
            unRow.Children.Add(Pill(Strings.T("about.uninstall"), delegate { Uninstall.Run(true, true); }, false));
            box.Children.Add(unRow);
            return box;
        }

        /// <summary>Minimise / close glyph drawn as strokes, matching the card's flat style.</summary>
        UIElement Chrome(string kind, Action act)
        {
            var host = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Margin = new Thickness(2, 0, 0, 0)
            };
            var canvas = new Canvas { Width = 30, Height = 30 };

            if (kind == "min")
            {
                var l = new Line { X1 = 10, Y1 = 15, X2 = 20, Y2 = 15, Stroke = new SolidColorBrush(TextDim), StrokeThickness = 1.3 };
                l.StrokeStartLineCap = PenLineCap.Round;
                l.StrokeEndLineCap = PenLineCap.Round;
                canvas.Children.Add(l);
            }
            else
            {
                canvas.Children.Add(new Line { X1 = 11, Y1 = 11, X2 = 19, Y2 = 19, Stroke = new SolidColorBrush(TextDim), StrokeThickness = 1.3 });
                canvas.Children.Add(new Line { X1 = 19, Y1 = 11, X2 = 11, Y2 = 19, Stroke = new SolidColorBrush(TextDim), StrokeThickness = 1.3 });
            }

            host.Child = canvas;
            // Kit motion: a fast wash on the way in, a slower one going out, and the glyph
            // brightens with it so the button reads as one surface.
            var chromeBg = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            host.Background = chromeBg;
            var glyphInk = new SolidColorBrush(TextDim);
            foreach (var child in canvas.Children)
            {
                var ln = child as Line;
                if (ln != null) ln.Stroke = glyphInk;
            }
            host.MouseEnter += delegate
            {
                UiMotion.Color(chromeBg, Color.FromArgb(26, 255, 255, 255), _kit.MotionFast);
                UiMotion.Color(glyphInk, TextMain, _kit.MotionFast);
            };
            host.MouseLeave += delegate
            {
                UiMotion.Color(chromeBg, Color.FromArgb(0, 255, 255, 255), _kit.MotionBase);
                UiMotion.Color(glyphInk, TextDim, _kit.MotionBase);
            };
            var chromeScale = new ScaleTransform(1, 1);
            host.RenderTransformOrigin = new Point(0.5, 0.5);
            host.RenderTransform = chromeScale;
            host.PreviewMouseLeftButtonDown += delegate { UiMotion.Scale(chromeScale, 0.92, 90); };
            host.PreviewMouseLeftButtonUp += delegate { UiMotion.Scale(chromeScale, 1.0, 200); };
            host.MouseLeftButtonUp += delegate
            {
                // Minimising or hiding swallows the mouse-up that would undo the press dim,
                // so put it back before acting -- otherwise the button stays at 0.86.
                UiMotion.Opacity(host, 1.0, _kit.MotionFast);
                act();
            };
            return host;
        }
    }
}
