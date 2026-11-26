// ---------------------------------------------------------------------------
// CardPreview.cs
//
/// The board on the settings page: a real Canvas holding real module controls.
///
/// It owns no drawing code. Everything visible comes from ModuleRenderer.Draw — the same call the
/// card uses — with the metrics scaled down to fit. Because the modules are live controls, the
/// board can tag them, hit-test them and (next step) drag them; a bitmap could not.
// ---------------------------------------------------------------------------

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Amc
{
    public class CardPreview : Grid
    {
        readonly Canvas _canvas = new Canvas();
        CardElements _el;
        DateTime _loggedAt = DateTime.MinValue;

        // --- drag state (Step 8) ---
        CardWindow _card;
        LayoutResult _lay;
        double _s = 1.0;
        ModuleKind? _drag;
        Point _grab;
        double _startX, _startY;
        bool _moving;
        readonly Line _guideV = new Line { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = { 3, 3 }, Opacity = 0.5, Visibility = Visibility.Collapsed };
        readonly Line _guideH = new Line { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = { 3, 3 }, Opacity = 0.5, Visibility = Visibility.Collapsed };

        /// <summary>Widest the board will draw, in device-independent pixels.</summary>
        public double FitWidth = 430;

        /// <summary>The card's own Scale, so the board looks like the thing on the desktop.</summary>
        public double Scale = 1.0;

        /// <summary>Last module the user clicked; the acceptance check reads this.</summary>
        public ModuleKind? LastHit;

        /// <summary>Raised when a module is clicked (drag handling comes next).</summary>
        public event Action<ModuleKind, FrameworkElement> ModuleClicked;

        /// <summary>Grid the drag snaps to, in model units.</summary>
        public double Snap = 4.0;

        void HookDrag()
        {
            _canvas.MouseLeftButtonDown += OnBoardDown;
            _canvas.MouseMove += OnBoardMove;
            _canvas.MouseLeftButtonUp += OnBoardUp;
        }

        static ModuleKind? KindOf(object source)
        {
            var el = source as FrameworkElement;
            while (el != null)
            {
                if (el.Tag is ModuleKind) return (ModuleKind)el.Tag;
                el = el.Parent as FrameworkElement;
            }
            return null;
        }

        void OnBoardDown(object sender, MouseButtonEventArgs e)
        {
            if (_card == null || _lay == null) return;
            ModuleKind? kind = KindOf(e.OriginalSource);
            if (kind == null) return;
            ModuleRect r;
            if (!_lay.TryFind(kind.Value, out r)) return;

            ModuleConfig mc = _card.Model.Find(kind.Value);
            if (mc == null) return;

            // seed explicit coordinates from where auto-layout put it, then drag from there
            if (double.IsNaN(mc.X)) mc.X = r.X / _s;
            if (double.IsNaN(mc.Y)) mc.Y = r.Y / _s;
            // Derive the factor from this model at both scales. The card's own last layout can be
            // stale (it lags a settings change), which made 2.2x drags move 13% too little.
            LayoutResult baseLay = LayoutEngine.Compute(_card.Model, _card.Metrics());
            if (baseLay.CardW > 0) _s = _lay.CardW / baseLay.CardW;

            _drag = kind;
            _grab = e.GetPosition(_canvas);
            _startX = mc.X;
            _startY = mc.Y;
            _moving = false;
            _canvas.CaptureMouse();
            e.Handled = true;
        }

        void OnBoardMove(object sender, MouseEventArgs e)
        {
            if (_drag == null || _card == null) return;
            Point p = e.GetPosition(_canvas);
            // canvas units are already scaled by s, so divide to get model units
            double mx = _startX + (p.X - _grab.X) / _s;
            double my = _startY + (p.Y - _grab.Y) / _s;

            ApplyGuides(ref mx, ref my);
            if (Snap > 0.5)
            {
                mx = Math.Round(mx / Snap) * Snap;
                my = Math.Round(my / Snap) * Snap;
            }
            if (mx < 0) mx = 0;
            if (my < 0) my = 0;

            ModuleConfig mc = _card.Model.Find(_drag.Value);
            if (mc == null) return;
            if (Math.Abs(mc.X - mx) < 0.01 && Math.Abs(mc.Y - my) < 0.01) return;
            mc.X = mx;
            mc.Y = my;
            _moving = true;
            _card.InvalidateModel();          // card re-renders, raises LayoutChanged, board rebinds
        }

        void OnBoardUp(object sender, MouseButtonEventArgs e)
        {
            if (_drag != null && _moving)
                LyricsService.Log("board drag " + _drag.Value + " -> " + _card.Model.Find(_drag.Value).X.ToString("0.0") + "," + _card.Model.Find(_drag.Value).Y.ToString("0.0") + " s=" + _s.ToString("0.000"));
            _drag = null;
            _moving = false;
            _guideV.Visibility = Visibility.Collapsed;
            _guideH.Visibility = Visibility.Collapsed;
            _canvas.ReleaseMouseCapture();
        }

        /// <summary>Snaps onto a sibling's edges or centre line when close, and shows the guide.</summary>
        void ApplyGuides(ref double mx, ref double my)
        {
            _guideV.Visibility = Visibility.Collapsed;
            _guideH.Visibility = Visibility.Collapsed;
            if (_lay == null || _drag == null) return;

            ModuleRect self;
            if (!_lay.TryFind(_drag.Value, out self)) return;

            const double near = 3.0;
            double bestX = double.NaN, bestY = double.NaN;
            foreach (ModuleRect r in _lay.Rects)
            {
                if (Math.Abs(r.X - self.X) < 0.01 && Math.Abs(r.Y - self.Y) < 0.01) continue; // self
                double[] xs = { r.X / _s, (r.X + r.W / 2) / _s, (r.X + r.W) / _s };
                double[] ys = { r.Y / _s, (r.Y + r.H / 2) / _s, (r.Y + r.H) / _s };
                foreach (double x in xs) if (Math.Abs(mx - x) <= near) bestX = x;
                foreach (double y in ys) if (Math.Abs(my - y) <= near) bestY = y;
            }
            if (!double.IsNaN(bestX))
            {
                mx = bestX;
                _guideV.X1 = _guideV.X2 = bestX * _s;
                _guideV.Y1 = 0;
                _guideV.Y2 = _lay.CardH;
                _guideV.Visibility = Visibility.Visible;
            }
            if (!double.IsNaN(bestY))
            {
                my = bestY;
                _guideH.Y1 = _guideH.Y2 = bestY * _s;
                _guideH.X1 = 0;
                _guideH.X2 = _lay.CardW;
                _guideH.Visibility = Visibility.Visible;
            }
        }

        public CardPreview()
        {
            Children.Add(_canvas);
            Children.Add(_guideV);
            Children.Add(_guideH);
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Top;
        }

    /// <summary>Renders the card's current state into the board, reusing the controls it built last time.</summary>
        public void Bind(CardWindow card)
        {
            if (card == null) return;
            CardMetrics baseMetrics = card.Metrics();
            CardConfig model = card.Model;   // the live model: board edits and card layout must agree

            // Everything geometric comes from LayoutEngine at the effective scale: the board is the
            // card shrunk, not a second design. Shrink again when the card is wider than the board.
            double s = Scale <= 0 ? 1.0 : Scale;
            LayoutResult layout = LayoutEngine.Compute(model, LayoutEngine.Scaled(baseMetrics, s));
            if (layout.CardW > FitWidth && layout.CardW > 0)
            {
                s *= FitWidth / layout.CardW;
                layout = LayoutEngine.Compute(model, LayoutEngine.Scaled(baseMetrics, s));
            }

            // Fonts ride the same factor; the card's own scene carries the desktop sizes.
            CardScene scene = card.Scene();
            scene.TitleSize *= s;
            scene.ArtistSize *= s;
            scene.LyricSize *= s;
            scene.BarThickness *= s;

            _card = card;
            _lay = layout;
            _s = s;
            HookDrag();

            if (_el == null)
            {
                var hooks = new CardHooks();
                hooks.Built = delegate(ModuleKind kind, FrameworkElement el)
                {
                    el.MouseLeftButtonDown += delegate
                    {
                        LastHit = kind;
                        if (ModuleClicked != null) ModuleClicked(kind, el);
                    };
                };
                _el = ModuleRenderer.Draw(_canvas, model, LayoutEngine.Scaled(baseMetrics, s), layout, scene, hooks);
            }

            ModuleRenderer.Update(_canvas, _el, model, layout, scene, card.Progress01, scene.Playing);
            // one comparison line a second is enough to prove the board is the card shrunk by s
            if ((DateTime.Now - _loggedAt).TotalMilliseconds > 1000)
            {
                _loggedAt = DateTime.Now;
                LayoutResult cardLayout = card.CardLayout;
                string cardDump = cardLayout == null ? "<none>" : cardLayout.Dump().Replace("\r", "").Replace("\n", " | ");
                try
                {
                    Point pt = PointToScreen(new Point(0, 0));
                    LyricsService.Log("board at " + (int)pt.X + "," + (int)pt.Y + " size=" + (int)Width + "x" + (int)Height);
                }
                catch { }
                LyricsService.Log("layout s=" + s.ToString("0.000") + " card[" + cardDump + "] board[" + layout.Dump().Replace("\r", "").Replace("\n", " | ") + "]");
            }

            Width = layout.CardW;
            Height = layout.CardH;
            _canvas.Width = layout.CardW;
            _canvas.Height = layout.CardH;
        }
    }
}
