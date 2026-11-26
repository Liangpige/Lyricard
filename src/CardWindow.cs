// ---------------------------------------------------------------------------
// CardWindow.cs
//
/// The desktop card itself: one always-on-top, click-through-where-empty window that
/// paints the artwork, the single lyric line, the progress bar and the three transport
/// buttons. Also owns the right-click menu, the drag/scale gestures and the idle states.
// ---------------------------------------------------------------------------
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
        Canvas _root;
        // Step 4: the card is a canvas of module hosts; LayoutEngine decides the rectangles and
        // ModuleRenderer pushes them onto these hosts (keeping the elements alive across passes).
        Canvas _canvas;
        FrameworkElement _hostTitle, _hostLyric, _hostArtist;
        TextBlock _artist;
        CardElements _elem;
        // Step 5: every hit test asks the layout where the modules are, not the visual tree.
        LayoutResult _layout;

        // Step 6: while the card is being dragged we only move content around; the window
        // size is committed once the mouse comes back up, so a transparent window does not
        // get resized dozens of times per second during the gesture.
        /// <summary>
        /// Raised whenever the model or the look changed. The Look page's preview listens to
        /// this instead of polling — one owner of the data, several views.
        /// </summary>
        public event Action LayoutChanged;
        /// <summary>Fires when a look setting changed: the board must redraw with the new fonts.</summary>
        public event Action LookChanged;

        bool _suspendSize;
        double _pendingW = -1, _pendingH = -1;
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
        FrameworkElement _content;
        Ellipse _artEllipse;
        Ellipse _artTop;
        Rectangle _artSquare;
        Rectangle _artSquareRing;
        public Tray TrayRef;
        Grid _artHost;
        ScaleTransform _artScale;
        RotateTransform _artRotate;
        bool _spinOn;
        double _spinAngle;

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
        // Debounce for the track key: SMTC re-reports the previous title for a moment when a
        // track ends and the player advances, which used to fire two switch transitions.
        string _seenKey = "";
        DateTime _seenSince = DateTime.MinValue;
        string _holdLogged = "";
        DateTime _unstableSince = DateTime.MinValue;
        // The track reached its end and the player has not announced the next one yet: freeze on
        // the last frame rather than resetting to zero on every tick.
        bool _endHold;
        DateTime _endHoldAt = DateTime.MinValue;
        // A VIP preview stops at 60 s and the player jumps straight into the next song: the
        // reported position snaps back to ~0 while the title lags behind for a beat or two.
        // Without a guard the bar and the lyric line lurch twice in 300 ms (""抽搐""). While a
        // switch is settling we hold one clean zero state instead of replaying each partial.
        DateTime _switchUntil = DateTime.MinValue;
        double _lastPos = 0;
        double _pos = -1;
        double _durShown;
        int _lineIdx = -1;
        int _tickCount;
        DateTime _lastTick = DateTime.Now;
        DateTime _lineShownAt = DateTime.Now;
        string _lineText = "";
        bool _lineFaded;
        byte[] _lastArt;
        bool _artPending;
        DateTime _artPendingAt;
        bool _suppressed;
        // "no player at all" state (Apple Music not installed / not running).
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
        // ---- module layout (Step 4) -----------------------------------------

        /// <summary>
        /// Build the module model for the current look. Today it is the default template with the
        /// configured cover size and bar thickness; later steps will load it from config instead.
        /// </summary>
        CardConfig _model;

        /// <summary>The live model: the board edits this, the card renders it.</summary>
        public CardConfig Model
        {
            get
            {
                if (_model == null) _model = CardModel();
                return _model;
            }
        }

        /// <summary>Direct edit hook for the board: change Model, then ask for a re-render.</summary>
        /// <summary>Drop the cached template and load it again. Toggling a template flag mutates the
        /// file; anything still holding the old instance would otherwise keep rendering the old flag.</summary>
        public void ReloadModel()
        {
            _model = null;
            LyricsService.Log("model reload inline=" + (Model != null && Model.TitleArtistInline ? "1" : "0"));
            Render();
        }

        public void InvalidateModel()
        {
            LyricsService.Log("model invalidate -> re-render");
            Render();
        }

        public CardConfig CardModel()
        {
            // Sizes come from the metrics, never pinned into the model: a pinned 96 px cover or
            // 3 px bar would stay at desktop size when the board renders the same model smaller.
            // The template comes from layout.ini; a missing file falls back to the factory look.
            return CardConfig.LoadOr(CardConfig.DefaultPath);
        }

        /// <summary>
        /// Persist the live template (called after the board applies a draft). The previous file is
        /// kept as layout.prev.ini so Cancel can put the card back the way it was.
        /// </summary>
        public bool SaveModel()
        {
            CardConfig m = Model;
            if (m == null) return false;
            try
            {
                string path = CardConfig.DefaultPath;
                if (System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".prev", true);
            }
            catch (Exception) { }
            return m.Save(CardConfig.DefaultPath);
        }

        /// <summary>Stash a template as the "before" state so Cancel can return to it.</summary>
        public void StorePreviousModel(CardConfig model)
        {
            if (model == null) return;
            model.Save(CardConfig.DefaultPath + ".prev");
        }

        /// <summary>Undo the last applied template. False when there is nothing to go back to.</summary>
        public bool RestorePreviousModel()
        {
            string prev = CardConfig.DefaultPath + ".prev";
            try
            {
                if (!System.IO.File.Exists(prev)) return false;
                CardConfig old = CardConfig.Parse(System.IO.File.ReadAllText(prev, System.Text.Encoding.UTF8));
                CardConfig live = Model;
                live.Modules.Clear();
                for (int i = 0; i < old.Modules.Count; i++) live.Modules.Add(old.Modules[i].Clone());
                live.Save(CardConfig.DefaultPath);
                InvalidateModel();
                LyricsService.Log("board cancel -> restored template from layout.prev.ini");
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Metrics for the current look: fonts follow the scale sliders; the column width is fixed.</summary>
        public CardMetrics Metrics()
        {
            CardMetrics m = new CardMetrics();
            m.ArtSize = _cfg.ArtSize;
            m.BarThickness = _cfg.BarThickness;
            m.TitleFont = _baseTitleSize * _cfg.TitleScale;
            m.ArtistFont = _baseArtistSize * _cfg.ArtistScale;
            m.LyricFont = _baseLyricSize * _cfg.LyricScale;
            // The text column must NOT follow BarScale. It used to, so shortening the progress bar
            // also pulled the auto-placed cover (and the card's width) leftwards, which looked like
            // the cover moving on its own. Bar length is the Progress module's width and nothing else.
            double col = Math.Max(120, NaturalCol());
            m.CardW = col + CardBase();
            return m;
        }

        /// <summary>
        /// Recompute the layout and push it onto the living elements. Called after a look change
        /// and once when the window is built; nothing here creates or destroys visuals.
        /// </summary>
        void Render()
        {
            if (_canvas == null || _card == null) return;
            CardConfig model = Model;
            // Character caps live in the template as "may show N characters"; resolve them to a
            // width on a copy so the saved file keeps W auto and the cap stays live.
            CardConfig effective = model.Clone();
            effective.ResolveMaxCharsWidths();
            LayoutResult lay = LayoutEngine.Compute(effective, Metrics());
            if (_suspendSize)
            {
                // content only; CommitCardSize() runs when the drag ends
                _pendingW = lay.CardW;
                _pendingH = lay.CardH;
            }
            else
            {
                ApplyCardSize(lay.CardW, lay.CardH, true);
            }
            if (_elem == null) _elem = new CardElements();
            _elem.Title = _hostTitle;
            _elem.Lyrics = _hostLyric;
            _elem.Progress = _barRow;
            _elem.Controls = _content;
            _elem.Cover = _artHost;
            _layout = lay;
            ModuleRenderer.Apply(_canvas, model, lay, _elem);
            ApplyControlsPos();
            PlaceArtist(model, lay);
            ApplyModuleCaps(lay);
            NotifyLayoutChanged();
        }

        /// <summary>
        /// Keep the inline run and the standalone box in sync with whichever one the template uses.
        /// Each player tick calls this, so it must not resurrect the inline artist after the board
        /// moved the artist module onto its own line.
        /// </summary>
        /// <summary>Force the artist line to re-read the template. Changing "same line" flips which
        /// element owns the text, and the text itself has not changed, so the tick-time "only write
        /// when different" guard would leave both copies empty.</summary>
        public void RefreshArtistLine()
        {
            SetArtistText(_artistText);
        }

        void SetArtistText(string art)
        {
            _artistText = art ?? "";
            ModuleConfig ac = Model == null ? null : Model.Find(ModuleKind.Artist);
            bool sameLine = Model != null && Model.TitleArtistInline;
            bool standalone = !sameLine && ac != null && (!double.IsNaN(ac.X) || !double.IsNaN(ac.Y));
            if (_runArtist != null) _runArtist.Text = standalone ? "" : ("   " + _artistText);
            if (_artist != null && standalone) _artist.Text = _artistText;
            if (_hostArtist != null) _hostArtist.Visibility = standalone ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// The artist can be dragged anywhere: once its module carries explicit coordinates it gets
        /// its own box and the inline run on the title line is cleared. Left alone it keeps riding
        /// on the title line, which is the default look.
        /// </summary>
        void PlaceArtist(CardConfig model, LayoutResult lay)
        {
            if (_artist == null || _hostArtist == null || _runArtist == null) return;
            ModuleConfig ac = model.Find(ModuleKind.Artist);
            ModuleRect ar;
            // Two states, not three: same line (inline run) or its own box (wherever the layout puts
            // it). Hiding the box because the module still had an automatic position is what made the
            // artist vanish when the switch was turned off.
            bool sameLine = model.TitleArtistInline;
            bool explicitPos = !sameLine;
            if (!explicitPos || !lay.TryFind(ModuleKind.Artist, out ar))
            {
                if (!explicitPos)
                {
                    // Same line: the artist belongs to the title run. Write it here as well, because a
                    // toggle changes *which* element owns the text while the text itself is unchanged,
                    // so the "only write when different" guard in the tick can leave both sides blank.
                    _artist.Visibility = Visibility.Collapsed;
                    _hostArtist.Visibility = Visibility.Collapsed;
                    _hostArtist.Width = 0; _hostArtist.Height = 0;
                    if (_runArtist != null && _artistText != null
                        && _runArtist.Text != ("   " + _artistText))
                        _runArtist.Text = "   " + _artistText;
                    return;
                }
            }
            if (!lay.TryFind(ModuleKind.Artist, out ar))
            {
                LyricsService.Log("artist place: no rect");
                return;
            }
            LyricsService.Log("artist place standalone=" + (explicitPos ? "1" : "0")
                + " rect=" + ar.X.ToString("0.0") + "," + ar.Y.ToString("0.0") + " " + ar.W.ToString("0.0") + "x" + ar.H.ToString("0.0")
                + " text=" + (_artistText ?? ""));
            _runArtist.Text = "";
            _hostArtist.Visibility = Visibility.Visible;
            _hostArtist.Width = Math.Max(1.0, ar.W);
            _hostArtist.Height = Math.Max(1.0, ar.H);
            Canvas.SetLeft(_hostArtist, ar.X);
            Canvas.SetTop(_hostArtist, ar.Y);
            _artist.Text = _artistText;
            _boxArtistW = Math.Max(1.0, ar.W);
            _artist.Width = Math.Max(1.0, ar.W);
            LyricsService.Log("artist host vis=" + _hostArtist.Visibility
                + " w=" + _hostArtist.Width.ToString("0.0") + " at=" + Canvas.GetLeft(_hostArtist).ToString("0.0")
                + "," + Canvas.GetTop(_hostArtist).ToString("0.0")
                + " txt=" + (_artist.Text ?? "") + " op=" + _artist.Opacity.ToString("0.00"));
            UpdateLayout();
        }

        /// <summary>
        /// A module's W is a hard cap: real text is clipped to it with an ellipsis, it never widens
        /// the card. Without this the title/lyric runs could push the window wider than the model.
        /// </summary>
        Canvas _dbg;

        void DrawDebugRects(LayoutResult lay)
        {
            if (_dbg == null)
            {
                _dbg = new Canvas { IsHitTestVisible = false };
                Panel root = null;
                if (_hostTitle != null) root = _hostTitle.Parent as Panel;
                if (root == null && Content is Panel) root = (Panel)Content;
                if (root != null) root.Children.Add(_dbg);
            }
            _dbg.Children.Clear();
            foreach (ModuleKind k in new ModuleKind[] { ModuleKind.Title, ModuleKind.Artist, ModuleKind.Lyrics, ModuleKind.Progress, ModuleKind.Cover, ModuleKind.Controls })
            {
                ModuleRect r;
                if (!lay.TryFind(k, out r)) continue;
                var b = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(1.0, r.W), Height = Math.Max(1.0, r.H),
                    Stroke = k == ModuleKind.Title ? Brushes.Red : k == ModuleKind.Artist ? Brushes.Lime : Brushes.DeepSkyBlue,
                    StrokeThickness = 1.0
                };
                Canvas.SetLeft(b, r.X); Canvas.SetTop(b, r.Y);
                _dbg.Children.Add(b);
            }
        }

        int _geoLog;

        void LogGeometry(LayoutResult lay)
        {
            if (lay == null) return;
            ModuleRect rr;
            string parts = "";
            if (lay.TryFind(ModuleKind.Title, out rr)) parts += string.Format(" title={0:F0},{1:F0} {2:F0}x{3:F0}", rr.X, rr.Y, rr.W, rr.H);
            if (lay.TryFind(ModuleKind.Artist, out rr)) parts += string.Format(" artist={0:F0},{1:F0} {2:F0}x{3:F0}", rr.X, rr.Y, rr.W, rr.H);
            if (lay.TryFind(ModuleKind.Lyrics, out rr)) parts += string.Format(" lyrics={0:F0},{1:F0} {2:F0}x{3:F0}", rr.X, rr.Y, rr.W, rr.H);
            if (lay.TryFind(ModuleKind.Cover, out rr)) parts += string.Format(" cover={0:F0},{1:F0} {2:F0}x{3:F0}", rr.X, rr.Y, rr.W, rr.H);
            LyricsService.Log(string.Format("geo content={0:F0}x{1:F0} win={2:F0}x{3:F0} card={4:F0}x{5:F0} scale={6:F2} clear={7}{8}",
                lay.BoundsW, lay.BoundsH,
                Width, Height,
                _card == null ? -1 : _card.ActualWidth, _card == null ? -1 : _card.ActualHeight,
                _cfg.Scale, _cfg.ClearBg, parts));
        }

        void ApplyModuleCaps(LayoutResult lay)
        {
            ModuleRect t, l;
            if (lay.TryFind(ModuleKind.Title, out t) && _title != null)
            {
                // The host is exactly the module rect. Without that the TextBlock is measured
                // against the whole canvas and MaxWidth never has anything to trim.
                if (_hostTitle != null) { _hostTitle.Width = Math.Max(1.0, t.W); _hostTitle.Height = Math.Max(1.0, t.H); _hostTitle.ClipToBounds = true; }
                // Width, not MaxWidth: the box *is* the text box, so the TextBlock has to be told
                // the exact measure or WPF sizes it against the whole canvas and never trims.
                if ((_geoLog++ % 15) == 0) LogGeometry(lay);
                // AMC_DEBUG_RECTS=1 draws the module rects straight on the card, so a screenshot
                // shows whether the text actually lives where the template says it does.
                if (Environment.GetEnvironmentVariable("AMC_DEBUG_RECTS") == "1") DrawDebugRects(lay);
                _boxTitleW = Math.Max(1.0, t.W);
                _title.Width = Math.Max(1.0, t.W);
                _title.TextTrimming = TextTrimming.CharacterEllipsis;
                _title.TextWrapping = TextWrapping.NoWrap;
            }
            if (lay.TryFind(ModuleKind.Lyrics, out l) && _lyric != null)
            {
                if (_hostLyric != null) { _hostLyric.Width = Math.Max(1.0, l.W); _hostLyric.Height = Math.Max(1.0, l.H); _hostLyric.ClipToBounds = true; }
                _lyric.Width = Math.Max(1.0, l.W);
                _lyric.TextTrimming = TextTrimming.CharacterEllipsis;
                _lyric.TextWrapping = TextWrapping.NoWrap;
            }
        }

        /// <summary>Font size a module asks for, or the fallback the look settings imply.</summary>
        public double ModuleFontSize(ModuleKind kind)
        {
            ModuleConfig m = Model == null ? null : Model.Find(kind);
            if (m == null || m.FontSize <= 0) return 0.0;
            return m.FontSize;
        }

        double ModuleFont(ModuleKind kind, double fallback)
        {
            double pinned = ModuleFontSize(kind);
            return pinned > 0 ? pinned : fallback;
        }

        /// <summary>The artist name, cached so the standalone artist box can show it.</summary>
        string _artistText = "";

        /// <summary>Tell every view that the card's look changed. Safe to call by hand.</summary>
        public void NotifyLayoutChanged()
        {
            if (LayoutChanged != null) LayoutChanged();
        }

        /// <summary>The card itself — the preview board samples this visual, it does not redraw it.</summary>
        public FrameworkElement CardVisual
        {
            get { return _card; }
        }

        /// <summary>The artwork element itself (a rotating Ellipse, or null while it has no art).</summary>
        public FrameworkElement CardArt
        {
            get { return _artEllipse; }
        }

        /// <summary>The configured overall scale, so a sketch can draw at the same factor.</summary>
        public double ScaleValue
        {
            get { return _cfg.Scale; }
        }

        /// <summary>Playback position as 0..1, read straight from the clock the card shows.</summary>
        public double Progress01
        {
            get { return _durShown > 1 ? _pos / _durShown : 0; }
        }

        /// <summary>Vertical padding between the card edge and its content canvas.</summary>
        public double CardPadTop
        {
            get { return 12.0; }
        }

        /// <summary>Last computed layout, so a view can reuse the card's own rects.</summary>
        public LayoutResult CardLayout
        {
            get { return _layout; }
        }

        /// <summary>Logical size of the card right now (without the shadow margin).</summary>
        public double CardWidth
        {
            get { return _card != null && _card.Width > 1 ? _card.Width : CardW; }
        }

        public double CardHeight
        {
            get { return _card != null && _card.Height > 1 ? _card.Height : 122.0; }
        }

        /// <summary>Point the board at this card. Content follows automatically.</summary>
        /// <summary>Current content, style and artwork as plain data; the board paints from this.</summary>
        public CardScene Scene()
        {
            var sc = new CardScene();
            sc.Title = _runTitle == null ? "" : _runTitle.Text;
            sc.Artist = _runArtist == null ? "" : _runArtist.Text.Trim();
            sc.Lyric = _lyric == null ? "" : _lyric.Text;
            sc.Progress01 = _durShown > 1 ? _pos / _durShown : 0;
            sc.Playing = _playingShown;
            sc.Artwork = _artEllipse == null ? null : _artEllipse.Fill;
            sc.Spin = _artRotate == null ? 0 : _artRotate.Angle;
            sc.Halo = false;   // the cover glow was removed
            // A module may pin its own size; otherwise the global look scale decides.
            sc.TitleSize = ModuleFont(ModuleKind.Title, _baseTitleSize * _cfg.TitleScale);
            sc.ArtistSize = ModuleFont(ModuleKind.Artist, _baseArtistSize * _cfg.ArtistScale);
            sc.LyricSize = ModuleFont(ModuleKind.Lyrics, _baseLyricSize * _cfg.LyricScale);
            sc.BarThickness = _cfg.BarThickness;
            sc.TitleBrush = _runTitle == null ? Brushes.White : _runTitle.Foreground;
            sc.ArtistBrush = _runArtist == null ? Brushes.White : _runArtist.Foreground;
            sc.LyricBrush = _lyric == null ? Brushes.White : _lyric.Foreground;
            sc.BarTrackBrush = _barTrack == null ? Brushes.White : _barTrack.Background;
            sc.BarFillBrush = _barFill == null ? Brushes.White : _barFill.Background;
            sc.ControlsBrush = _btnPlay == null || _btnPlay.Glyph == null || _btnPlay.Glyph.Children.Count == 0
                ? Brushes.White : ((Shape)_btnPlay.Glyph.Children[0]).Fill;
            return sc;
        }

        /// <summary>
        /// Retired with the sketch pad: the board draws placeholders from its own draft and
        /// never mirrors the card, so there is nothing to feed it any more.
        /// </summary>

        /// <summary>
        /// Card size changes are eased over 120 ms so a lyric line that grows or shrinks does
        /// not make the window snap. Width only moves when the look asks for it, so both are
        /// animated together to keep the cover from sliding while the box grows.
        /// </summary>
        void ApplyCardSize(double w, double h, bool animate)
        {
            if (_card == null) return;
            double fromW = _card.ActualWidth > 1 ? _card.ActualWidth : w;
            double fromH = _card.ActualHeight > 1 ? _card.ActualHeight : h;
            bool same = Math.Abs(fromW - w) < 0.5 && Math.Abs(fromH - h) < 0.5;
            if (!animate || same)
            {
                _card.BeginAnimation(FrameworkElement.WidthProperty, null);
                _card.BeginAnimation(FrameworkElement.HeightProperty, null);
                _card.Width = w;
                _card.Height = h;
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var aw = new DoubleAnimation(fromW, w, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease };
            var ah = new DoubleAnimation(fromH, h, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease };
            _card.BeginAnimation(FrameworkElement.WidthProperty, aw);
            _card.BeginAnimation(FrameworkElement.HeightProperty, ah);
        }

        /// <summary>Finish a deferred resize (end of a drag).</summary>
        void CommitCardSize()
        {
            if (_pendingW > 0 && _pendingH > 0) ApplyCardSize(_pendingW, _pendingH, false);
            _pendingW = _pendingH = -1;
        }

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
            // The template decides where the transport row sits: the Controls module owns X/Y/W,
            // and the row is centred inside that box. There is no percentage setting any more.
            ModuleRect rc;
            if (_layout != null && _layout.TryFind(ModuleKind.Controls, out rc))
            {
                double hostW = rc.W;
                double btnW2 = (_controls.ActualWidth > 1) ? _controls.ActualWidth : 90.0;
                double freeInBox = hostW - btnW2;
                if (freeInBox < 0) freeInBox = 0;
                _controls.HorizontalAlignment = HorizontalAlignment.Left;
                _controls.Margin = new Thickness(freeInBox / 2.0, 0, 0, 0);
            }
            else
            {
                _controls.HorizontalAlignment = HorizontalAlignment.Left;
                _controls.Margin = new Thickness(0);
            }
            UpdateLayout();
        }

        /// <summary>
        /// Text sits at the left edge of its module box and is cut by the box width. Alignment and
        /// the old indent settings are gone: the template's rectangle is the only thing that decides
        /// where text starts, so nothing is left to re-apply except the neutral values.
        /// </summary>
        /// <summary>Text alignment inside a module box, straight from the template.</summary>
        public TextAlignment ModuleAlignment(ModuleKind kind)
        {
            ModuleConfig mc = Model == null ? null : Model.Find(kind);
            if (mc == null) return TextAlignment.Left;
            switch (mc.Align)
            {
                case ModuleAlign.Center: return TextAlignment.Center;
                case ModuleAlign.Right: return TextAlignment.Right;
                default: return TextAlignment.Left;
            }
        }

        /// <summary>Horizontal placement of the block itself, so a centred line centres in the box.</summary>
        static HorizontalAlignment HostAlign(TextAlignment ta)
        {
            if (ta == TextAlignment.Center) return HorizontalAlignment.Center;
            if (ta == TextAlignment.Right) return HorizontalAlignment.Right;
            return HorizontalAlignment.Left;
        }

        public void ApplyAlign()
        {
            if (_title == null || _lyric == null || _barRow == null) return;
            var taTitle = ModuleAlignment(ModuleKind.Title);
            _title.TextAlignment = taTitle;
            _title.HorizontalAlignment = HostAlign(taTitle);
            _title.Margin = new Thickness(0);
            var taLyric = ModuleAlignment(ModuleKind.Lyrics);
            _lyric.TextAlignment = taLyric;
            _lyric.HorizontalAlignment = HostAlign(taLyric);
            _lyric.Margin = new Thickness(0);
            if (_artist != null)
            {
                var taArtist = ModuleAlignment(ModuleKind.Artist);
                _artist.TextAlignment = taArtist;
                _artist.HorizontalAlignment = HostAlign(taArtist);
            }
            _barRow.Margin = new Thickness(0);
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

            // The template owns the cover size. ArtSize is only the fallback used when the Cover
            // module is auto-sized, so a size set in the template and a size dragged in the board
            // stay the same number (they used to disagree: layout said W, the disc drew ArtSize).
            double artSide = _cfg.ArtSize;
            ModuleRect coverRc;
            if (_layout != null && _layout.TryFind(ModuleKind.Cover, out coverRc) && coverRc.W > 1)
                artSide = Math.Min(coverRc.W, coverRc.H > 1 ? coverRc.H : coverRc.W);
            _artHost.Width = artSide;
            _artHost.Height = artSide;
            _artHost.InvalidateVisual();

            // Card size belongs to the template now (LayoutEngine), applied by Render/ApplyCardSize.
            // This used to recompute the width here from the old column formula and then nudge Left
            // by the difference, "to keep the right edge glued" -- with the template driving the size
            // that difference was pure noise, so every look change slid the card left by a pixel or two.

            Render();
            try { UpdateLayout(); } catch { }
            if (!double.IsNaN(Left)) ClampToScreen();
            if (LookChanged != null) LookChanged();
        }

        void BuildUi()
        {
            _card = new Border
            {
                Width = CardW,
                Height = double.NaN,   // grows with the content / cover size
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(0),   // coordinates are absolute: no inset anywhere
                Background = new SolidColorBrush(Color.FromArgb(238, 18, 18, 21)),
                BorderBrush = White(0.10),
                BorderThickness = new Thickness(1)
            };

            _shadowHost = new Border
            {
                // Gutter around the card inside its own window. Press-scale shrinks the card and
                // hover rings/halo draw slightly outside the card box; with zero padding those
                // pixels were clipped by the window edge. The window already reserves 2*ShadowPad.
                Padding = new Thickness(ShadowPad),
                Background = Brushes.Transparent,
                Child = _card,
                LayoutTransform = new ScaleTransform(_cfg.Scale, _cfg.Scale)
            };
            // No drop shadow on the card itself: the only shadow in this window is the
            // cover halo (ApplyBackdrop decides whether it is on).
            Content = _shadowHost;

            // The card is one canvas with a host per module. Hosts keep the exact alignment
            // behaviour of the old stack (right-aligned text, margins, clipping) but their
            // rectangles now come from LayoutEngine instead of stack order.
            var root = new Canvas { ClipToBounds = false };
            _root = root;
            _canvas = root;
            _card.Child = root;

            // --- title + artist on one line: title bold, artist dimmer and smaller ---
            var titleHost = new Grid { ClipToBounds = true };
            _hostTitle = titleHost;
            _title = new TextBlock
            {
                FontSize = 15.5,
                Foreground = White(0.97),
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0)
            };
            _runTitle = new Run("Apple Music") { FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = White(0.97) };
            _runArtist = new Run("") { FontSize = 11.5, Foreground = White(0.62) };
            _title.Inlines.Add(_runTitle);
            _title.Inlines.Add(_runArtist);
            titleHost.Children.Add(_title);
            root.Children.Add(titleHost);

            // --- artist as its own draggable module (hidden while it rides on the title line) ---
            var artistHost = new Grid { ClipToBounds = true, Visibility = Visibility.Collapsed };
            _hostArtist = artistHost;
            _artist = new TextBlock
            {
                FontSize = 11.5,
                Foreground = White(0.62),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Text = ""
            };
            // Parent the TextBlock itself. Wrapping it in a Grid looked harmless, but the wrapper was
            // measured by the canvas while the block was measured by the Grid, and the artist ended up
            // laid out with real coordinates yet never painted.
            root.Children.Add(_artist);
            _hostArtist = _artist;

            // --- lyrics ---
            var lyricHost = new Grid { ClipToBounds = true };
            _hostLyric = lyricHost;
            _lyric = new TextBlock
            {
                FontSize = 15,
                Foreground = White(0.95),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Text = "",
                RenderTransform = new TranslateTransform(0, 0)
            };
            lyricHost.Children.Add(_lyric);
            root.Children.Add(lyricHost);

            // --- progress ---
            var barGrid = new Grid();
            _barTrack = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = White(0.16),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
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
            root.Children.Add(barGrid);

            // --- transport controls: their own row, right aligned ---
            var bottom = new Grid();

            _controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Opacity = ControlsIdle, HorizontalAlignment = HorizontalAlignment.Right };
            _btnPlay = MakeButton("play", delegate { CommandPlayPause(); }, 0);
            _controls.Children.Add(MakeButton("prev", delegate { _smtc.Prev(); }, -1).Root);
            _controls.Children.Add(_btnPlay.Root);
            _controls.Children.Add(MakeButton("next", delegate { _smtc.Next(); }, 1).Root);

            bottom.Children.Add(_controls);
            bottom.SizeChanged += delegate { ApplyControlsPos(); };
            _content = bottom;
            root.Children.Add(bottom);

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
            _artEllipse = new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(255, 42, 42, 48)) };
            // the cover itself turns, the ring around it stays put
            _artRotate = new RotateTransform(0);
            _artEllipse.RenderTransformOrigin = new Point(0.5, 0.5);
            _artEllipse.RenderTransform = _artRotate;
            artGrid.Children.Add(_artEllipse);
            // The swap layer: a new cover is drawn here at opacity 0 and fades in. If the bitmap is
            // not yet uploaded when it is assigned, the frame shows the old cover underneath
            // instead of an empty disc -- which is what the post-load flash was.
            _artTop = new Ellipse { Fill = null, Opacity = 0.0, IsHitTestVisible = false };
            _artTop.RenderTransformOrigin = new Point(0.5, 0.5);
            _artTop.RenderTransform = _artRotate;
            artGrid.Children.Add(_artTop);
            artGrid.Children.Add(new Ellipse { Stroke = White(0.13), StrokeThickness = 1, Name = "ring" });
            // Same layout slot as the disc, so switching shape never moves or resizes the cover.
            _artSquare = new Rectangle { Fill = null, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            artGrid.Children.Add(_artSquare);
            _artSquareRing = new Rectangle { Stroke = White(0.13), StrokeThickness = 1, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            artGrid.Children.Add(_artSquareRing);
            root.Children.Add(artGrid);
            ApplyArtShape();

            // --- interactions ---
            _card.MouseLeftButtonDown += OnCardMouseDown;
            _card.MouseWheel += OnWheel;
            _card.MouseRightButtonUp += OnRightClick;
            _card.MouseEnter += delegate
            {
                Animate(_controls, 1.0, 120);
                if (_artScale != null) AnimScale(_artScale, 1.03, 220);
            };
            _card.MouseLeave += delegate
            {
                Animate(_controls, ControlsIdle, 220);
                if (_artScale != null) AnimScale(_artScale, 1.0, 260);
            };
            Render();
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
                _artHost.Effect = null;   // the glow is gone: it bled over the neighbouring text

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
                _artHost.Effect = null;   // the glow is gone: it bled over the neighbouring text
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
            if (_cfg.ClickThrough) ex |= Native.WS_EX_TRANSPARENT;
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
                // cfg.Left/Top are card coordinates; the window has a ShadowPad gutter around it
                Left = _cfg.Left - ShadowPad * _cfg.Scale;
                Top = _cfg.Top - ShadowPad * _cfg.Scale;
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
            if (_cfg.ClickThrough) return;
            // the progress row handles its own press (seek); dragging from there used to be
            // impossible because the bar swallowed the event - keep that rule, now via the rect
            if (HitModule(e.GetPosition(_canvas), ModuleKind.Progress, 4.0)) return;
            _suspendSize = true;
            try { DragMove(); } catch { }
            _suspendSize = false;
            CommitCardSize();
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

        /// <summary>
        /// Point-in-module test in card-content units. padY widens a thin row vertically so the
        /// progress bar stays as easy to hit as it was when it owned a border of its own height.
        /// </summary>
        bool HitModule(Point p, ModuleKind kind, double padY)
        {
            LayoutResult lay = _layout;
            if (lay == null) return false;
            ModuleRect r;
            if (!lay.TryFind(kind, out r)) return false;
            return p.X >= r.X && p.X <= r.X + r.W
                && p.Y >= r.Y - padY && p.Y <= r.Y + r.H + padY;
        }

        void OnBarClick(object s, MouseButtonEventArgs e)
        {
            var np = _smtc.Current;
            if (np == null || !np.Has || np.Dur.TotalSeconds < 5) return;
            ModuleRect r;
            if (_layout == null || !_layout.TryFind(ModuleKind.Progress, out r)) return;
            Point p = e.GetPosition(_canvas);
            double frac = (p.X - r.X) / Math.Max(1.0, r.W);
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            LyricsService.Log("bar seek frac=" + frac.ToString("0.000", CultureInfo.InvariantCulture)
                + " t=" + (frac * np.Dur.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture)
                + " rect=" + r.X.ToString("0.0", CultureInfo.InvariantCulture) + "," + r.Y.ToString("0.0", CultureInfo.InvariantCulture)
                + " " + r.W.ToString("0.0", CultureInfo.InvariantCulture) + "x" + r.H.ToString("0.0", CultureInfo.InvariantCulture));
            _smtc.Seek(frac * np.Dur.TotalSeconds);
            e.Handled = true;
        }

        // --- hooks used by the settings panel (live preview) ---
        public void ApplyCurrentScale()
        {
            ApplyScale(true);
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

        /// <summary>
        /// Resize the whole card. The scale lives in the shadow host's LayoutTransform, so the
        /// window follows it; easing that transform (120 ms) is what makes a wheel notch glide
        /// instead of snapping. Animating is skipped on startup and while dragging, where a
        /// straight jump is what the user expects.
        /// </summary>
        void ApplyScale(bool animate = true)
        {
            double oldW = (CardWidthNow() + ShadowPad * 2) * ((ScaleTransform)_shadowHost.LayoutTransform).ScaleX;
            var st = (ScaleTransform)_shadowHost.LayoutTransform;
            if (animate && !_suspendSize)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var ax = new DoubleAnimation(st.ScaleX, _cfg.Scale, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease };
                var ay = new DoubleAnimation(st.ScaleY, _cfg.Scale, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
            }
            else
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                st.ScaleX = _cfg.Scale;
                st.ScaleY = _cfg.Scale;
            }
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
            // Keep the nudge inside a sane band: +/-10s is already far beyond any plausible
            // reporting lag, and it stops a stuck key from walking the value to absurdity.
            _cfg.LyricOffset = Config.Clamp(_cfg.LyricOffset + delta, -10.0, 10.0);
            _cfg.Save();
        }

        /// <summary>Click-through on/off. While it is on the card stops receiving mouse input
        /// altogether (no drag, no clicks, no context menu) and every click lands on the
        /// window below -- the tray menu and the Home page stay available to turn it back off.</summary>
        public void SetClickThrough(bool on)
        {
            _cfg.ClickThrough = on;
            _cfg.Save();
            ApplyClickThrough();
        }

        void ApplyClickThrough()
        {
            if (_hwnd == IntPtr.Zero) return;
            int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
            ex = _cfg.ClickThrough ? (ex | Native.WS_EX_TRANSPARENT)
                                   : (ex & ~Native.WS_EX_TRANSPARENT);
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex);
            LyricsService.Log("click-through " + (_cfg.ClickThrough ? "on" : "off") + " ex=0x" + ex.ToString("X"));
        }

        public void ToggleClearBg()
        {
            SetClearBg(!_cfg.ClearBg);
        }

        /// <summary>True while the cover is drawn as a disc (the square sleeve never turns).</summary>
        bool IsRoundArt
        {
            get { return !string.Equals(_cfg.ArtShape, "square", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Circle or square sleeve. Square never turns, so the angle is parked at 0.</summary>
        public void ApplyArtShape()
        {
            bool square = string.Equals(_cfg.ArtShape, "square", StringComparison.OrdinalIgnoreCase);
            if (_artEllipse != null) _artEllipse.Visibility = square ? Visibility.Collapsed : Visibility.Visible;
            if (_artTop != null) _artTop.Visibility = square ? Visibility.Collapsed : Visibility.Visible;
            if (_artSquare != null)
            {
                _artSquare.Visibility = square ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_artSquareRing != null) _artSquareRing.Visibility = square ? Visibility.Visible : Visibility.Collapsed;
            if (square)
            {
                _spinAngle = 0.0;
                if (_artRotate != null) _artRotate.Angle = 0.0;
            }
        }

        public void SetArtShape(string shape)
        {
            _cfg.ArtShape = string.Equals(shape, "square", StringComparison.OrdinalIgnoreCase) ? "square" : "circle";
            _cfg.Save();
            ApplyArtShape();
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
            _cfg.Left = Left + ShadowPad * _cfg.Scale;
            _cfg.Top = Top + ShadowPad * _cfg.Scale;
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
            var passItem = new MenuItem { Header = Strings.T("menu.clickthrough"), IsCheckable = true, IsChecked = _cfg.ClickThrough };
            passItem.Click += delegate { SetClickThrough(!_cfg.ClickThrough); passItem.IsChecked = _cfg.ClickThrough; };
            m.Items.Add(passItem);
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
                passItem.IsChecked = _cfg.ClickThrough;
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

        /// <summary>Hard character cap for a module: MaxChars in the template, 0 = no cap.</summary>
        string CapChars(string text, ModuleKind kind)
        {
            if (string.IsNullOrEmpty(text) || Model == null) return text ?? "";
            ModuleConfig mc = Model.Find(kind);
            if (mc == null || mc.MaxChars <= 0 || text.Length <= mc.MaxChars) return text;
            int n = Math.Max(1, mc.MaxChars - 1);
            return text.Substring(0, n).TrimEnd() + "…";
        }

        /// <summary>
        /// The module box IS the text box: a string longer than the box gets an ellipsis at the
        /// box edge. Measured with FormattedText because a width-constrained TextBlock inside a
        /// manually laid out canvas does not trim reliably on its own.
        /// </summary>
        // Fitting is a FormattedText measure plus, when the text is too long, a per-character
        // loop. It used to run twice on every 30 fps tick with the same inputs, which alone was
        // a quarter of a core. Inputs change once per song at most, so memoise the last answer.
        readonly Dictionary<string, string> _fitCache = new Dictionary<string, string>();

        string FitToBox(string text, double boxW, double fontSize, FontWeight weight)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (boxW <= 2.0 || fontSize <= 0.0) return text;
            string key = fontSize.ToString("F2", CultureInfo.InvariantCulture) + "|"
                       + boxW.ToString("F0", CultureInfo.InvariantCulture) + "|" + text;
            string hit;
            if (_fitCache.TryGetValue(key, out hit)) return hit;
            string fitted = FitToBoxSlow(text, boxW, fontSize, weight);
            if (_fitCache.Count > 24) _fitCache.Clear();
            _fitCache[key] = fitted;
            return fitted;
        }

        string FitToBoxSlow(string text, double boxW, double fontSize, FontWeight weight)
        {
            double pw = 1.0;
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src != null && src.CompositionTarget != null) pw = src.CompositionTarget.TransformToDevice.M11;
            }
            catch { }
            Typeface tf = new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal);
            Func<string, double> w = t => new FormattedText(t, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, tf, fontSize, Brushes.White, pw).WidthIncludingTrailingWhitespace;
            if (w(text) <= boxW) return text;
            int cut = text.Length;
            while (cut > 1)
            {
                cut--;
                string cand = text.Substring(0, cut).TrimEnd() + "…";
                if (w(cand) <= boxW) return cand;
            }
            return "…";
        }

        // ---------------- data tick ----------------

        double _boxTitleW = 320.0;
        double _boxArtistW = 120.0;

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
            TickBody();
        }

        void TickBody()
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
                    _idle = false;
                    if (!IsVisible && !_suppressed) ShowAnimated();
                    _doc = null;
                    _docKey = "";
                    _pendingKey = "";
                    _lineIdx = -1;
                    _lineText = "";
                    _pos = 0;
                    // Lead with the player that is making the noise: that is the one the user has
                    // to fix, and the name fits where the song title normally goes.
                    _runTitle.Text = _hintPlayer ?? "Lyricard";
                    SetArtistText(Strings.T("app.version"));
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
                    _idle = false;
                    if (!IsVisible && !_suppressed) ShowAnimated();
                    _doc = null;
                    _docKey = "";
                    _pendingKey = "";
                    _lineIdx = -1;
                    _lineText = "";
                    _pos = 0;
                    _runTitle.Text = "Lyricard";
                    SetArtistText(Strings.T("app.version"));
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
                _idle = false;
                return;
            }
            _idle = false;
            if (!IsVisible && !_suppressed) ShowAnimated();
            // A track with no artwork at all would otherwise leave the disc dimmed forever.
            if (_artPending && (DateTime.Now - _artPendingAt).TotalSeconds > 1.5) _artPending = false;
            if (_artHost.Opacity < 1.0) _artHost.Opacity = 1.0;
            // The overlay has finished fading in: promote it to the base and hide it again, so the
            // next track starts from a single, already-correct ellipse.
            if (_artTop != null && _artTop.Fill != null && _artTop.Opacity >= 0.99
                && !ReferenceEquals(_artEllipse.Fill, _artTop.Fill))
            {
                Brush done = _artTop.Fill;
                _artTop.BeginAnimation(UIElement.OpacityProperty, null);
                _artTop.Opacity = 0.0;
                _artTop.Fill = null;
                _artEllipse.Fill = done;
            }
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
            // One revolution per 36 s, advanced on the card's own 30 fps timer. A layered window
            // pushes a full bitmap per frame, so the frame rate *is* the cost; 30 fps halves what
            // the old 60 fps animation clock burned and still looks continuous.
            if (_spinOn && _artRotate != null && IsRoundArt)
            {
                _spinAngle = (_spinAngle + 360.0 / 36.0 / 30.0) % 360.0;
                _artRotate.Angle = _spinAngle;
            }

            var artistOut = "";
            var albumOut = "";
            TextUtil.SplitArtistAlbum(np.Artist, out artistOut, out albumOut);

            string ttl = string.IsNullOrEmpty(np.Title) ? "Apple Music" : np.Title;
            string art = FitToBox(CapChars(artistOut, ModuleKind.Artist), _boxArtistW, _artist.FontSize, FontWeights.Normal);
            double titleBoxW = _boxTitleW;
            if (Model != null && Model.TitleArtistInline && art.Length > 0)
            {
                // Same line: the artist rides inside the title box, so the title has to leave room for
                // it. Without this the title filled the box and the inline run was clipped away.
                double pw2 = 1.0;
                try
                {
                    var src2 = PresentationSource.FromVisual(this);
                    if (src2 != null && src2.CompositionTarget != null) pw2 = src2.CompositionTarget.TransformToDevice.M11;
                }
                catch { }
                double artW = MeasureW("   " + art, _artist.FontSize, FontWeights.Normal, pw2);
                titleBoxW = Math.Max(24.0, _boxTitleW - artW);
            }
            string titleShown = FitToBox(CapChars(ttl, ModuleKind.Title), titleBoxW, _title.FontSize, FontWeights.SemiBold);
            // Assigning .Text invalidates measure/arrange even when the string is identical, and
            // this runs 30 times a second, so only write when something really changed.
            if (_runTitle.Text != titleShown) _runTitle.Text = titleShown;
            if (_artistText != art) SetArtistText(art);
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

            // progress: run our own clock at 1x and *slew* toward the SMTC estimate instead of
            // snapping to it. Apple's Position only lands every ~3s, so snapping made the lyric
            // line jump back and forth right on line boundaries.
            var nowT = DateTime.Now;
            double dt = (nowT - _lastTick).TotalSeconds;
            if (dt < 0) dt = 0;
            if (dt > 0.5) dt = 0.5;
            _lastTick = nowT;

            // Around an auto-advance SMTC hands us half-updated snapshots: the position resets
            // first, the title follows a beat later, and for a moment the *previous* title comes
            // back. Acting on every one of those made the card flash old line -> waiting -> old
            // line -> new song. So require a key to hold still before we believe it.
            string rawKey = (np.Aumid ?? "") + "|" + (np.Title ?? "");
            if (rawKey != _seenKey)
            {
                _seenKey = rawKey;
                _seenSince = nowT;
                if (_unstableSince == DateTime.MinValue) _unstableSince = nowT;
            }
            // Safety valve: a player that keeps alternating titles (two sessions fighting over the
            // card) must not pin us in "waiting" forever -- after 3s take whatever is on offer.
            bool settled = (nowT - _seenSince).TotalMilliseconds >= 400
                           || (_unstableSince != DateTime.MinValue && (nowT - _unstableSince).TotalMilliseconds > 3000);
            if (settled) _unstableSince = DateTime.MinValue;
            if (!settled && _holdLogged != rawKey)
            {
                _holdLogged = rawKey;
                LyricsService.Log("settle hold " + rawKey);
            }
            else if (settled && _holdLogged.Length > 0)
            {
                _holdLogged = "";
                LyricsService.Log("settled " + rawKey);
            }

            // lyric document (only once the title has stopped moving)
            string key = np.Title + "|" + artistOut;
            if (settled && key != _docKey && key != _pendingKey)
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

            // New song: start the bar at zero instead of carrying the old position over. This used
            // to happen by accident (the old anchor snapped us to the reported 0.0); now that an
            // untrusted player is never snapped, it has to be explicit.
            string trackKey = settled ? rawKey : _trackKey;
            if (trackKey != _trackKey)
            {
                _trackKey = trackKey;
                _pos = 0;
                _lineIdx = -1;
                _endHold = false;
                _switchUntil = nowT.AddMilliseconds(900);

                // Players publish the title a beat before the artwork, so for a few hundred ms there
                // is no cover for the new track. Blanking the disc made that gap read as a flash;
                // keep the previous cover instead, dimmed, and let ApplyArt swap it in place.
                _lastArt = null;
                _artPending = true;
                _artPendingAt = DateTime.Now;
                LyricsService.Log("art pending " + trackKey);
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
            if (nowT < _switchUntil || !settled)
            {
                // Settling: one clean state, no half-updated lyric lines. !settled covers the
                // window right after the title starts moving, where the old key is still the
                // only thing we can trust.
                _pos = 0;
                _lineIdx = -1;
                _lineText = TxtWait;
                _lyric.Text = TxtWait;
                _barFill.Width = 0;
                return;
            }
            double total = np.Dur.TotalSeconds;
            _durShown = total;
            double target = np.EstimateSeconds(nowT);
            // End of the track, but the player has not published the next title yet. Park on the
            // last frame and stop doing position math: QQ Music keeps the old title for up to a
            // second at the boundary, and every reset to zero here would bounce straight back to
            // "duration" on the next tick -- that was the 30Hz flicker between the last line and
            // "Waiting for music" (log: line idx=35 / idx=-1 alternating every 45ms).
            if (_endHold)
            {
                // Most players reset the reported position to ~0 a moment before they publish the
                // next title, so "position jumped back" alone is not a reason to un-hold: that would
                // flash the first lyric line for half a second at every track change. Only accept it
                // once it has persisted long enough that it cannot be the boundary any more.
                bool moved = target < total - 5.0;
                bool stale = moved && (nowT - _endHoldAt).TotalSeconds > 2.5;
                if (total <= 1 || stale) { _endHold = false; }
                else _pos = total;
            }
            // A player that keeps reporting the same position (NetEase: 0.35 forever, and 0.0 while
            // paused) must never be allowed to yank the bar back - that is what looked like
            // "paused, and the progress restarted".
            bool trustTarget = !np.PosFrozen && !np.PosUntrusted;
            if (_endHold)
            {
                // Latched at the end of the track: nothing to advance, nothing to correct.
            }
            else if (_pos < 0 || (trustTarget && Math.Abs(target - _pos) > 1.2))
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
            }
            // Either branch can land us on the end of the track (Snap to the reported duration, or
            // the clock walking past it). Freeze there once, from wherever it happened.
            if (!_endHold && np.Playing && total > 1 && _pos > total - 0.4)
            {
                _pos = total;
                _endHold = true;
                _endHoldAt = nowT;
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
            // The fill is a layout participant: every Width change forces a measure/arrange pass
            // over the whole card (TextBlocks included, which rebuild their FormattedText). At
            // 30 fps that was most of the card's CPU. One pixel of progress is invisible, so only
            // touch the width when the pixel actually changes.
            double targetW = Math.Max(0, Math.Round(_barTrack.ActualWidth * frac));
            if (Math.Abs(_barFill.Width - targetW) >= 1.0) _barFill.Width = targetW;
            // A zero-width rounded Border still paints a 1px nub on the left; without a length
            // the bar is decoration only, so keep the fill out of the picture entirely.
            _barFill.Visibility = dur2 > 1 ? Visibility.Visible : Visibility.Collapsed;


            UpdateLyric();
            PreviewPing();
        }

        /// <summary>
        /// Tell the Look-page board when something it shows actually changed. Rendering the whole
        /// preview on every tick would be wasteful; a track change or a new lyric line is enough.
        /// </summary>
        string _pvSig = null;
        void PreviewPing()
        {
            if (LayoutChanged == null) return;
            string sig = (_runTitle != null ? _runTitle.Text : "") + "\u0001"
                       + (_runArtist != null ? _runArtist.Text : "") + "\u0001"
                       + (_lyric != null ? _lyric.Text : "") + "\u0001"
                       + (_playingShown ? "1" : "0") + "\u0001"
                       + ((int)_pos).ToString() + "\u0001"
                       + (_artEllipse != null && _artEllipse.Fill != null ? "a" : "-");
            if (sig == _pvSig) return;
            _pvSig = sig;
            NotifyLayoutChanged();
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
                _lyric.Text = CapChars(text, ModuleKind.Lyrics);
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
            if (!_spinOn)
            {
                _spinOn = true;
                LyricsService.Log("art spin start");
            }
        }

        void SpinPause()
        {
            _spinOn = false;
        }

        void SpinStop()
        {
            _spinOn = false;
            _spinAngle = 0.0;
            if (_artRotate != null)
            {
                try { _artRotate.ApplyAnimationClock(RotateTransform.AngleProperty, null); } catch { }
                _artRotate.Angle = 0;
            }
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

                LyricsService.Log("art apply " + bytes.Length + " bytes");
                var brush = new ImageBrush(shown) { Stretch = Stretch.UniformToFill };
                // Cross-fade: the base ellipse keeps the *old* cover and the new artwork lands on the
                // overlay, fading in. Whatever delay the image brush needs before it paints, the
                // frame still shows the old disc instead of an empty one. The commit (copy the
                // overlay brush down, drop the overlay) happens in Tick once it is fully opaque.
                // Atomic swap. A cross-fade sounded safer, but blending two album covers reads as a
                // ghost/again-flash on screen; the old cover simply stays until this assignment and
                // disappears in the same UI turn, which is what "no flash" means for a 96 px disc.
                bool firstCover = _artEllipse.Fill == null;
                if (_artTop != null)
                {
                    _artTop.BeginAnimation(UIElement.OpacityProperty, null);
                    _artTop.Opacity = 0.0;
                    _artTop.Fill = null;
                }
                _artEllipse.Fill = brush;
                if (_artSquare != null) _artSquare.Fill = brush;
                if (firstCover) FadeIn(_artEllipse, 280);
                _artEllipse.Opacity = 1.0;
                _artPending = false;
                _artHost.Opacity = 1.0;
                LyricsService.Log("art shown");

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
