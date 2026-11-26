// ---------------------------------------------------------------------------
// LayoutEngine.cs
//
// Turns a CardConfig into concrete rectangles. No WPF types, no window, no
// drawing: feed it the config and the current metrics, get back where every
// module sits and how big the card has to be.
//
// Coordinates are "card logical units": origin at the top-left of the card's
// *content box*, i.e. inside the border and padding. That keeps the numbers
// independent of window chrome, of the scale factor and of the monitor DPI.
//
// Rules, in the order the engine applies them:
//   1. Enabled modules only. W/H = -1 means "ask the metrics": text modules are
//      as wide as the text column and as tall as one line of their font; the
//      progress bar is column-wide and BarThickness tall; the cover is a square
//      of ArtSize.
//   2. The text modules are stacked top-to-bottom in ZIndex order, each with the
//      gap that belongs before it (Lyrics 4, Progress 8, ...). Explicit X/Y win
//      over the stack.
//   3. Artist is an *inline* module: it shares the Title line instead of taking
//      a line of its own, exactly like the shipping card (bold title + dimmer
//      artist on one baseline). If there is no title it behaves like a normal
//      stacked line.
//   4. The cover is right-aligned and vertically centred when it has no X/Y.
//   5. The content box is as tall as the taller of {stacked column, cover}, and
//      both are centred inside it -- which is what the current card does: the
//      column is centred against the cover, not the other way round.
//   6. The card is the union of the enabled boxes plus padding and border, and
//      is translated so the union starts at (0,0).
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Amc
{
    /// <summary>Every number the layout needs. Mirrors today's card so the first cut is a no-op.</summary>
    public class CardMetrics
    {
        public double PadX = 16;
        public double PadY = 12;
        public double Border = 1;

        public double ColGap = 20;        // space between the text column and the cover
        public double ArtSize = 96;
        public double CardW = 470;        // nominal width the column is measured against

        public double TitleFont = 15.5;
        public double ArtistFont = 11.5;
        public double LyricFont = 15;
        public double AlbumFont = 11.5;

        public double LyricGap = 4;       // between the title line and the lyrics
        public double BarGap = 8;         // between the lyrics and the bar
        public double BarThickness = 3;
        public double ControlsGap = 6;    // between the bar and the transport row
        public double ControlsH = 30;     // button hit box height

        /// <summary>
        /// WPF's default line box for a TextBlock: font size times this factor.
        /// 15.5 -> 19.7 and 15 -> 19.1, exactly what the running card logs today.
        /// </summary>
        public double LineFactor = 1.271;
    }

    /// <summary>One module's box, in card logical units.</summary>
    public struct ModuleRect
    {
        public ModuleKind Kind;
        public double X, Y, W, H;

        public ModuleRect(ModuleKind kind, double x, double y, double w, double h)
        {
            Kind = kind; X = x; Y = y; W = w; H = h;
        }

        public double Right { get { return X + W; } }
        public double Bottom { get { return Y + H; } }

        public override string ToString()
        {
            return Kind + " " + Num(X) + "," + Num(Y) + " " + Num(W) + "x" + Num(H);
        }

        static string Num(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    }

    /// <summary>Where everything ended up, plus the card size the union implies.</summary>
    public class LayoutResult
    {
        public List<ModuleRect> Rects = new List<ModuleRect>();
        public double CardW;
        public double CardH;

        /// <summary>Union of the enabled boxes, before normalisation (normally already at 0,0).</summary>
        public double BoundsX, BoundsY, BoundsW, BoundsH;

        public bool TryFind(ModuleKind kind, out ModuleRect rect)
        {
            for (int i = 0; i < Rects.Count; i++)
                if (Rects[i].Kind == kind) { rect = Rects[i]; return true; }
            rect = new ModuleRect(kind, 0, 0, 0, 0);
            return false;
        }

        /// <summary>Human-readable dump; the acceptance for the layout step is this text.</summary>
        public string Dump()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("card ").Append(Num(CardW)).Append("x").Append(Num(CardH)).Append("\r\n");
            sb.Append("bounds ").Append(Num(BoundsX)).Append(",").Append(Num(BoundsY))
              .Append(" ").Append(Num(BoundsW)).Append("x").Append(Num(BoundsH)).Append("\r\n");
            for (int i = 0; i < Rects.Count; i++)
                sb.Append("  ").Append(Rects[i].ToString()).Append("\r\n");
            return sb.ToString();
        }

        static string Num(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    }

    public static class LayoutEngine
    {
        /// <summary>Compute every box for a config. Disabled and absent modules are simply not in the result.</summary>
        /// <summary>Same layout at a different size: every length scales except the line factor.</summary>
        public static CardMetrics Scaled(CardMetrics m, double s)
        {
            var r = new CardMetrics();
            r.PadX = m.PadX * s;
            r.PadY = m.PadY * s;
            r.Border = m.Border * s;
            r.ColGap = m.ColGap * s;
            r.ArtSize = m.ArtSize * s;
            r.CardW = m.CardW * s;
            r.TitleFont = m.TitleFont * s;
            r.ArtistFont = m.ArtistFont * s;
            r.LyricFont = m.LyricFont * s;
            r.LyricGap = m.LyricGap * s;
            r.BarGap = m.BarGap * s;
            r.BarThickness = Math.Max(1.0, m.BarThickness * s);
            r.ControlsGap = m.ControlsGap * s;
            r.ControlsH = m.ControlsH * s;
            r.LineFactor = m.LineFactor;
            return r;
        }

        public static LayoutResult Compute(CardConfig cfg, CardMetrics m)
        {
            LayoutResult result = new LayoutResult();
            if (cfg == null) return result;
            if (m == null) m = new CardMetrics();

            double colW = TextColumnWidth(cfg, m);
            List<ModuleConfig> order = StackingOrder(cfg);

            // ---- pass 1: heights and the height of the stacked column -------------
            double colH = 0;
            bool haveTitle = false;
            for (int i = 0; i < order.Count; i++)
            {
                ModuleConfig mc = order[i];
                if (!mc.Enabled || mc.Kind == ModuleKind.Cover) continue;
                if (mc.Kind == ModuleKind.Artist && haveTitle) continue;      // inline, no line of its own
                if (mc.Kind == ModuleKind.Title) haveTitle = true;
                if (colH > 0 || HasGap(mc.Kind)) colH += GapBefore(mc.Kind, m);
                colH += HeightOf(mc, m);
            }

            ModuleConfig coverCfg = cfg.Find(ModuleKind.Cover);
            bool hasCover = coverCfg != null && coverCfg.Enabled;
            double coverSide = hasCover ? (coverCfg.W > 0 ? coverCfg.W : m.ArtSize) : 0;
            double contentH = Math.Max(colH, coverSide);

            // ---- pass 2: place everything, centring column and cover --------------
            double colTop = (contentH - colH) / 2.0;
            double cursor = colTop;
            double titleTop = colTop;
            double titleH = 0;
            haveTitle = false;
            bool firstLine = true;

            for (int i = 0; i < order.Count; i++)
            {
                ModuleConfig mc = order[i];
                if (!mc.Enabled) continue;

                double w = mc.W > 0 ? mc.W : WidthOf(mc, colW, m);
                double h = HeightOf(mc, m);
                double x, y;

                if (mc.Kind == ModuleKind.Cover)
                {
                    x = double.IsNaN(mc.X) ? colW + m.ColGap : mc.X;
                    y = double.IsNaN(mc.Y) ? (contentH - h) / 2.0 : mc.Y;
                    result.Rects.Add(new ModuleRect(mc.Kind, x, y, w, h));
                    continue;
                }

                bool inline = mc.Kind == ModuleKind.Artist && haveTitle;
                if (inline)
                {
                    x = double.IsNaN(mc.X) ? colW - w : mc.X;
                    y = double.IsNaN(mc.Y) ? titleTop : mc.Y;
                    result.Rects.Add(new ModuleRect(mc.Kind, x, y, w, h));
                    continue;
                }

                if (!firstLine) cursor += GapBefore(mc.Kind, m);
                firstLine = false;

                x = double.IsNaN(mc.X) ? 0 : mc.X;
                y = double.IsNaN(mc.Y) ? cursor : mc.Y;
                result.Rects.Add(new ModuleRect(mc.Kind, x, y, w, h));

                if (mc.Kind == ModuleKind.Title)
                {
                    haveTitle = true;
                    titleTop = y;
                    titleH = h;
                }
                cursor = y + h;
            }

            // ---- union, then normalise to (0,0) ----------------------------------
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < result.Rects.Count; i++)
            {
                ModuleRect mr = result.Rects[i];
                if (mr.X < minX) minX = mr.X;
                if (mr.Y < minY) minY = mr.Y;
                if (mr.Right > maxX) maxX = mr.Right;
                if (mr.Bottom > maxY) maxY = mr.Bottom;
            }
            if (result.Rects.Count == 0) { minX = minY = maxX = maxY = 0; }

            for (int i = 0; i < result.Rects.Count; i++)
            {
                ModuleRect mr = result.Rects[i];
                mr.X -= minX;
                mr.Y -= minY;
                result.Rects[i] = mr;
            }

            result.BoundsX = 0;
            result.BoundsY = 0;
            result.BoundsW = maxX - minX;
            result.BoundsH = maxY - minY;
            result.CardW = result.BoundsW + (m.PadX + m.Border) * 2;
            result.CardH = result.BoundsH + (m.PadY + m.Border) * 2;
            return result;
        }

        /// <summary>Modules in the order they stack: ZIndex first, enum order to break ties.</summary>
        public static List<ModuleConfig> StackingOrder(CardConfig cfg)
        {
            List<ModuleConfig> order = new List<ModuleConfig>(cfg.Modules);
            order.Sort(delegate(ModuleConfig a, ModuleConfig b)
            {
                int c = a.ZIndex.CompareTo(b.ZIndex);
                return c != 0 ? c : ((int)a.Kind).CompareTo((int)b.Kind);
            });
            return order;
        }

        /// <summary>Width available to the text column: nominal card width minus chrome, gap and cover.</summary>
        public static double TextColumnWidth(CardConfig cfg, CardMetrics m)
        {
            ModuleConfig cover = cfg.Find(ModuleKind.Cover);
            bool hasCover = cover != null && cover.Enabled;
            double art = !hasCover ? 0 : (cover.W > 0 ? cover.W : m.ArtSize);
            double gap = hasCover ? m.ColGap : 0;
            return m.CardW - (m.PadX + m.Border) * 2 - gap - art;
        }

        public static double LineH(double font, CardMetrics m)
        {
            return font * m.LineFactor;
        }

        static double WidthOf(ModuleConfig mc, double colW, CardMetrics m)
        {
            if (mc.Kind == ModuleKind.Cover) return m.ArtSize;
            if (mc.Kind == ModuleKind.Progress) return colW;
            if (mc.Kind == ModuleKind.Controls) return colW;
            return colW;
        }

        static double HeightOf(ModuleConfig mc, CardMetrics m)
        {
            if (mc.H > 0) return mc.H;
            switch (mc.Kind)
            {
                case ModuleKind.Cover: return mc.W > 0 ? mc.W : m.ArtSize;
                case ModuleKind.Progress: return m.BarThickness;
                case ModuleKind.Controls: return m.ControlsH;
                case ModuleKind.Title: return LineH(mc.FontSize > 0 ? mc.FontSize : m.TitleFont, m);
                case ModuleKind.Artist: return LineH(mc.FontSize > 0 ? mc.FontSize : m.ArtistFont, m);
                case ModuleKind.Album: return LineH(mc.FontSize > 0 ? mc.FontSize : m.AlbumFont, m);
                default: return LineH(mc.FontSize > 0 ? mc.FontSize : m.LyricFont, m);
            }
        }

        static bool HasGap(ModuleKind kind)
        {
            return kind == ModuleKind.Lyrics || kind == ModuleKind.Progress
                || kind == ModuleKind.Album || kind == ModuleKind.Controls;
        }

        static double GapBefore(ModuleKind kind, CardMetrics m)
        {
            switch (kind)
            {
                case ModuleKind.Lyrics: return m.LyricGap;
                case ModuleKind.Progress: return m.BarGap;
                case ModuleKind.Album: return m.LyricGap;
                case ModuleKind.Controls: return m.ControlsGap;
                default: return 0;
            }
        }

        // ---- coordinate conversion ------------------------------------------

        /// <summary>
        /// Logical (card content box) -> screen device pixels.
        /// originX / originY is that content box's top-left in screen DIP, scale is the
        /// card's own Scale, dpi is the monitor's factor (1.25 on this machine).
        /// </summary>
        public static void LogicalToScreen(double lx, double ly, double originX, double originY,
                                           double scale, double dpi, out double sx, out double sy)
        {
            sx = (originX + lx * scale) * dpi;
            sy = (originY + ly * scale) * dpi;
        }

        /// <summary>Inverse of <see cref="LogicalToScreen"/>; round trips exactly.</summary>
        public static void ScreenToLogical(double sx, double sy, double originX, double originY,
                                           double scale, double dpi, out double lx, out double ly)
        {
            if (scale <= 0) scale = 1;
            if (dpi <= 0) dpi = 1;
            lx = (sx / dpi - originX) / scale;
            ly = (sy / dpi - originY) / scale;
        }
    }
}
