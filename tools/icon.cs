// Lyricard icon generator.
//   draws the mark at several sizes and writes a multi-resolution .ico
//   csc icon.cs /r:System.Drawing.dll  ->  icon.exe  ->  assets\lyricard.ico
// Design: a dark "card" tile, two lyric bars on the left, a vinyl disc on the right.
// Small sizes drop the disc grooves and thin strokes so 16px still reads.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class IconGen
{
    static float Ease(double t) { return (float)t; }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2f;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Draws the mark into a square bitmap of the given size.
    static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float s = size / 256f;
            bool small = size <= 24;

            // Pure black rounded tile.
            float pad = small ? size * 0.02f : size * 0.035f;
            var tile = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
            float rad = size * (small ? 0.21f : 0.22f);
            using (var path = RoundRect(tile, rad))
            using (var bg = new SolidBrush(Color.Black))
                g.FillPath(bg, path);

            // Two white lyric bars on the left; lengths differ so it reads as text.
            float bh = Math.Max(1.8f, size * 0.075f);
            float bx = size * 0.13f;
            float bw1 = size * 0.33f;
            float bw2 = size * 0.21f;
            float mid = size * 0.5f;
            float by1 = mid - bh * 1.55f;
            float by2 = mid + bh * 0.55f;
            using (var b = new SolidBrush(Color.White))
            {
                using (var p = RoundRect(new RectangleF(bx, by1, bw1, bh), bh / 2f))
                    g.FillPath(b, p);
                using (var p = RoundRect(new RectangleF(bx, by2, bw2, bh), bh / 2f))
                    g.FillPath(b, p);
            }

            // Hollow white circle on the right (cover placeholder, no fill).
            // Slightly smaller than the first cut and pulled in from the right edge,
            // otherwise the ring visually touches the tile corner at large sizes.
            float cx = size * (small ? 0.690f : 0.700f);
            float cr = size * (small ? 0.130f : 0.150f);
            float sw = Math.Max(1.5f, size * 0.050f);
            using (var pen = new Pen(Color.White, sw))
                g.DrawEllipse(pen, cx - cr, mid - cr, cr * 2f, cr * 2f);
        }
        return bmp;
    }

    // Upscale a downgrown render so the 32px variant is not soft.
    static Bitmap RenderCrisp(int size)
    {
        int ss = size <= 32 ? 4 : 1;          // supersample small sizes
        using (var big = Render(size * ss))
        {
            if (ss == 1 && size == 256) return (Bitmap)big.Clone();
            var outp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(outp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(big, new Rectangle(0, 0, size, size));
            }
            return outp;
        }
    }

    static byte[] Png(Bitmap b)
    {
        using (var ms = new MemoryStream())
        {
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // 32bpp bottom-up DIB + empty AND mask, as stored inside .ico.
    static byte[] Dib(Bitmap b)
    {
        int w = b.Width, h = b.Height;
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(40);              // biSize
        bw.Write(w);               // biWidth
        bw.Write(h * 2);           // biHeight (XOR + AND)
        bw.Write((short)1);        // biPlanes
        bw.Write((short)32);       // biBitCount
        bw.Write(0);               // BI_RGB
        bw.Write(w * h * 4);       // biSizeImage
        bw.Write(0); bw.Write(0);  // ppm
        bw.Write(0); bw.Write(0);  // colors
        for (int y = h - 1; y >= 0; y--)
            for (int x = 0; x < w; x++)
            {
                Color c = b.GetPixel(x, y);
                bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
            }
        int maskRow = ((w + 31) / 32) * 4;
        bw.Write(new byte[maskRow * h]);
        bw.Flush();
        return ms.ToArray();
    }

    static void WriteIco(string path, List<KeyValuePair<int, byte[]>> images)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
            int offset = 6 + 16 * images.Count;
            foreach (var kv in images)
            {
                int dim = kv.Key >= 256 ? 0 : kv.Key;
                w.Write((byte)dim); w.Write((byte)dim);
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(kv.Value.Length); w.Write(offset);
                offset += kv.Value.Length;
            }
            foreach (var kv in images) w.Write(kv.Value);
        }
    }

    static void Main(string[] args)
    {
        string outIco = args.Length > 0 ? args[0] : "lyricard.ico";
        string sheet = args.Length > 1 ? args[1] : null;

        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<KeyValuePair<int, byte[]>>();
        var rendered = new Dictionary<int, Bitmap>();
        foreach (int sz in sizes)
        {
            Bitmap b = RenderCrisp(sz);
            rendered[sz] = b;
            images.Add(new KeyValuePair<int, byte[]>(sz, sz >= 256 ? Png(b) : Dib(b)));
        }
        WriteIco(outIco, images);

        if (sheet != null && sheet.Length > 0)
        {
            // preview: 256 128 48 32 16 on a light and a dark strip
            int[] show = { 256, 128, 48, 32, 16 };
            int gap = 16, totalW = gap * (show.Length + 1) + 256 + 128 + 48 + 32 + 16;
            int totalH = 256 + 96 + gap * 3;
            using (var canvas = new Bitmap(totalW, totalH))
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.FromArgb(255, 240, 240, 242));
                g.FillRectangle(new SolidBrush(Color.FromArgb(255, 28, 28, 34)), 0, 256 + gap * 2, totalW, 96 + gap);
                int x = gap, y = gap;
                foreach (int sz in show)
                {
                    var b = rendered[sz];
                    int oy = (256 - sz) / 2 + gap;
                    g.DrawImage(b, x, oy, sz, sz);
                    g.DrawImage(b, x, 256 + gap * 2 + (96 - sz) / 2, sz, sz);
                    x += sz + gap;
                }
                g.Dispose();
                canvas.Save(sheet, ImageFormat.Png);
            }
        }
        Console.Write("ok " + outIco + "\n");
    }
}
