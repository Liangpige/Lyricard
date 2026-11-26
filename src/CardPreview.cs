// ---------------------------------------------------------------------------
// CardPreview.cs
//
/// The live preview at the top of the Look page.
///
/// It owns no rendering of its own. What it shows is a snapshot of the desktop card's
/// own visual tree — same corner radius, same spin angle, same glyphs, same paddings,
/// because it *is* the card. A VisualBrush was tried first and dropped: it loses the
/// card's circular clip on the artwork, which showed up as a rounded square.
///
/// Refreshing is event driven: CardWindow raises LayoutChanged when the layout or the
/// displayed content changes, and this control re-snapshots. No timer.
// ---------------------------------------------------------------------------

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Amc
{
    /// <summary>Scaled, live view of the desktop card, used by the settings page.</summary>
    public class CardPreview : Canvas
    {
        /// <summary>Room available to the board; the sample shrinks to fit if it is wider.</summary>
        public double FitWidth = 430;

        /// <summary>Scale the card is currently displayed at.</summary>
        public double Scale = 1.0;

        readonly Image _img = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        readonly Image _art = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true, Visibility = Visibility.Collapsed };

        public CardPreview()
        {
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Top;
            Children.Add(_img);
            Children.Add(_art);
        }

        /// <summary>Re-sample the card. Called from CardWindow.LayoutChanged.</summary>
        public void Bind(CardWindow card)
        {
            if (card == null) return;
            FrameworkElement visual = card.CardVisual;
            double w = card.CardWidth;
            double h = card.CardHeight;
            if (visual == null || w <= 1 || h <= 1) return;

            double s = Scale > 0 ? Scale : 1.0;
            double room = FitWidth > 60 ? FitWidth : 430;
            if (w * s > room) s = room / w;

            // Snapshot at 2x so the shrunken board stays crisp.
            double px = 2.0;
            int bw = Math.Max(1, (int)Math.Ceiling(w * px));
            int bh = Math.Max(1, (int)Math.Ceiling(h * px));

            var rtb = new RenderTargetBitmap(bw, bh, 96.0 * px, 96.0 * px, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            _img.Source = rtb;

            // RenderTargetBitmap loses the artwork's Ellipse geometry when the card's halo
            // effect is on: the circle came back as a square. Draw the art element once more,
            // as a root visual (where the shape *is* honoured) and clip it with an ellipse
            // built from the card's own Cover rect.
            _art.Visibility = Visibility.Collapsed;
            FrameworkElement art = card.CardArt;
            LayoutResult lay = card.CardLayout;
            ModuleRect cover;
            if (art != null && art.IsVisible && lay != null && lay.TryFind(ModuleKind.Cover, out cover) && cover.W > 1)
            {
                int aw = Math.Max(1, (int)Math.Ceiling(cover.W * px));
                int ah = Math.Max(1, (int)Math.Ceiling(cover.H * px));
                var ab = new RenderTargetBitmap(aw, ah, 96.0 * px, 96.0 * px, PixelFormats.Pbgra32);
                ab.Render(art);
                ab.Freeze();

                _art.Source = ab;
                _art.Width = cover.W * s;
                _art.Height = cover.H * s;
                // LayoutEngine rects are relative to the content canvas; the snapshot starts at
                // the card edge, so add the card's own padding back.
                Canvas.SetLeft(_art, (cover.X + CardWindow.CardPadX) * s);
                Canvas.SetTop(_art, (cover.Y + card.CardPadTop) * s);
                _art.Clip = new EllipseGeometry(new Point(cover.W * s / 2.0, cover.H * s / 2.0),
                                                cover.W * s / 2.0, cover.H * s / 2.0);
                _art.Visibility = Visibility.Visible;
            }
            _img.Width = w * s;
            _img.Height = h * s;
            Width = w * s;
            Height = h * s;
        }
    }
}
