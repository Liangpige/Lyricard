// ---------------------------------------------------------------------------
// CardConfig.cs
//
// The card's layout as data: which modules exist, where each one sits, and what
// it looks like. This is the model the modular rewrite will render from, so it
// has to be complete, hand-editable and lossless on a round trip.
//
// Format notes:
//   * INI-style, one section per module, keys are the field names. Written with
//     CRLF and no BOM so a text editor on this machine sees it unchanged.
//   * Any number that is "let the layout decide" is stored as -1 (W/H) or left
//     out entirely (X/Y). That keeps "unset" and "0" from meaning the same thing.
//   * Unknown sections and unknown keys are ignored on purpose: a newer build
//     writing extra fields must not stop an older one from loading.
//   * Nothing here touches Config.cs, the window or the UI yet. Step 1 is the
//     model only; wiring it up is a later step, on purpose.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Amc
{
    /// <summary>Bumped when the on-disk shape changes in a way readers must know about.</summary>
    public static class CardSchema
    {
        public const int Version = 1;
    }

    /// <summary>The modules the card can draw. Order here is the default paint order.</summary>
    public enum ModuleKind
    {
        Cover = 0,
        Title = 1,
        Artist = 2,
        Album = 3,
        Lyrics = 4,
        Progress = 5,
        /// <summary>The transport row. Not user-configurable at first, but it has to take part in
        /// the vertical stack - without it the text column would be shorter than the card and the
        /// title would drift down by half a cover.</summary>
        Controls = 6
    }

    /// <summary>Horizontal alignment of a module inside its own box.</summary>
    public enum ModuleAlign
    {
        Auto = 0,
        Left = 1,
        Center = 2,
        Right = 3
    }

    /// <summary>One module's placement and look. Every field has a defined "auto" value.</summary>
    public class ModuleConfig
    {
        public ModuleKind Kind = ModuleKind.Title;

        public bool Enabled = true;

        /// <summary>Left edge in card units. NaN = let the layout decide.</summary>
        public double X = double.NaN;

        /// <summary>Top edge in card units. NaN = let the layout decide.</summary>
        public double Y = double.NaN;

        /// <summary>Width in card units, -1 = auto.</summary>
        public double W = -1;

        /// <summary>Height in card units, -1 = auto.</summary>
        public double H = -1;

        /// <summary>Font size in card units, 0 = inherit the module's default.</summary>
        public double FontSize = 0;

        /// <summary>Colour as #RRGGBB or #AARRGGBB, "" = inherit.</summary>
        public string Color = "";

        public ModuleAlign Align = ModuleAlign.Auto;

        /// <summary>Paint order; larger draws later (on top).</summary>
        public int ZIndex = 0;

        public ModuleConfig() { }

        public ModuleConfig(ModuleKind kind)
        {
            Kind = kind;
            ZIndex = (int)kind;
        }

        public ModuleConfig Clone()
        {
            ModuleConfig c = new ModuleConfig(Kind);
            c.Enabled = Enabled;
            c.X = X;
            c.Y = Y;
            c.W = W;
            c.H = H;
            c.FontSize = FontSize;
            c.Color = Color ?? "";
            c.Align = Align;
            c.ZIndex = ZIndex;
            return c;
        }

        public bool ValueEquals(ModuleConfig o)
        {
            if (o == null) return false;
            return Kind == o.Kind
                && Enabled == o.Enabled
                && SameNumber(X, o.X)
                && SameNumber(Y, o.Y)
                && SameNumber(W, o.W)
                && SameNumber(H, o.H)
                && SameNumber(FontSize, o.FontSize)
                && string.Equals(Color ?? "", o.Color ?? "", StringComparison.Ordinal)
                && Align == o.Align
                && ZIndex == o.ZIndex;
        }

        // NaN != NaN in plain double maths, but for config purposes "unset" has to
        // compare equal to "unset", otherwise a round trip looks lossy when it is not.
        static bool SameNumber(double a, double b)
        {
            if (double.IsNaN(a) && double.IsNaN(b)) return true;
            return a.Equals(b);
        }
    }

    /// <summary>Whole-card layout description. Immutable in spirit: edit a clone, then swap.</summary>
    public class CardConfig
    {
        public int SchemaVersion = CardSchema.Version;

        public List<ModuleConfig> Modules = new List<ModuleConfig>();

        // The card is exactly this big, in logical units. The editor owns these numbers; the
        // window and the board both derive their size from them and nothing else.
        public double CardW = 470;
        public double CardH = 122;

        public CardConfig() { }

        /// <summary>
        /// The layout the current card uses, expressed in the new model: cover on the
        /// right, a text column holding title / artist / lyrics, progress bar underneath.
        /// Album is present but disabled because the shipping card never draws it.
        /// </summary>
        public static CardConfig Default()
        {
            CardConfig c = new CardConfig();
            c.Modules.Add(new ModuleConfig(ModuleKind.Cover));
            c.Modules.Add(new ModuleConfig(ModuleKind.Title));
            ModuleConfig artist = new ModuleConfig(ModuleKind.Artist);
            // The artist rides on the title line by default. Give it a real width so the editor
            // shows a box you can grab instead of one that hides under the title's full-width box.
            artist.W = 120;
            c.Modules.Add(artist);
            ModuleConfig album = new ModuleConfig(ModuleKind.Album);
            album.Enabled = false;
            c.Modules.Add(album);
            c.Modules.Add(new ModuleConfig(ModuleKind.Lyrics));
            c.Modules.Add(new ModuleConfig(ModuleKind.Progress));
            c.Modules.Add(new ModuleConfig(ModuleKind.Controls));
            return c;
        }

        public ModuleConfig Find(ModuleKind kind)
        {
            for (int i = 0; i < Modules.Count; i++)
                if (Modules[i].Kind == kind) return Modules[i];
            return null;
        }

        public CardConfig Clone()
        {
            CardConfig c = new CardConfig();
            c.SchemaVersion = SchemaVersion;
            c.CardW = CardW;
            c.CardH = CardH;
            for (int i = 0; i < Modules.Count; i++) c.Modules.Add(Modules[i].Clone());
            return c;
        }

        public bool ValueEquals(CardConfig o)
        {
            if (o == null) return false;
            if (SchemaVersion != o.SchemaVersion) return false;
            if (Modules.Count != o.Modules.Count) return false;
            for (int i = 0; i < Modules.Count; i++)
                if (!Modules[i].ValueEquals(o.Modules[i])) return false;
            return true;
        }

        // ---- write -----------------------------------------------------------

        public string Serialize()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# Lyricard card layout\r\n");
            sb.Append("SchemaVersion=").Append(SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("CardW=").Append(Num(CardW)).Append("\r\n");
            sb.Append("CardH=").Append(Num(CardH)).Append("\r\n");

            int kindCount = Enum.GetValues(typeof(ModuleKind)).Length;
            for (int k = 0; k < kindCount; k++)
            {
                ModuleKind kind = (ModuleKind)k;
                ModuleConfig m = Find(kind);
                if (m == null) continue;   // a module the file did not mention stays absent

                sb.Append("\r\n[").Append(kind.ToString()).Append("]\r\n");
                sb.Append("Enabled=").Append(m.Enabled ? "True" : "False").Append("\r\n");
                if (!double.IsNaN(m.X)) sb.Append("X=").Append(Num(m.X)).Append("\r\n");
                if (!double.IsNaN(m.Y)) sb.Append("Y=").Append(Num(m.Y)).Append("\r\n");
                sb.Append("W=").Append(Num(m.W)).Append("\r\n");
                sb.Append("H=").Append(Num(m.H)).Append("\r\n");
                sb.Append("FontSize=").Append(Num(m.FontSize)).Append("\r\n");
                sb.Append("Color=").Append(m.Color ?? "").Append("\r\n");
                sb.Append("Align=").Append(m.Align.ToString()).Append("\r\n");
                sb.Append("ZIndex=").Append(m.ZIndex.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }
            return sb.ToString();
        }

        // ---- read ------------------------------------------------------------

        /// <summary>Where the template lives: a sibling of config.ini so a backup is still two files.</summary>
        public static string DefaultPath
        {
            get { return System.IO.Path.Combine(Amc.Config.Dir, "layout.ini"); }
        }

        /// <summary>
        /// Load the saved template. A missing or unreadable file means "factory template" — the
        /// first run must behave exactly like the built-in look, never like an empty card.
        /// </summary>
        public static CardConfig LoadOr(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return Default();
                string text = System.IO.File.ReadAllText(path, Encoding.UTF8);
                CardConfig c = Parse(text);
                return c;
            }
            catch (Exception)
            {
                return Default();
            }
        }

        /// <summary>Write the template. Returns false instead of throwing: a failed save must not kill the card.</summary>
        public bool Save(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(path, Serialize(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static CardConfig Parse(string text)
        {
            CardConfig c = new CardConfig();
            if (text == null) return c;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            ModuleConfig current = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i].Trim();
                if (raw.Length == 0) continue;
                if (raw[0] == '#' || raw[0] == ';') continue;

                if (raw[0] == '[')
                {
                    int close = raw.IndexOf(']');
                    if (close < 0) continue;
                    string name = raw.Substring(1, close - 1).Trim();
                    ModuleKind kind;
                    if (!TryKind(name, out kind)) { current = null; continue; }   // unknown section: skip it
                    current = c.Find(kind);
                    if (current == null) { current = new ModuleConfig(kind); c.Modules.Add(current); }
                    continue;
                }

                int eq = raw.IndexOf('=');
                if (eq < 0) continue;
                string key = raw.Substring(0, eq).Trim();
                string val = raw.Substring(eq + 1).Trim();

                if (current == null)
                {
                    // Only SchemaVersion lives outside a section.
                    if (key.Equals("SchemaVersion", StringComparison.OrdinalIgnoreCase))
                        c.SchemaVersion = Int(val, CardSchema.Version);
                    else if (key.Equals("CardW", StringComparison.OrdinalIgnoreCase)) c.CardW = Number(val, 470);
                    else if (key.Equals("CardH", StringComparison.OrdinalIgnoreCase)) c.CardH = Number(val, 122);
                    continue;
                }

                if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) current.Enabled = Bool(val, true);
                else if (key.Equals("X", StringComparison.OrdinalIgnoreCase)) current.X = NumOrAuto(val);
                else if (key.Equals("Y", StringComparison.OrdinalIgnoreCase)) current.Y = NumOrAuto(val);
                else if (key.Equals("W", StringComparison.OrdinalIgnoreCase)) current.W = NumOrAuto(val);
                else if (key.Equals("H", StringComparison.OrdinalIgnoreCase)) current.H = NumOrAuto(val);
                else if (key.Equals("FontSize", StringComparison.OrdinalIgnoreCase)) current.FontSize = Number(val, 0);
                else if (key.Equals("Color", StringComparison.OrdinalIgnoreCase)) current.Color = val;
                else if (key.Equals("Align", StringComparison.OrdinalIgnoreCase)) current.Align = AlignOf(val);
                else if (key.Equals("ZIndex", StringComparison.OrdinalIgnoreCase)) current.ZIndex = Int(val, (int)current.Kind);
                // anything else: ignored, by design
            }

            // Canonical order, so two files that mean the same thing compare equal.
            c.Modules.Sort(delegate(ModuleConfig a, ModuleConfig b) { return ((int)a.Kind).CompareTo((int)b.Kind); });
            return c;
        }

        // ---- small helpers ---------------------------------------------------

        static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static double Number(string s, double dflt)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return dflt;
        }

        /// <summary>"auto", "" or a bare dash means "let the layout decide" (-1).</summary>
        static double NumOrAuto(string s)
        {
            if (s.Length == 0) return -1;
            if (s.Equals("auto", StringComparison.OrdinalIgnoreCase)) return -1;
            if (s == "-") return -1;
            return Number(s, -1);
        }

        static int Int(string s, int dflt)
        {
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return dflt;
        }

        static bool Bool(string s, bool dflt)
        {
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0") return false;
            return dflt;
        }

        static ModuleAlign AlignOf(string s)
        {
            if (s.Equals("Left", StringComparison.OrdinalIgnoreCase)) return ModuleAlign.Left;
            if (s.Equals("Center", StringComparison.OrdinalIgnoreCase)) return ModuleAlign.Center;
            if (s.Equals("Right", StringComparison.OrdinalIgnoreCase)) return ModuleAlign.Right;
            return ModuleAlign.Auto;
        }

        static bool TryKind(string s, out ModuleKind kind)
        {
            int n = Enum.GetValues(typeof(ModuleKind)).Length;
            for (int i = 0; i < n; i++)
            {
                ModuleKind k = (ModuleKind)i;
                if (k.ToString().Equals(s, StringComparison.OrdinalIgnoreCase)) { kind = k; return true; }
            }
            kind = ModuleKind.Title;
            return false;
        }
    }
}
