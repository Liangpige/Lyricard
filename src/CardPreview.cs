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
        DateTime _loggedAt = DateTime.MinValue;

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

    /// <summary>Renders the card's current state into the board, reusing the controls it built last time.</summary>
        public void Bind(CardWindow card)
        {
            if (card == null) return;
            CardMetrics baseMetrics = card.Metrics();
            CardConfig model = card.CardModel();

            // Everything geometric comes from LayoutEngine at the effective scale: the board is the
            // card shrunk, not a second design. Shrink again when the card is wider than the board.
            double s = Scale <= 0 ? 1.0 : Scale;
            LayoutResult layout = LayoutEngine.Compute(model, LayoutEngine.Scaled(baseMetrics, s));
            if (layout.CardW > FitWidth && layout.CardW > 0)
            {
                s *= FitWidth / layout.CardW;
                layout = LayoutEngine.Compute(model, LayoutEngine.Scaled(baseMetrics, s));
            }

            // Fonts ride the same factor; the card's own scene carries the desktop sizes.
            CardScene scene = card.Scene();
            scene.TitleSize *= s;
            scene.ArtistSize *= s;
            scene.LyricSize *= s;
            scene.BarThickness *= s;

            if (_el == null)
            {
                var hooks = new CardHooks();
                hooks.Built = delegate(ModuleKind kind, FrameworkElement el)
                {
                    el.MouseLeftButtonDown += delegate
                    {
                        LastHit = kind;
                        if (ModuleClicked != null) ModuleClicked(kind, el);
                    };
                };
                _el = ModuleRenderer.Draw(_canvas, model, LayoutEngine.Scaled(baseMetrics, s), layout, scene, hooks);
            }

            ModuleRenderer.Update(_canvas, _el, model, layout, scene, card.Progress01, scene.Playing);
            // one comparison line a second is enough to prove the board is the card shrunk by s
            if ((DateTime.Now - _loggedAt).TotalMilliseconds > 1000)
            {
                _loggedAt = DateTime.Now;
                LayoutResult cardLayout = card.CardLayout;
                string cardDump = cardLayout == null ? "<none>" : cardLayout.Dump().Replace("\r", "").Replace("\n", " | ");
                LyricsService.Log("layout s=" + s.ToString("0.000") + " card[" + cardDump + "] board[" + layout.Dump().Replace("\r", "").Replace("\n", " | ") + "]");
            }

            Width = layout.CardW;
            Height = layout.CardH;
            _canvas.Width = layout.CardW;
            _canvas.Height = layout.CardH;
        }
    }
}
