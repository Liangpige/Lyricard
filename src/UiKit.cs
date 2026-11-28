using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

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
            if (tt == null) return;
            var ax = new DoubleAnimation(x, TimeSpan.FromMilliseconds(ms));
            ax.EasingFunction = Out;
            var ay = new DoubleAnimation(y, TimeSpan.FromMilliseconds(ms));
            ay.EasingFunction = Out;
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

            var grow = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(ms));
            grow.EasingFunction = Out;
            var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(ms));
            fade.BeginTime = TimeSpan.FromMilliseconds(ms * 0.25);
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
            el.Opacity = 0;
            var tt = el.RenderTransform as TranslateTransform;
            if (tt == null) { tt = new TranslateTransform(0, lift); el.RenderTransform = tt; }
            else tt.Y = lift;
            var fade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(ms));
            fade.EasingFunction = Out;
            el.BeginAnimation(UIElement.OpacityProperty, fade);
            Slide(tt, 0, 0, ms);
        }
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
                    rest = t.Accent; hot = t.AccentHover; fg = t.OnAccent; break;
                case UiButtonKind.Danger:
                    rest = t.Danger; hot = Lighten(t.Danger, 18); fg = t.OnAccent; break;
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

            border.MouseEnter += delegate { UiMotion.Color(bg, hot, t.MotionFast); };
            border.MouseLeave += delegate { UiMotion.Color(bg, rest, t.MotionBase); };
            border.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                UiMotion.Scale(st, 0.96, 80);
                var tint = kind == UiButtonKind.Primary || kind == UiButtonKind.Danger
                    ? Color.FromArgb(70, 255, 255, 255)
                    : Color.FromArgb(46, t.Accent.R, t.Accent.G, t.Accent.B);
                UiMotion.Ripple(border, e, tint, 1.0, t.MotionSlow);
            };
            border.PreviewMouseLeftButtonUp += delegate
            {
                UiMotion.Scale(st, 1.0, t.MotionBase);
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
            const double W = 42, H = 24, K = 17, PAD = 3.5;
            var track = new SolidColorBrush(on ? t.Accent : Color.FromArgb(255, 62, 62, 70));
            var knobFill = new SolidColorBrush(on ? t.OnAccent : Color.FromArgb(235, 232, 234, 240));

            var host = new Border();
            host.Width = W; host.Height = H;
            host.CornerRadius = new CornerRadius(H / 2);
            host.Background = track;
            host.Cursor = Cursors.Hand;
            host.Focusable = true;
            host.FocusVisualStyle = null;

            var knob = new Ellipse();
            knob.Width = knob.Height = K;
            knob.Fill = knobFill;
            knob.HorizontalAlignment = HorizontalAlignment.Left;
            knob.VerticalAlignment = VerticalAlignment.Center;
            knob.Margin = new Thickness(PAD, 0, 0, 0);
            var kt = new TranslateTransform(on ? (W - K - PAD * 2) : 0, 0);
            knob.RenderTransform = kt;

            var grid = new Grid();
            grid.Children.Add(knob);
            host.Child = grid;

            bool state = on;
            bool hover = false;
            Action toggle = null;
            Action applyColours = delegate
            {
                Color c = state ? t.Accent : Color.FromArgb(255, 62, 62, 70);
                if (hover && !state) c = Color.FromArgb(255, 76, 76, 86);
                UiMotion.Color(track, c, t.MotionBase);
            };

            host.MouseEnter += delegate { hover = true; applyColours(); };
            host.MouseLeave += delegate { hover = false; applyColours(); };
            host.MouseLeftButtonDown += delegate { host.Focus(); };
            toggle = delegate
            {
                state = !state;
                UiMotion.Slide(kt, state ? (W - K - PAD * 2) : 0, 0, t.MotionBase);
                UiMotion.Color(knobFill, state ? t.OnAccent : Color.FromArgb(235, 232, 234, 240), t.MotionBase);
                applyColours();
                if (changed != null) changed(state);
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
        public static Grid Segmented(UiTheme t, string[] items, int index, Action<int> changed)
        {
            const double ITEM_W = 78, H = 30;
            var root = new Grid();
            root.HorizontalAlignment = HorizontalAlignment.Left;
            root.Width = ITEM_W * items.Length;
            root.Height = H;

            var well = new Border();
            well.CornerRadius = new CornerRadius(t.RadiusControl + 2);
            well.Background = t.Brush(t.SurfaceAlt);
            well.BorderBrush = t.Brush(t.Border);
            well.BorderThickness = new Thickness(t.StrokeWidth);
            root.Children.Add(well);

            var pillBg = new SolidColorBrush(t.Surface);
            var pill = new Border();
            pill.Width = ITEM_W - 4;
            pill.Height = H - 4;
            pill.CornerRadius = new CornerRadius(t.RadiusControl);
            pill.Background = pillBg;
            pill.HorizontalAlignment = HorizontalAlignment.Left;
            pill.VerticalAlignment = VerticalAlignment.Top;
            pill.Margin = new Thickness(2, 2, 0, 0);
            var pt = new TranslateTransform(index * ITEM_W, 0);
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
                cell.Width = ITEM_W;
                cell.Height = H;
                cell.Cursor = Cursors.Hand;
                cell.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                var tb = Label(t, items[i], t.FontBody,
                               i == index ? t.Text : t.TextTertiary, i == index);
                // Own the brush: selecting a segment animates it, and animating the shared frozen
                // token throws.
                tb.Foreground = new SolidColorBrush(i == index ? t.Text : t.TextTertiary);
                tb.TextAlignment = TextAlignment.Center;
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                cell.Child = tb;
                labels[i] = tb;
                cell.MouseLeftButtonUp += delegate
                {
                    if (idx == current) return;
                    int prev = current;
                    current = idx;
                    UiMotion.Slide(pt, idx * ITEM_W, 0, t.MotionBase);
                    UiMotion.Color((SolidColorBrush)labels[prev].Foreground, t.TextTertiary, t.MotionBase);
                    labels[prev].FontWeight = FontWeights.Normal;
                    UiMotion.Opacity(pill, 1.0, t.MotionBase);
                    labels[idx].FontWeight = FontWeights.SemiBold;
                    var fg = new SolidColorBrush(t.TextTertiary);
                    labels[idx].Foreground = fg;
                    UiMotion.Color(fg, t.Text, t.MotionBase);
                    if (changed != null) changed(idx);
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
            b.CornerRadius = new CornerRadius(t.RadiusPill);
            b.Background = bg;
            b.Padding = new Thickness(11, 5, remove == null ? 11 : 8, 5);
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
                x.MouseLeftButtonUp += delegate { remove(); };
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
            box.CaretBrush = t.Brush(t.Accent);
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
                UiMotion.Color(edge, t.Accent, t.MotionFast);
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

        /// <summary>Thin scrollbar, built as a Style in code (the kit does not read XAML).</summary>
        public static ScrollViewer Scroll(UiTheme t, UIElement child)
        {
            var sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.PanningMode = PanningMode.VerticalOnly;
            sv.Content = child;
            sv.Template = ScrollTemplate(t);
            return sv;
        }

        static ControlTemplate _scrollTemplate;
        static UiTheme _scrollTheme;

        static ControlTemplate ScrollTemplate(UiTheme t)
        {
            if (_scrollTemplate != null && ReferenceEquals(_scrollTheme, t)) return _scrollTemplate;

            var root = new FrameworkElementFactory(typeof(Grid));

            var contentHost = new FrameworkElementFactory(typeof(ScrollContentPresenter));
            contentHost.SetValue(ScrollContentPresenter.CanContentScrollProperty, false);
            contentHost.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            root.AppendChild(contentHost);

            var bar = new FrameworkElementFactory(typeof(ScrollBar));
            bar.SetValue(ScrollBar.OrientationProperty, Orientation.Vertical);
            bar.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            bar.SetValue(FrameworkElement.WidthProperty, 6.0);
            bar.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));
            bar.SetValue(Control.TemplateProperty, ScrollBarTemplate(t));
            root.AppendChild(bar);

            var tpl = new ControlTemplate(typeof(ScrollViewer));
            tpl.VisualTree = root;
            _scrollTemplate = tpl;
            _scrollTheme = t;
            return tpl;
        }

        static ControlTemplate _barTemplate;
        static UiTheme _barTheme;

        static ControlTemplate ScrollBarTemplate(UiTheme t)
        {
            if (_barTemplate != null && ReferenceEquals(_barTheme, t)) return _barTemplate;

            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.WidthProperty, 6.0);

            var track = new FrameworkElementFactory(typeof(Border));
            track.SetValue(Border.WidthProperty, 6.0);
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            track.SetValue(Border.BackgroundProperty, t.Brush(Color.FromArgb(10, 255, 255, 255)));
            grid.AppendChild(track);

            var thumb = new FrameworkElementFactory(typeof(Border));
            thumb.SetValue(Border.WidthProperty, 6.0);
            thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            thumb.SetValue(Border.BackgroundProperty, t.Brush(Color.FromArgb(64, 255, 255, 255)));
            thumb.SetValue(FrameworkElement.NameProperty, "PART_Thumb");
            grid.AppendChild(thumb);

            var tpl = new ControlTemplate(typeof(ScrollBar));
            tpl.VisualTree = grid;
            var trigger = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Vertical };
            tpl.Triggers.Add(trigger);
            _barTemplate = tpl;
            _barTheme = t;
            return tpl;
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

        static Color Lighten(Color c, int by)
        {
            return Color.FromArgb(c.A,
                (byte)Math.Min(255, c.R + by),
                (byte)Math.Min(255, c.G + by),
                (byte)Math.Min(255, c.B + by));
        }
    }
}
