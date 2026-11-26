using System;
using System.Text;
using Amc;

static class T
{
    static int fails;

    static void Check(string what, bool ok)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
        if (!ok) fails++;
    }

    static void Main()
    {
        // 1. default -> text -> parse -> equal
        CardConfig d = CardConfig.Default();
        string t1 = d.Serialize();
        CardConfig p1 = CardConfig.Parse(t1);
        Check("default survives a round trip", d.ValueEquals(p1));
        Check("re-serialising gives byte-identical text", t1 == p1.Serialize());

        // 2. every field set, including "auto" sentinels and odd values
        CardConfig full = CardConfig.Default();
        ModuleConfig cover = full.Find(ModuleKind.Cover);
        cover.X = 396; cover.Y = 12; cover.W = 96; cover.H = 96;
        cover.FontSize = 0; cover.Color = "#AABBCCDD"; cover.Align = ModuleAlign.Center; cover.ZIndex = 40;

        ModuleConfig lyrics = full.Find(ModuleKind.Lyrics);
        lyrics.FontSize = 19.1; lyrics.Color = "#FFFFFFFF"; lyrics.Align = ModuleAlign.Right;
        lyrics.W = -1; lyrics.H = -1;                       // auto stays auto
        lyrics.X = double.NaN; lyrics.Y = double.NaN;       // unset stays unset

        ModuleConfig album = full.Find(ModuleKind.Album);
        album.Enabled = true;                               // flip the disabled one too
        album.W = 123.456; album.ZIndex = -2;

        string t2 = full.Serialize();
        CardConfig p2 = CardConfig.Parse(t2);
        Check("fully populated config survives a round trip", full.ValueEquals(p2));
        Check("idempotent on the second pass", t2 == p2.Serialize());
        Check("auto H stays -1", p2.Find(ModuleKind.Lyrics).H == -1);
        Check("unset X stays NaN", double.IsNaN(p2.Find(ModuleKind.Lyrics).X));
        Check("colour keeps alpha", p2.Find(ModuleKind.Cover).Color == "#AABBCCDD");
        Check("negative ZIndex kept", p2.Find(ModuleKind.Album).ZIndex == -2);
        Check("fractional width kept", p2.Find(ModuleKind.Album).W == 123.456);

        // 3. hand-written file with 'auto', junk keys, an unknown section, no SchemaVersion
        string hand =
            "[Title]\r\n" +
            "Enabled=yes\r\n" +              // invalid bool -> default true, no crash
            "H=auto\r\n" +
            "W=-\r\n" +
            "Bogus=1\r\n" +                  // unknown key ignored
            "\r\n[Nonsense]\r\n" +           // unknown section skipped whole
            "Enabled=False\r\n" +
            "\r\n[Lyrics]\r\n" +
            "X=12.5\r\n" +
            "align=LEFT\r\n" +
            "FontSize=oops\r\n";             // invalid number -> 0
        CardConfig ph = CardConfig.Parse(hand);
        Check("unknown section dropped", ph.Find(ModuleKind.Cover) == null);
        Check("only mentioned modules are present", ph.Modules.Count == 2);
        Check("auto keyword -> -1", ph.Find(ModuleKind.Title).H == -1);
        Check("dash -> -1", ph.Find(ModuleKind.Title).W == -1);
        Check("bad bool falls back to default", ph.Find(ModuleKind.Title).Enabled);
        Check("bad number falls back to 0", ph.Find(ModuleKind.Lyrics).FontSize == 0);
        Check("case-insensitive align", ph.Find(ModuleKind.Lyrics).Align == ModuleAlign.Left);
        Check("missing SchemaVersion defaults to current", ph.SchemaVersion == CardSchema.Version);
        Check("hand file round trip stable", ph.Serialize() == CardConfig.Parse(ph.Serialize()).Serialize());

        // 4. empty / null input must not throw
        Check("null parses to empty", CardConfig.Parse(null).Modules.Count == 0);
        Check("empty parses to empty", CardConfig.Parse("").Modules.Count == 0);
        Check("empty serialises without throwing", CardConfig.Parse("").Serialize().Length > 0);

        // 5. clone is deep
        CardConfig a = CardConfig.Default();
        CardConfig b = a.Clone();
        b.Find(ModuleKind.Title).FontSize = 99;
        Check("clone does not alias the original", a.Find(ModuleKind.Title).FontSize == 0 && b.Find(ModuleKind.Title).FontSize == 99);

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "ALL GOOD" : (fails + " FAILED"));
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
