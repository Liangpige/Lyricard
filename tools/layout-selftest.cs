using System;
using Amc;

static class LT
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
        if (!ok) fails++;
    }
    static bool Near(double a, double b, double tol) { return Math.Abs(a - b) <= tol; }

    static void Main()
    {
        CardConfig cfg = CardConfig.Default();
        CardMetrics m = new CardMetrics();
        LayoutResult r = LayoutEngine.Compute(cfg, m);

        Console.WriteLine("---- default template ----");
        Console.Write(r.Dump());
        Console.WriteLine();

        ModuleRect t, a, l, p, c;
        bool okT = r.TryFind(ModuleKind.Title, out t);
        bool okA = r.TryFind(ModuleKind.Artist, out a);
        bool okL = r.TryFind(ModuleKind.Lyrics, out l);
        bool okP = r.TryFind(ModuleKind.Progress, out p);
        bool okC = r.TryFind(ModuleKind.Cover, out c);

        ModuleRect alb;
        Check("all five visible modules present (album stays disabled)", okT && okA && okL && okP && okC && !r.TryFind(ModuleKind.Album, out alb));

        // old card, logged in amc.log: title=19.7 lyric=19.1 art=96 barW=320 colW=320
        Check("column width 320 (log colW=320)", Near(LayoutEngine.TextColumnWidth(cfg, m), 320, 0.001));
        Check("title line height 19.7 (log title=19.7)", Near(t.H, 19.7, 0.01));
        Check("lyric line height 19.1 (log lyric=19.1)", Near(l.H, 19.07, 0.01));
        Check("progress bar 320x3 (log barW=320)", Near(p.W, 320, 0.001) && Near(p.H, 3, 0.001));
        Check("cover 96x96 flush right (log art=96)", Near(c.W, 96, 0.001) && Near(c.H, 96, 0.001) && Near(c.X, 340, 0.001));
        Check("cover sits at the content top (centred against the taller column)", Near(c.Y, 0, 0.001));

        // the column is centred inside the 96-tall cover, as the shipping card does
        double colH = t.H + m.LyricGap + l.H + m.BarGap + p.H;   // 53.77
        double expectedTop = (96 - colH) / 2.0;
        Check("title starts at (96-columnH)/2 = 21.115", Near(t.Y, expectedTop, 0.01));
        Check("lyrics 4 below the title", Near(l.Y, t.Y + t.H + 4, 0.01));
        Check("bar 8 below the lyrics", Near(p.Y, l.Y + l.H + 8, 0.01));
        Check("artist shares the title line (no extra line)", Near(a.Y, t.Y, 0.001) && Near(a.H, 11.5 * 1.271, 0.01));

        // card size: measured window was 648x213 device px at 125% => 122.4 x 170.4 DIP incl. 24 shadow
        Check("card is 470 wide (measured 470.4)", Near(r.CardW, 470, 0.01));
        Check("card is 122 tall (measured 122.4)", Near(r.CardH, 122, 0.01));
        Check("bounds 436x96, padding+border add 34/26", Near(r.BoundsW, 436, 0.001) && Near(r.BoundsH, 96, 0.001));
        Check("nothing sticks out to the left of the origin", Near(r.BoundsX, 0, 0.001));

        // explicit values win over the stack
        CardConfig pinned = CardConfig.Default();
        pinned.Find(ModuleKind.Lyrics).Y = 10;
        pinned.Find(ModuleKind.Cover).X = 12;
        LayoutResult pr = LayoutEngine.Compute(pinned, m);
        ModuleRect pl, pc;
        pr.TryFind(ModuleKind.Lyrics, out pl);
        pr.TryFind(ModuleKind.Cover, out pc);
        Check("explicit Y overrides the stack", Near(pl.Y, 10, 0.001));
        Check("explicit X overrides the cover default", Near(pc.X, 12, 0.001));

        // no cover: the column takes the full width, the card keeps its nominal width
        CardConfig noCover = CardConfig.Default();
        noCover.Find(ModuleKind.Cover).Enabled = false;
        LayoutResult nr = LayoutEngine.Compute(noCover, m);
        Check("without a cover the column is 436 wide", Near(LayoutEngine.TextColumnWidth(noCover, m), 436, 0.001));
        Check("without a cover the card is still 470x…", Near(nr.CardW, 470, 0.01));

        // explicit H is respected, -1 means auto
        CardConfig hi = CardConfig.Default();
        hi.Find(ModuleKind.Progress).H = 30;
        ModuleRect hp;
        LayoutEngine.Compute(hi, m).TryFind(ModuleKind.Progress, out hp);
        Check("explicit H wins over BarThickness", Near(hp.H, 30, 0.001));

        // empty config: only padding remains, nothing throws
        LayoutResult empty = LayoutEngine.Compute(new CardConfig(), m);
        Check("empty config yields padding-only card", Near(empty.CardW, 34, 0.001) && Near(empty.CardH, 26, 0.001));

        // conversions
        double[] scales = { 0.55, 0.79, 1.0, 2.2 };
        double[] dpis = { 1.0, 1.25, 1.5 };
        bool roundTrip = true;
        for (int i = 0; i < scales.Length; i++)
            for (int j = 0; j < dpis.Length; j++)
            {
                double sx, sy, bx, by;
                LayoutEngine.LogicalToScreen(120.5, 33.25, 907.2, 0, scales[i], dpis[j], out sx, out sy);
                LayoutEngine.ScreenToLogical(sx, sy, 907.2, 0, scales[i], dpis[j], out bx, out by);
                if (!Near(bx, 120.5, 1e-9) || !Near(by, 33.25, 1e-9)) roundTrip = false;
            }
        Check("LogicalToScreen/ScreenToLogical round trip (4 scales x 3 dpi)", roundTrip);

        // one hand-checked value: 2.2x at 907.2 DIP, dpi 1.25, logical (0,0) -> 1134.0
        double kx, ky;
        LayoutEngine.LogicalToScreen(0, 0, 907.2, 0, 2.2, 1.25, out kx, out ky);
        Check("2.2x origin maps to 1134 device px (matches the 22x shot rect x=1134)", Near(kx, 1134.0, 0.001));

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "ALL GOOD" : (fails + " FAILED"));
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
