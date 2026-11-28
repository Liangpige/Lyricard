using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Amc
{
    /// <summary>
    /// A small set of motion helpers. Everything is a WPF animation on the property that
    /// actually needs to move, so the effects stay interruptible: hovering out mid-tween
    /// runs the reverse from wherever the value happens to be.
    /// </summary>
    public static class UiMotion
    {
        public static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction InOut = new CubicEase { EasingMode = EasingMode.EaseInOut };
        /// <summary>Snappier than Cubic: slides arrive without a long tail of travel.</summary>
        public static readonly IEasingFunction Quick = new QuinticEase { EasingMode = EasingMode.EaseOut };

        /// <summary>Runs work once a tween of the given length has had time to land.</summary>
        internal static void Defer(int ms, Action work)
        {
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(ms + 15);
            timer.Tick += delegate(object s, EventArgs e)
            {
                timer.Stop();
                if (work != null) work();
            };
            timer.Start();
        }

        /// <summary>Animate a brush's colour. The brush must be unfrozen (created for one control).</summary>
        public static void Color(SolidColorBrush brush, Color to, int ms)
        {
            if (brush == null) return;
            // Theme brushes are handed out frozen (one instance per colour). Animating a frozen
            // Freezable throws, so interactive elements must own a mutable copy -- and this guard
            // keeps a stray frozen brush from taking the window down.
            if (brush.IsFrozen) return;
            var a = new ColorAnimation(to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = Out;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }

        public static void Opacity(UIElement el, double to, int ms)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = Out;
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        public static void Scale(ScaleTransform st, double to, int ms)
        {
            if (st == null) return;
            var ax = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            ax.EasingFunction = Out;
            var ay = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            ay.EasingFunction = Out;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
        }

        public static void Slide(TranslateTransform tt, double x, double y, int ms)
        {
            Slide(tt, x, y, ms, Out);
        }

        public static void Slide(TranslateTransform tt, double x, double y, int ms, IEasingFunction ease)
        {
            if (tt == null) return;
            var ax = new DoubleAnimation(x, TimeSpan.FromMilliseconds(ms));
            ax.EasingFunction = ease;
            var ay = new DoubleAnimation(y, TimeSpan.FromMilliseconds(ms));
            ay.EasingFunction = ease;
            tt.BeginAnimation(TranslateTransform.XProperty, ax);
            tt.BeginAnimation(TranslateTransform.YProperty, ay);
        }

        /// <summary>
        /// HandyControl's click feedback: a soft circle grows out of the pointer and fades.
        /// The host must be clipped to its own rounded shape (UiKit does that for buttons).
        /// </summary>
        public static void Ripple(Border host, MouseButtonEventArgs e, Color tint, double peak, int ms)
        {
            if (host == null) return;
            var pos = e.GetPosition(host);
            double w = host.ActualWidth, h = host.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double dx = Math.Max(pos.X, w - pos.X);
            double dy = Math.Max(pos.Y, h - pos.Y);
            double r = Math.Sqrt(dx * dx + dy * dy);

            var layer = RippleLayer(host);

            var dot = new Ellipse();
            dot.Width = dot.Height = r * 2;
            dot.Fill = new SolidColorBrush(tint);
            dot.IsHitTestVisible = false;
            dot.RenderTransformOrigin = new Point(0.5, 0.5);
            var st = new ScaleTransform(0.05, 0.05);
            dot.RenderTransform = st;
            Canvas.SetLeft(dot, pos.X - r);
            Canvas.SetTop(dot, pos.Y - r);
            layer.Children.Add(dot);

            if (peak > 0) dot.Opacity = peak < 1.0 ? peak : 1.0;
            var grow = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(ms));
            grow.EasingFunction = Quick;
            var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(ms));
            fade.BeginTime = TimeSpan.FromMilliseconds(ms * 0.20);
            fade.EasingFunction = Out;
            fade.Completed += delegate { layer.Children.Remove(dot); };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            dot.BeginAnimation(UIElement.OpacityProperty, fade);
            if (peak < 0) return;
        }

        /// <summary>Wraps button content in the Grid + ripple canvas the Button style expects.</summary>
        internal static Grid Body(UIElement content)
        {
            var g = new Grid();
            g.Children.Add(content);
            g.Children.Add(RippleCanvas());
            return g;
        }

        static Canvas RippleCanvas()
        {
            var c = new Canvas();
            c.Name = "ripple";
            c.IsHitTestVisible = false;
            c.ClipToBounds = true;
            c.Background = Brushes.Transparent;
            return c;
        }

        /// <summary>
        /// The transparent canvas a ripple is painted on. A Canvas measures to zero, so a dot
        /// bigger than the button cannot stretch the button -- a Grid child with negative margins
        /// did exactly that.
        /// </summary>
        static Canvas RippleLayer(Border host)
        {
            var panel = host.Child as Grid;
            if (panel == null)
            {
                var old = host.Child;
                host.Child = null;
                panel = new Grid();
                if (old != null) panel.Children.Add(old);
                host.Child = panel;
            }
            for (int i = 0; i < panel.Children.Count; i++)
            {
                var c = panel.Children[i] as Canvas;
                if (c != null && c.Name == "ripple") return c;
            }
            var layer = RippleCanvas();
            panel.Children.Add(layer);
            return layer;
        }

        /// <summary>Fade in and rise a few pixels: the entrance used by pages and cards.</summary>
        public static void Enter(FrameworkElement el, int ms, double lift)
        {
            Enter(el, ms, lift, 0);
        }

        /// <summary>Same entrance, held back by <paramref name="delay"/> ms (used to stagger cards).</summary>
        public static void Enter(FrameworkElement el, int ms, double lift, int delay)
        {
            var wait = TimeSpan.FromMilliseconds(delay);
            el.Opacity = 0;
            var tt = el.RenderTransform as TranslateTransform;
            if (tt == null) { tt = new TranslateTransform(0, lift); el.RenderTransform = tt; }
            else tt.Y = lift;

            var fade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(ms));
            fade.BeginTime = wait;
            fade.EasingFunction = Out;
            el.BeginAnimation(UIElement.OpacityProperty, fade);

            var ax = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(ms));
            ax.BeginTime = wait;
            ax.EasingFunction = Out;
            tt.BeginAnimation(TranslateTransform.YProperty, ax);
        }
    }

    /// <summary>Semantic colour of a notice or a badge.</summary>
    public enum UiTone
    {
        Info,
        Ok,
        Warn,
        Danger
    }

    /// <summary>Which visual weight a button gets.</summary>
    public enum UiButtonKind
    {
        Primary,
        Ghost,
        Quiet,
        Danger
    }

    /// <summary>
    /// The control factory: every widget is built from primitives in code, so the whole kit stays
    /// one file that a C# 5 compiler can chew through and nothing depends on a resource dictionary.
    /// </summary>
    public static class UiKit
    {
        // ---------------------------------------------------------------- text ----

        public static TextBlock Label(UiTheme t, string text, double size, Color colour, bool bold)
        {
            var tb = new TextBlock();
            tb.Text = text;
            tb.FontFamily = new FontFamily(t.FontFamily);
            tb.FontSize = size;
            tb.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;
            tb.Foreground = t.Brush(colour);
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            tb.VerticalAlignment = VerticalAlignment.Center;
            return tb;
        }

        public static TextBlock Hint(UiTheme t, string text)
        {
            var tb = Label(t, text, t.FontCaption, t.TextTertiary, false);
            tb.TextWrapping = TextWrapping.Wrap;
            tb.Margin = new Thickness(0, 2, 0, 0);
            return tb;
        }

        public static Border Divider(UiTheme t)
        {
            var b = new Border();
            b.Height = 1;
            b.Background = t.Brush(t.Border);
            b.Margin = new Thickness(0, t.Space3, 0, t.Space3);
            return b;
        }

        // ---------------------------------------------------------------- cards ---

        /// <summary>A surface with a title row and a body. Round, hairline, no drop shadow (the
        /// app runs on a transparent window where WPF shadows are expensive and get clipped).</summary>
        public static Border Card(UiTheme t, string title, string caption, UIElement body)
        {
            var root = new Border();
            root.CornerRadius = new CornerRadius(t.RadiusCard);
            root.Background = t.Brush(t.Surface);
            root.BorderBrush = t.Brush(t.Border);
            root.BorderThickness = new Thickness(t.StrokeWidth);
            root.Padding = new Thickness(t.Space4);
            root.SnapsToDevicePixels = true;

            var stack = new StackPanel();
            if (title != null)
            {
                stack.Children.Add(Label(t, title, t.FontTitle, t.Text, true));
                if (caption != null)
                {
                    var cap = Label(t, caption, t.FontCaption, t.TextTertiary, false);
                    cap.Margin = new Thickness(0, 3, 0, 0);
                    stack.Children.Add(cap);
                }
                if (body != null) stack.Children.Add(new Border { Height = t.Space3 });
            }
            if (body != null) stack.Children.Add(body);
            root.Child = stack;
            return root;
        }

        /// <summary>Small uppercase-ish label that opens a group of rows.</summary>
        public static TextBlock Group(UiTheme t, string text)
        {
            var tb = Label(t, text, t.FontCaption, t.TextTertiary, false);
            tb.Margin = new Thickness(2, t.Space3, 0, t.Space2);
            return tb;
        }

        // --------------------------------------------------------------- button ---

        public static Border Button(UiTheme t, string text, UiButtonKind kind, Action click)
        {
            Color rest, hot;
            Color fg;
            switch (kind)
            {
                case UiButtonKind.Primary:
                    rest = t.Solid; hot = t.SolidHover; fg = t.OnSolid; break;
                case UiButtonKind.Danger:
                    // Monochrome by design: the capsule stays solid, the label carries the warning.
                    rest = t.Solid; hot = t.SolidHover; fg = t.DangerInk; break;
                case UiButtonKind.Ghost:
                    rest = Color.FromArgb(0, t.Surface.R, t.Surface.G, t.Surface.B);
                    hot = t.Hover; fg = t.Text; break;
                default: // Quiet
                    rest = Color.FromArgb(0, 255, 255, 255);
                    hot = t.Hover; fg = t.TextSecondary; break;
            }

            var bg = new SolidColorBrush(rest);
            var border = new Border();
            border.CornerRadius = new CornerRadius(t.RadiusControl);
            border.Background = bg;
            border.BorderBrush = t.Brush(kind == UiButtonKind.Ghost ? t.Border : Color.FromArgb(0, 0, 0, 0));
            border.BorderThickness = new Thickness(t.StrokeWidth);
            border.Padding = new Thickness(14, 7, 14, 7);
            border.Cursor = Cursors.Hand;
            border.Child = UiMotion.Body(Label(t, text, t.FontBody, fg, true));

            var st = new ScaleTransform(1, 1);
            border.RenderTransformOrigin = new Point(0.5, 0.5);
            border.RenderTransform = st;
            ClipRounded(border, t.RadiusControl);

            bool filled = kind == UiButtonKind.Primary || kind == UiButtonKind.Danger;
            border.MouseEnter += delegate { UiMotion.Color(bg, hot, t.MotionFast); };
            border.MouseLeave += delegate { UiMotion.Color(bg, rest, t.MotionBase); };
            border.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                // Press: small and quick. Release: a slightly slower spring back, so the button
                // reads as rubber rather than as a snap.
                UiMotion.Scale(st, 0.965, 90);
                UiMotion.Color(bg, t.SolidPressed, 90);
                var tint = filled ? Color.FromArgb(26, 0, 0, 0) : Color.FromArgb(30, 255, 255, 255);
                UiMotion.Ripple(border, e, tint, 1.0, t.MotionSlow);
            };
            border.PreviewMouseLeftButtonUp += delegate
            {
                UiMotion.Scale(st, 1.0, 200);
                UiMotion.Color(bg, hot, 140);
            };
            if (click != null)
            {
                border.MouseLeftButtonUp += delegate { click(); };
            }
            return border;
        }

        // --------------------------------------------------------------- switch ---

        /// <summary>HandyControl's toggle: a 42×24 track with a knob that slides, not snaps.</summary>
        public static Border Switch(UiTheme t, bool on, Action<bool> changed)
        {
            return Switch(t, on, true, changed);
        }

        /// <summary>The same switch, but a disabled one goes grey and swallows the click: the
        /// player list needs to show a row without letting anyone bind to it.</summary>
        public static Border Switch(UiTheme t, bool on, bool enabled, Action<bool> changed)
        {
            const double W = 42, H = 24, K = 17, PAD = 3.5;
            Color onTrack = enabled ? t.Solid : Color.FromArgb(255, 52, 52, 58);
            Color offTrack = enabled ? Color.FromArgb(255, 62, 62, 70) : Color.FromArgb(255, 44, 44, 50);
            Color knobOn = enabled ? t.OnSolid : Color.FromArgb(255, 120, 122, 130);
            Color knobOff = enabled ? Color.FromArgb(235, 232, 234, 240) : Color.FromArgb(150, 200, 202, 210);

            var track = new SolidColorBrush(on ? onTrack : offTrack);
            var knobFill = new SolidColorBrush(on ? knobOn : knobOff);

            var host = new Border();
            host.Width = W; host.Height = H;
            host.CornerRadius = new CornerRadius(H / 2);
            host.Background = track;
            host.Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
            host.Focusable = enabled;
            host.FocusVisualStyle = null;

            var knob = new Ellipse();
            knob.Width = knob.Height = K;
            knob.Fill = knobFill;
            knob.HorizontalAlignment = HorizontalAlignment.Left;
            knob.VerticalAlignment = VerticalAlignment.Center;
            knob.Margin = new Thickness(PAD, 0, 0, 0);
            var kt = new TranslateTransform(on ? (W - K - PAD * 2) : 0, 0);

            var ks = new ScaleTransform(1, 1);
            knob.RenderTransformOrigin = new Point(0.5, 0.5);
            var kgroup = new TransformGroup();
            kgroup.Children.Add(kt);
            kgroup.Children.Add(ks);
            knob.RenderTransform = kgroup;

            var grid = new Grid();
            grid.Children.Add(knob);
            host.Child = grid;

            if (!enabled) return host;

            bool state = on;
            bool hover = false;
            Action applyColours = delegate
            {
                Color c = state ? t.Solid : Color.FromArgb(255, 62, 62, 70);
                if (hover && !state) c = Color.FromArgb(255, 76, 76, 86);
                UiMotion.Color(track, c, t.MotionBase);
            };

            host.MouseEnter += delegate { hover = true; applyColours(); };
            host.MouseLeave += delegate { hover = false; applyColours(); };
            host.MouseLeftButtonDown += delegate
            {
                host.Focus();
                UiMotion.Scale(ks, 0.88, 90);
            };
            host.MouseLeftButtonUp += delegate { UiMotion.Scale(ks, 1.0, 200); };
            Action toggle = delegate
            {
                state = !state;
                // Quintic rather than Cubic: the knob arrives instead of drifting in.
                UiMotion.Slide(kt, state ? (W - K - PAD * 2) : 0, 0, 150, UiMotion.Quick);
                UiMotion.Color(knobFill, state ? t.OnSolid : Color.FromArgb(235, 232, 234, 240), t.MotionBase);
                applyColours();
                // The caller usually rebuilds; rebuilding on this frame would turn the slide into a
                // jump, so report once the knob has arrived.
                bool settled = state;
                UiMotion.Defer(t.MotionBase, delegate { if (changed != null) changed(settled); });
            };
            host.MouseLeftButtonUp += delegate { toggle(); };
            host.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Space && e.Key != Key.Enter) return;
                e.Handled = true;
                toggle();
            };
            return host;
        }

        // -------------------------------------------------------------- stepper ---

        public static Grid Stepper(UiTheme t, double value, double min, double max, double step,
                                   string format, Action<double> changed)
        {
            var g = new Grid();
            g.HorizontalAlignment = HorizontalAlignment.Left;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            double v = value;
            var readout = Label(t, v.ToString(format), t.FontBody, t.Text, true);
            readout.MinWidth = 46;
            readout.TextAlignment = TextAlignment.Center;
            readout.Margin = new Thickness(t.Space2, 0, t.Space2, 0);

            Action<double> set = delegate(double nv)
            {
                if (nv < min) nv = min;
                if (nv > max) nv = max;
                v = nv;
                readout.Text = v.ToString(format);
                if (changed != null) changed(v);
            };

            var minus = Square(t, "-", delegate { set(v - step); });
            var plus = Square(t, "+", delegate { set(v + step); });
            Grid.SetColumn(minus, 0); Grid.SetColumn(readout, 1); Grid.SetColumn(plus, 2);
            g.Children.Add(minus); g.Children.Add(readout); g.Children.Add(plus);
            return g;
        }

        static Border Square(UiTheme t, string glyph, Action click)
        {
            var bg = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            var b = new Border();
            b.Width = b.Height = 26;
            b.CornerRadius = new CornerRadius(t.RadiusControl);
            b.Background = bg;
            b.Cursor = Cursors.Hand;
            var tb = Label(t, glyph, 14, t.TextSecondary, false);
            tb.TextAlignment = TextAlignment.Center;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            b.Child = tb;
            ClipRounded(b, t.RadiusControl);
            b.MouseEnter += delegate { UiMotion.Color(bg, t.Hover, t.MotionFast); };
            b.MouseLeave += delegate { UiMotion.Color(bg, Color.FromArgb(0, 255, 255, 255), t.MotionBase); };
            b.MouseLeftButtonUp += delegate { click(); };
            return b;
        }

        // ------------------------------------------------------------ segmented ---

        /// <summary>HandyControl-ish tab strip: the highlight slides to the picked item.</summary>
        /// <summary>Pixel width of a label at the theme's body size, for controls that size to text.</summary>
        static double MeasureWidth(UiTheme t, string text)
        {
            var dpi = 1.0;
            try
            {
                var src = System.Windows.Media.VisualTreeHelper.GetDpi(new System.Windows.Controls.Border());
                dpi = src.PixelsPerDip > 0 ? src.PixelsPerDip : 1.0;
            }
            catch { }
            var ft = new FormattedText(text ?? "",
                System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(t.FontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                t.FontBody, Brushes.Black, dpi);
            return ft.Width;
        }

        public static Grid Segmented(UiTheme t, string[] items, int index, Action<int> changed)
        {
            const double H = 30, MIN_W = 62, PAD = 30;
            var root = new Grid();
            root.HorizontalAlignment = HorizontalAlignment.Left;
            root.Height = H;

            // Width per item, not a fixed column: "Scale the card" is twice as wide as "Off", and a
            // fixed column truncated the English labels.
            var widths = new double[items.Length];
            var offsets = new double[items.Length];
            double total = 0;
            for (int i = 0; i < items.Length; i++)
            {
                widths[i] = Math.Max(MIN_W, MeasureWidth(t, items[i]) + PAD);
                offsets[i] = total;
                total += widths[i];
            }
            root.Width = total;

            var well = new Border();
            well.CornerRadius = new CornerRadius(t.RadiusControl + 2);
            well.Background = t.Brush(t.SurfaceAlt);
            well.BorderBrush = t.Brush(t.Border);
            well.BorderThickness = new Thickness(t.StrokeWidth);
            root.Children.Add(well);

            var pillBg = new SolidColorBrush(t.Solid);
            var pill = new Border();
            pill.Width = widths[index] - 4;
            pill.Height = H - 4;
            pill.CornerRadius = new CornerRadius(t.RadiusControl);
            pill.Background = pillBg;
            pill.HorizontalAlignment = HorizontalAlignment.Left;
            pill.VerticalAlignment = VerticalAlignment.Top;
            pill.Margin = new Thickness(2, 2, 0, 0);
            var pt = new TranslateTransform(offsets[index], 0);
            pill.RenderTransform = pt;
            root.Children.Add(pill);

            var strip = new StackPanel();
            strip.Orientation = Orientation.Horizontal;
            root.Children.Add(strip);

            var labels = new TextBlock[items.Length];
            int current = index;
            for (int i = 0; i < items.Length; i++)
            {
                int idx = i;
                var cell = new Border();
                cell.Width = widths[i];
                cell.Height = H;
                cell.Cursor = Cursors.Hand;
                cell.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                var tb = Label(t, items[i], t.FontBody,
                               i == index ? t.OnSolid : t.TextTertiary, i == index);
                // Own the brush: selecting a segment animates it, and animating the shared frozen
                // token throws.
                tb.Foreground = new SolidColorBrush(i == index ? t.OnSolid : t.TextTertiary);
                tb.TextAlignment = TextAlignment.Center;
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                cell.Child = tb;
                labels[i] = tb;
                cell.MouseLeftButtonUp += delegate
                {
                    if (idx == current) return;
                    int prev = current;
                    current = idx;
                    pill.Width = widths[idx] - 4;
                    UiMotion.Slide(pt, offsets[idx], 0, 200, UiMotion.Quick);
                    UiMotion.Color((SolidColorBrush)labels[prev].Foreground, t.TextTertiary, 150);
                    labels[prev].FontWeight = FontWeights.Normal;
                    labels[idx].FontWeight = FontWeights.SemiBold;
                    var fg = new SolidColorBrush(t.TextTertiary);
                    labels[idx].Foreground = fg;
                    UiMotion.Color(fg, t.OnSolid, 150);
                    // Almost every caller rebuilds the page, which would replace this control in the
                    // middle of the slide. Let the highlight land first, then report.
                    UiMotion.Defer(t.MotionBase, delegate { if (changed != null) changed(idx); });
                };
                strip.Children.Add(cell);
            }
            return root;
        }

        // ---------------------------------------------------------------- chips ---

        public static Border Chip(UiTheme t, string text, Action remove)
        {
            var bg = new SolidColorBrush(t.SurfaceAlt);
            var b = new Border();
            b.CornerRadius = new CornerRadius(t.RadiusTag);
            b.Background = bg;
            b.Padding = new Thickness(10, 4, remove == null ? 10 : 7, 4);
            b.Margin = new Thickness(0, 0, t.Space2, 0);
            b.Cursor = Cursors.Hand;

            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Children.Add(Label(t, text, t.FontCaption, t.TextSecondary, false));
            if (remove != null)
            {
                var x = Label(t, "\u00d7", 12, t.TextTertiary, false);
                x.Foreground = new SolidColorBrush(t.TextTertiary);
                x.Margin = new Thickness(6, 0, 0, 1);
                x.Cursor = Cursors.Hand;
                x.MouseEnter += delegate { UiMotion.Color((SolidColorBrush)x.Foreground, t.Danger, t.MotionFast); };
                x.MouseLeave += delegate { UiMotion.Color((SolidColorBrush)x.Foreground, t.TextTertiary, t.MotionBase); };
                // Swallow the click: without this the chip's own handler also fires and the
                // preset would be deleted *and* applied in the same press.
                x.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    remove();
                };
                row.Children.Add(x);
            }
            b.Child = row;
            // A pill this short does not need a rectangular clip: the Border already rounds its
            // own background, and clipping it here made the capsule render with a ragged right end.
            b.ClipToBounds = true;
            b.MouseEnter += delegate { UiMotion.Color(bg, t.Hover, t.MotionFast); };
            b.MouseLeave += delegate { UiMotion.Color(bg, t.SurfaceAlt, t.MotionBase); };
            return b;
        }

        // --------------------------------------------------------------- input ----

        /// <summary>Search field: pill, magnifier, placeholder, accent border on focus.</summary>
        public static Border Search(UiTheme t, string placeholder, Action<string> changed)
        {
            var bg = new SolidColorBrush(t.SurfaceAlt);
            var edge = new SolidColorBrush(t.Border);
            var b = new Border();
            b.Height = 34;
            b.MinWidth = 220;
            b.HorizontalAlignment = HorizontalAlignment.Left;
            // A pill is half the height, not "a huge number": WPF clamps oversized radii to an
            // ellipse-ish blob, which is exactly what this looked like before.
            b.CornerRadius = new CornerRadius(17);
            b.Background = bg;
            b.BorderBrush = edge;
            b.BorderThickness = new Thickness(t.StrokeWidth);
            b.Padding = new Thickness(10, 0, 10, 0);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lens = new Path();
            lens.Data = Geometry.Parse("M7.5,1 A6.5,6.5 0 1,0 7.6,1 Z M12.2,12.2 L17,17");
            lens.Width = 14; lens.Height = 14;
            lens.Stroke = t.Brush(t.TextTertiary);
            lens.StrokeThickness = 1.5;
            lens.StrokeStartLineCap = PenLineCap.Round;
            lens.StrokeEndLineCap = PenLineCap.Round;
            lens.VerticalAlignment = VerticalAlignment.Center;
            lens.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(lens);

            var box = new TextBox();
            box.Background = Brushes.Transparent;
            box.BorderThickness = new Thickness(0);
            box.Foreground = t.Brush(t.Text);
            box.CaretBrush = t.Brush(t.Solid);
            box.FontFamily = new FontFamily(t.FontFamily);
            box.FontSize = t.FontBody;
            box.VerticalContentAlignment = VerticalAlignment.Center;
            box.Padding = new Thickness(0);
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            var ph = Label(t, placeholder, t.FontBody, t.TextTertiary, false);
            ph.IsHitTestVisible = false;
            Grid.SetColumn(ph, 1);
            row.Children.Add(ph);

            b.Child = row;
            // Clip with the real radius (half the height). A RectangleGeometry given a huge radius
            // is not clamped the way Border.CornerRadius is -- it rendered as a fat ellipse.
            ClipRounded(b, 17);

            box.TextChanged += delegate
            {
                ph.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (changed != null) changed(box.Text);
            };
            box.GotKeyboardFocus += delegate
            {
                UiMotion.Color(edge, t.Solid, t.MotionFast);
                b.BorderThickness = new Thickness(t.StrokeWidth + 0.5);
            };
            box.LostKeyboardFocus += delegate
            {
                UiMotion.Color(edge, t.Border, t.MotionBase);
                b.BorderThickness = new Thickness(t.StrokeWidth);
            };
            b.MouseLeftButtonDown += delegate { box.Focus(); };
            return b;
        }

        // -------------------------------------------------------------- scroll -----

        /// <summary>A scroll viewport with a thin indicator drawn by the kit.</summary>
        /// <remarks>
        /// The bar cannot be a themed ScrollBar: a hand-built template needs a Track, and Track does
        /// not implement IAddChild, so FrameworkElementFactory cannot give it children -- the only
        /// border that could stand in as PART_Thumb then stretched over the whole rail and stopped
        /// showing the position. Squeezing the stock bar's width had no visible effect either (its
        /// template keeps the system width). So the bar is hidden and the kit draws the position
        /// itself, in a grid next to the content.
        /// </remarks>
        public static ScrollViewer Scroll(UiTheme t, UIElement child, Color background, out FrameworkElement hostView)
        {
            var sv = new ScrollViewer();
            sv.Background = t.Brush(background);
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.PanningMode = PanningMode.VerticalOnly;
            sv.CanContentScroll = false;   // pixel offsets, so the ease is not quantised to items
            sv.Content = child;

            var thumb = new Border();
            thumb.Width = 4;
            thumb.CornerRadius = new CornerRadius(2);
            thumb.HorizontalAlignment = HorizontalAlignment.Right;
            thumb.VerticalAlignment = VerticalAlignment.Top;
            thumb.Background = t.Brush(Color.FromArgb(92, 255, 255, 255));
            thumb.Visibility = Visibility.Collapsed;
            thumb.Cursor = Cursors.Hand;
            // A little air from the frame, so the handle reads as an object rather than an edge.
            thumb.Margin = new Thickness(0, 4, 4, 4);

            var host = new Grid();
            host.Children.Add(sv);
            host.Children.Add(thumb);
            WireThumb(t, sv, thumb);
            SmoothWheel(sv);
            // Callers add hostView (the shell places a Border and a ContentControl around it) while
            // keeping the ScrollViewer itself, which is what the page asks for offsets.
            hostView = host;
            return sv;
        }

        /// <summary>Wheel scrolling eases into place instead of hopping a fixed number of lines.</summary>
        /// <remarks>
        /// WPF's own wheel handler moves the offset once per notch, which on a tall page reads as a
        /// snap. Here the offset is animated: every frame covers a share of what is left, so a notch
        /// settles in about a fifth of a second and a spun wheel still tracks the finger.
        /// The indicator listens to ScrollChanged, so the handle rides the same animation.
        /// </remarks>
        static void SmoothWheel(ScrollViewer sv)
        {
            const double Step = 60;        // distance of one notch
            const double Settle = 0.075;   // seconds, time constant of the ease
            double target = 0;
            long lastTick = 0;
            bool easing = false;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            // The frame handler is created once and only added or removed, never nulled: a wheel
            // notch after a finished ease has to find it again.
            EventHandler frame = null;
            Action stop = null;

            frame = delegate
            {
                if (!easing) return;
                long now = clock.ElapsedTicks;
                double dt = lastTick == 0 ? 1.0 / 60.0 : (now - lastTick) / (double)System.Diagnostics.Stopwatch.Frequency;
                lastTick = now;
                if (dt <= 0) dt = 1.0 / 60.0;
                if (dt > 0.05) dt = 0.05;      // never lurch after a stall

                // Frame-rate independent ease: the distance covered per second is the same whether
                // this machine renders at 60 or 144 Hz.
                double k = 1.0 - Math.Exp(-dt / Settle);
                double cur = sv.VerticalOffset;
                double left = target - cur;
                if (Math.Abs(left) < 0.6)
                {
                    stop();
                    return;
                }
                sv.ScrollToVerticalOffset(cur + left * k);
            };

            stop = delegate
            {
                if (!easing) return;
                easing = false;
                CompositionTarget.Rendering -= frame;
                sv.ScrollToVerticalOffset(target);
            };

            sv.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
            {
                if (sv.ScrollableHeight <= 0) return;
                // Take over from wherever the viewport actually is, so a notch arriving mid-ease
                // continues the motion instead of restarting it.
                if (!easing) target = sv.VerticalOffset;
                target -= e.Delta / 120.0 * Step;
                if (target < 0) target = 0;
                if (target > sv.ScrollableHeight) target = sv.ScrollableHeight;
                if (!easing)
                {
                    easing = true;
                    lastTick = 0;
                    CompositionTarget.Rendering += frame;
                }
                e.Handled = true;
            };

            // Anything else that moves the viewport (the handle, a restored page) takes over; a new
            // page resizes the viewport, and an ease still running would drag it off the restored spot.
            sv.ScrollChanged += delegate
            {
                if (!easing) target = sv.VerticalOffset;
            };
            sv.SizeChanged += delegate
            {
                stop();
                target = sv.VerticalOffset;
            };
        }

        static void WireThumb(UiTheme t, ScrollViewer sv, Border thumb)
        {
            Action place = delegate
            {
                double track = sv.ViewportHeight - 8;
                double extent = sv.ExtentHeight;
                if (track <= 0 || extent <= sv.ViewportHeight + 0.5)
                {
                    thumb.Visibility = Visibility.Collapsed;
                    return;
                }
                thumb.Visibility = Visibility.Visible;
                double h = Math.Max(28, track * sv.ViewportHeight / extent);
                if (h > track) h = track;
                double max = Math.Max(0.001, extent - sv.ViewportHeight);
                double y = (track - h) * (sv.VerticalOffset / max);
                thumb.Height = h;
                thumb.Margin = new Thickness(0, 4 + y, 4, 4);
            };

            sv.ScrollChanged += delegate { place(); };
            sv.SizeChanged += delegate { place(); };

            double grab = 0;
            bool dragging = false;
            thumb.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                grab = e.GetPosition(thumb).Y;
                dragging = true;
                thumb.CaptureMouse();
                e.Handled = true;
            };
            thumb.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!dragging) return;
                double track = sv.ViewportHeight - 8;
                double max = Math.Max(0.001, sv.ExtentHeight - sv.ViewportHeight);
                double room = Math.Max(1, track - thumb.Height);
                double y = e.GetPosition(thumb.Parent as IInputElement).Y - grab - 4;
                if (y < 0) y = 0;
                if (y > room) y = room;
                sv.ScrollToVerticalOffset(y / room * max);
            };
            thumb.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                dragging = false;
                thumb.ReleaseMouseCapture();
            };
            thumb.MouseEnter += delegate { UiMotion.Color((SolidColorBrush)thumb.Background, Color.FromArgb(150, 255, 255, 255), t.MotionFast); };
            thumb.MouseLeave += delegate { UiMotion.Color((SolidColorBrush)thumb.Background, Color.FromArgb(92, 255, 255, 255), t.MotionBase); };
            place();
        }

        /// <summary>HandyControl rounds every control edge; WPF needs the clip spelled out for
        /// children that animate (ripples) so they cannot paint past the corner.</summary>
        public static void ClipRounded(Border b, double radius)
        {
            b.SizeChanged += delegate
            {
                if (b.ActualWidth <= 0 || b.ActualHeight <= 0) return;
                b.Clip = new RectangleGeometry(new Rect(0, 0, b.ActualWidth, b.ActualHeight), radius, radius);
            };
        }

        // ------------------------------------------------------------ checkbox ----

        /// <summary>A 18 px rounded box plus a label; the whole row is the hit target.</summary>
        public static Border Checkbox(UiTheme t, string text, bool on, Action<bool> changed)
        {
            bool state = on;
            var fill = new SolidColorBrush(state ? t.Solid : Color.FromArgb(0, 0, 0, 0));
            var edge = new SolidColorBrush(state ? t.Solid : t.Track);

            var mark = new Path();
            mark.Data = Geometry.Parse("M3,7.2 L6,10.4 L11.4,3.6");
            mark.Stroke = t.Brush(t.OnSolid);
            mark.StrokeThickness = 1.8;
            mark.StrokeStartLineCap = PenLineCap.Round;
            mark.StrokeEndLineCap = PenLineCap.Round;
            mark.Width = 14; mark.Height = 14;
            mark.Opacity = state ? 1 : 0;
            mark.IsHitTestVisible = false;

            var box = new Border();
            box.Width = box.Height = 18;
            box.CornerRadius = new CornerRadius(5);
            box.Background = fill;
            box.BorderBrush = edge;
            box.BorderThickness = new Thickness(1.4);
            box.Child = mark;
            box.VerticalAlignment = VerticalAlignment.Center;

            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Children.Add(box);
            var label = Label(t, text, t.FontBody, t.Text, false);
            label.Margin = new Thickness(t.Space2, 0, 0, 0);
            row.Children.Add(label);

            var host = new Border();
            host.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            host.Cursor = Cursors.Hand;
            host.Padding = new Thickness(0, 3, 0, 3);
            host.Child = row;
            host.Focusable = true;
            host.FocusVisualStyle = null;

            Action apply = delegate
            {
                UiMotion.Color(fill, state ? t.Solid : Color.FromArgb(0, 0, 0, 0), 120);
                UiMotion.Color(edge, state ? t.Solid : t.Track, 120);
                UiMotion.Opacity(mark, state ? 1 : 0, 120);
            };
            Action act = delegate
            {
                state = !state;
                apply();
                if (changed != null) changed(state);
            };
            host.MouseLeftButtonUp += delegate { act(); };
            host.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Space && e.Key != Key.Enter) return;
                e.Handled = true;
                act();
            };
            return host;
        }

        // --------------------------------------------------------------- input ----

        /// <summary>One-line text field: sunken well, hairline that turns solid on focus.</summary>
        public static Border Input(UiTheme t, string placeholder, string text, Action<string> changed)
        {
            var edge = new SolidColorBrush(t.Border);
            var b = new Border();
            b.Height = 32;
            b.MinWidth = 220;
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.CornerRadius = new CornerRadius(t.RadiusControl);
            b.Background = new SolidColorBrush(t.SurfaceAlt);
            b.BorderBrush = edge;
            b.BorderThickness = new Thickness(t.StrokeWidth);
            b.Padding = new Thickness(10, 0, 10, 0);

            var box = new TextBox();
            box.Text = text == null ? "" : text;
            box.Background = Brushes.Transparent;
            box.BorderThickness = new Thickness(0);
            box.Foreground = t.Brush(t.Text);
            box.CaretBrush = t.Brush(t.Solid);
            box.FontFamily = new FontFamily(t.FontFamily);
            box.FontSize = t.FontBody;
            box.VerticalContentAlignment = VerticalAlignment.Center;
            box.Padding = new Thickness(0);

            var ph = Label(t, placeholder, t.FontBody, t.TextTertiary, false);
            ph.IsHitTestVisible = false;
            ph.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            var g = new Grid();
            g.Children.Add(box);
            g.Children.Add(ph);
            b.Child = g;
            ClipRounded(b, t.RadiusControl);

            box.TextChanged += delegate
            {
                ph.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (changed != null) changed(box.Text);
            };
            box.GotKeyboardFocus += delegate
            {
                UiMotion.Color(edge, t.Solid, t.MotionFast);
                b.BorderThickness = new Thickness(t.StrokeWidth + 0.5);
            };
            box.LostKeyboardFocus += delegate
            {
                UiMotion.Color(edge, t.Border, t.MotionBase);
                b.BorderThickness = new Thickness(t.StrokeWidth);
            };
            b.MouseLeftButtonDown += delegate { box.Focus(); };

            // Expose the TextBox so a caller can read the value without keeping a second reference.
            b.Tag = box;
            return b;
        }

        /// <summary>The text currently in an <see cref="Input"/>.</summary>
        public static string InputText(Border field)
        {
            var box = field == null ? null : field.Tag as TextBox;
            return box == null ? "" : box.Text;
        }

        // -------------------------------------------------------------- slider ----

        /// <summary>Rail + knob, dragged with the mouse; values are continuous, not stepped.</summary>
        public static Grid Slider(UiTheme t, double min, double max, double value, Action<double> changed)
        {
            const double KNOB = 14;

            var rail = new Border();
            rail.Height = 4;
            rail.CornerRadius = new CornerRadius(2);
            rail.Background = t.Brush(t.Track);
            rail.VerticalAlignment = VerticalAlignment.Center;

            var fill = new Border();
            fill.Height = 4;
            fill.CornerRadius = new CornerRadius(2);
            fill.Background = t.Brush(t.Solid);
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.VerticalAlignment = VerticalAlignment.Center;

            var knob = new Border();
            knob.Width = knob.Height = KNOB;
            knob.CornerRadius = new CornerRadius(KNOB / 2);
            knob.Background = new SolidColorBrush(t.Solid);
            knob.HorizontalAlignment = HorizontalAlignment.Left;
            knob.VerticalAlignment = VerticalAlignment.Center;
            knob.IsHitTestVisible = false;

            var host = new Grid();
            host.Height = 22;
            host.Width = 200;
            host.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            host.Cursor = Cursors.Hand;
            host.Children.Add(rail);
            host.Children.Add(fill);
            host.Children.Add(knob);

            double frac = max > min ? (value - min) / (max - min) : 0;
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;

            Action layout = delegate
            {
                double w = host.ActualWidth;
                if (w <= 0) return;
                fill.Width = Math.Max(0, frac * w);
                knob.Margin = new Thickness(Math.Max(0, Math.Min(w - KNOB, frac * (w - KNOB))), 0, 0, 0);
            };
            host.SizeChanged += delegate { layout(); };
            host.Loaded += delegate { layout(); };
            layout();

            bool dragging = false;
            Action<MouseEventArgs> seek = delegate(MouseEventArgs e)
            {
                double w = host.ActualWidth;
                if (w <= 0) return;
                double x = e.GetPosition(host).X - KNOB / 2;
                double span = Math.Max(1, w - KNOB);
                double f = x / span;
                if (f < 0) f = 0;
                if (f > 1) f = 1;
                if (Math.Abs(f - frac) < 0.0001) return;
                frac = f;
                layout();
                if (changed != null) changed(min + frac * (max - min));
            };
            host.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                dragging = true;
                host.CaptureMouse();
                seek(e);
                UiMotion.Scale(knob.RenderTransform as ScaleTransform, 0.9, 90);
            };
            host.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (dragging) seek(e);
            };
            host.MouseLeftButtonUp += delegate
            {
                if (!dragging) return;
                dragging = false;
                host.ReleaseMouseCapture();
            };
            host.MouseEnter += delegate { UiMotion.Color((SolidColorBrush)fill.Background, t.SolidHover, t.MotionFast); };
            host.MouseLeave += delegate { UiMotion.Color((SolidColorBrush)fill.Background, t.Solid, t.MotionBase); };
            return host;
        }

        // -------------------------------------------------------- progress bar ----

        public static Border ProgressBar(UiTheme t, double fraction)
        {
            var rail = new Border();
            rail.Height = 6;
            rail.CornerRadius = new CornerRadius(3);
            rail.Background = t.Brush(t.Track);
            ClipRounded(rail, 3);

            var fill = new Border();
            fill.Name = "fill";
            fill.Height = 6;
            fill.CornerRadius = new CornerRadius(3);
            fill.Background = t.Brush(t.Solid);
            fill.HorizontalAlignment = HorizontalAlignment.Left;

            var g = new Grid();
            g.Children.Add(fill);
            rail.Child = g;
            SetProgress(rail, fraction);
            rail.SizeChanged += delegate
            {
                double f = rail.Tag is double ? (double)rail.Tag : 0;
                SetProgress(rail, f);
            };
            return rail;
        }

        public static void SetProgress(Border bar, double fraction)
        {
            if (bar == null) return;
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            bar.Tag = fraction;
            var g = bar.Child as Grid;
            if (g == null || g.Children.Count == 0) return;
            var fill = g.Children[0] as Border;
            if (fill == null) return;
            double w = bar.ActualWidth;
            fill.Width = w > 0 ? fraction * w : 0;
        }

        // ------------------------------------------------------- colour swatches ---

        /// <summary>A palette row: round chips, a ring on the current colour, optional reset.</summary>
        public static WrapPanel ColorSwatches(UiTheme t, Color? current, Color[] palette,
                                             Action<Color> pick, Action reset)
        {
            return ColorSwatches(t, current, palette, pick, reset, null);
        }

        /// <summary>Same row, plus an ellipsis chip that opens a bigger palette.</summary>
        public static WrapPanel ColorSwatches(UiTheme t, Color? current, Color[] palette,
                                             Action<Color> pick, Action reset, Action more)
        {
            // 20 px with a 4 px gap: the app's rows carry ten slots (clear + eight colours + the
            // "..." chip) and must stay on one line inside a card, where the padding costs ~28 px.
            const double D = 20;
            var row = new WrapPanel();
            row.Orientation = Orientation.Horizontal;

            if (reset != null)
            {
                var clear = new Border();
                clear.Width = clear.Height = D;
                clear.CornerRadius = new CornerRadius(D / 2);
                clear.BorderBrush = t.Brush(t.Track);
                clear.BorderThickness = new Thickness(1);
                clear.Margin = new Thickness(0, 0, 4, 0);
                clear.Cursor = Cursors.Hand;
                var x = Label(t, "\u00d7", 12, t.TextTertiary, false);
                x.HorizontalAlignment = HorizontalAlignment.Center;
                x.TextAlignment = TextAlignment.Center;
                clear.Child = x;
                clear.MouseLeftButtonUp += delegate { reset(); };
                row.Children.Add(clear);
            }

            if (palette != null)
            {
                for (int i = 0; i < palette.Length; i++)
                {
                    Color c = palette[i];
                    bool on = current.HasValue && current.Value == c;

                    var dot = new Border();
                    dot.Width = dot.Height = D;
                    dot.CornerRadius = new CornerRadius(D / 2);
                    dot.Background = t.Brush(c);
                    dot.Margin = new Thickness(0, 0, 4, 0);
                    dot.Cursor = Cursors.Hand;
                    dot.BorderThickness = new Thickness(on ? 2 : 0);
                    dot.BorderBrush = t.Brush(t.Text);
                    dot.Tag = c;

                    var st = new ScaleTransform(1, 1);
                    dot.RenderTransformOrigin = new Point(0.5, 0.5);
                    dot.RenderTransform = st;
                    dot.MouseEnter += delegate { UiMotion.Scale(st, 1.12, 110); };
                    dot.MouseLeave += delegate { UiMotion.Scale(st, 1.0, 160); };
                    dot.MouseLeftButtonUp += delegate { if (pick != null) pick(c); };
                    row.Children.Add(dot);
                }
            }

            if (more != null)
            {
                var ell = new Border();
                ell.Width = ell.Height = D;
                ell.CornerRadius = new CornerRadius(D / 2);
                ell.Background = t.Brush(t.SurfaceAlt);
                ell.BorderBrush = t.Brush(t.Track);
                ell.BorderThickness = new Thickness(1);
                ell.Margin = new Thickness(0);
                ell.Cursor = Cursors.Hand;
                var dots = Label(t, "\u2026", 12, t.TextSecondary, false);
                dots.HorizontalAlignment = HorizontalAlignment.Center;
                dots.TextAlignment = TextAlignment.Center;
                ell.Child = dots;
                ell.MouseLeftButtonUp += delegate { more(); };
                row.Children.Add(ell);
            }
            // The last swatch never needs a trailing gap; without this the extra 5 px is enough
            // to push the "..." chip onto a second line in the two-column layout.
            if (row.Children.Count > 0)
                ((FrameworkElement)row.Children[row.Children.Count - 1]).Margin = new Thickness(0);
            return row;
        }

        // ------------------------------------------------------------- list row ---

        /// <summary>Title over a caption with a control on the right; hover wash like a list item.</summary>
        public static Border ListRow(UiTheme t, string title, string subtitle, UIElement trailing)
        {
            var bg = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            var b = new Border();
            b.CornerRadius = new CornerRadius(t.RadiusControl);
            b.Background = bg;
            b.Padding = new Thickness(t.Space3, 8, t.Space3, 8);
            b.Margin = new Thickness(0, 0, 0, 2);
            b.MouseEnter += delegate { UiMotion.Color(bg, t.Hover, t.MotionFast); };
            b.MouseLeave += delegate { UiMotion.Color(bg, Color.FromArgb(0, 0, 0, 0), t.MotionBase); };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(Label(t, title, t.FontBody, t.Text, false));
            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = Label(t, subtitle, t.FontCaption, t.TextTertiary, false);
                sub.Margin = new Thickness(0, 1, 0, 0);
                text.Children.Add(sub);
            }
            g.Children.Add(text);

            if (trailing != null)
            {
                var fe = trailing as FrameworkElement;
                if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(trailing, 1);
                g.Children.Add(trailing);
            }
            b.Child = g;
            return b;
        }

        // --------------------------------------------------------------- notice ---

        /// <summary>Inline message: tinted plate, a 3 px tone bar, and the message.</summary>
        public static Border Notice(UiTheme t, UiTone tone, string text)
        {
            Color ink = ToneInk(t, tone);
            var b = new Border();
            b.CornerRadius = new CornerRadius(t.RadiusControl);
            b.Background = t.Brush(Color.FromArgb(26, ink.R, ink.G, ink.B));
            b.Padding = new Thickness(0);
            b.Margin = new Thickness(0, 0, 0, t.Space2);

            var bar = new Border();
            bar.Width = 3;
            bar.Background = t.Brush(ink);
            bar.CornerRadius = new CornerRadius(2, 0, 0, 2);

            var body = Label(t, text, t.FontCaption, ink, false);
            body.TextWrapping = TextWrapping.Wrap;
            body.Margin = new Thickness(t.Space3, 9, t.Space3, 9);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(bar);
            Grid.SetColumn(body, 1);
            g.Children.Add(body);
            b.Child = g;
            return b;
        }

        /// <summary>A small tinted tag, for states and counts.</summary>
        public static Border Badge(UiTheme t, string text, UiTone tone)
        {
            Color ink = ToneInk(t, tone);
            var b = new Border();
            b.CornerRadius = new CornerRadius(t.RadiusTag);
            b.Background = t.Brush(Color.FromArgb(34, ink.R, ink.G, ink.B));
            b.Padding = new Thickness(8, 2, 8, 2);
            b.Margin = new Thickness(0, 0, 6, 0);
            b.VerticalAlignment = VerticalAlignment.Center;
            var tb = Label(t, text, 10.5, ink, true);
            b.Child = tb;
            return b;
        }

        static Color ToneInk(UiTheme t, UiTone tone)
        {
            switch (tone)
            {
                case UiTone.Ok: return t.Success;
                case UiTone.Warn: return t.Warning;
                case UiTone.Danger: return t.Danger;
                default: return t.TextSecondary;
            }
        }

        // -------------------------------------------------------------- spinner ---

        /// <summary>An arc that turns while the control is loaded; stop by removing it.</summary>
        public static Grid Spinner(UiTheme t, double size)
        {
            var arc = new Path();
            arc.Data = Geometry.Parse("M11,2 A9,9 0 1,1 2.6,6.5");
            arc.Stroke = t.Brush(t.TextTertiary);
            arc.StrokeThickness = 2;
            arc.StrokeStartLineCap = PenLineCap.Round;
            arc.StrokeEndLineCap = PenLineCap.Round;
            arc.HorizontalAlignment = HorizontalAlignment.Center;
            arc.VerticalAlignment = VerticalAlignment.Center;

            var g = new Grid();
            g.Width = g.Height = size;
            var scale = size / 20.0;
            var st = new ScaleTransform(scale, scale);
            st.CenterX = 10; st.CenterY = 10;
            arc.RenderTransform = st;
            g.Children.Add(arc);

            var spin = new RotateTransform(0, size / 2, size / 2);
            g.RenderTransform = spin;
            g.Loaded += delegate
            {
                var a = new DoubleAnimation(360, TimeSpan.FromMilliseconds(900));
                a.RepeatBehavior = RepeatBehavior.Forever;
                spin.BeginAnimation(RotateTransform.AngleProperty, a);
            };
            return g;
        }

        static Color Lighten(Color c, int by)
        {
            return Color.FromArgb(c.A,
                (byte)Math.Min(255, c.R + by),
                (byte)Math.Min(255, c.G + by),
                (byte)Math.Min(255, c.B + by));
        }
    }
}
