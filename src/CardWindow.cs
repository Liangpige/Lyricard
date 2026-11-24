using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Amc
{
    public class CardWindow : Window
    {
        // default lives in Config.ArtistMaxChars so the settings panel can change it

        public static bool OpenSettingsOnStart;

        // The three "no lyric content" states, kept in one place so that every source
        // (QQ / netease / lrclib / local .lrc) ends up rendering identically.
        public const string TxtWait = "♪ Waiting for music";      // fetching, or before the first line
        public const string TxtInstr = "♪ Instrumental · no lyrics"; // the track has no vocals
        public const string TxtNone = "♪ No lyrics available";     // nothing found anywhere

        public const double CardW = 470;

        // Long titles must not run all the way to the left edge: keep two characters of
        // breathing room (two full-width glyphs at the title font size).
        // left-edge breathing room, in DIP; configurable from the settings panel
        public const double CardH = 120;
        public const double ShadowPad = 24;

        Config _cfg;
        SmtcService _smtc;
        LyricsService _lyr = new LyricsService();
        DispatcherTimer _timer;

        Border _shadowHost;
        ScaleTransform _inScale;
        TranslateTransform _inShift;
        int _visSeq;
        IntPtr _hwnd;
        int _topTicks;
        bool _menuOpen;
        Border _card;
        Grid _root;
        Color _artBg = Color.FromArgb(238, 18, 18, 21);
        bool _hasArtBg;
        TextBlock _title;
        Run _runTitle;
        Run _runArtist;
        TextBlock _lyric;
        Grid _barRow;
        Border _barTrack;
        Border _barFill;
        StackPanel _controls;
        StackPanel _content;
        Ellipse _artEllipse;
        public Tray TrayRef;
        Grid _artHost;
        ScaleTransform _artScale;
        RotateTransform _artRotate;
        AnimationClock _spin;
        Btn _btnPlay;
        bool _playingShown;
        DateTime _playCmdAt;
        bool _playCmdWant;
        bool _playCmdActive;
        bool _idle;
        readonly List<Btn> _buttons = new List<Btn>();

        LyricDoc _doc;
        string _docKey = "";
        string _titleKey = "";
        string _pendingKey = "";
        string _trackKey = "";
        // A VIP preview stops at 60 s and the player jumps straight into the next song: the
        // reported position snaps back to ~0 while the title lags behind for a beat or two.
        // Without a guard the bar and the lyric line lurch twice in 300 ms (""抽搐""). While a
        // switch is settling we hold one clean zero state instead of replaying each partial.
        DateTime _switchUntil = DateTime.MinValue;
        double _lastPos = 0;
        double _pos = -1;
        int _lineIdx = -1;
        int _tickCount;
        DateTime _lastTick = DateTime.Now;
        DateTime _lineShownAt = DateTime.Now;
        string _lineText = "";
        bool _lineFaded;
        byte[] _lastArt;
        bool _hover;
        bool _suppressed;
        // "no player at all" state (Apple Music not installed / not running).
        bool _noPlayer;
        int _audibleTicks;
        string _hintPlayer;
        DateTime _hintAt = DateTime.MinValue;

        /// <summary>Set for logon launches: stay hidden until some player publishes a session.</summary>
        public bool HideWhenIdle;

        public Config Cfg { get { return _cfg; } }

        public CardWindow(Config cfg, SmtcService smtc)
        {
            _cfg = cfg;
            _smtc = smtc;
            _smtc.ListenAll = cfg.ListenAll;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = false;
            Focusable = false;
            Title = Strings.T("app.title") + " " + Strings.T("app.version");

            BuildUi();
            ApplyLook();

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closing += delegate { RememberPosition(); _cfg.Save(); };

            _timer = new DispatcherTimer(DispatcherPriority.Render);
            _timer.Interval = TimeSpan.FromMilliseconds(33);
            _timer.Tick += Tick;
            _timer.Start();

        }

        // ---------------- construction ----------------

        static SolidColorBrush White(double a)
        {
            return new SolidColorBrush(Color.FromArgb((byte)(a * 255), 255, 255, 255));
        }

        // Base font sizes; the personalised look multiplies these.
        double _baseTitleSize = 15.5;
        double _baseArtistSize = 11.5;
        double _baseLyricSize = 15.0;

        // "#RRGGBB" or "#AARRGGBB"; null / empty means "use the built-in colour".
        static Color? TryHex(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim();
            if (t.StartsWith("#")) t = t.Substring(1);
            try
            {
                if (t.Length == 6)
                    return Color.FromArgb(255,
                        Convert.ToByte(t.Substring(0, 2), 16),
                        Convert.ToByte(t.Substring(2, 2), 16),
                        Convert.ToByte(t.Substring(4, 2), 16));
                if (t.Length == 8)
                    return Color.FromArgb(
                        Convert.ToByte(t.Substring(0, 2), 16),
                        Convert.ToByte(t.Substring(2, 2), 16),
                        Convert.ToByte(t.Substring(4, 2), 16),
                        Convert.ToByte(t.Substring(6, 2), 16));
            }
            catch { }
            return null;
        }

        Brush BarTrackBrush()
        {
            Color? c = TryHex(_cfg.BarColor);
            if (c.HasValue) return new SolidColorBrush(c.Value);
            return _cfg.ClearBg ? White(0.30) : White(0.16);
        }

        Brush BarFillBrush()
        {
            Color? c = TryHex(_cfg.BarFillColor);
            if (c.HasValue) return new SolidColorBrush(c.Value);
            return White(0.92);
        }

        // Applies every personalised style knob. Safe to call at any time (live preview).
        public const double CardPadX = 16;
        public const double CardBorder = 1;
        public const double ColGap = 20;

        // width taken by everything except the text column: padding + border + gap + cover
        public double CardBase()
        {
            return CardPadX * 2 + CardBorder * 2 + ColGap + _cfg.ArtSize;
        }

        // natural (100%) width of the text column
        public double NaturalCol()
        {
            return CardW - CardBase();
        }

        public double CardWidthNow()
        {
            return (_card != null && _card.Width > 1) ? _card.Width : CardW;
        }

        // Horizontal placement of the transport buttons: 0% = flush left, 50% = centered,
        // 100% = flush right (the default). Percent is taken over the FREE width
        // (column width minus the button row's own width), so it stays meaningful when the
        // card widens or narrows.
        /// <summary>
        /// Place the transport row horizontally inside the text column: 0% = flush left with the
        /// progress bar, 100% = flush right. The row keeps its natural width; only the left margin
        /// changes, and it is recomputed once the buttons have a real ActualWidth.
        /// </summary>
        public void ApplyControlsPos()
        {
            if (_controls == null) return;
            double colW = (_content != null && _content.ActualWidth > 1)
                ? _content.ActualWidth
                : Math.Max(120, NaturalCol() * _cfg.BarScale);
            double btnW = (_controls.ActualWidth > 1) ? _controls.ActualWidth : 90.0;
            double free = colW - btnW;
            if (free < 0) free = 0;
            double pct = _cfg.ControlsX;
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            _controls.HorizontalAlignment = HorizontalAlignment.Left;
            _controls.Margin = new Thickness(free * pct / 100.0, 0, 0, 0);
            UpdateLayout();
        }

        // Text alignment. The "indent" setting is the breathing room kept on the side the text
        // grows away from: right-aligned text reserves it on the left, left-aligned on the right,
        // centered splits it between both sides.
        /// <summary>
        /// Left / centre / right for the two text lines. The padding is a left margin in right
        /// alignment, a right margin in left alignment and half-and-half when centred - which keeps
        /// "usable width = column width - padding" true in all three cases, so FitTitle needs no
        /// special casing.
        /// </summary>
        public void ApplyAlign()
        {
            if (_title == null || _lyric == null || _barRow == null) return;
            string a = (_cfg.TextAlign ?? "right").Trim().ToLowerInvariant();
            HorizontalAlignment ha = HorizontalAlignment.Right;
            TextAlignment ta = TextAlignment.Right;
            if (a == "left") { ha = HorizontalAlignment.Left; ta = TextAlignment.Left; }
            else if (a == "center") { ha = HorizontalAlignment.Center; ta = TextAlignment.Center; }
            else a = "right";

            double ti = _cfg.TitleIndent;
            double li = _cfg.LyricIndent;
            double tl = 0.0, tr = 0.0, ll = 0.0, lr = 0.0;
            if (a == "right") { tl = ti; ll = li; }
            else if (a == "left") { tr = ti; lr = li; }
            else { tl = tr = ti / 2.0; ll = lr = li / 2.0; }

            _title.HorizontalAlignment = ha;
            _title.TextAlignment = ta;
            _title.Margin = new Thickness(tl, 0, tr, 0);

            _lyric.HorizontalAlignment = ha;
            _lyric.TextAlignment = ta;
            _lyric.Margin = new Thickness(ll, 4, lr, 0);

            // The progress bar does NOT follow the text indent or the alignment: it always spans the
            // full content column. Tying it to the lyric indent made the bar shrink whenever the text
            // breathing room grew, which looked like a bug. Keep it independent.
            _barRow.Margin = new Thickness(0, 8, 0, 0);

            UpdateLayout();
        }

        /// <summary>
        /// Re-apply every appearance setting to live elements: font sizes and colours, progress bar
        /// thickness/colours, artwork size, text margins/alignment, control placement. Called once
        /// after the UI is built and again whenever the settings panel changes something.
        /// </summary>
        public void ApplyLook()
        {
            if (_title == null) return;
            double ts = _baseTitleSize * _cfg.TitleScale;
            double asz = _baseArtistSize * _cfg.ArtistScale;
            double ls = _baseLyricSize * _cfg.LyricScale;
            _title.FontSize = ts;
            _runTitle.FontSize = ts;
            _runArtist.FontSize = asz;
            _lyric.FontSize = ls;
            // alignment + breathing room are layout, they do not follow from FontSize,
            // so re-apply them here (forgetting this is what made the indent slider dead)
            ApplyAlign();
            ApplyControlsPos();

            Color? ct = TryHex(_cfg.TitleColor);
            _runTitle.Foreground = ct.HasValue ? (Brush)new SolidColorBrush(ct.Value) : White(0.97);
            Color? ca = TryHex(_cfg.ArtistColor);
            _runArtist.Foreground = ca.HasValue ? (Brush)new SolidColorBrush(ca.Value) : White(0.62);
            Color? cl = TryHex(_cfg.LyricColor);
            _lyric.Foreground = cl.HasValue ? (Brush)new SolidColorBrush(cl.Value) : White(0.95);

            double thick = _cfg.BarThickness;
            _barTrack.Height = thick;
            _barFill.Height = thick;
            _barTrack.CornerRadius = new CornerRadius(thick / 2.0);
            _barFill.CornerRadius = new CornerRadius(thick / 2.0);
            _barTrack.Background = BarTrackBrush();
            _barFill.Background = BarFillBrush();

            if (_buttons != null)
            {
                foreach (Btn b in _buttons)
                {
                    FillGlyph(b.Glyph, b.Kind);
                    b.Glow.Background = GlyphTint(31);
                    b.Ring.BorderBrush = GlyphTint(140);
                }
            }

            _artHost.Width = _cfg.ArtSize;
            _artHost.Height = _cfg.ArtSize;
            _artHost.InvalidateVisual();

            // content column width: natural width (left of the cover) times the bar-length factor.
            // The column must NOT get an explicit Width: the Grid cell clips a wider child on the
            // right, which chops the last characters of a right-aligned line.
            double oldCardW = CardWidthNow();
            double colW = Math.Max(120, NaturalCol() * _cfg.BarScale);
            _card.Width = colW + CardBase();
            // keep the right edge glued to where it was, so growing/shrinking the bar does not
            // drag the whole card sideways
            double grow = _card.Width - oldCardW;
            if (Math.Abs(grow) > 0.5 && !double.IsNaN(Left))
                Left -= grow * _cfg.Scale;

            _barFill.Width = _barFill.Width;   // no-op, keeps the fill from being re-measured away

            try { UpdateLayout(); } catch { }
            if (!double.IsNaN(Left)) ClampToScreen();
        }

        void BuildUi()
        {
            _card = new Border
            {
                Width = CardW,
                Height = double.NaN,   // grows with the content / cover size
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 12, 16, 12),
                Background = new SolidColorBrush(Color.FromArgb(238, 18, 18, 21)),
                BorderBrush = White(0.10),
                BorderThickness = new Thickness(1)
            };

            _shadowHost = new Border
            {
                Padding = new Thickness(ShadowPad),
                Background = Brushes.Transparent,
                Child = _card,
                LayoutTransform = new ScaleTransform(_cfg.Scale, _cfg.Scale)
            };
            // No drop shadow on the card itself: the only shadow in this window is the
            // cover halo (ApplyBackdrop decides whether it is on).
            Content = _shadowHost;

            var root = new Grid();
            _root = root;
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _card.Child = root;

            // --- content column (right aligned, kept inside the cover's height) ---
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            _content = left;
            left.SizeChanged += delegate { ApplyControlsPos(); };
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            // title + artist on one line: title bold, artist dimmer and smaller
            _title = new TextBlock
            {
                FontSize = 15.5,
                Foreground = White(0.97),
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(_cfg.TitleIndent, 0, 0, 0)
            };
            _runTitle = new Run("Apple Music") { FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = White(0.97) };
            _runArtist = new Run("") { FontSize = 11.5, Foreground = White(0.62) };
            _title.Inlines.Add(_runTitle);
            _title.Inlines.Add(_runArtist);
            left.Children.Add(_title);

            _lyric = new TextBlock
            {
                FontSize = 15,
                Foreground = White(0.95),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(_cfg.LyricIndent, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                Text = "",
                RenderTransform = new TranslateTransform(0, 0)
            };
            left.Children.Add(_lyric);

            // --- progress ---
            // NOTE: the 8px gap lives on the shared Grid, not on _barTrack — putting it on the
            // track only shifted the dim bar down 8px and left the bright fill floating above it.
            var barGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            _barTrack = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = White(0.16),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _barFill = new Border
            {
                Height = 3,
                Width = 0,
                CornerRadius = new CornerRadius(1.5),
                Background = White(0.92),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            _barRow = barGrid;
            barGrid.Children.Add(_barTrack);
            barGrid.Children.Add(_barFill);
            barGrid.MouseLeftButtonDown += OnBarClick;
            left.Children.Add(barGrid);

            // --- transport controls: their own row, right aligned ---
            var bottom = new Grid { Margin = new Thickness(0, 6, 0, 0) };

            _controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Opacity = ControlsIdle, HorizontalAlignment = HorizontalAlignment.Right };
            _btnPlay = MakeButton("play", delegate { CommandPlayPause(); }, 0);
            _controls.Children.Add(MakeButton("prev", delegate { _smtc.Prev(); }, -1).Root);
            _controls.Children.Add(_btnPlay.Root);
            _controls.Children.Add(MakeButton("next", delegate { _smtc.Next(); }, 1).Root);

            bottom.Children.Add(_controls);
            left.Children.Add(bottom);

            // --- cover ---
            var artGrid = new Grid
            {
                Width = 96,
                Height = 96,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            _artHost = artGrid;
            _artScale = new ScaleTransform(1, 1);
            artGrid.RenderTransform = _artScale;
            Grid.SetColumn(artGrid, 1);
            _artEllipse = new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(255, 42, 42, 48)) };
            // the cover itself turns, the ring around it stays put
            _artRotate = new RotateTransform(0);
            _artEllipse.RenderTransformOrigin = new Point(0.5, 0.5);
            _artEllipse.RenderTransform = _artRotate;
            artGrid.Children.Add(_artEllipse);
            artGrid.Children.Add(new Ellipse { Stroke = White(0.13), StrokeThickness = 1 });
            root.Children.Add(artGrid);

            // --- interactions ---
            _card.MouseLeftButtonDown += OnCardMouseDown;
            _card.MouseWheel += OnWheel;
            _card.MouseRightButtonUp += OnRightClick;
            _card.MouseEnter += delegate
            {
                _hover = true;
                Animate(_controls, 1.0, 120);
                if (_artScale != null) AnimScale(_artScale, 1.03, 220);
            };
            _card.MouseLeave += delegate
            {
                _hover = false;
                Animate(_controls, ControlsIdle, 220);
                if (_artScale != null) AnimScale(_artScale, 1.0, 260);
            };
            _card.ContextMenu = BuildMenu();
            ApplyBackdrop();
        }

        // Clear background mode: nothing but text + cover on the desktop.
        // the cover sits right next to the text column, so its halo is tighter and pushed
        // down-right: a centred blur would bleed left and clip the last characters
        /// <summary>Optional halo behind text, for light wallpapers. Off by default.</summary>
        static DropShadowEffect TextHalo()
        {
            return new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 11,
                ShadowDepth = 0,
                Opacity = 0.85
            };
        }

        static DropShadowEffect ArtHalo()
        {
            return new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 7,
                ShadowDepth = 4,
                Direction = 45,
                Opacity = 0.55
            };
        }

        /// <summary>
        /// Paint the card in one of two modes: transparent (no panel, text and artwork carry their
        /// own shadows) or shaded (panel with a gradient derived from the artwork's average colour).
        /// Only brushes change here - sizes and paddings stay identical so switching never reflows.
        /// </summary>
        /// <summary>Re-applies background + effects after a look change (settings panel).</summary>
        public void ApplyBackdropNow()
        {
            ApplyBackdrop();
        }

        void ApplyBackdrop()
        {
            if (_cfg.ClearBg)
            {
                // null brushes => empty areas are not hit-testable, so desktop clicks pass through
                Background = null;
                _card.Background = null;
                // keep BorderThickness at 1 here: it is a layout property, dropping it to 0
                // would shift the inner content (and the card height) when the option is toggled
                _card.BorderBrush = null;
                _shadowHost.Effect = null;
                // halo goes on the static host, not on _artEllipse: the ellipse carries the
                // spin transform, so an effect there would rotate the shadow with the cover
                _artEllipse.Effect = null;
                _artHost.Effect = _cfg.ArtShadow ? ArtHalo() : null;

                // Only the cover keeps a shadow by default (see ArtHalo). TextShadow is the
                // opt-in exception for light wallpapers, where white text without it vanishes.
                var th = _cfg.TextShadow ? TextHalo() : null;
                _title.Effect = th;
                _lyric.Effect = th;
                _controls.Effect = th;
                _barTrack.Background = BarTrackBrush();
            }
            else
            {
                Background = Brushes.Transparent;
                _card.BorderBrush = White(0.10);
                _shadowHost.Effect = null;
                _artEllipse.Effect = null;
                _artHost.Effect = _cfg.ArtShadow ? ArtHalo() : null;
                _card.Background = _hasArtBg
                    ? (Brush)new LinearGradientBrush(_artBg, Color.FromArgb(238, 12, 12, 15), 45)
                    : new SolidColorBrush(Color.FromArgb(238, 18, 18, 21));

                _title.Effect = null;
                _lyric.Effect = null;

                _controls.Effect = null;
                _barTrack.Background = BarTrackBrush();
            }
        }

        // ---------------- media buttons ----------------

        /// <summary>Transport-button colour: user-picked, otherwise the built-in white.</summary>
        Brush GlyphBrush()
        {
            return new SolidColorBrush(GlyphColor());
        }

        // Unset means "follow the lyric colour", which keeps the icons readable when the
        // user switched to dark text for a light wallpaper; falling back to white otherwise.
        Color GlyphColor()
        {
            Color? c = TryHex(_cfg.ControlsColor);
            if (c.HasValue) return c.Value;
            Color? l = TryHex(_cfg.LyricColor);
            if (l.HasValue) return l.Value;
            return Colors.White;
        }

        Brush GlyphTint(byte alpha)
        {
            Color b = GlyphColor();
            return new SolidColorBrush(Color.FromArgb(alpha, b.R, b.G, b.B));
        }

        // Icons are hand-drawn vector shapes (rounded bars + round-joined triangles) so they
        // stay crisp at any DPI and never depend on an icon font being installed.
        Shape GlyphBar(double x, double y, double w, double h)
        {
            var r = new Rectangle
            {
                Width = w,
                Height = h,
                RadiusX = Math.Min(w, h) / 2.4,
                RadiusY = Math.Min(w, h) / 2.4,
                Fill = GlyphBrush(),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            return r;
        }

        Shape GlyphTri(string data)
        {
            var p = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                Fill = GlyphBrush(),
                Stroke = GlyphBrush(),
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            return p;
        }

        void FillGlyph(Canvas c, string kind)
        {
            c.Children.Clear();
            if (kind == "pause")
            {
                c.Children.Add(GlyphBar(2.9, 2.5, 3.4, 10.9));
                c.Children.Add(GlyphBar(9.6, 2.5, 3.4, 10.9));
            }
            else if (kind == "play")
            {
                c.Children.Add(GlyphTri("M 3.4,2.2 L 12.9,7.95 L 3.4,13.7 Z"));
            }
            else if (kind == "prev")
            {
                c.Children.Add(GlyphBar(1.2, 2.5, 2.5, 10.9));
                c.Children.Add(GlyphTri("M 13.0,2.4 L 13.0,13.5 L 5.3,7.95 Z"));
            }
            else
            {
                c.Children.Add(GlyphBar(12.3, 2.5, 2.5, 10.9));
                c.Children.Add(GlyphTri("M 3.0,2.4 L 3.0,13.5 L 10.7,7.95 Z"));
            }
        }

        sealed class Btn
        {
            public Grid Root;
            public Border Glow;
            public Border Ring;
            public string Kind;
            public Canvas Glyph;
            public ScaleTransform IconScale;
            public TranslateTransform IconShift;
        }

        Btn MakeButton(string kind, Action act, int nudge)
        {
            var b = new Btn();
            var root = new Grid
            {
                Width = 30,
                Height = 30,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 2, 0)
            };
            b.Root = root;
            b.Kind = kind;

            b.Glow = new Border { CornerRadius = new CornerRadius(15), Background = GlyphTint(31), Opacity = 0.0 };
            root.Children.Add(b.Glow);

            b.Ring = new Border
            {
                CornerRadius = new CornerRadius(15),
                BorderBrush = GlyphTint(140),
                BorderThickness = new Thickness(1),
                Opacity = 0.0,
                RenderTransform = new ScaleTransform(0.6, 0.6),
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false
            };
            root.Children.Add(b.Ring);

            var host = new Grid { RenderTransformOrigin = new Point(0.5, 0.5) };
            b.IconScale = new ScaleTransform(1, 1);
            b.IconShift = new TranslateTransform(0, 0);
            var tg = new TransformGroup();
            tg.Children.Add(b.IconScale);
            tg.Children.Add(b.IconShift);
            host.RenderTransform = tg;

            b.Glyph = new Canvas
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            FillGlyph(b.Glyph, kind);
            host.Children.Add(b.Glyph);
            root.Children.Add(host);

            root.MouseEnter += delegate
            {
                AnimOpacity(b.Glow, 1.0, 130);
                AnimScale(b.IconScale, 1.12, 160);
            };
            root.MouseLeave += delegate
            {
                AnimOpacity(b.Glow, 0.0, 220);
                AnimScale(b.IconScale, 1.0, 200);
            };
            root.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                AnimScale(b.IconScale, 0.86, 70);
                Ripple(b.Ring);
                if (nudge != 0) Nudge(b.IconShift, nudge * 2.5);
                act();
            };
            root.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                AnimScale(b.IconScale, root.IsMouseOver ? 1.12 : 1.0, 210);
            };

            _buttons.Add(b);
            return b;
        }

        /// <summary>
        /// Sends play/pause and shows the result straight away. Several clients (NetEase) need a
        /// moment before their SMTC state catches up, and the 200 ms poll would otherwise flip the
        /// icon back to the stale value - which read as "the button does nothing".
        /// </summary>
        void CommandPlayPause()
        {
            _playCmdWant = !_playingShown;
            _playCmdAt = DateTime.Now;
            _playCmdActive = true;
            SetPlayIcon(_playCmdWant, true);
            _smtc.PlayPause();
        }

        void SetPlayIcon(bool playing, bool animate)
        {
            _playingShown = playing;
            if (_btnPlay == null) return;
            FillGlyph(_btnPlay.Glyph, playing ? "pause" : "play");
            if (animate) PopIn(_btnPlay.IconScale);
        }

        void PopIn(ScaleTransform st)
        {
            var a = new DoubleAnimation(0.84, 1.0, TimeSpan.FromMilliseconds(190));
            a.EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void AnimScale(ScaleTransform st, double to, int ms)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var ax = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            var ay = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            ax.EasingFunction = ease;
            ay.EasingFunction = ease;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
        }

        void FadeIn(UIElement el, int ms)
        {
            el.Opacity = 1.0;
            var a = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        void FadeInTo(UIElement el, double to, int ms)
        {
            el.Opacity = to;
            var a = new DoubleAnimation(0.0, to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        void AnimOpacity(UIElement el, double to, int ms)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        void Ripple(Border ring)
        {
            var st = (ScaleTransform)ring.RenderTransform;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var sx = new DoubleAnimation(0.6, 1.5, TimeSpan.FromMilliseconds(430));
            var sy = new DoubleAnimation(0.6, 1.5, TimeSpan.FromMilliseconds(430));
            var op = new DoubleAnimation(0.5, 0.0, TimeSpan.FromMilliseconds(430));
            sx.EasingFunction = ease;
            sy.EasingFunction = ease;
            op.EasingFunction = ease;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            ring.BeginAnimation(UIElement.OpacityProperty, op);
        }

        void Nudge(TranslateTransform tr, double to)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            a.Duration = TimeSpan.FromMilliseconds(280);
            a.KeyFrames.Add(new LinearDoubleKeyFrame(to, KeyTime.FromPercent(0.32)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
            tr.BeginAnimation(TranslateTransform.XProperty, a);
        }

        // ---------------- window plumbing ----------------

        void OnSourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);
            _hwnd = hwnd;
            Native.BringToTop(_hwnd);
        }

        void OnLoaded(object sender, EventArgs e)
        {
            if (OpenSettingsOnStart)
            {
                OpenSettingsOnStart = false;
                Dispatcher.BeginInvoke(new Action(delegate { OpenSettings(); }));
            }
            Opacity = _cfg.Opacity;
            PlaceInitial();
            double dpx = 1.0;
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src != null && src.CompositionTarget != null) dpx = src.CompositionTarget.TransformToDevice.M11;
            }
            catch { }
            LyricsService.Log("loaded wa=" + WorkArea() + " dpi=" + dpx.ToString("0.00", CultureInfo.InvariantCulture) +
                              " left=" + Left.ToString("0.0", CultureInfo.InvariantCulture) +
                              " top=" + Top.ToString("0.0", CultureInfo.InvariantCulture) +
                              " scale=" + _cfg.Scale.ToString("0.00", CultureInfo.InvariantCulture));
            Dispatcher.BeginInvoke(new Action(delegate
            {
                LyricsService.Log("measured w=" + ActualWidth.ToString("0.0", CultureInfo.InvariantCulture) +
                                  " h=" + ActualHeight.ToString("0.0", CultureInfo.InvariantCulture));
                ApplyControlsPos();
                LyricsService.Log("layout title=" + _title.ActualHeight.ToString("0.0", CultureInfo.InvariantCulture) +
                                  " lyric=" + _lyric.ActualHeight.ToString("0.0", CultureInfo.InvariantCulture) +
                                  " bottom=" + _controls.ActualHeight.ToString("0.0", CultureInfo.InvariantCulture) +
                                  " art=" + _artEllipse.ActualHeight.ToString("0.0", CultureInfo.InvariantCulture) +
                                  " barW=" + _barTrack.ActualWidth.ToString("0.0", CultureInfo.InvariantCulture) +
                                  " colW=" + _content.ActualWidth.ToString("0.0", CultureInfo.InvariantCulture) +
                                  " edgeT=" + EdgeX(_title).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " edgeL=" + EdgeX(_lyric).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " edgeB=" + EdgeX(_barTrack).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " edgeC=" + EdgeX(_controls).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " edgeTL=" + EdgeX0(_title).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " align=" + (_cfg.TextAlign ?? "right") +
                                  " leftT=" + EdgeX0(_title).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " leftL=" + EdgeX0(_lyric).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " leftC=" + EdgeX0(_controls).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " btnW=" + _controls.ActualWidth.ToString("0.0", CultureInfo.InvariantCulture) +
                                  " leftB=" + EdgeX0(_barTrack).ToString("0.0", CultureInfo.InvariantCulture));
                LyricsService.Log("bar track y=" + EdgeY(_barTrack).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " h=" + (_barTrack.ActualHeight * DpiScale()).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " | fill y=" + EdgeY(_barFill).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " h=" + (_barFill.ActualHeight * DpiScale()).ToString("0.0", CultureInfo.InvariantCulture) +
                                  " w=" + _barFill.ActualWidth.ToString("0.0", CultureInfo.InvariantCulture));
            }), DispatcherPriority.Loaded);
        }

        static Rect WorkArea()
        {
            // SystemParameters.WorkArea is already in DIP and excludes the taskbar.
            return SystemParameters.WorkArea;
        }

        double ActualW()
        {
            double w = ActualWidth;
            if (w < 1) w = (CardWidthNow() + ShadowPad * 2) * _cfg.Scale;
            return w;
        }

        double ActualH()
        {
            double h = ActualHeight;
            if (h < 1) h = (CardH + ShadowPad * 2) * _cfg.Scale;
            return h;
        }

        void PlaceInitial()
        {
            Rect wa = WorkArea();
            double w = (CardWidthNow() + ShadowPad * 2) * _cfg.Scale;
            double h = (CardH + ShadowPad * 2) * _cfg.Scale;

            bool ok = !double.IsNaN(_cfg.Left) && !double.IsNaN(_cfg.Top)
                      && _cfg.Left > wa.Left - 40 && _cfg.Left < wa.Right - 80
                      && _cfg.Top > wa.Top - 40 && _cfg.Top < wa.Bottom - 60;
            if (ok)
            {
                Left = _cfg.Left;
                Top = _cfg.Top;
                ClampToScreen();
            }
            else
            {
                Left = wa.Right - w - 12;
                Top = wa.Top + 12;
            }
        }

        void ClampToScreen()
        {
            Rect wa = WorkArea();
            double w = ActualW();
            double h = ActualH();
            if (Left + w > wa.Right) Left = wa.Right - w - 1;
            if (Top + h > wa.Bottom) Top = wa.Bottom - h - 1;
            if (Left < wa.Left) Left = wa.Left;
            if (Top < wa.Top) Top = wa.Top;
        }

        // ---------------- interaction ----------------

        void OnCardMouseDown(object s, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2)
            {
                CommandPlayPause();
                e.Handled = true;
                return;
            }
            if (_cfg.Locked) return;
            try { DragMove(); } catch { }
            RememberPosition();
        }

        void OnRightClick(object s, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_card.ContextMenu != null)
            {
                _card.ContextMenu.PlacementTarget = _card;
                _card.ContextMenu.IsOpen = true;
            }
        }

        void OnWheel(object s, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                NudgeOpacity(e.Delta > 0 ? 0.05 : -0.05);
            }
            else
            {
                NudgeScale(e.Delta > 0 ? 0.06 : -0.06);
            }
        }

        void OnBarClick(object s, MouseButtonEventArgs e)
        {
            var np = _smtc.Current;
            if (np == null || !np.Has || np.Dur.TotalSeconds < 5) return;
            var el = (FrameworkElement)s;
            double x = e.GetPosition(el).X;
            double frac = x / Math.Max(1, el.ActualWidth);
            _smtc.Seek(frac * np.Dur.TotalSeconds);
            e.Handled = true;
        }

        // --- hooks used by the settings panel (live preview) ---
        public void ApplyCurrentScale()
        {
            ApplyScale();
        }

        public void ApplyCurrentOpacity()
        {
            Opacity = _cfg.Opacity;
        }

        public void SetListenAll(bool on)
        {
            _cfg.ListenAll = on;
            _smtc.ListenAll = on;
            _cfg.Save();
        }

        // Re-runs the cover treatment (saturation / highlight roll-off) on the current cover.
        public void RefreshArt()
        {
            if (_lastArt != null && _lastArt.Length > 0) ApplyArt(_lastArt);
            else ApplyBackdrop();
        }

        SettingsWindow _settings;

        /// <summary>The main page lives in its own window; the card just opens it.</summary>
        public void OpenMainPage()
        {
            try { MainWindow.Open(_cfg, this); }
            catch (Exception ex) { LyricsService.Log("main page failed: " + ex.GetType().Name + ": " + ex.Message); }
        }

        public void OpenSettings()
        {
            // One settings surface only: the appearance page of the main window. The old
            // standalone panel (SettingsWindow) is retired but left compiled so the change
            // can be reverted in a single commit.
            try
            {
                var mw = MainWindow.Open(_cfg, this);
                mw.ShowPage("look");
                LyricsService.Log("settings -> appearance page");
                return;
            }
            catch (Exception ex) { LyricsService.Log("settings page failed: " + ex.Message); }
            if (_settings != null)
            {
                try { _settings.Activate(); _settings.Focus(); } catch { }
                return;
            }
            _settings = new SettingsWindow(_cfg, this);
            _settings.Closed += delegate { _settings = null; Native.BringToTop(_hwnd); };
            _settings.Show();
            Rect wa = WorkArea();
            try
            {
                _settings.Left = wa.Left + (wa.Width - _settings.ActualWidth) / 2;
                _settings.Top = wa.Top + (wa.Height - _settings.ActualHeight) / 2;
            }
            catch { }
            LyricsService.Log("settings at " + _settings.Left + "," + _settings.Top + " size " + _settings.ActualWidth + "x" + _settings.ActualHeight + " dpi " + DpiScale());
        }

        public void NudgeScale(double delta)
        {
            _cfg.Scale = Config.Clamp(_cfg.Scale + delta, 0.55, 2.2);
            ApplyScale();
        }

        void ApplyScale()
        {
            double oldW = (CardWidthNow() + ShadowPad * 2) * ((ScaleTransform)_shadowHost.LayoutTransform).ScaleX;
            var st = (ScaleTransform)_shadowHost.LayoutTransform;
            st.ScaleX = _cfg.Scale;
            st.ScaleY = _cfg.Scale;
            UpdateLayout();
            double newW = (CardWidthNow() + ShadowPad * 2) * _cfg.Scale;
            if (!double.IsNaN(Left)) Left = Left + (oldW - newW);
            ClampToScreen();
            RememberPosition();
            _cfg.Save();
        }

        public void NudgeOpacity(double delta)
        {
            _cfg.Opacity = Config.Clamp(_cfg.Opacity + delta, 0.25, 1.0);
            Opacity = _cfg.Opacity;
            _cfg.Save();
        }

        public void NudgeOffset(double delta)
        {
            _cfg.LyricOffset += delta;
            _cfg.Save();
        }

        public void ToggleLock()
        {
            _cfg.Locked = !_cfg.Locked;
            _cfg.Save();
        }

        public void ToggleClearBg()
        {
            SetClearBg(!_cfg.ClearBg);
        }

        public void SetArtSpin(bool on)
        {
            _cfg.ArtSpin = on;
            _cfg.Save();
            if (!on) SpinStop();
        }

        public void ToggleArtSpin()
        {
            SetArtSpin(!_cfg.ArtSpin);
        }

        public void SetClearBg(bool on)
        {
            _cfg.ClearBg = on;
            ApplyBackdrop();
            _cfg.Save();
        }

        void RememberPosition()
        {
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            _cfg.Left = Left;
            _cfg.Top = Top;
        }

        // ---------------- context menu ----------------

        ContextMenu BuildMenu()
        {
            // Deliberately short: the card menu is the quick path (open the main page, fix the
            // lyrics, nudge timing, lock, quit). Everything cosmetic lives in the main page.
            var m = new ContextMenu();
            m.Items.Add(Item(Strings.T("menu.main"), delegate { OpenMainPage(); }));
            m.Items.Add(new Separator());
            m.Items.Add(Item(Strings.T("menu.refetch"), delegate { Refetch(); }));
            m.Items.Add(Item(Strings.T("menu.switchSrc"), delegate
            {
                // one-shot: ask the service to ignore the source that produced the current doc
                LyricsService.SkipSource = (_doc != null) ? _doc.Source : "";
                Refetch();
            }));
            m.Items.Add(Item(Strings.T("menu.pickLyric"), delegate { OpenCandidates(); }));
            m.Items.Add(new Separator());
            m.Items.Add(Item(Strings.T("menu.lyricEarly"), delegate { NudgeOffset(0.1); }));
            m.Items.Add(Item(Strings.T("menu.lyricLate"), delegate { NudgeOffset(-0.1); }));
            m.Items.Add(new Separator());
            var lockItem = new MenuItem { Header = Strings.T("menu.lock"), IsCheckable = true, IsChecked = _cfg.Locked };
            lockItem.Click += delegate { ToggleLock(); lockItem.IsChecked = _cfg.Locked; };
            m.Items.Add(lockItem);
            m.Items.Add(new Separator());
            m.Items.Add(Item(Strings.T("menu.close"), delegate { Quit(); }));
            m.Items.Add(new Separator());
            var cancelItem = Item(Strings.T("menu.cancel"), null);
            cancelItem.Click += delegate { m.IsOpen = false; };
            m.Items.Add(cancelItem);
            MenuTheme.Apply(m);
            m.Opened += delegate
            {
                // While the menu is open the 1.5s topmost re-check must stand down, otherwise
                // it lifts the card above the menu's own popup window.
                _menuOpen = true;
                lockItem.IsChecked = _cfg.Locked;
                // Measure the menu live inside the predicate: at Opened time the popup has not
                // been laid out yet (ActualWidth is the 208px MinWidth), so a rect captured once
                // would mark every click as "outside" and the hook would swallow it - menu items
                // would never fire.
                // This window is WS_EX_NOACTIVATE, so the popup never gets mouse capture and
                // WPF will not close it on an outside click by itself - a low level hook does it.
                // The hook must measure the *popup root*, not the ContextMenu: m.ActualHeight
                // reports 627 while the rendered menu is ~700 tall, which made real item clicks
                // look like outside clicks (they were then swallowed).
                try
                {
                    Native.BeginOutsideClickWatch(
                        delegate(int x, int y)
                        {
                            try
                            {
                                PresentationSource src = PresentationSource.FromVisual(m);
                                FrameworkElement root = (src != null) ? (src.RootVisual as FrameworkElement) : null;
                                if (root == null) return true;
                                Point p = root.PointToScreen(new Point(0, 0));
                                double left = p.X, top = p.Y;
                                double right = p.X + root.ActualWidth, bottom = p.Y + root.ActualHeight;
                                // The custom template draws taller than the popup reports, so grow the
                                // rect by the real item bounds - otherwise lower items read as "outside".
                                foreach (object o in m.Items)
                                {
                                    var fe = o as FrameworkElement;
                                    if (fe == null || !fe.IsVisible || fe.ActualHeight <= 0) continue;
                                    Point b = fe.PointToScreen(new Point(fe.ActualWidth, fe.ActualHeight));
                                    if (b.X > right) right = b.X;
                                    if (b.Y > bottom) bottom = b.Y;
                                }
                                if (right - left < 20 || bottom - top < 20) return true;
                                return x >= left && x <= right && y >= top && y <= bottom;
                            }
                            catch { return true; }
                        },
                        delegate { m.IsOpen = false; });
                }
                catch { }
            };
            // A menu is its own topmost popup; once it closes the card can end up behind the
            // window the user clicked from, so put it back on top right away.
            m.Closed += delegate
            {
                Native.EndOutsideClickWatch();
                _menuOpen = false;
                Native.BringToTop(_hwnd);
            };
            return m;
        }

        static MenuItem Item(string text, Action act)
        {
            var mi = new MenuItem { Header = text };
            if (act != null) mi.Click += delegate { act(); };
            return mi;
        }

        /// <summary>
        /// Show the ranked candidates for the current track and let the user override the pick.
        /// Searching is a network round-trip, so the window opens immediately with a "searching"
        /// line and fills itself from a worker thread.
        /// </summary>
        public void OpenCandidates()
        {
            Snapshot np = _smtc.Current;
            LyricsService.Log("picker requested title=" + (np == null ? "null" : np.Title) + " artist=" + (np == null ? "" : np.Artist));
            if (np == null || string.IsNullOrEmpty(np.Title)) return;
            string t = np.Title;
            string a = np.Artist;
            double d = np.Dur.TotalSeconds;
            string sub = string.IsNullOrEmpty(a) ? t : t + " — " + a;

            CandidateWindow win;
            try
            {
                win = new CandidateWindow(Strings.T("menu.pickLyric"), sub);
            }
            catch (Exception ex)
            {
                LyricsService.Log("picker ctor fail " + ex.GetType().Name + ": " + ex.Message);
                return;
            }
            win.Pick = delegate(LyricChoice ch)
            {
                var doc = _lyr.Fetch(ch, t, a, d);
                if (doc == null || doc.Lines == null || doc.Lines.Count < 3) return;
                _doc = doc;
                _lineIdx = -1;
                _lineText = "";
                UpdateLyric();
                LyricsService.Log("lyric picker chose " + ch.Source + " " + doc.Matched);
            };
            try
            {
                win.Show();
                win.Activate();
                LyricsService.Log("picker shown");
            }
            catch (Exception ex)
            {
                LyricsService.Log("picker show fail " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            var worker = new System.Threading.Thread(delegate()
            {
                List<LyricChoice> list = null;
                try { list = _lyr.Candidates(t, a, d); }
                catch (Exception ex) { LyricsService.Log("cand fail " + ex.GetType().Name); }
                Dispatcher.BeginInvoke(new Action(delegate { win.SetCandidates(list); }));
            });
            worker.IsBackground = true;
            worker.Start();
        }

        public void UserToggle()
        {
            _suppressed = !_suppressed;
            if (_suppressed)
            {
                HideAnimated();
            }
            else
            {
                var np = _smtc.Current;
                if (np != null && np.Has) ShowAnimated();
            }
        }

        public void Quit()
        {
            RememberPosition();
            _cfg.Save();
            // Fade the card out first, then shut down: a window that vanishes instantly reads
            // as a crash, not as "quit".
            var seq = ++_visSeq;
            LyricsService.Log("quit: fade start");
            AnimateCard(0.0, 0.0, 0.988, 90, null);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            t.Tick += delegate
            {
                t.Stop();
                if (seq != _visSeq) return;
                LyricsService.Log("quit: shutdown");
                Application.Current.Shutdown();
            };
            t.Start();
        }

        // ---------------- entrance / exit motion ----------------
        // Apple-ish: short, no bounce, tiny travel. The card settles into place from its own
        // top-right corner instead of popping in, and leaves with a quicker, quieter fade.

        void EnsureIntroTransforms()
        {
            if (_inScale != null) return;
            _inScale = new ScaleTransform(1.0, 1.0);
            _inShift = new TranslateTransform(0.0, 0.0);
            var tg = new TransformGroup();
            tg.Children.Add(_inScale);
            tg.Children.Add(_inShift);
            _card.RenderTransformOrigin = new Point(1.0, 0.0);
            _card.RenderTransform = tg;
        }

        void ClearIntroAnimations()
        {
            if (_inScale == null) return;
            _shadowHost.BeginAnimation(UIElement.OpacityProperty, null);
            _inScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _inScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _inShift.BeginAnimation(TranslateTransform.YProperty, null);
        }

        void AnimateCard(double toOpacity, double toShiftY, double toScale, int ms, EventHandler done)
        {
            EnsureIntroTransforms();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

            var fo = new DoubleAnimation(_shadowHost.Opacity, toOpacity, TimeSpan.FromMilliseconds(ms));
            fo.EasingFunction = toOpacity > _shadowHost.Opacity ? ease : easeIn;
            _shadowHost.BeginAnimation(UIElement.OpacityProperty, fo);

            var fy = new DoubleAnimation(_inShift.Y, toShiftY, TimeSpan.FromMilliseconds(ms));
            fy.EasingFunction = ease;
            _inShift.BeginAnimation(TranslateTransform.YProperty, fy);

            var fs = new DoubleAnimation(_inScale.ScaleX, toScale, TimeSpan.FromMilliseconds(ms));
            fs.EasingFunction = ease;
            if (done != null) fs.Completed += done;
            _inScale.BeginAnimation(ScaleTransform.ScaleXProperty, fs);
            _inScale.BeginAnimation(ScaleTransform.ScaleYProperty, fs.Clone());
        }

        public void ShowAnimated()
        {
            EnsureIntroTransforms();
            ++_visSeq;
            if (!IsVisible)
            {
                ClearIntroAnimations();
                _shadowHost.Opacity = 0.0;
                _inScale.ScaleX = 0.965;
                _inScale.ScaleY = 0.965;
                _inShift.Y = 8.0;
                Show();
                Native.BringToTop(_hwnd);
            }
            AnimateCard(1.0, 0.0, 1.0, 220, null);
        }

        public void HideAnimated()
        {
            if (!IsVisible) return;
            // Freeze whatever the entrance left on screen into the base values first: clearing
            // an animation snaps back to the base, which would make the card vanish instantly.
            var curOp = _shadowHost.Opacity;
            var curSc = _inScale.ScaleX;
            var curY = _inShift.Y;
            ClearIntroAnimations();
            _shadowHost.Opacity = curOp;
            _inScale.ScaleX = curSc;
            _inScale.ScaleY = curSc;
            _inShift.Y = curY;
            var seq = ++_visSeq;
            // 47 ms: three times faster than the old 140 ms. Closing is something the user
            // asked for, so it should feel instant -- only the entrance gets to take its time.
            AnimateCard(0.0, 0.0, 0.990, 47, delegate
            {
                if (seq == _visSeq) Hide();
            });
        }

        // ---------------- data tick ----------------

        // TextBlock's own trimming reserves room for the ellipsis, which pushes the
        // visible right edge away from the column edge. Measure and cut by hand so the
        // title/artist line, the lyric, the bar and the buttons share one right edge.
        // Keep the artist line short so a long list of names cannot eat the whole row.
        string ClipArtist(string a)
        {
            if (string.IsNullOrEmpty(a)) return "";
            int max = _cfg.ArtistMaxChars;
            if (max < 4) max = 4;
            if (a.Length <= max) return a;
            string cut = a.Substring(0, max - 1).TrimEnd();
            return cut + "…";
        }

        /// <summary>
        /// Truncate the title so title + artist fit the column on one line. Measured with
        /// FormattedText at the *scaled* font sizes (TextTrimming would reserve the ellipsis and
        /// push the right edge inward instead of filling the line).
        /// </summary>
        string FitTitle(string ttl, string art)
        {
            double ts = _baseTitleSize * _cfg.TitleScale;
            double asz = _baseArtistSize * _cfg.ArtistScale;
            double avail = (_content != null && _content.ActualWidth > 1)
                ? _content.ActualWidth
                : Math.Max(120, NaturalCol() * _cfg.BarScale);
            avail -= _cfg.TitleIndent;
            if (avail < 24) avail = 24;
            double pw = 1.0;
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src != null && src.CompositionTarget != null) pw = src.CompositionTarget.TransformToDevice.M11;
            }
            catch { }
            double wa = MeasureW(art, asz, FontWeights.Normal, pw);
            if (MeasureW(ttl, ts, FontWeights.SemiBold, pw) + wa <= avail) return ttl;
            string cut = ttl;
            while (cut.Length > 4)
            {
                cut = cut.Substring(0, cut.Length - 1);
                string cand = cut + "…";
                if (MeasureW(cand, ts, FontWeights.SemiBold, pw) + wa <= avail) return cand;
            }
            return cut;
        }

        double EdgeX0(FrameworkElement el)
        {
            try { return el.PointToScreen(new Point(0, 0)).X; }
            catch { return -1; }
        }

        double EdgeX(FrameworkElement el)
        {
            try
            {
                return Math.Round(el.PointToScreen(new Point(el.ActualWidth, 0)).X, 1);
            }
            catch { return -1; }
        }

        double EdgeY(FrameworkElement el)
        {
            try
            {
                return Math.Round(el.PointToScreen(new Point(0, 0)).Y, 1);
            }
            catch { return -1; }
        }

        double DpiScale()
        {
            var src = PresentationSource.FromVisual(this);
            if (src == null || src.CompositionTarget == null) return 1.0;
            return src.CompositionTarget.TransformToDevice.M11;
        }

        double MeasureW(string text, double size, FontWeight weight, double pw)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                       new Typeface(_title.FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
                                       size, Brushes.White, pw);
            return ft.WidthIncludingTrailingWhitespace;
        }

        void Tick(object sender, EventArgs e)
        {
            // Nothing else is allowed to bury the card: re-assert the topmost band position
            // about twice a second (cheap, does not activate or repaint).
            if (++_topTicks >= 45)
            {
                _topTicks = 0;
                if (IsVisible && !_menuOpen && !Native.ForegroundIsShell()) Native.BringToTop(_hwnd);
            }

            var np = _smtc.Current;
            if (np == null || !np.Has)
            {
                // Autostart (--background): stay completely off the desktop until a player
                // publishes a session.
                if (HideWhenIdle)
                {
                    if (IsVisible && !_suppressed) HideAnimated();
                    _noPlayer = true;
                    _idle = false;
                    return;
                }
                // Nothing publishes a session, yet the speakers are busy. Some players
                // (NetEase Cloud Music in particular) only publish when their own SMTC
                // switch is on; staying invisible there looks like the app died.
                if (DateTime.Now - _hintAt > TimeSpan.FromSeconds(1.0))
                {
                    _hintAt = DateTime.Now;
                    var ids = new System.Collections.Generic.HashSet<string>();
                    string[] seen = SmtcService.SessionAumids;
                    if (seen != null) for (int i = 0; i < seen.Length; i++) ids.Add(seen[i] ?? "");
                    _hintPlayer = AudioPeak.AudiblePlayerWithoutSession(ids);
                }
                if (_hintPlayer != null) _audibleTicks++; else _audibleTicks = 0;
                if (_audibleTicks >= 4)
                {
                    _noPlayer = false;
                    _idle = false;
                    if (!IsVisible && !_suppressed) ShowAnimated();
                    _doc = null;
                    _docKey = "";
                    _pendingKey = "";
                    _lineIdx = -1;
                    _lineText = "";
                    _pos = 0;
                    _runTitle.Text = "Lyricard";
                    _runArtist.Text = Strings.T("app.version");
                    _lyric.Text = Strings.F("idle.nosession.line", _hintPlayer ?? "");
                    _barRow.Visibility = Visibility.Collapsed;
                    _controls.Visibility = Visibility.Collapsed;
                    _artHost.Opacity = 0.0;
                    SetPlayIcon(false, false);
                    SpinStop();
                    return;
                }

                // Something is publishing a session, but the user did not bind it. Hiding
                // silently would read as "the app is broken", so show a one-line hint card.
                string unbound = SmtcService.UnboundAumid;
                if (unbound != null && unbound.Length > 0 && !HideWhenIdle)
                {
                    _noPlayer = false;
                    _idle = false;
                    if (!IsVisible && !_suppressed) ShowAnimated();
                    _doc = null;
                    _docKey = "";
                    _pendingKey = "";
                    _lineIdx = -1;
                    _lineText = "";
                    _pos = 0;
                    _runTitle.Text = "Lyricard";
                    _runArtist.Text = Strings.T("app.version");
                    _lyric.Text = Strings.F("idle.unbound.line", Players.DisplayName(unbound));
                    _barRow.Visibility = Visibility.Collapsed;
                    _controls.Visibility = Visibility.Collapsed;
                    _artHost.Opacity = 0.0;
                    SetPlayIcon(false, false);
                    SpinStop();
                    return;
                }

                // Apple Music creates no media session until it actually plays something,
                // but the master expects the card to appear together with the app. So while
                // the app is running we stay on screen in an idle state and only hide when
                // it is gone for good.
                if (AmProbe.IsRunning() && SmtcService.AppleMusicWanted)
                {
                    _noPlayer = false;
                    if (!IsVisible && !_suppressed) ShowAnimated();
                    if (!_idle)
                    {
                        _idle = true;
                        _doc = null;
                        _docKey = "";
                        _pendingKey = "";
                        _lineIdx = -1;
                        _lineText = "";
                        _runTitle.Text = "Apple Music";
                        _runArtist.Text = "";
                        _lyric.Text = TxtWait;
                        _barFill.Width = 0;
                        _barRow.Visibility = Visibility.Collapsed;
                        _controls.Visibility = Visibility.Collapsed;
                        _pos = 0;
                        _artHost.Opacity = 0.0;
                        _playingShown = false;
                        SetPlayIcon(false, false);
                        SpinStop();
                    }
                    return;
                }
                // Apple Music is closed: keep the desktop clean. The way in is the tray icon
                // or the shortcut that opens the main window - no "nothing found" card.
                if (IsVisible && !_suppressed) HideAnimated();
                _noPlayer = true;
                _idle = false;
                return;
            }
            _idle = false;
            _noPlayer = false;
            if (!IsVisible && !_suppressed) ShowAnimated();
            if (_artHost.Opacity < 1.0) _artHost.Opacity = 1.0;
            if (_barRow.Visibility != Visibility.Visible)
            {
                _barRow.Visibility = Visibility.Visible;
                _controls.Visibility = Visibility.Visible;
                FadeIn(_barRow, 200);
                FadeInTo(_controls, IsMouseOver ? 1.0 : ControlsIdle, 200);
            }

            if (_playCmdActive)
            {
                // Hold the optimistic icon until the player confirms, or until 1.5 s have passed -
                // the stale snapshot must not flip it back in the meantime.
                if (np.Playing == _playCmdWant || (DateTime.Now - _playCmdAt).TotalSeconds > 1.5)
                    _playCmdActive = false;
            }
            else if (np.Playing != _playingShown)
            {
                SetPlayIcon(np.Playing, true);
            }
            if (np.Playing) SpinResume(); else SpinPause();

            var artistOut = "";
            var albumOut = "";
            TextUtil.SplitArtistAlbum(np.Artist, out artistOut, out albumOut);

            string ttl = string.IsNullOrEmpty(np.Title) ? "Apple Music" : np.Title;
            string art = string.IsNullOrEmpty(artistOut) ? "" : "   " + ClipArtist(artistOut);
            _runTitle.Text = FitTitle(ttl, art);
            _runArtist.Text = art;
            if (np.Key != _titleKey)
            {
                _titleKey = np.Key;
                FadeIn(_title, 320);
            }

            // art
            if (np.Art != null && !ReferenceEquals(np.Art, _lastArt))
            {
                _lastArt = np.Art;
                ApplyArt(np.Art);
            }

            // lyric document
            string key = np.Title + "|" + artistOut;
            if (key != _docKey && key != _pendingKey)
            {
                LyricsService.Log("fetch start t=" + np.Title + " | a=" + artistOut + " | dur=" + ((int)np.Dur.TotalSeconds));
                _pendingKey = key;
                _doc = null;
                _docKey = "";
                // Lyrics are fetched on a background thread, which can take a second or
                // two. Keep something in the lyric line meanwhile instead of leaving it blank.
                _lineText = TxtWait;
                _lyric.Text = TxtWait;
                Animate(_lyric, 1.0, 200);
                double dur = np.Dur.TotalSeconds;
                string t = np.Title, a = artistOut;
                var th = new System.Threading.Thread(delegate ()
                {
                    LyricDoc doc = null;
                    try { doc = _lyr.Get(t, a, dur, false); }
                    catch { doc = null; }
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (_pendingKey != key) return;
                        _doc = doc;
                        _docKey = key;
                        _lineIdx = -1;
                        if (doc == null || doc.Lines.Count == 0)
                        {
                            if (doc != null && doc.Instrumental)
                            {
                                _lineText = TxtInstr;
                                _lyric.Text = TxtInstr;
                            }
                            else
                            {
                                _lineText = TxtNone;
                                _lyric.Text = TxtNone;
                            }
                            Animate(_lyric, 1.0, 150);
                        }
                    }));
                });
                th.IsBackground = true;
                th.Start();
            }

            // progress: run our own clock at 1x and *slew* toward the SMTC estimate instead of
            // snapping to it. Apple's Position only lands every ~3s, so snapping made the lyric
            // line jump back and forth right on line boundaries.
            var nowT = DateTime.Now;
            double dt = (nowT - _lastTick).TotalSeconds;
            if (dt < 0) dt = 0;
            if (dt > 0.5) dt = 0.5;
            _lastTick = nowT;
            // New song: start the bar at zero instead of carrying the old position over. This used
            // to happen by accident (the old anchor snapped us to the reported 0.0); now that an
            // untrusted player is never snapped, it has to be explicit.
            string trackKey = (np.Aumid ?? "") + "|" + (np.Title ?? "");
            if (trackKey != _trackKey)
            {
                _trackKey = trackKey;
                _pos = 0;
                _lineIdx = -1;
                _switchUntil = nowT.AddMilliseconds(900);
            }
            else if (_lastPos - _pos > 3.0
                && !(np.Dur.TotalSeconds > 1 && _lastPos > np.Dur.TotalSeconds - 1.5))
            {
                // Same title but the position fell off a cliff: this is the preview/track
                // boundary, not a user seek. Treat it as a switch so it renders once.
                // (A song that simply looped also jumps back to zero -- that one must not
                // flash "waiting", so it is excluded by the "was at the very end" test.)
                _switchUntil = nowT.AddMilliseconds(900);
            }
            _lastPos = _pos;
            if (nowT < _switchUntil)
            {
                // Settling: one clean state, no half-updated lyric lines.
                _pos = 0;
                _lineIdx = -1;
                _lineText = TxtWait;
                _lyric.Text = TxtWait;
                _barFill.Width = 0;
                return;
            }
            double target = np.EstimateSeconds(nowT);
            // A player that keeps reporting the same position (NetEase: 0.35 forever, and 0.0 while
            // paused) must never be allowed to yank the bar back - that is what looked like
            // "paused, and the progress restarted".
            bool trustTarget = !np.PosFrozen && !np.PosUntrusted;
            if (_pos < 0 || (trustTarget && Math.Abs(target - _pos) > 1.2))
            {
                _pos = target;
            }
            else
            {
                if (np.Playing) _pos += dt;
                double err = target - _pos;
                // A player whose timestamps proved fake gets no correction at all - the sliver of
                // slew would otherwise drag the bar back to its fake zero over a minute.
                double corr = trustTarget ? err * 0.10 : 0.0;
                if (!trustTarget)
                {
                    // Untrusted player: follow its timeline instead of free-running away from it
                    // (NetEase's own clock can tick slower than wall time, which used to leave the
                    // bar half a minute ahead of the lyrics). Forward only - the position never
                    // decreases, so pausing can not look like a restart.
                    if (np.Playing)
                    {
                        if (target > _pos) _pos = target;
                        // never lead the player's own timeline by more than a hair, or the lyrics
                        // would run a second early on top of the player's own lead
                        else if (_pos - target > 0.35) _pos = target + 0.35;
                    }
                }
                if (corr > 0.04) corr = 0.04;
                if (corr < -0.04) corr = -0.04;
                _pos += corr;
                if (_pos < 0) _pos = 0;
                double total = np.Dur.TotalSeconds;
                if (np.Playing && total > 1 && _pos > total - 0.4)
                {
                    // the player looped or the clock overran: go back to the first line
                    _pos = 0;
                    _lineIdx = -1;
                }
            }
            _tickCount++;
            if (_tickCount % 60 == 0)
                LyricsService.Log("clock pos=" + _pos.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    + " target=" + target.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    + " err=" + (target - _pos).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    + " play=" + np.Playing + " smtc=" + np.Pos.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    + " frozen=" + np.PosFrozen + " dur=" + np.Dur.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    + " untrusted=" + np.PosUntrusted + " key=" + np.Title.Substring(0, Math.Min(6, np.Title.Length)));

            // Some clients (NetEase Cloud Music among them) never report a duration, which used
            // to leave the progress bar pinned at zero. Fall back to the last lyric timestamp so
            // the bar still moves, and keep 0 only when there is nothing to go on.
            double dur2 = np.Dur.TotalSeconds;
            // A reported duration is only trusted while the position still fits inside it:
            // QQ Music is known to report a wrong running time for some tracks (a 7:31 song
            // came back as 21.7s), and trusting that pins the bar at 100% for the whole song.
            bool durTrusted = dur2 > 1 && _pos <= dur2 * 1.2 + 3.0;
            // Guessing the length from the last lyric line only makes sense for the players we
            // know report nothing useful (NetEase & friends). For everyone else a missing
            // duration means the file simply has no length to report (foobar2000 local files),
            // and guessing from possibly-wrong lyrics would invent a bogus progress.
            if (!durTrusted && Players.IsLaggyTimeline(np.Aumid)
                && _doc != null && _doc.Lines != null && _doc.Lines.Count > 0)
                dur2 = _doc.Lines[_doc.Lines.Count - 1].Time + 5.0;
            else if (!durTrusted) dur2 = 0;
            // When nothing tells us how long the track is (local files under foobar2000 / VLC
            // report a position but no end time) the bar keeps its shape as a static, empty
            // track -- it just never advances, which reads as "no progress information" rather
            // than a stalled progress bar.
            double frac = dur2 > 1 ? Config.Clamp(_pos / dur2, 0, 1) : 0;
            _barFill.Width = Math.Max(0, _barTrack.ActualWidth * frac);
            // A zero-width rounded Border still paints a 1px nub on the left; without a length
            // the bar is decoration only, so keep the fill out of the picture entirely.
            _barFill.Visibility = dur2 > 1 ? Visibility.Visible : Visibility.Collapsed;


            UpdateLyric();
        }

        static string Fmt(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int s = (int)Math.Floor(seconds);
            int m = s / 60;
            int r = s % 60;
            if (m >= 60) return string.Format("{0}:{1:00}:{2:00}", m / 60, m % 60, r);
            return string.Format("{0}:{1:00}", m, r);
        }

        /// <summary>
        /// Pick the active line for the current position and push it to the lyric TextBlock.
        /// Hysteresis: once a line is showing it stays until the position passes the next line's
        /// timestamp minus 0.35s, so jitter around a boundary cannot flip back and forth.
        /// </summary>
        void UpdateLyric()
        {
            if (_doc == null || _doc.Lines.Count == 0) return;
            double t = _pos + _cfg.LyricOffset;
            var lines = _doc.Lines;
            int idx = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Time <= t) idx = i;
                else break;
            }
            // Hysteresis: a small clock correction must not bounce the display back to the
            // previous line, otherwise the lyric flickers between two lines.
            if (_lineIdx >= 0 && _lineIdx < lines.Count && idx < _lineIdx && t > lines[_lineIdx].Time - 0.35)
                idx = _lineIdx;
            _lineIdx = idx;
            string text;
            // Before the first line (intros can be a minute long) there is nothing to show yet;
            // a bare note looks like a rendering glitch, so use the same placeholder as
            // while lyrics are loading.
            if (idx < 0) text = TxtWait;
            else text = lines[idx].Text;

            if (text != _lineText)
            {
                LyricsService.Log("line idx=" + idx + " at=" + t.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + text);
                _lineText = text;
                _lineShownAt = DateTime.Now;
                _lineFaded = false;
                _lyric.Text = text;
                Animate(_lyric, 1.0, 260);
                var tt = (TranslateTransform)_lyric.RenderTransform;
                var slide = new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(300));
                slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                tt.BeginAnimation(TranslateTransform.YProperty, slide);
            }
            else if (!_lineFaded && (DateTime.Now - _lineShownAt).TotalSeconds > 6.0)
            {
                _lineFaded = true;
                Animate(_lyric, 0.55, 900);
            }
        }

        void Animate(UIElement el, double to, int ms)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        // Desaturate the cover and let its darkest pixels go semi-transparent so the
        // wallpaper shows through. WPF has no built-in effect for "luminance to alpha"
        // and the pixel-shader route needs fxc.exe (not installed here), so do it on the
        // CPU once per track: at most 192x192 = 36k pixels, well under a millisecond.
        const double HiKnee = 0.62;

        /// <summary>Opacity of the transport row when the pointer is not over the card.
        /// Hovering animates it to 1.0. Was 0.62 before 2026-11-22 (+10 points).</summary>
        const double ControlsIdle = 0.72;

        static BitmapSource FadeArt(BitmapSource src, double sat, double floorA, double hiCut)
        {
            var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var buf = new byte[stride * h];
            bgra.CopyPixels(buf, stride, 0);
            double maxIn = 0, maxOut = 0, sumIn = 0, sumOut = 0;
            int npx = 0;
            for (int i = 0; i < buf.Length; i += 4)
            {
                double b = buf[i], g = buf[i + 1], r = buf[i + 2], a = buf[i + 3];
                double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                if (lum > maxIn) maxIn = lum;
                sumIn += lum;
                r = lum + (r - lum) * sat;
                g = lum + (g - lum) * sat;
                b = lum + (b - lum) * sat;
                // Soft highlight rolloff: leave everything below the knee alone and pull the
                // bright end down by up to hiCut (12% at pure white), so a bright cover stops
                // glowing without looking dimmed.
                if (hiCut > 0.0005)
                {
                    double lt = lum / 255.0;
                    double kt = (lt - HiKnee) / (1.0 - HiKnee);
                    if (kt > 0.0)
                    {
                        if (kt > 1.0) kt = 1.0;
                        double f = 1.0 - hiCut * kt;
                        r *= f; g *= f; b *= f;
                        lum *= f;
                    }
                }
                double t = lum / 255.0;
                double alpha = 255.0 * (floorA + (1.0 - floorA) * Math.Pow(t, 0.85));
                if (alpha > 255.0) alpha = 255.0;
                if (alpha < 0.0) alpha = 0.0;
                alpha = alpha * (a / 255.0);
                if (lum > maxOut) maxOut = lum;
                sumOut += lum; npx++;
                buf[i] = (byte)(b < 0 ? 0 : (b > 255 ? 255 : b));
                buf[i + 1] = (byte)(g < 0 ? 0 : (g > 255 ? 255 : g));
                buf[i + 2] = (byte)(r < 0 ? 0 : (r > 255 ? 255 : r));
                buf[i + 3] = (byte)alpha;
            }
            LyricsService.Log("fadeart sat=" + sat + " floor=" + floorA + " hi=" + hiCut + " maxLum=" + maxIn.ToString("0.0") + " -> " + maxOut.ToString("0.0") + " meanIn=" + (sumIn / Math.Max(1, npx)).ToString("0.0") + " meanOut=" + (sumOut / Math.Max(1, npx)).ToString("0.0"));
            var res = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, stride);
            res.Freeze();
            return res;
        }

        // Slow vinyl-style spin. One turn takes 36 s so it reads as drift, not motion.
        // Paused with the music and reset when the card goes back to the idle state.
        void SpinResume()
        {
            if (!_cfg.ArtSpin) return;
            if (_spin == null)
            {
                var a = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(36));
                a.RepeatBehavior = RepeatBehavior.Forever;
                _spin = a.CreateClock();
                _artRotate.ApplyAnimationClock(RotateTransform.AngleProperty, _spin);
                LyricsService.Log("art spin start");
            }
            else if (_spin.CurrentState == ClockState.Stopped)
            {
                _spin.Controller.Begin();
            }
            else
            {
                _spin.Controller.Resume();
            }
        }

        void SpinPause()
        {
            if (_spin != null && _spin.CurrentState == ClockState.Active)
            {
                _spin.Controller.Pause();
            }
        }

        void SpinStop()
        {
            if (_spin != null)
            {
                _spin.Controller.Remove();
                _spin = null;
            }
            try { _artRotate.ApplyAnimationClock(RotateTransform.AngleProperty, null); _artRotate.Angle = 0; }
            catch { }
        }

        void ApplyArt(byte[] bytes)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                // NOTE: BitmapCreateOptions.IgnoreImageCache + OnLoad + StreamSource throws
                // ArgumentNullException("key") in ImagingCache.RemoveFromCache on .NET Fx.
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(bytes);
                bi.DecodePixelWidth = 192;
                bi.EndInit();
                bi.Freeze();

                BitmapSource shown = bi;
                if (_cfg.ArtSat < 0.999 || _cfg.ArtBlackAlpha < 0.999 || _cfg.ArtHiCut > 0.0005)
                    shown = FadeArt(bi, _cfg.ArtSat, _cfg.ArtBlackAlpha, _cfg.ArtHiCut);

                var brush = new ImageBrush(shown) { Stretch = Stretch.UniformToFill };
                FadeIn(_artEllipse, 280);
                _artEllipse.Fill = brush;

                var c = AverageColor(bi);
                _artBg = Color.FromArgb(238,
                    (byte)Math.Min(255, 12 + c.R * 0.16),
                    (byte)Math.Min(255, 12 + c.G * 0.16),
                    (byte)Math.Min(255, 14 + c.B * 0.18));
                _hasArtBg = true;
                ApplyBackdrop();
            }
            catch (Exception ex) { LyricsService.Log("applyart fail " + ex.GetType().Name + " " + ex.Message + "\n" + ex.StackTrace); }
        }

        static Color AverageColor(BitmapSource src)
        {
            try
            {
                var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                conv.Freeze();
                int w = Math.Min(64, conv.PixelWidth), h = Math.Min(64, conv.PixelHeight);
                int stride = w * 4;
                var px = new byte[stride * h];
                conv.CopyPixels(new Int32Rect(0, 0, w, h), px, stride, 0);
                long r = 0, g = 0, b = 0, n = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    byte b0 = px[i], g0 = px[i + 1], r0 = px[i + 2];
                    if (r0 + g0 + b0 < 24) continue;
                    b += b0; g += g0; r += r0; n++;
                }
                if (n == 0) return Color.FromRgb(30, 30, 34);
                return Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
            }
            catch { return Color.FromRgb(30, 30, 34); }
        }

        /// <summary>Rebuild the context menu after an interface-language change.</summary>
        public void Relang()
        {
            Title = Strings.T("app.title");
            _card.ContextMenu = BuildMenu();
            // the replaced menu and its item handlers are otherwise kept alive forever
            if (TrayRef != null) TrayRef.Relang();
            if (_settings != null) _settings.Relang();
        }

        /// <summary>True when a media session is currently being shown.</summary>
        public bool HasSession
        {
            get { return _smtc != null && _smtc.Current.Has; }
        }

        /// <summary>Switch interface language from the context menu (0 = follow system).</summary>
        public void SetLang(int lang)
        {
            LyricsService.Log("set lang " + lang);
            _cfg.Lang = lang;
            _cfg.Save();
            Strings.Resolve(lang);
            Relang();
        }

        public void ExportLyrics()
        {
            int written, existed, skipped;
            string dir;
            try { dir = LyricsService.ExportToLocal(out written, out existed, out skipped); }
            catch { return; }
            LyricsService.Log("export lrc -> " + dir + " written=" + written + " existed=" + existed + " skipped=" + skipped);
            if (TrayRef != null)
                TrayRef.Balloon(Strings.T("export.title"), Strings.F("export.body", written, existed, skipped));
        }

        public void Refetch()
        {
            var np = _smtc.Current;
            if (np == null || !np.Has) return;
            string artistOut, albumOut;
            TextUtil.SplitArtistAlbum(np.Artist, out artistOut, out albumOut);
            string t = np.Title, a = artistOut;
            double dur = np.Dur.TotalSeconds;
            _lyric.Text = "♪ Searching lyrics…";
            _lineText = "♪ Searching lyrics…";
            var th = new System.Threading.Thread(delegate ()
            {
                LyricDoc doc = null;
                try { doc = _lyr.Get(t, a, dur, true); }
                catch { doc = null; }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _doc = doc;
                    _docKey = t + "|" + a;
                    _pendingKey = "";
                    _lineIdx = -1;
                    if (doc == null || doc.Lines.Count == 0)
                    {
                        if (doc != null && doc.Instrumental)
                        {
                            _lineText = TxtInstr;
                            _lyric.Text = TxtInstr;
                        }
                        else
                        {
                            _lineText = TxtNone;
                            _lyric.Text = TxtNone;
                        }
                    }
                    else
                    {
                        _lineText = "";
                    }
                }));
            });
            th.IsBackground = true;
            th.Start();
        }
    }
}
