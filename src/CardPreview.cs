// ---------------------------------------------------------------------------
// CardPreview.cs
//
/// The layout board shown at the top of the Look page. It is a *sketch pad*, not a
/// copy of the card: every module is drawn as a plain placeholder box, and edits go
/// into a private draft copy of the model. The real card only changes when the user
/// presses Apply — that is what keeps dragging silky and the card perfectly still.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Amc
{
    public class CardPreview : Grid
    {
        /// <summary>Widest the sketch is allowed to grow before it is scaled down again.</summary>
        public double FitWidth = 430;

        /// <summary>Grid the dragged boxes snap to, in model units.</summary>
        public double Snap = 4.0;

        /// <summary>Raised when the user presses Apply (draft committed) or Cancel (draft binned).</summary>
        public event Action ApplyRequested;
        public event Action CancelRequested;

        readonly Canvas _canvas = new Canvas();
        readonly Dictionary<ModuleKind, FrameworkElement> _boxes = new Dictionary<ModuleKind, FrameworkElement>();
        readonly Line _guideV = new Line { Stroke = new SolidColorBrush(Color.FromArgb(90, 120, 190, 255)), StrokeThickness = 1, Visibility = Visibility.Collapsed };
        readonly Line _guideH = new Line { Stroke = new SolidColorBrush(Color.FromArgb(90, 120, 190, 255)), StrokeThickness = 1, Visibility = Visibility.Collapsed };

        CardWindow _card;
        CardConfig _draft;
        CardConfig _applied;   // what the card looked like before the last Apply (Cancel undoes it)
        LayoutResult _lay;
        double _s = 1.0;
        double _padX, _padY;

        ModuleKind? _drag;
        bool _started;
        Point _grab;
        double _seedX, _seedY, _startX, _startY;

        static readonly Brush BoxStroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
        static readonly Brush BoxFill = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255));
        static readonly Brush LabelBrush = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));

        public CardPreview()
        {
            Children.Add(_canvas);
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Top;
            _canvas.Background = Brushes.Transparent;
            _canvas.MouseLeftButtonDown += OnDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseLeftButtonUp += OnUp;
        }

        public bool HasDraft
        {
            get { return _draft != null; }
        }

        // -- draft lifecycle ---------------------------------------------------

        /// <summary>Start (or restart) a draft from the card's live model.</summary>
        public void BeginDraft(CardWindow card)
        {
            _card = card;
            _draft = card == null ? null : card.Model.Clone();
            Rebuild();
        }

        /// <summary>Discard the draft and go back to what the card is actually using.</summary>
        /// <summary>
        /// Cancel means "undo": if an Apply already happened, the card goes back to the template it
        /// had before that Apply (and that is written to disk again). With no Apply yet it simply
        /// drops the working draft.
        /// </summary>
        public void Revert()
        {
            if (_card == null) return;
            if (_card.RestorePreviousModel())
            {
                _applied = null;
                _draft = _card.Model.Clone();
                Rebuild();
                return;
            }
            if (_applied != null)
            {
                CardConfig live = _card.Model;
                live.Modules.Clear();
                for (int i = 0; i < _applied.Modules.Count; i++) live.Modules.Add(_applied.Modules[i].Clone());
                _applied = null;
                _card.InvalidateModel();
                _card.SaveModel();
                LyricsService.Log("board cancel -> restored template");
            }
            _draft = _card.Model.Clone();
            Rebuild();
        }

        /// <summary>Push the draft onto the card and make it permanently active.</summary>
        public void Commit()
        {
            LyricsService.Log("board apply drafts=" + (_draft == null ? -1 : _draft.Modules.Count));
            if (_card == null || _draft == null) return;
            CardConfig live = _card.Model;
            _applied = live.Clone();                          // memory fallback
            _card.StorePreviousModel(live.Clone());           // survives the rebuild after Apply
            live.Modules.Clear();
            for (int i = 0; i < _draft.Modules.Count; i++) live.Modules.Add(_draft.Modules[i].Clone());
            _card.InvalidateModel();
            _draft = _card.Model.Clone();
            Rebuild();
        }

        public void RaiseApply()
        {
            Commit();
            if (ApplyRequested != null) ApplyRequested();
        }

        public void RaiseCancel()
        {
            Revert();
            if (CancelRequested != null) CancelRequested();
        }

        // -- drawing -----------------------------------------------------------

        void Rebuild()
        {
            _canvas.Children.Clear();
            _boxes.Clear();
            if (_draft == null || _card == null) return;

            CardMetrics baseMetrics = _card.Metrics();
            double s = _card.ScaleValue;
            LayoutResult lay = LayoutEngine.Compute(_draft, LayoutEngine.Scaled(baseMetrics, s), true);

            // two measure passes: text width is not perfectly linear in the factor
            for (int pass = 0; pass < 2 && lay.CardW > FitWidth && lay.CardW > 0; pass++)
            {
                s *= FitWidth / lay.CardW;
                lay = LayoutEngine.Compute(_draft, LayoutEngine.Scaled(baseMetrics, s), true);
            }

            _s = Math.Max(0.05, s);
            _lay = lay;
            // The card's own Border padding is where content really starts; the board must use the
            // same origin or every box sits (16,12) model units away from where the card draws it.
            CardMetrics sm = LayoutEngine.Scaled(baseMetrics, s);
            // The card is padding-free now: its content origin IS the window origin, so the board
            // must not add any inset either. Keep the fields (drag maths reads them) at zero.
            _padX = 0.0;
            _padY = 0.0;

            _canvas.Width = lay.CardW * _s;
            _canvas.Height = lay.CardH * _s;
            Width = _canvas.Width;
            Height = _canvas.Height;

            Border frame = new Border
            {
                Width = _canvas.Width,
                Height = _canvas.Height,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            };
            _canvas.Children.Add(frame);

            for (int i = 0; i < _draft.Modules.Count; i++)
            {
                ModuleConfig mc = _draft.Modules[i];
                if (!mc.Enabled) continue;
                ModuleRect r;
                if (!lay.TryFind(mc.Kind, out r)) continue;

                FrameworkElement box;
                if (mc.Kind == ModuleKind.Cover) box = Placeholder("封面", r, true);
                else if (mc.Kind == ModuleKind.Title) box = Placeholder("歌名", r, false);
                else if (mc.Kind == ModuleKind.Artist) box = Placeholder("艺人", r, false);
                else if (mc.Kind == ModuleKind.Album) box = Placeholder("专辑", r, false);
                else if (mc.Kind == ModuleKind.Lyrics) box = Placeholder("歌词", r, false);
                else if (mc.Kind == ModuleKind.Progress) box = Placeholder("进度条", r, false);
                else if (mc.Kind == ModuleKind.Controls) box = Placeholder("按钮", r, false);
                else box = Placeholder("按钮", r, false);

                box.Tag = mc.Kind;
                Canvas.SetLeft(box, (r.X + _padX) * _s);
                Canvas.SetTop(box, (r.Y + _padY) * _s);
                _canvas.Children.Add(box);
                _boxes[mc.Kind] = box;
            }

            _canvas.Children.Add(_guideV);
            _canvas.Children.Add(_guideH);
        }

        FrameworkElement Placeholder(string label, ModuleRect r, bool round)
        {
            double w = Math.Max(2.0, r.W * _s);
            double h = Math.Max(2.0, r.H * _s);

            if (round)
            {
                double d = Math.Min(w, h);
                var ring = new Ellipse
                {
                    Width = d,
                    Height = d,
                    Stroke = BoxStroke,
                    StrokeThickness = 1,
                    Fill = BoxFill
                };
                var wrap = new Grid { Width = w, Height = h, Background = Brushes.Transparent };
                wrap.Children.Add(ring);
                return wrap;
            }

            var border = new Border
            {
                Width = w,
                Height = h,
                CornerRadius = new CornerRadius(3),
                BorderBrush = BoxStroke,
                BorderThickness = new Thickness(1),
                Background = BoxFill
            };
            border.Child = new TextBlock
            {
                Text = label,
                FontSize = Math.Max(7.0, Math.Min(h * 0.45, Math.Max(9.0, 11.0 * _s))),
                Foreground = LabelBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            return border;
        }

        // -- dragging ----------------------------------------------------------

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (_draft == null || _lay == null) return;
            ModuleKind? kind = KindOf(e.OriginalSource);
            if (kind == null) return;
            ModuleRect r;
            if (!_lay.TryFind(kind.Value, out r)) return;
            ModuleConfig mc = _draft.Find(kind.Value);
            if (mc == null) return;

            // seed lazily: a click that never moves must leave the draft untouched
            _seedX = double.IsNaN(mc.X) ? r.X : mc.X;
            _seedY = double.IsNaN(mc.Y) ? r.Y : mc.Y;
            _startX = _seedX;
            _startY = _seedY;
            _started = false;
            _drag = kind;
            _grab = e.GetPosition(_canvas);
            _canvas.CaptureMouse();
            e.Handled = true;
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (_drag == null || _draft == null) return;
            Point p = e.GetPosition(_canvas);

            if (!_drag.HasValue || e.LeftButton != MouseButtonState.Pressed) return;

            if (!_started)
            {
                if (Math.Abs(p.X - _grab.X) < 3.0 && Math.Abs(p.Y - _grab.Y) < 3.0) return;
                ModuleConfig seed = _draft.Find(_drag.Value);
                if (seed != null)
                {
                    seed.X = _seedX;
                    seed.Y = _seedY;
                }
                _started = true;
            }

            double mx = _startX + (p.X - _grab.X) / _s;
            double my = _startY + (p.Y - _grab.Y) / _s;

            ShowGuides(mx, my);
            if (Snap > 0.5)
            {
                mx = Math.Round(mx / Snap) * Snap;
                my = Math.Round(my / Snap) * Snap;
            }

            // No clamping to the current content box: modules may be dragged outside it and the
            // card is expected to grow with them. Only a sanity bound so a stuck pointer cannot
            // write absurd numbers into the template.
            if (mx < -2000) mx = -2000; else if (mx > 6000) mx = 6000;
            if (my < -2000) my = -2000; else if (my > 6000) my = 6000;

            ModuleConfig mc = _draft.Find(_drag.Value);
            if (mc == null) return;
            if (Math.Abs(mc.X - mx) < 0.01 && Math.Abs(mc.Y - my) < 0.01) return;
            mc.X = mx;
            mc.Y = my;
            Rebuild();
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            _drag = null;
            _started = false;
            _guideV.Visibility = Visibility.Collapsed;
            _guideH.Visibility = Visibility.Collapsed;
            _canvas.ReleaseMouseCapture();
            Rebuild();
        }

        void ShowGuides(double mx, double my)
        {
            _guideV.Visibility = Visibility.Collapsed;
            _guideH.Visibility = Visibility.Collapsed;
            if (_draft == null || _lay == null) return;

            const double tol = 2.0;
            for (int i = 0; i < _draft.Modules.Count; i++)
            {
                ModuleConfig other = _draft.Modules[i];
                if (!other.Enabled || _drag == other.Kind) continue;
                ModuleRect or;
                if (!_lay.TryFind(other.Kind, out or)) continue;

                if (Math.Abs(or.X - mx) < tol || Math.Abs(or.X + or.W * 0.5 - mx) < tol)
                {
                    double gx = (or.X + _padX) * _s;
                    _guideV.X1 = gx;
                    _guideV.X2 = gx;
                    _guideV.Y1 = 0;
                    _guideV.Y2 = _canvas.Height;
                    _guideV.Visibility = Visibility.Visible;
                }
                if (Math.Abs(or.Y - my) < tol || Math.Abs(or.Y + or.H * 0.5 - my) < tol)
                {
                    double gy = (or.Y + _padY) * _s;
                    _guideH.Y1 = gy;
                    _guideH.Y2 = gy;
                    _guideH.X1 = 0;
                    _guideH.X2 = _canvas.Width;
                    _guideH.Visibility = Visibility.Visible;
                }
            }
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
    }
}
