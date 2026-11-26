// ---------------------------------------------------------------------------
// CardPreview.cs
//
/// The live preview drawn at the top of the Look page: the same model, the same
/// LayoutEngine and the same ModuleRenderer as the desktop card, laid out on a small
/// canvas instead of a transparent window. It owns its own visuals (the card's ones
/// belong to the card window) and is redrawn only when the card says the look changed
/// — no timer, no polling.
///
/// Scaling is done by scaling the *metrics* and laying the card out at 1:1. That keeps
/// every glyph, gap and border proportional, and avoids the layout passes that a
/// LayoutTransform on a measured element drags along.
// ---------------------------------------------------------------------------

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Amc
{
    /// <summary>Static mirror of the desktop card, used by the settings page.</summary>
    public class CardPreview : Grid
    {
        Border _card;
        Canvas _canvas;
        TextBlock _title, _lyric;
        Run _runTitle, _runArtist;
        Border _barTrack, _barFill;
        Canvas[] _glyph = new Canvas[3];
        Border _artHost;
        Ellipse _artRing;
        CardElements _elem;
        LayoutResult _lay;

        /// <summary>How much room the preview may take horizontally (device independent px).</summary>
        public double FitWidth = 430;

        static readonly Brush Fg = Frozen(Color.FromArgb(247, 255, 255, 255));
        static readonly Brush FgDim = Frozen(Color.FromArgb(158, 255, 255, 255));
        static readonly Brush FgLyric = Frozen(Color.FromArgb(242, 255, 255, 255));

        static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public CardPreview()
        {
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Top;
            Build();
        }

        // ---- construction ---------------------------------------------------

        void Build()
        {
            _card = new Border
            {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                BorderBrush = Frozen(Color.FromArgb(26, 255, 255, 255)),
                Background = Frozen(Color.FromArgb(238, 18, 18, 21)),
                ClipToBounds = true
            };
            Children.Add(_card);

            _canvas = new Canvas { ClipToBounds = true };
            _card.Child = _canvas;

            var titleHost = new Grid { ClipToBounds = true };
            _title = new TextBlock
            {
                FontSize = 15.5,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            _runTitle = new Run("Apple Music") { FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = Fg };
            _runArtist = new Run("") { FontSize = 11.5, Foreground = FgDim };
            _title.Inlines.Add(_runTitle);
            _title.Inlines.Add(_runArtist);
            titleHost.Children.Add(_title);
            _canvas.Children.Add(titleHost);

            var lyricHost = new Grid { ClipToBounds = true };
            _lyric = new TextBlock
            {
                FontSize = 15,
                Foreground = FgLyric,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Text = ""
            };
            lyricHost.Children.Add(_lyric);
            _canvas.Children.Add(lyricHost);

            var barGrid = new Grid();
            _barTrack = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = Frozen(Color.FromArgb(41, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            _barFill = new Border
            {
                Height = 3,
                Width = 0,
                CornerRadius = new CornerRadius(1.5),
                Background = Frozen(Color.FromArgb(235, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            barGrid.Children.Add(_barTrack);
            barGrid.Children.Add(_barFill);
            _canvas.Children.Add(barGrid);

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            for (int i = 0; i < 3; i++)
            {
                var g = new Canvas { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                _glyph[i] = g;
                var holder = new Grid { Width = 30, Height = 30, Margin = new Thickness(1, 0, 1, 0) };
                holder.Children.Add(g);
                controls.Children.Add(holder);
            }
            _canvas.Children.Add(controls);

            _artHost = new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(48), ClipToBounds = true };
            _artRing = new Ellipse { Stroke = Frozen(Color.FromArgb(33, 255, 255, 255)), StrokeThickness = 1 };
            _artHost.Child = _artRing;
            _canvas.Children.Add(_artHost);

            _elem = new CardElements
            {
                Title = titleHost,
                Lyrics = lyricHost,
                Progress = barGrid,
                Controls = controls,
                Cover = _artHost
            };
        }

        // ---- drawing --------------------------------------------------------

        /// <summary>
        /// Repaint from the current model. Everything the desktop card cares about arrives as a
        /// parameter on purpose: the preview never reaches back into the card.
        /// </summary>
        public void Render(CardConfig model, CardMetrics metrics, double cardScale,
            string title, string artist, string lyric, double progress01, bool playing,
            Brush cover, string controlsColor)
        {
            if (_canvas == null || model == null || metrics == null) return;

            double s = cardScale > 0 ? cardScale : 1.0;
            CardMetrics ms = Scaled(metrics, s);
            double want = FitWidth > 60 ? FitWidth : 430;
            if (ms.CardW > want) ms = Scaled(metrics, Math.Max(0.2, s * want / Math.Max(1.0, ms.CardW)));

            _lay = LayoutEngine.Compute(model, ms);
            // LayoutEngine's rects start at the card's content origin, i.e. inside the same
            // Padding the desktop card uses. Without this the whole layout slides up-left and
            // the artwork hangs off the right edge.
            var pad = new Thickness(ms.PadX, ms.PadY, ms.PadX, ms.PadY);
            _card.Padding = pad;
            _card.Width = _lay.CardW;
            _card.Height = _lay.CardH;
            _canvas.Width = Math.Max(0, _lay.CardW - pad.Left - pad.Right);
            _canvas.Height = Math.Max(0, _lay.CardH - pad.Top - pad.Bottom);
            Width = _lay.CardW;
            Height = _lay.CardH;

            _runTitle.Text = title ?? "";
            _runArtist.Text = (artist ?? "").Length > 0 ? "  " + artist : "";
            _runTitle.FontSize = ms.TitleFont;
            _runArtist.FontSize = ms.ArtistFont;
            _title.FontSize = ms.TitleFont;
            _lyric.FontSize = ms.LyricFont;
            _lyric.Text = lyric ?? "";

            Brush glyphBrush = FgLyric;
            Color? col = TryColor(controlsColor);
            if (col.HasValue) glyphBrush = new SolidColorBrush(col.Value);
            DrawGlyph(0, "prev", glyphBrush, s);
            DrawGlyph(1, playing ? "pause" : "play", glyphBrush, s);
            DrawGlyph(2, "next", glyphBrush, s);

            ModuleRect pr = _layRect(ModuleKind.Progress);
            _barTrack.Height = ms.BarThickness;
            _barFill.Height = ms.BarThickness;
            _barTrack.CornerRadius = new CornerRadius(ms.BarThickness / 2);
            _barFill.CornerRadius = new CornerRadius(ms.BarThickness / 2);
            _barFill.Width = Math.Max(0, pr.W * Clamp01(progress01));

            _artHost.Width = ms.ArtSize;
            _artHost.Height = ms.ArtSize;
            _artHost.CornerRadius = new CornerRadius(ms.ArtSize / 2);
            _artRing.Width = ms.ArtSize;
            _artRing.Height = ms.ArtSize;
            _artHost.Background = cover;   // null clears it: the desktop card shows an empty ring too

            ModuleRenderer.Apply(_canvas, model, _lay, _elem);
        }

        /// <summary>Every metric except the line factor (a ratio) scales with the card.</summary>
        static CardMetrics Scaled(CardMetrics m, double s)
        {
            var r = new CardMetrics();
            r.PadX = m.PadX * s;
            r.PadY = m.PadY * s;
            r.Border = m.Border;
            r.ColGap = m.ColGap * s;
            r.ArtSize = m.ArtSize * s;
            r.CardW = m.CardW * s;
            r.TitleFont = m.TitleFont * s;
            r.ArtistFont = m.ArtistFont * s;
            r.LyricFont = m.LyricFont * s;
            r.AlbumFont = m.AlbumFont * s;
            r.LyricGap = m.LyricGap * s;
            r.BarGap = m.BarGap * s;
            r.BarThickness = Math.Max(1.0, m.BarThickness * s);
            r.ControlsGap = m.ControlsGap * s;
            r.ControlsH = m.ControlsH * s;
            r.LineFactor = m.LineFactor;
            return r;
        }

        ModuleRect _layRect(ModuleKind kind)
        {
            ModuleRect r;
            if (_lay != null && _lay.TryFind(kind, out r)) return r;
            return new ModuleRect();
        }

        static double Clamp01(double v)
        {
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }

        static Color? TryColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }

        // Same glyphs the card draws, so the preview is not a look-alike but the same shape.
        void DrawGlyph(int slot, string kind, Brush brush, double s)
        {
            Canvas c = _glyph[slot];
            if (c == null) return;
            c.Children.Clear();
            if (kind == "pause")
            {
                c.Children.Add(Bar(2.9, 2.5, 3.4, 10.9, brush, s));
                c.Children.Add(Bar(9.6, 2.5, 3.4, 10.9, brush, s));
            }
            else if (kind == "play")
            {
                c.Children.Add(Tri("M 3.4,2.2 L 12.9,7.95 L 3.4,13.7 Z", brush, s));
            }
            else if (kind == "prev")
            {
                c.Children.Add(Bar(1.2, 2.5, 2.5, 10.9, brush, s));
                c.Children.Add(Tri("M 13.0,2.4 L 13.0,13.5 L 5.3,7.95 Z", brush, s));
            }
            else
            {
                c.Children.Add(Bar(12.3, 2.5, 2.5, 10.9, brush, s));
                c.Children.Add(Tri("M 3.0,2.4 L 3.0,13.5 L 10.7,7.95 Z", brush, s));
            }
        }

        static Rectangle Bar(double x, double y, double w, double h, Brush b, double s)
        {
            var r = new Rectangle { Width = w * s, Height = h * s, RadiusX = 1, RadiusY = 1, Fill = b };
            Canvas.SetLeft(r, x * s);
            Canvas.SetTop(r, y * s);
            return r;
        }

        static Path Tri(string data, Brush b, double s)
        {
            var p = new Path { Data = Geometry.Parse(data), Fill = b };
            p.LayoutTransform = new ScaleTransform(s, s);
            return p;
        }
    }
}
