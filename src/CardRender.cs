// ---------------------------------------------------------------------------
// CardRender.cs
//
/// The single rendering entry point for "show the desktop card somewhere else".
///
/// The Look-page board is not allowed to draw the card a second time: a second
/// implementation always drifts (radius, spin angle, glyph metrics, padding...).
/// So RenderCard paints the card's own live visual tree into a target canvas at a
/// requested scale. Whatever the card shows — circular cover with its current spin
/// angle, lyric line, progress, glyphs — is what the board shows, by construction.
// ---------------------------------------------------------------------------

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Amc
{
    /// <summary>Scale-and-sample plumbing for the preview board.</summary>
    public static class CardRender
    {
        /// <summary>
        /// Paint <paramref name="cardVisual"/> into <paramref name="target"/> at
        /// <paramref name="scale"/>. The target ends up sized to the scaled card, so callers
        /// can treat it as the card's footprint.
        /// </summary>
        public static void RenderCard(Canvas target, Visual cardVisual, double cardW, double cardH, double scale)
        {
            if (target == null) return;
            target.Children.Clear();
            if (cardVisual == null || cardW <= 1 || cardH <= 1) return;

            double s = scale > 0 ? scale : 1.0;

            var brush = new VisualBrush(cardVisual)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, cardW, cardH)
            };

            var shot = new Rectangle
            {
                Width = cardW,
                Height = cardH,
                Fill = brush,
                SnapsToDevicePixels = true
            };
            shot.LayoutTransform = new ScaleTransform(s, s);

            target.Width = cardW * s;
            target.Height = cardH * s;
            target.Children.Add(shot);
        }
    }
}
