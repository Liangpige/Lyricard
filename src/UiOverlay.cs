using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Amc
{
    /// <summary>
    /// A modal layer inside one window: a scrim over the page plus a centred card. Living in the
    /// same visual tree (instead of a second Window) keeps focus, keyboard and the theme honest, and
    /// the scrim can be animated with everything else.
    /// </summary>
    public sealed class UiDialogHost
    {
        readonly Panel _root;
        readonly UiTheme _t;
        Grid _scrim;
        bool _open;

        public UiDialogHost(Panel root, UiTheme t)
        {
            if (root == null) throw new ArgumentNullException("root");
            _root = root;
            _t = t;
        }

        public bool IsOpen { get { return _open; } }

        /// <summary>Puts the card on a scrim and returns it, so callers can keep a reference.</summary>
        public Border Show(Border card)
        {
            if (card == null) throw new ArgumentNullException("card");
            Hide();

            _scrim = new Grid();
            _scrim.Background = _t.Brush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
            _scrim.Opacity = 0.0;

            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.MaxWidth = 420;
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(0.97, 0.97);
            var slide = new TranslateTransform(0, 6);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(slide);
            card.RenderTransform = group;

            _scrim.Children.Add(card);
            // Clicking the dim area dismisses, the same way a desktop dialog does. The card stops the
            // event so a click inside never reaches the scrim.
            _scrim.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (ReferenceEquals(e.OriginalSource, _scrim)) Hide();
            };
            card.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };

            _root.PreviewKeyDown += OnKey;
            _root.Children.Add(_scrim);
            _open = true;

            UiMotion.Opacity(_scrim, 1.0, _t.MotionBase);
            UiMotion.Slide(slide, 0, 0, _t.MotionBase);
            var to = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(_t.MotionBase));
            to.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, to);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, to);

            card.Focusable = true;
            card.Focus();
            return card;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Hide();
        }

        public void Hide()
        {
            if (!_open) return;
            _open = false;
            _root.PreviewKeyDown -= OnKey;
            if (_scrim != null && _root.Children.Contains(_scrim))
            {
                var gone = _scrim;
                _scrim = null;
                UiMotion.Opacity(gone, 0.0, _t.MotionFast);
                var timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(_t.MotionFast + 20);
                timer.Tick += delegate
                {
                    timer.Stop();
                    _root.Children.Remove(gone);
                };
                timer.Start();
            }
        }
    }

    /// <summary>
    /// The transient layer: a flyout that hangs off an element, and a hover tip. Both are Popups so
    /// they can overhang the window; both animate in, because a panel that appears instantly reads
    /// as a glitch.
    /// </summary>
    public static class UiOverlays
    {
        public static Popup Flyout(UIElement anchor, Border panel, PlacementMode mode)
        {
            if (anchor == null) throw new ArgumentNullException("anchor");
            if (panel == null) throw new ArgumentNullException("panel");

            panel.Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = 0.30,
                Color = Colors.Black
            };
            panel.RenderTransformOrigin = new Point(0.5, 0.0);
            var slide = new TranslateTransform(0, -6);
            panel.RenderTransform = slide;

            var pop = new Popup();
            pop.PlacementTarget = anchor;
            pop.Placement = mode;
            // StaysOpen=false grabs the mouse and treats the opening click's own release as an
            // outside click, so the panel flashed open and shut again. Stay open and dismiss by hand.
            pop.StaysOpen = true;
            pop.AllowsTransparency = true;
            pop.PopupAnimation = PopupAnimation.None;
            pop.Child = panel;
            pop.HorizontalOffset = 0;
            pop.VerticalOffset = 6;
            // Dismiss on a click anywhere else. A click on the anchor is left alone: the caller owns
            // that toggle, otherwise it would close here and immediately reopen.
            MouseButtonEventHandler away = delegate(object s, MouseButtonEventArgs e)
            {
                if (panel.IsMouseOver || anchor.IsMouseOver) return;
                pop.IsOpen = false;
            };
            KeyEventHandler esc = delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) pop.IsOpen = false;
            };

            // The window is resolved when the panel first opens: a flyout is often built before its
            // anchor is in a window, and GetWindow returns null then.
            Window attached = null;
            pop.Opened += delegate
            {
                panel.Opacity = 0.0;
                UiMotion.Opacity(panel, 1.0, 150);
                UiMotion.Slide(slide, 0, 0, 150);
                if (attached != null) return;
                attached = Window.GetWindow(anchor);
                if (attached == null) return;
                attached.PreviewMouseLeftButtonDown += away;
                attached.PreviewKeyDown += esc;
            };
            pop.Closed += delegate
            {
                if (attached == null) return;
                attached.PreviewMouseLeftButtonDown -= away;
                attached.PreviewKeyDown -= esc;
                attached = null;
            };
            return pop;
        }

        /// <summary>Hover tip: appears after a short dwell, never on a mere pass-through.</summary>
        public static void Tip(FrameworkElement target, string text)
        {
            if (target == null || string.IsNullOrEmpty(text)) return;

            var card = new Border();
            card.Background = System.Windows.Media.Brushes.Black;
            card.Padding = new Thickness(9, 5, 9, 6);
            card.CornerRadius = new CornerRadius(6);
            card.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.28, Color = Colors.Black };
            var label = new TextBlock();
            label.Text = text;
            label.FontSize = 11.5;
            label.Foreground = System.Windows.Media.Brushes.White;
            label.TextWrapping = TextWrapping.NoWrap;
            card.Child = label;

            var pop = new Popup();
            pop.PlacementTarget = target;
            pop.Placement = PlacementMode.Mouse;
            pop.AllowsTransparency = true;
            pop.StaysOpen = true;
            pop.IsHitTestVisible = false;
            pop.PopupAnimation = PopupAnimation.None;
            pop.Child = card;
            pop.HorizontalOffset = 12;
            pop.VerticalOffset = 16;

            var dwell = new DispatcherTimer();
            dwell.Interval = TimeSpan.FromMilliseconds(420);
            dwell.Tick += delegate
            {
                dwell.Stop();
                card.Opacity = 0.0;
                pop.IsOpen = true;
                UiMotion.Opacity(card, 1.0, 120);
            };
            target.MouseEnter += delegate { dwell.Start(); };
            target.MouseLeave += delegate
            {
                dwell.Stop();
                pop.IsOpen = false;
            };
            target.MouseLeftButtonDown += delegate
            {
                dwell.Stop();
                pop.IsOpen = false;
            };
        }

        /// <summary>A two-button card for UiDialogHost.Show: returns true when the primary wins.</summary>
        public static Border ConfirmCard(UiTheme t, string title, string body, string ok, string cancel,
                                         Action<bool> decided)
        {
            var stack = new StackPanel();

            var head = UiKit.Label(t, title, 15.0, t.Text, true);
            stack.Children.Add(head);

            if (!string.IsNullOrEmpty(body))
            {
                var text = UiKit.Label(t, body, t.FontBody, t.TextSecondary, false);
                text.TextWrapping = TextWrapping.Wrap;
                text.Margin = new Thickness(0, t.Space2, 0, 0);
                text.MaxWidth = 360;
                stack.Children.Add(text);
            }

            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.HorizontalAlignment = HorizontalAlignment.Right;
            row.Margin = new Thickness(0, t.Space4, 0, 0);

            Border primary = null;
            if (!string.IsNullOrEmpty(cancel))
            {
                var no = UiKit.Button(t, cancel, UiButtonKind.Ghost, delegate
                {
                    if (decided != null) decided(false);
                });
                no.Margin = new Thickness(0, 0, t.Space2, 0);
                row.Children.Add(no);
            }
            primary = UiKit.Button(t, ok, UiButtonKind.Primary, delegate
            {
                if (decided != null) decided(true);
            });
            row.Children.Add(primary);
            stack.Children.Add(row);

            var card = new Border();
            card.Background = t.Brush(t.Surface);
            card.BorderBrush = t.Brush(t.BorderStrong);
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(t.RadiusCard + 2);
            card.Padding = new Thickness(t.Space4, t.Space3 + 2, t.Space4, t.Space4);
            card.Child = stack;
            card.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.35, Color = Colors.Black };
            return card;
        }
    }
}
