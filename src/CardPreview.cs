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

namespace Amc
{
    public class CardPreview : Grid
    {
        readonly Canvas _canvas = new Canvas();
        CardElements _el;

        /// <summary>Widest the board will draw, in device-independent pixels.</summary>
        public double FitWidth = 430;

        /// <summary>The card's own Scale, so the board looks like the thing on the desktop.</summary>
        public double Scale = 1.0;

        /// <summary>Last module the user clicked; the acceptance check reads this.</summary>
        public ModuleKind? LastHit;

        /// <summary>Raised when a module is clicked (drag handling comes next).</summary>
        public event Action<ModuleKind, FrameworkElement> ModuleClicked;

        public CardPreview()
        {
            Children.Add(_canvas);
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Top;
        }

        /// <summary>Renders the card's current state into the board.</summary>
        public void Bind(CardWindow card)
        {
            if (card == null) return;
            CardMetrics baseMetrics = card.Metrics();
            CardConfig model = card.CardModel();

            double s = Scale <= 0 ? 1.0 : Scale;
            CardMetrics metrics = LayoutEngine.Scaled(baseMetrics, s);
            LayoutResult layout = LayoutEngine.Compute(model, metrics);
            if (layout.CardW > FitWidth && layout.CardW > 0) s *= FitWidth / layout.CardW;

            // one card, one renderer: same builder the window uses
            var hooks = new CardHooks();
            hooks.Built = delegate(ModuleKind kind, FrameworkElement el)
            {
                el.MouseLeftButtonDown += delegate
                {
                    LastHit = kind;
                    if (ModuleClicked != null) ModuleClicked(kind, el);
                };
            };

            CardScene scene = card.Scene();
            _el = ModuleRenderer.Draw(_canvas, model, metrics, layout, scene, hooks);
            Width = layout.CardW;
            Height = layout.CardH;
            _canvas.Width = layout.CardW;
            _canvas.Height = layout.CardH;
        }
    }
}
