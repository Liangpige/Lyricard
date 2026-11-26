// ---------------------------------------------------------------------------
// ModuleRenderer.cs
//
/// Draws the card by walking the module list: LayoutEngine has already decided where every
/// box goes, this file only pushes those rectangles onto the existing visual elements.
///
/// Keeping the elements (text blocks, bar, buttons, cover) instead of rebuilding them on each
/// pass means hover / press animations and the play glyph keep their state; all that moves is
/// position and size. Order is decided by ZIndex, so a later template can put the cover behind
/// the text without touching the painter.
// ---------------------------------------------------------------------------

using System.Windows;
using System.Windows.Controls;

namespace Amc
{
    /// <summary>The visual for each module kind. A null entry is simply not drawn.</summary>
    public class CardElements
    {
        public FrameworkElement Title;
        public FrameworkElement Lyrics;
        public FrameworkElement Progress;
        public FrameworkElement Controls;
        public FrameworkElement Cover;
    }

    /// <summary>Places elements on a canvas according to a computed layout.</summary>
    public static class ModuleRenderer
    {
        public static void Apply(Canvas canvas, CardConfig model, LayoutResult layout, CardElements el)
        {
            if (canvas == null || layout == null) return;
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
    }
}
