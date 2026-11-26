// ---------------------------------------------------------------------------
// ModuleRenderer.cs
//
/// The one place that knows what a module looks like.
///
/// LayoutEngine decides where every box goes; this file builds the actual controls, paints their
/// contents (text runs, progress fill, glyphs, the turning cover with its halo and ring) and puts
/// them on whichever Canvas it is handed. Neither the card window nor the settings board draws a
/// module itself, so the two can never drift apart visually.
///
/// Nothing here may touch a window: the caller passes a plain scene (text/values/brushes) and, when
/// it wants interaction, a hook that receives (kind, element) after the element is built. That is
/// how the board gets its Tag-ed, clickable modules without the renderer knowing the board exists.
// ---------------------------------------------------------------------------

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Amc
{
    /// <summary>Everything a card shows, without any reference to a player or a window.</summary>
    public class CardScene
    {
        public string Title = "";
        public string Artist = "";
        public string Lyric = "";
        public double Progress01;
        public bool Playing;
        public Brush Artwork;
        public double Spin;
        public bool Halo;
        public Brush TitleBrush = Brushes.White;
        public Brush ArtistBrush = Brushes.White;
        public Brush LyricBrush = Brushes.White;
        public Brush BarTrackBrush = Brushes.White;
        public Brush BarFillBrush = Brushes.White;
        public Brush ControlsBrush = Brushes.White;
        public double TitleSize = 15.5;
        public double ArtistSize = 11.5;
        public double LyricSize = 15;
        public double BarThickness = 3;
    }

    /// <summary>Optional callbacks so a host can react to modules without the renderer knowing it.</summary>
    public class CardHooks
    {
        public Action Play;
        public Action Prev;
        public Action Next;
        /// <summary>Called for every built module: the host can Tag it, attach handlers, measure it.</summary>
        public Action<ModuleKind, FrameworkElement> Built;
    }

    /// <summary>The visual for each module kind, plus the handles a host needs to update content.</summary>
    public class CardElements
    {
        public FrameworkElement Title;
        public FrameworkElement Lyrics;
        public FrameworkElement Progress;
        public FrameworkElement Controls;
        public FrameworkElement Cover;

        public Grid TitleHost;
        public TextBlock TitleText;
        public Run TitleRun;
        public Run ArtistRun;
        public Grid LyricHost;
        public TextBlock LyricText;
        public Grid ProgressHost;
        public Border BarTrack;
        public Border BarFill;
        public Grid ControlsHost;
        public StackPanel ControlsPanel;
        public Canvas PlayGlyph;
        public Grid CoverHost;
        public Ellipse ArtEllipse;
    }

    /// <summary>Builds module visuals from a scene and a computed layout, then places them.</summary>
    public static class ModuleRenderer
    {
        /// <summary>Builds + places every enabled module on <paramref name="target"/>, replacing its content.</summary>
        public static CardElements Draw(Canvas target, CardConfig model, CardMetrics metrics, LayoutResult layout,
                                        CardScene scene, CardHooks hooks)
        {
            if (target == null || layout == null) return null;
            target.Children.Clear();
            CardElements el = Build(model, metrics, layout, scene, hooks);
            Add(target, el.Cover);
            Add(target, el.Title);
            Add(target, el.Lyrics);
            Add(target, el.Progress);
            Add(target, el.Controls);
            Apply(target, model, layout, el);
            return el;
        }

        static void Add(Canvas target, FrameworkElement el)
        {
            if (el != null) target.Children.Add(el);
        }

        /// <summary>Creates the module controls (not yet attached to anything).</summary>
        public static CardElements Build(CardConfig model, CardMetrics metrics, LayoutResult layout,
                                         CardScene scene, CardHooks hooks)
        {
            var el = new CardElements();
            if (scene == null) scene = new CardScene();

            // --- title: one run for the song, a smaller one for the artist (they share a line) ---
            el.TitleRun = new Run(scene.Title) { FontSize = scene.TitleSize, FontWeight = FontWeights.SemiBold };
            el.ArtistRun = new Run(scene.Artist) { FontSize = scene.ArtistSize, FontWeight = FontWeights.Normal };
            el.TitleText = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            el.TitleText.Inlines.Add(el.TitleRun);
            el.TitleText.Inlines.Add(new Run("  "));
            el.TitleText.Inlines.Add(el.ArtistRun);
            el.TitleHost = new Grid { ClipToBounds = true };
            el.TitleHost.Children.Add(el.TitleText);
            el.Title = el.TitleHost;

            // --- lyrics ---
            el.LyricText = new TextBlock
            {
                Text = scene.Lyric,
                FontSize = scene.LyricSize,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };
            el.LyricHost = new Grid { ClipToBounds = true };
            el.LyricHost.Children.Add(el.LyricText);
            el.Lyrics = el.LyricHost;

            // --- progress ---
            el.BarTrack = new Border
            {
                Height = scene.BarThickness,
                CornerRadius = new CornerRadius(scene.BarThickness / 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            el.BarFill = new Border
            {
                Height = scene.BarThickness,
                CornerRadius = new CornerRadius(scene.BarThickness / 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 0
            };
            el.ProgressHost = new Grid();
            el.ProgressHost.Children.Add(el.BarTrack);
            el.ProgressHost.Children.Add(el.BarFill);
            el.Progress = el.ProgressHost;

            // --- transport controls ---
            el.ControlsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            el.ControlsPanel.Children.Add(GlyphButton("prev", hooks == null ? null : hooks.Prev));
            Canvas playGlyph;
            Grid playRoot = PlayButton(scene.Playing, hooks == null ? null : hooks.Play, out playGlyph);
            el.PlayGlyph = playGlyph;
            el.ControlsPanel.Children.Add(playRoot);
            el.ControlsPanel.Children.Add(GlyphButton("next", hooks == null ? null : hooks.Next));
            el.ControlsHost = new Grid();
            el.ControlsHost.Children.Add(el.ControlsPanel);
            el.Controls = el.ControlsHost;

            // --- cover: turns, haloed, ringed, and clipped to its own ellipse ---
            el.CoverHost = new Grid { RenderTransformOrigin = new Point(0.5, 0.5) };
            el.ArtEllipse = new Ellipse { RenderTransformOrigin = new Point(0.5, 0.5) };
            if (scene.Spin != 0) el.ArtEllipse.RenderTransform = new RotateTransform(scene.Spin);
            el.CoverHost.Children.Add(el.ArtEllipse);
            el.CoverHost.Children.Add(new Ellipse { Stroke = Tint(Colors.White, 0.13), StrokeThickness = 1 });
            if (scene.Halo) el.CoverHost.Effect = Halo();
            el.Cover = el.CoverHost;

            Paint(el, scene, metrics);
            if (hooks != null && hooks.Built != null)
            {
                Hook(hooks, ModuleKind.Title, el.Title);
                Hook(hooks, ModuleKind.Lyrics, el.Lyrics);
                Hook(hooks, ModuleKind.Progress, el.Progress);
                Hook(hooks, ModuleKind.Controls, el.Controls);
                Hook(hooks, ModuleKind.Cover, el.Cover);
            }
            return el;
        }

        static void Hook(CardHooks hooks, ModuleKind kind, FrameworkElement el)
        {
            if (el == null) return;
            el.Tag = kind;
            hooks.Built(kind, el);
        }

        /// <summary>Refreshes an already-built tree: repaint, reposition, re-fill. Never rebuilds.</summary>
        public static void Update(Canvas target, CardElements el, CardConfig model, LayoutResult layout,
                                  CardScene scene, double progress01, bool playing)
        {
            if (target == null || el == null || layout == null) return;
            Paint(el, scene, null);
            Apply(target, model, layout, el);
            SetProgress(el, layout, progress01);
            SetPlayGlyph(el, playing);
        }

        /// <summary>Repaints the contents (text, fill, cover, colours) without rebuilding the tree.</summary>
        public static void Paint(CardElements el, CardScene scene, CardMetrics metrics)
        {
            if (el == null || scene == null) return;
            if (el.TitleRun != null)
            {
                if (el.TitleRun.Text != scene.Title) el.TitleRun.Text = scene.Title;
                el.TitleRun.FontSize = scene.TitleSize;
                el.TitleRun.Foreground = scene.TitleBrush;
            }
            if (el.ArtistRun != null)
            {
                if (el.ArtistRun.Text != scene.Artist) el.ArtistRun.Text = scene.Artist;
                el.ArtistRun.FontSize = scene.ArtistSize;
                el.ArtistRun.Foreground = scene.ArtistBrush;
            }
            if (el.LyricText != null)
            {
                if (el.LyricText.Text != scene.Lyric) el.LyricText.Text = scene.Lyric;
                el.LyricText.FontSize = scene.LyricSize;
                el.LyricText.Foreground = scene.LyricBrush;
            }
            if (el.BarTrack != null) el.BarTrack.Background = scene.BarTrackBrush;
            if (el.BarFill != null) el.BarFill.Background = scene.BarFillBrush;
            if (el.ControlsPanel != null) ApplyControlsInk(el.ControlsPanel, scene.ControlsBrush);
            if (el.ArtEllipse != null) el.ArtEllipse.Fill = scene.Artwork;
        }

        /// <summary>Width of the progress fill for a layout, clamped to the bar.</summary>
        public static void SetProgress(CardElements el, LayoutResult layout, double progress01)
        {
            if (el == null || el.BarFill == null) return;
            ModuleRect r;
            if (layout == null || !layout.TryFind(ModuleKind.Progress, out r))
            {
                el.BarFill.Width = 0;
                return;
            }
            double p = progress01;
            if (p < 0) p = 0;
            if (p > 1) p = 1;
            el.BarFill.Width = r.W * p;
        }

        /// <summary>Swaps the middle button between play and pause without touching the rest.</summary>
        public static void SetPlayGlyph(CardElements el, bool playing)
        {
            if (el == null || el.PlayGlyph == null) return;
            FillGlyph(el.PlayGlyph, playing ? "pause" : "play");
        }

        // ---- geometry ----

        /// <summary>Places already-built elements on a canvas according to a computed layout.</summary>
        public static void Apply(Canvas canvas, CardConfig model, LayoutResult layout, CardElements el)
        {
            if (canvas == null || layout == null || el == null) return;
            Place(canvas, el.Cover, model, layout, ModuleKind.Cover);
            Place(canvas, el.Title, model, layout, ModuleKind.Title);
            Place(canvas, el.Lyrics, model, layout, ModuleKind.Lyrics);
            Place(canvas, el.Progress, model, layout, ModuleKind.Progress);
            Place(canvas, el.Controls, model, layout, ModuleKind.Controls);
        }

        static void Place(Canvas canvas, FrameworkElement el, CardConfig model, LayoutResult layout, ModuleKind kind)
        {
            if (el == null) return;
            ModuleRect r;
            if (!layout.TryFind(kind, out r))
            {
                // disabled for this template: keep it out of the visual tree cost, not just invisible
                el.Visibility = Visibility.Collapsed;
                return;
            }
            el.Visibility = Visibility.Visible;
            Canvas.SetLeft(el, r.X);
            Canvas.SetTop(el, r.Y);
            el.Width = r.W;
            el.Height = r.H;
            ModuleConfig cfg = model == null ? null : model.Find(kind);
            Panel.SetZIndex(el, cfg == null ? 0 : cfg.ZIndex);
        }

        // ---- parts ----

        static Grid GlyphButton(string kind, Action act)
        {
            var root = new Grid
            {
                Width = 30,
                Height = 30,
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 2, 0)
            };
            var glyph = new Canvas
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            FillGlyph(glyph, kind);
            root.Children.Add(glyph);
            if (act != null) root.MouseLeftButtonUp += delegate { act(); };
            return root;
        }

        static Grid PlayButton(bool playing, Action act, out Canvas glyphOut)
        {
            var glyph = new Canvas
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            FillGlyph(glyph, playing ? "pause" : "play");
            var root = new Grid
            {
                Width = 30,
                Height = 30,
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 2, 0)
            };
            root.Children.Add(glyph);
            if (act != null) root.MouseLeftButtonUp += delegate { act(); };
            glyphOut = glyph;
            return root;
        }

        static void FillGlyph(Canvas c, string kind)
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

        static Rectangle GlyphBar(double x, double y, double w, double h)
        {
            return new Rectangle { Width = w, Height = h, RadiusX = 1.2, RadiusY = 1.2, Fill = Brushes.White };
        }

        static Path GlyphTri(string data)
        {
            return new Path { Data = Geometry.Parse(data), Fill = Brushes.White };
        }

        static void ApplyControlsInk(StackPanel panel, Brush brush)
        {
            foreach (UIElement child in panel.Children)
            {
                var g = child as Panel;
                if (g == null) continue;
                foreach (UIElement k in g.Children)
                {
                    var sh = k as Shape;
                    if (sh != null) { sh.Fill = brush == null ? Brushes.White : brush; continue; }
                    var cv = k as Canvas;
                    if (cv == null) continue;
                    foreach (UIElement q in cv.Children)
                    {
                        var sh2 = q as Shape;
                        if (sh2 != null) sh2.Fill = brush == null ? Brushes.White : brush;
                    }
                }
            }
        }

        static SolidColorBrush Tint(Color c, double a)
        {
            return new SolidColorBrush(Color.FromArgb((byte)(a * 255), c.R, c.G, c.B));
        }

        static DropShadowEffect Halo()
        {
            return new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 2,
                Direction = 315,
                Opacity = 0.55
            };
        }
    }
}
