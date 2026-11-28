using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Amc
{
    /// <summary>
    /// Design tokens for the code-built UI, in the spirit of HandyControl: a small set of
    /// semantic colours, one radius scale, one spacing scale, and the motion timings the
    /// control factory animates with. Nothing here touches XAML — callers ask for Brushes.
    ///
    /// Two palettes exist (dark is the one the app ships with). Codes that need a colour must
    /// ask the theme, never hard-code one, so switching palettes is a one-line change.
    /// </summary>
    public sealed class UiTheme
    {
        // ---- surfaces -------------------------------------------------------------
        public Color Window;          // the page behind the cards
        public Color Surface;         // card / control background
        public Color SurfaceAlt;      // inputs, sunken wells
        public Color Hover;           // hover wash on quiet controls (already alpha-blended)
        public Color Pressed;
        public Color Border;          // hairline between surfaces
        public Color BorderStrong;    // focused input, active segment edge

        // ---- text ----------------------------------------------------------------
        public Color Text;            // headlines, values
        public Color TextSecondary;   // labels
        public Color TextTertiary;    // hints, disabled

        // ---- accent --------------------------------------------------------------
        public Color Accent;
        public Color AccentHover;
        public Color AccentSoft;      // 12% accent, for selected backgrounds
        public Color OnAccent;        // text/glyph drawn on Accent
        public Color Danger;
        public Color Success;

        // ---- the solid pill ------------------------------------------------------
        // The app's own controls are monochrome: a white capsule with ink on it. Primary and
        // Danger both use this background; danger is carried by the label colour, not by a
        // red block.
        public Color Solid;
        public Color SolidHover;
        public Color SolidPressed;
        public Color OnSolid;
        public Color DangerInk;

        // ---- grooves -------------------------------------------------------------
        public Color Track;           // slider groove, progress rail, checkbox outline
        public Color TrackOn;         // filled part of a rail
        public Color Warning;

        // ---- geometry ------------------------------------------------------------
        public double RadiusCard = 10;
        public double RadiusControl = 7;
        public double RadiusPill = 999;
        /// <summary>Tags and chips: a soft rectangle, not a stadium — the stadium read as an ellipse
        /// at this size.</summary>
        public double RadiusTag = 6;
        public double StrokeWidth = 1;

        // ---- spacing scale -------------------------------------------------------
        public double Space1 = 4;
        public double Space2 = 8;
        public double Space3 = 12;
        public double Space4 = 16;
        public double Space5 = 24;

        // ---- type scale ----------------------------------------------------------
        public double FontTitle = 14.5;
        public double FontBody = 12.5;
        public double FontCaption = 11.5;
        public string FontFamily = "Microsoft YaHei UI, Segoe UI";

        // ---- motion --------------------------------------------------------------
        public int MotionFast = 110;    // hover / press feedback
        public int MotionBase = 180;    // switch, segment slide
        public int MotionSlow = 260;    // page entrance, ripple

        // ---- brushes -------------------------------------------------------------
        readonly Dictionary<uint, Brush> _cache = new Dictionary<uint, Brush>();

        /// <summary>Frozen brush for a colour; alpha participates in the cache key.</summary>
        public Brush Brush(Color c)
        {
            uint key = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
            Brush b;
            if (_cache.TryGetValue(key, out b)) return b;
            var s = new SolidColorBrush(c);
            s.Freeze();
            _cache[key] = s;
            return s;
        }

        /// <summary>A token with a different alpha (e.g. 12% accent for a selected row).</summary>
        public Brush Alpha(Color c, byte a)
        {
            return Brush(Color.FromArgb(a, c.R, c.G, c.B));
        }

        // HandyControl's dark palette, warmed very slightly so it sits next to album art
        // without looking blue-cold. Accent is the familiar HandyControl blue, lifted a step
        // so it still reads on a dark surface.
        public static UiTheme Dark()
        {
            var t = new UiTheme();
            t.Window = Color.FromRgb(16, 16, 19);
            t.Surface = Color.FromRgb(26, 26, 30);
            t.SurfaceAlt = Color.FromRgb(34, 34, 39);
            t.Hover = Color.FromArgb(26, 255, 255, 255);
            t.Pressed = Color.FromArgb(46, 0, 0, 0);
            t.Border = Color.FromArgb(26, 255, 255, 255);
            t.BorderStrong = Color.FromArgb(70, 255, 255, 255);

            t.Text = Color.FromArgb(246, 246, 247, 250);
            t.TextSecondary = Color.FromArgb(168, 226, 228, 234);
            t.TextTertiary = Color.FromArgb(104, 222, 224, 230);

            t.Accent = Color.FromRgb(72, 152, 232);
            t.AccentHover = Color.FromRgb(96, 174, 245);
            t.AccentSoft = Color.FromArgb(38, 90, 170, 245);
            t.OnAccent = Color.FromRgb(250, 251, 253);
            t.Danger = Color.FromRgb(224, 92, 92);
            t.Solid = Color.FromRgb(242, 243, 245);
            t.SolidHover = Color.FromRgb(255, 255, 255);
            t.SolidPressed = Color.FromRgb(226, 228, 232);
            t.OnSolid = Color.FromRgb(22, 24, 27);
            t.DangerInk = Color.FromRgb(206, 84, 84);
            t.Track = Color.FromArgb(38, 255, 255, 255);
            t.TrackOn = Color.FromRgb(214, 216, 220);
            t.Success = Color.FromRgb(88, 190, 130);
            t.Warning = Color.FromRgb(226, 170, 82);
            return t;
        }

        // HandyControl's own look is the light one; kept here so a page can be previewed both
        // ways and so the kit is not secretly dark-only.
        public static UiTheme Light()
        {
            var t = new UiTheme();
            t.Window = Color.FromRgb(244, 245, 247);
            t.Surface = Color.FromRgb(255, 255, 255);
            t.SurfaceAlt = Color.FromRgb(247, 248, 250);
            t.Hover = Color.FromArgb(20, 0, 0, 0);
            t.Pressed = Color.FromArgb(34, 0, 0, 0);
            t.Border = Color.FromArgb(22, 0, 0, 0);
            t.BorderStrong = Color.FromArgb(64, 0, 0, 0);

            t.Text = Color.FromRgb(24, 26, 30);
            t.TextSecondary = Color.FromRgb(88, 92, 100);
            t.TextTertiary = Color.FromRgb(138, 142, 150);

            t.Accent = Color.FromRgb(35, 126, 214);
            t.AccentHover = Color.FromRgb(58, 146, 232);
            t.AccentSoft = Color.FromArgb(28, 35, 126, 214);
            t.OnAccent = Color.FromRgb(255, 255, 255);
            t.Danger = Color.FromRgb(207, 74, 74);
            t.Solid = Color.FromRgb(22, 24, 27);
            t.SolidHover = Color.FromRgb(35, 38, 44);
            t.SolidPressed = Color.FromRgb(14, 16, 19);
            t.OnSolid = Color.FromRgb(255, 255, 255);
            t.DangerInk = Color.FromRgb(192, 50, 47);
            t.Track = Color.FromArgb(30, 0, 0, 0);
            t.TrackOn = Color.FromRgb(60, 64, 72);
            t.Success = Color.FromRgb(60, 160, 104);
            t.Warning = Color.FromRgb(196, 138, 46);
            return t;
        }
    }
}
