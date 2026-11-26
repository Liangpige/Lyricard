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
    public class MainWindow : Window
    {
        public const double WinW = 640;
        public const double WinH = 480;
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
        /// Opens the window straight on one page ("home" / "look" / "lyrics" / "about").
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
            return new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                Margin = new Thickness(0, 16, 0, 12)
            };
        }

        UIElement Section(string text)
        {
            var t = Label(text, 11, TextFaint, FontWeights.Normal);
            t.Margin = new Thickness(0, 12, 0, 6);
            if (_sectionCount++ == 0) return t;
            var box = new StackPanel();
            box.Children.Add(new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                Margin = new Thickness(0, 14, 0, 0)
            });
            box.Children.Add(t);
            return box;
        }

        /// <summary>Page shell: big title, then the content. Gives every page a top edge.</summary>
        UIElement PageFrame(string key, UIElement body)
        {
            var box = new StackPanel();
            string navKey = key == "look" ? "nav.appearance" : "nav." + key;
            var title = Label(Strings.T(navKey), 17, TextMain, FontWeights.SemiBold);
            title.Margin = new Thickness(0, 0, 0, 12);
            box.Children.Add(title);
            box.Children.Add(body);
            return box;
        }

        /// <summary>Label above its control, matching the settings panel's stacked layout.</summary>
        FrameworkElement Row(string label, UIElement control)
        {
            var box = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
            var l = Label(label, 11.5, TextDim, FontWeights.Normal);
            l.Margin = new Thickness(0, 0, 0, 3);
            box.Children.Add(l);
            if (control is FrameworkElement) ((FrameworkElement)control).HorizontalAlignment = HorizontalAlignment.Left;
            box.Children.Add(control);
            return box;
        }

        /// <summary>Thin dark scrollbar used by the page body (see BuildUi).</summary>
        const string ThinScrollBarXaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='ScrollBar'>
  <Setter Property='Width' Value='10'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border CornerRadius='5' Margin='1,0' Background='#59FFFFFF'/>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

        // palette shared by every colour row: neutral first, then accents
        static readonly string[] Swatches = new string[]
        {
            "", "#FFFFFFFF", "#FFC9CCD2", "#FF8D939C", "#FF4A4F57",
            "#FFE5484D", "#FFFFB224", "#FF3FB950", "#FF3B82F6"
        };

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
            var dots = new WrapPanel();
            string sel = current ?? "";
            foreach (string hex in Swatches)
            {
                string value = hex;
                bool chosen = string.Equals(value ?? "", sel ?? "", StringComparison.OrdinalIgnoreCase);
                Color c;
                Border dot = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(9),
                    Margin = new Thickness(0, 0, 6, 0),
                    BorderThickness = new Thickness(chosen ? 2 : 1),
                    BorderBrush = new SolidColorBrush(chosen
                        ? Color.FromArgb(255, 236, 238, 242)
                        : Color.FromArgb(40, 255, 255, 255)),
                    Background = string.IsNullOrEmpty(value)
                        ? new SolidColorBrush(Color.FromArgb(18, 255, 255, 255))
                        : new SolidColorBrush(TryHex(value, out c) ? c : Colors.White),
                    Tag = value
                };
                dot.Cursor = Cursors.Hand;
                if (string.IsNullOrEmpty(value))
                {
                    dot.Child = Label("\u00d7", 11, TextDim, FontWeights.SemiBold);
                    ((TextBlock)dot.Child).HorizontalAlignment = HorizontalAlignment.Center;
                    ((TextBlock)dot.Child).VerticalAlignment = VerticalAlignment.Center;
                }
                dot.MouseLeftButtonUp += delegate
                {
                    sel = value ?? "";
                    apply(sel);
                    RepaintDots(dots, sel);
                };
                dots.Children.Add(dot);
            }

            var more = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Cursor = Cursors.Hand,
                Child = Label("\u2026", 11, TextDim, FontWeights.SemiBold)
            };
            ((TextBlock)more.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)more.Child).VerticalAlignment = VerticalAlignment.Center;
            more.MouseLeftButtonUp += delegate
            {
                ColorLibraryWindow.Open(label, sel, delegate(string v)
                {
                    sel = v ?? "";
                    apply(sel);
                    RepaintDots(dots, sel);
                });
            };
            dots.Children.Add(more);

            return Row(label, dots);
        }

        /// <summary>− value + stepper; every look value in this app uses the same one.</summary>
        UIElement Stepper(double value, double min, double max, double step, string fmt, Action<double> apply)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var val = Label(value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture), 12, TextDim, FontWeights.Normal);
            val.Width = 58;
            val.TextAlignment = TextAlignment.Center;
            double cur = value;

            Action<double> set = delegate(double v)
            {
                if (v < min) v = min;
                if (v > max) v = max;
                cur = Math.Round(v, 4);
                val.Text = cur.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
                apply(cur);
            };
            row.Children.Add(ChromeText("\u2212", delegate { set(cur - step); }));
            row.Children.Add(val);
            row.Children.Add(ChromeText("+", delegate { set(cur + step); }));
            return row;
        }

        Border ChromeText(string text, Action act)
        {
            var b = new Border
            {
                Width = 28,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Child = Label(text, 13, TextMain, FontWeights.Normal)
            };
            ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
            HoverFill(b, 20, 36);
            PressScale(b);
            b.MouseLeftButtonUp += delegate { act(); };
            return b;
        }

        Border Pill(string text, Action act, bool primary)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(primary
                    ? Color.FromArgb(255, 236, 238, 242)
                    : Color.FromArgb(22, 255, 255, 255)),
                Child = Label(text, 12, primary ? Color.FromRgb(18, 18, 20) : TextMain, FontWeights.SemiBold)
            };
            b.Cursor = Cursors.Hand;
            var brush = (SolidColorBrush)b.Background;
            Color rest = brush.Color;
            Color hot = primary ? Color.FromRgb(255, 255, 255) : Color.FromArgb(34, 255, 255, 255);
            b.MouseEnter += delegate { AnimateColor(brush, hot, 120); };
            b.MouseLeave += delegate { AnimateColor(brush, rest, 170); };
            PressScale(b);
            b.MouseLeftButtonUp += delegate { try { act(); } catch { } };
            return b;
        }

        /// <summary>A 42x22 switch, same shape as the settings panel uses.</summary>
        UIElement Toggle(bool on, Action<bool> apply)
        {
            Color onBg = Color.FromArgb(255, 236, 238, 242);
            Color offBg = Color.FromArgb(34, 255, 255, 255);
            var track = new Border
            {
                Width = 42,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(on ? onBg : offBg)
            };
            var knob = new Ellipse
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0),
                Fill = new SolidColorBrush(on ? Color.FromRgb(18, 18, 20) : Color.FromArgb(220, 236, 238, 242))
            };
            var tr = new TranslateTransform(on ? 20.0 : 0.0, 0.0);
            // Scale BEFORE translate: with the translate first the 0.94 press scale also
            // scaled the 20 px travel, so the knob drifted ~1 px left while held.
            var knobScale = new ScaleTransform(1.0, 1.0);
            var knobTf = new TransformGroup();
            knobTf.Children.Add(knobScale);
            knobTf.Children.Add(tr);
            knob.RenderTransformOrigin = new Point(0.5, 0.5);
            knob.RenderTransform = knobTf;
            track.Child = knob;
            // The knob dips while the switch is held, exactly like iOS.
            track.PreviewMouseLeftButtonDown += delegate { AnimateScale(knobScale, 0.94, 90); };
            Action release = delegate { AnimateScale(knobScale, 1.0, 160); };
            track.PreviewMouseLeftButtonUp += delegate { release(); };
            track.MouseLeave += delegate { release(); };
            bool state = on;
            track.MouseLeftButtonUp += delegate
            {
                state = !state;
                // The knob slides and the track cross-fades: 160 ms, ease-out, no bounce. A switch
                // that snaps feels broken next to the rest of the UI.
                var sx = new DoubleAnimation(state ? 20.0 : 0.0, TimeSpan.FromMilliseconds(180));
                sx.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                tr.BeginAnimation(TranslateTransform.XProperty, sx);
                AnimateColor((SolidColorBrush)track.Background, state ? onBg : offBg, 180);
                AnimateColor((SolidColorBrush)knob.Fill, state ? Color.FromRgb(18, 18, 20) : Color.FromArgb(220, 236, 238, 242), 180);
                try { apply(state); } catch { }
            };
            return track;
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
            var body = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(24, 20, 24, 20),
                Background = new SolidColorBrush(Bg),
                Content = _host
            };
            // A thin, self-drawn scrollbar: the stock one is a light Win32-ish bar that
            // fights the dark shell. Declared as XAML because a ScrollBar template in code
            // is a page of boilerplate.
            try { body.Resources.Add(typeof(ScrollBar), (Style)XamlReader.Parse(ThinScrollBarXaml)); }
            catch { }
            Grid.SetRow(body, 1);
            root.Children.Add(body);
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
                Fill = Brushes.Transparent
            };
            inner.Children.Add(line);
            box.Height = 34;
            // The active tab already carries the underline, so it does not light up on
            // hover -- only the inactive ones do.
            var hoverBrush = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            box.Background = hoverBrush;
            box.MouseEnter += delegate
            {
                AnimateColor(hoverBrush, Color.FromArgb((byte)(key == _page ? 0 : 18), 255, 255, 255), 120);
                // The word lifts towards full white too: a background wash alone reads as a
                // tooltip, the text shift is what makes the strip feel alive.
                AnimateColor((SolidColorBrush)t.Foreground, TextMain, 120);
            };
            box.MouseLeave += delegate
            {
                AnimateColor(hoverBrush, Color.FromArgb(0, 255, 255, 255), 170);
                AnimateColor((SolidColorBrush)t.Foreground, key == _page ? TextMain : TextDim, 170);
            };

            box.MouseLeftButtonUp += delegate { SetPage(key); };
            _tabBoxes.Add(box);
            _tabText[key] = t;
            _tabLine[key] = line;
            return box;
        }

        /// <summary>Fades a border's fill instead of snapping it; Apple-style 120~160 ms.</summary>
        static void HoverFill(Border b, int restAlpha, int hotAlpha)
        {
            var br = new SolidColorBrush(Color.FromArgb((byte)restAlpha, 255, 255, 255));
            b.Background = br;
            b.MouseEnter += delegate { AnimateColor(br, Color.FromArgb((byte)hotAlpha, 255, 255, 255), 120); };
            b.MouseLeave += delegate { AnimateColor(br, Color.FromArgb((byte)restAlpha, 255, 255, 255), 170); };
        }

        static void AnimateColor(SolidColorBrush br, Color to, int ms)
        {
            var a = new ColorAnimation(to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            br.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }

        /// <summary>Press feedback: the target dips to 97 % and springs back. 90 ms down, 160 ms up.</summary>
        static void PressScale(UIElement el)
        {
            var st = new ScaleTransform(1, 1);
            el.RenderTransform = st;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            bool down = false;
            // Held buttons also dim slightly: 0.97 scale is invisible on a 28 px chip, the
            // opacity dip is what actually reads as "pressed".
            el.PreviewMouseLeftButtonDown += delegate
            {
                down = true;
                AnimateScale(st, 0.97, 90);
                AnimateOpacity(el, 0.86, 90);
            };
            el.PreviewMouseLeftButtonUp += delegate
            {
                if (!down) return;
                down = false;
                AnimateScale(st, 1.0, 160);
                AnimateOpacity(el, 1.0, 160);
            };
            el.MouseLeave += delegate
            {
                if (!down) return;
                down = false;
                AnimateScale(st, 1.0, 160);
                AnimateOpacity(el, 1.0, 160);
            };
        }

        static void AnimateOpacity(UIElement el, double to, int ms)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        static void AnimateScale(ScaleTransform st, double to, int ms)
        {
            var e = new CubicEase { EasingMode = EasingMode.EaseOut };
            var sx = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            sx.EasingFunction = e;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, sx.Clone());
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
            else if (key == "about") body = AboutPage();
            else body = HomePage();
            _sectionCount = 0;
            _host.Content = PageFrame(key, body);

            if (samePage)
            {
                // Content swapped in place: restore where the user was looking instead of jumping
                // back to the top, and skip the fade/lift entirely.
                if (_scroll != null && keepScroll > 0.5)
                {
                    Dispatcher.BeginInvoke(new Action(delegate { _scroll.ScrollToVerticalOffset(keepScroll); }));
                }
                return;
            }

            // Page transition: the new page fades in while lifting 6 px. Short enough to read as
            // "the content was there all along" rather than as an effect.
            _host.Opacity = 1.0;
            var tt = new TranslateTransform(0, 6);
            _host.RenderTransform = tt;
            var fo = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140));
            fo.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            _host.BeginAnimation(UIElement.OpacityProperty, fo);
            var fy = new DoubleAnimation(6.0, 0.0, TimeSpan.FromMilliseconds(180));
            fy.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.YProperty, fy);
        }

        bool _showOthers;
        ScrollViewer _scroll;
        double _pendingScroll;

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

            // click-through: sits next to the language switch because it is a "how does this
            // thing behave" switch, not a look. Turning it on makes the card transparent to
            // the mouse; the tray menu and this page stay clickable to turn it back off.
            // A hairline keeps it from reading as a third language option.
            box.Children.Add(Rule());
            box.Children.Add(Row(Strings.T("page.home.clickthrough"),
                Toggle(_cfg.ClickThrough, delegate(bool v)
                {
                    _card.SetClickThrough(v);
                    RebuildAll();
                })));
            box.Children.Add(Hint(Strings.T("page.home.clickthrough.hint")));

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
            int shown = 0;
            for (int i = 0; i < detected.Count; i++)
            {
                var d = detected[i];
                if (!d.Installed && !d.Running) continue;
                shown++;
                string status = d.Running ? Strings.T("page.home.players.running")
                                           : Strings.T("page.home.players.installed");
                string key = d.Info.Key;
                box.Children.Add(Row(d.Info.Name + "  ·  " + status,
                    Toggle(IsBoundKey(key), delegate(bool v) { SetBound(key, v); })));
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
            RebuildAll();
        }

        UIElement Segmented(string[] labels, int selected, Action<int> pick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            // Every pill needs to be reachable from every other pill: clicking one has to move the
            // highlight, and the caller only reports "index N was chosen", so the row repaints itself.
            var pills = new List<Border>();
            var brushes = new List<SolidColorBrush>();
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                bool on = (i == selected);
                var b = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(12, 5, 12, 5),
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(on
                        ? Color.FromArgb(255, 236, 238, 242)
                        : Color.FromArgb(20, 255, 255, 255)),
                    Child = Label(labels[i], 11.5, on ? Color.FromRgb(20, 20, 22) : TextDim,
                        on ? FontWeights.SemiBold : FontWeights.Normal)
                };
                b.Cursor = Cursors.Hand;
                var segBrush = (SolidColorBrush)b.Background;
                Color segRest = segBrush.Color;
                Color segHot = on
                    ? Color.FromArgb(255, 255, 255, 255)
                    : Color.FromArgb(34, 255, 255, 255);
                b.MouseEnter += delegate { AnimateColor(segBrush, segHot, 120); };
                b.MouseLeave += delegate { AnimateColor(segBrush, segRest, 170); };
                PressScale(b);
                int captured = idx;
                b.MouseLeftButtonUp += delegate
                {
                    pick(captured);
                    SelectInRow(row, captured);
                };
                pills.Add(b);
                brushes.Add(segBrush);
                row.Children.Add(b);
            }
            return row;
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
            Title = Strings.T("app.title");
            Content = BuildUi();
            SetPage(_page);
        }

        CardPreview _preview;

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

        UIElement LookPage()
        {
            var box = new StackPanel();

            // No subtitle here any more: the divider further down already explains which half of
            // the page needs Apply and which half is live, and two notes said the same thing twice.

            // ---- board: a sketch pad, never a live mirror --------------------
            // Everything dragged here goes into a private draft; the card itself does not
            // move until the user presses Apply, which is what keeps this silky.
            if (_preview != null) { _card.LayoutChanged -= OnCardLayoutChanged; _card.LookChanged -= OnCardLookChanged; }
            _preview = new CardPreview();
            _preview.FitWidth = 430;
            _preview.ApplyRequested += delegate
            {
                bool saved = _card.SaveModel();
                LyricsService.Log("board apply -> layout.ini saved=" + (saved ? "1" : "0"));
                RebuildAll();
            };
            _preview.CancelRequested += delegate { RebuildAll(); };
            var board = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 16, 14, 16),
                Margin = new Thickness(0, 2, 0, 14)
            };
            // 1:1 with the card, so the panel scrolls instead of silently rescaling the drawing
            var boardScroll = new System.Windows.Controls.ScrollViewer
            {
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                Content = _preview,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var boardHost = new Grid { MinHeight = 150 };
            boardHost.Children.Add(boardScroll);
            board.Child = boardHost;
            box.Children.Add(board);

            var boardBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            boardBar.Children.Add(Pill(Strings.T("board.apply"), delegate { _preview.RaiseApply(); }, true));
            boardBar.Children.Add(Pill(Strings.T("board.cancel"), delegate { _preview.RaiseCancel(); }, false));
            boardBar.Children.Add(Pill("添加模块…", delegate { _preview.ShowAddMenu(); }, false));
            boardBar.Children.Add(Pill("刷新", delegate { _preview.RefreshFromCard(); }, false));
            box.Children.Add(boardBar);

            // 长度上限：it is the character cap, and with the box on auto the cap *is* the width,
            // so these three steer the frames you just saw above.
            var capStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            capStack.Children.Add(Row("歌名长度上限", Stepper(CapOf(ModuleKind.Title), 0, 80, 2, "0",
                delegate(double v) { SetCap(ModuleKind.Title, (int)Math.Round(v)); })));
            capStack.Children.Add(Row("艺人长度上限", Stepper(CapOf(ModuleKind.Artist), 0, 60, 2, "0",
                delegate(double v) { SetCap(ModuleKind.Artist, (int)Math.Round(v)); })));
            capStack.Children.Add(Row("歌词长度上限", Stepper(CapOf(ModuleKind.Lyrics), 0, 80, 2, "0",
                delegate(double v) { SetCap(ModuleKind.Lyrics, (int)Math.Round(v)); })));
            box.Children.Add(capStack);

            _card.LookChanged += OnCardLookChanged;
            _preview.BeginDraft(_card);

            // The board edits a draft template: nothing above this line touches the card until
            // 应用. Everything below is a live setting that repaints the card as you drag it.
            box.Children.Add(Rule());
            var applyNote = Label("以上修改需要点击【应用】才会生效；以下设置改动立即生效。",
                                  11.5, TextDim, FontWeights.Normal);
            applyNote.TextWrapping = TextWrapping.Wrap;
            applyNote.Margin = new Thickness(0, 0, 0, 4);
            box.Children.Add(applyNote);
            box.Children.Add(Rule());

            // ---- text -------------------------------------------------------
            box.Children.Add(Section(Strings.T("sec.text")));
            box.Children.Add(Row(Strings.T("lbl.titleSize"), Stepper(_cfg.TitleScale, 0.6, 1.6, 0.05, "0.00", delegate(double v)
            { _cfg.TitleScale = v; _card.ApplyLook(); _cfg.Save(); })));

            // Alignment lives on the module (layout.ini), not on the appearance settings: the box is
            // the text box, so "left/centre/right" is how the text sits inside it.
            box.Children.Add(Row("歌名对齐", Segmented(new string[] { "居左", "居中", "居右" },
                AlignIndex(ModuleKind.Title), delegate(int idx) { SetModuleAlign(ModuleKind.Title, idx); })));
            box.Children.Add(Row("艺人对齐", Segmented(new string[] { "居左", "居中", "居右" },
                AlignIndex(ModuleKind.Artist), delegate(int idx) { SetModuleAlign(ModuleKind.Artist, idx); })));
            box.Children.Add(Row("歌词对齐", Segmented(new string[] { "居左", "居中", "居右" },
                AlignIndex(ModuleKind.Lyrics), delegate(int idx) { SetModuleAlign(ModuleKind.Lyrics, idx); })));
            box.Children.Add(Row("歌名艺人同框", Toggle(SameLine(), delegate(bool on)
            {
                CardConfig m = _card.Model;
                if (m == null) return;
                m.TitleArtistInline = on;
                bool saved = m.Save(CardConfig.DefaultPath);
                LyricsService.Log("toggle inline=" + (on ? "1" : "0") + " saved=" + (saved ? "1" : "0"));
                _card.ReloadModel();
                _card.RefreshArtistLine();
                RebuildAll();
            })));
            box.Children.Add(ColorRow(Strings.T("lbl.titleColor"), _cfg.TitleColor, delegate(string v)
            { _cfg.TitleColor = v; _card.ApplyLook(); _cfg.Save(); }));

            box.Children.Add(Row(Strings.T("lbl.artistSize"), Stepper(_cfg.ArtistScale, 0.6, 1.6, 0.05, "0.00", delegate(double v)
            { _cfg.ArtistScale = v; _card.ApplyLook(); _cfg.Save(); })));
            // the artist length cap now lives on the module itself (MaxChars in layout.ini)
            box.Children.Add(ColorRow(Strings.T("lbl.artistColor"), _cfg.ArtistColor, delegate(string v)
            { _cfg.ArtistColor = v; _card.ApplyLook(); _cfg.Save(); }));

            box.Children.Add(Row(Strings.T("lbl.lyricSize"), Stepper(_cfg.LyricScale, 0.6, 1.6, 0.05, "0.00", delegate(double v)
            { _cfg.LyricScale = v; _card.ApplyLook(); _cfg.Save(); })));
            box.Children.Add(ColorRow(Strings.T("lbl.lyricColor"), _cfg.LyricColor, delegate(string v)
            { _cfg.LyricColor = v; _card.ApplyLook(); _cfg.Save(); }));



            // ---- progress bar -----------------------------------------------
            box.Children.Add(Section(Strings.T("sec.bar")));
            box.Children.Add(Row(Strings.T("lbl.barScale"), Stepper(_cfg.BarScale, 0.5, 3.0, 0.05, "0%", delegate(double v)
            {
                double oldScale = _cfg.BarScale <= 0 ? 1.0 : _cfg.BarScale;
                _cfg.BarScale = v;
                // The bar's length is the Progress module's width. Scale the width that is already
                // there instead of recomputing from a constant, so a width dragged in the board is
                // kept: newW = oldW * (new / old).
                CardConfig m2 = _card.Model;
                if (m2 != null)
                {
                    ModuleConfig pr = m2.Find(ModuleKind.Progress);
                    if (pr != null)
                    {
                        double baseW = pr.W > 0 ? pr.W / oldScale : 320.0;
                        pr.W = Math.Max(40.0, Math.Round(baseW * v));
                    }
                    _card.InvalidateModel();
                }
                _card.ApplyLook();
                _cfg.Save();
            })));
            box.Children.Add(Row(Strings.T("lbl.barThickness"), Stepper(_cfg.BarThickness, 2, 8, 0.5, "0.0", delegate(double v)
            { _cfg.BarThickness = v; _card.ApplyLook(); _cfg.Save(); })));
            box.Children.Add(ColorRow(Strings.T("lbl.barColor"), _cfg.BarColor, delegate(string v)
            { _cfg.BarColor = v; _card.ApplyLook(); _cfg.Save(); }));
            box.Children.Add(ColorRow(Strings.T("lbl.barFill"), _cfg.BarFillColor, delegate(string v)
            { _cfg.BarFillColor = v; _card.ApplyLook(); _cfg.Save(); }));

            // ---- artwork ----------------------------------------------------
            box.Children.Add(Section(Strings.T("sec.art")));
            box.Children.Add(Row(Strings.T("lbl.artSize"), Stepper(_cfg.ArtSize, 56, 160, 4, "0", delegate(double v)
            {
                _cfg.ArtSize = v;
                // one size for both views: write it into the Cover module, so the board's circle
                // grows with the disc instead of staying at the old radius
                CardConfig m2 = _card.Model;
                if (m2 != null)
                {
                    ModuleConfig cov = m2.Find(ModuleKind.Cover);
                    if (cov != null) { cov.W = v; cov.H = v; }
                    _card.InvalidateModel();
                }
                _card.ApplyLook();
                _cfg.Save();
            })));
            box.Children.Add(Row(Strings.T("lbl.artSat"), Stepper(_cfg.ArtSat, 0, 1, 0.05, "0.00", delegate(double v)
            { _cfg.ArtSat = v; _card.RefreshArt(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.artHiCut"), Stepper(_cfg.ArtHiCut, 0, 0.6, 0.02, "0.00", delegate(double v)
            { _cfg.ArtHiCut = v; _card.RefreshArt(); _cfg.Save(); })));
            // the cover glow was removed: the halo bled over the title and lyrics

            // ---- card -------------------------------------------------------
            box.Children.Add(Section(Strings.T("sec.card")));
            box.Children.Add(Row(Strings.T("lbl.scale"), Stepper(_cfg.Scale, 0.55, 2.2, 0.05, "0.00", delegate(double v)
            { _cfg.Scale = v; _card.ApplyCurrentScale(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.opacity"), Stepper(_cfg.Opacity, 0.25, 1.0, 0.02, "0.00", delegate(double v)
            { _cfg.Opacity = v; _card.ApplyCurrentOpacity(); _cfg.Save(); })));
            box.Children.Add(ColorRow(Strings.T("lbl.controlsColor"), _cfg.ControlsColor, delegate(string v)
            { _cfg.ControlsColor = v; _card.ApplyLook(); _cfg.Save(); }));

            box.Children.Add(Row(Strings.T("lbl.clearBg"), Toggle(_cfg.ClearBg, delegate(bool v)
            { _card.SetClearBg(v); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.artshape"), Segmented(
                new string[] { Strings.T("artshape.circle"), Strings.T("artshape.square") },
                string.Equals(_cfg.ArtShape, "square", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                delegate(int i) { _card.SetArtShape(i == 1 ? "square" : "circle"); RebuildAll(); })));
            box.Children.Add(Row(Strings.T("lbl.spin"), Toggle(_cfg.ArtSpin, delegate(bool v)
            { _card.SetArtSpin(v); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.textShadow"), Toggle(_cfg.TextShadow, delegate(bool v)
            { _cfg.TextShadow = v; _card.ApplyBackdropNow(); _cfg.Save(); })));

            // ---- presets ----------------------------------------------------
            box.Children.Add(Section(Strings.T("sec.presets")));

            var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
            var nameBox = new TextBox
            {
                Width = 200,
                Height = 28,
                FontSize = 12,
                Padding = new Thickness(8, 3, 8, 3),
                Foreground = new SolidColorBrush(TextMain),
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CaretBrush = new SolidColorBrush(TextMain),
                ToolTip = Strings.T("preset.prompt")
            };
            saveRow.Children.Add(nameBox);
            var savePill = Pill(Strings.T("preset.save"), delegate
            {
                string nm = (nameBox.Text == null) ? "" : nameBox.Text.Trim();
                if (nm.Length == 0) nm = Strings.F("preset.defaultName", _cfg.Presets.Count + 1);
                if (nm.Length > 14) nm = nm.Substring(0, 14);
                if (_cfg.Presets.Count >= 10) _cfg.Presets.RemoveAt(0);
                _cfg.Presets.Add(_cfg.CaptureLook(nm));
                _cfg.Save();
                SetPage("look");
            }, false);
            savePill.Margin = new Thickness(8, 0, 0, 0);
            saveRow.Children.Add(savePill);
            box.Children.Add(saveRow);

            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            if (_cfg.Presets.Count == 0) chips.Children.Add(Hint(Strings.T("preset.empty")));
            else for (int i = 0; i < _cfg.Presets.Count; i++) chips.Children.Add(PresetChip(_cfg.Presets[i]));
            box.Children.Add(chips);

            return box;
        }

        // one saved look: click the name to apply it, click the x to delete it
        Border PresetChip(Config.LookPreset p)
        {
            Color idle = Color.FromArgb(22, 255, 255, 255);
            Color hot = Color.FromArgb(38, 255, 255, 255);
            var b = new Border
            {
                Height = 27,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(idle),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(12, 0, 9, 0)
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var nm = Label(p.Name, 12, TextMain, FontWeights.Normal);
            nm.VerticalAlignment = VerticalAlignment.Center;
            var del = Label("\u00d7", 13, TextDim, FontWeights.Normal);
            del.Margin = new Thickness(9, 0, 0, 0);
            del.VerticalAlignment = VerticalAlignment.Center;
            del.Cursor = Cursors.Hand;
            sp.Children.Add(nm);
            sp.Children.Add(del);
            b.Child = sp;
            var chipBrush = (SolidColorBrush)b.Background;
            b.MouseEnter += delegate { AnimateColor(chipBrush, hot, 120); };
            b.MouseLeave += delegate { AnimateColor(chipBrush, idle, 170); };
            PressScale(b);
            del.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                args.Handled = true;   // the chip's own handler must not apply the preset as well
                _cfg.Presets.Remove(p);
                _cfg.Save();
                SetPage("look");
            };
            b.MouseLeftButtonUp += delegate
            {
                if (del.IsMouseOver) return;
                _cfg.ApplyLookPreset(p);
                _cfg.Save();
                _card.ApplyLook();
                _card.ApplyCurrentScale();
                _card.ApplyCurrentOpacity();
                SetPage("look");
            };
            return b;
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
            timingRow.Children.Add(Pill(Strings.T("lyr.early05"), delegate { nudge(-0.5); }, false));
            timingRow.Children.Add(Pill(Strings.T("lyr.early01"), delegate { nudge(-0.1); }, false));
            timingRow.Children.Add(Pill(Strings.T("lyr.late01"), delegate { nudge(0.1); }, false));
            timingRow.Children.Add(Pill(Strings.T("lyr.late05"), delegate { nudge(0.5); }, false));
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
            // Same 120/170 ms fade and press dip as the rest of the chrome, instead of the
            // instant background swap these two used to do.
            HoverFill(host, 0, 26);
            PressScale(host);
            host.MouseLeftButtonUp += delegate
            {
                // Minimising or hiding swallows the mouse-up that would undo the press dim,
                // so put it back before acting -- otherwise the button stays at 0.86.
                AnimateOpacity(host, 1.0, 120);
                act();
            };
            return host;
        }
    }
}
