// ---------------------------------------------------------------------------
// LookPages.cs
//
// The appearance tab is ten stops, one page each:
//   画板 / 卡片 / 歌名 / 艺人 / 歌词 / 进度条 / 按钮 / 频谱 / 封面 / 预设
// Every builder here fills the right-hand column for exactly one stop; LookPage()
// in MainWindow.cs only picks which one runs. A new stop is therefore a builder
// plus one entry in RailOrder -- never a slice of an already finished page, which
// is what broke the card grouping when it was tried.
// ---------------------------------------------------------------------------
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Amc
{
    public partial class MainWindow
    {
        /// <summary>The appearance tab's stops, in the order the user listed them.</summary>
        static readonly string[] RailOrder =
        {
            "rail.board", "rail.card", "rail.title", "rail.artist", "rail.lyrics",
            "sec.bar", "rail.buttons", "sec.spectrum", "rail.art", "rail.preset"
        };

        /// <summary>画板: the draft board, its four buttons, and every size the template owns.</summary>
        void BoardStop(StackPanel box)
        {
            // ---- board: a sketch pad, never a live mirror --------------------
            // Everything dragged here goes into a private draft; the card itself does not
            // move until the user presses Apply, which is what keeps this silky.
            var board = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 16, 14, 16),
                Margin = new Thickness(0, 2, 0, 14)
            };
            // 1:1 with the card, so the panel scrolls instead of silently rescaling the drawing
            var boardScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _preview,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var boardHost = new Grid { MinHeight = 150 };
            boardHost.Children.Add(boardScroll);
            board.Child = boardHost;
            box.Children.Add(board);

            var boardBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            boardBar.Children.Add(Pill(Strings.T("board.apply"), delegate { _preview.RaiseApply(); }, true));
            boardBar.Children.Add(Pill(Strings.T("board.cancel"), delegate { _preview.RaiseCancel(); }, false));
            boardBar.Children.Add(Pill(Strings.T("board.addModule"), delegate { _preview.ShowAddMenu(); }, false));
            boardBar.Children.Add(Pill(Strings.T("board.refresh"), delegate { _preview.RefreshFromCard(); }, false));
            box.Children.Add(boardBar);

            // 画板下的一句话说明：拖是改位置，尺寸在下面那些选项里改。
            var dragHint = Hint(Strings.T("board.dragHint"));
            dragHint.Margin = new Thickness(0, -6, 0, 12);
            box.Children.Add(dragHint);

            // Length caps: the character cap *is* the width while the box is on auto, so these
            // three steer the frames you just saw above.
            box.Children.Add(Section(Strings.T("page.look.size")));
            var capStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            capStack.Children.Add(Row(Strings.T("cap.title"), Stepper(CapOf(ModuleKind.Title), 0, 80, 2, "0",
                delegate(double v) { SetCap(ModuleKind.Title, (int)Math.Round(v)); })));
            capStack.Children.Add(Row(Strings.T("cap.artist"), Stepper(CapOf(ModuleKind.Artist), 0, 60, 2, "0",
                delegate(double v) { SetCap(ModuleKind.Artist, (int)Math.Round(v)); })));
            capStack.Children.Add(Row(Strings.T("cap.lyrics"), Stepper(CapOf(ModuleKind.Lyrics), 0, 80, 2, "0",
                delegate(double v) { SetCap(ModuleKind.Lyrics, (int)Math.Round(v)); })));
            capStack.Children.Add(Row(Strings.T("lbl.coverSize"), Stepper(_cfg.ArtSize, 56, 160, 4, "0", delegate(double v)
            {
                _cfg.ArtSize = v;
                // one size for both views: write it into the Cover module, so the board's circle
                // grows with the disc instead of staying at the old radius
                CardConfig m2 = _card.Model;
                if (m2 != null)
                {
                    ModuleConfig cov = m2.Find(ModuleKind.Cover);
                    if (cov != null) { cov.W = v; cov.H = v; }
                    _card.InvalidateModel();
                    // the cover size is the template's Cover module: persist it, do not just repaint
                    bool okSize = _card.SaveModel();
                    LyricsService.Log("artsize=" + v.ToString("0") + " saved=" + (okSize ? 1 : 0));
                }
                _card.ApplyLook();
                _cfg.Save();
                _preview.RefreshFromCard();
            })));
            // Same story as the cover: these steer the Progress and MusicBars modules, so they
            // sit with the draft controls instead of pretending to be instant repaints.
            capStack.Children.Add(Row(Strings.T("lbl.barLength"), Stepper(_cfg.BarScale, 0.5, 3.0, 0.05, "0%", delegate(double v)
            {
                double oldScale = _cfg.BarScale <= 0 ? 1.0 : _cfg.BarScale;
                _cfg.BarScale = v;
                // The bar's length is the Progress module's width. Scale the width that is already
                // there instead of recomputing from a constant, so a width dragged in the board is
                // kept: newW = oldW * (new / old).
                CardConfig m2 = _card.Model;
                if (m2 != null)
                {
                    ModuleConfig pr = m2.Find(ModuleKind.Progress);
                    if (pr != null)
                    {
                        double baseW = pr.W > 0 ? pr.W / oldScale : 320.0;
                        pr.W = Math.Max(40.0, Math.Round(baseW * v));
                    }
                    _card.InvalidateModel();
                    // the width lives in the template, so write it there and let the board follow
                    _card.SaveModel();
                }
                _card.ApplyLook();
                _cfg.Save();
                _preview.RefreshFromCard();
            })));
            // The transport row is a template size as well: its height is the button hit box, so
            // the 暂停/播放 box in the board above grows with it (width follows so the three
            // buttons still fit inside).
            capStack.Children.Add(Row(Strings.T("lbl.btnSize"), Stepper(ControlsSizeOf(), 18, 60, 2, "0",
                delegate(double v) { ControlsModule(v); })));
            box.Children.Add(TwoColumn(capStack));

            // The spectrum's length and height are template sizes too (they write straight into
            // the MusicBars module); the rest of the spectrum is pure look and lives on 频谱.
            box.Children.Add(Row(Strings.T("lbl.barsLength"), Stepper(_cfg.BarsLength, 40, 420, 10, "0", delegate(double v)
            {
                _cfg.BarsLength = v;
                BarsModule(v, -1);
                _cfg.Save();
            })));
            box.Children.Add(Row(Strings.T("lbl.barsHeight"), Stepper(_cfg.BarsMaxHeight, 8, 90, 2, "0", delegate(double v)
            {
                _cfg.BarsMaxHeight = v;
                BarsModule(-1, v);
                _cfg.Save();
            })));

            // The board edits a draft template: nothing above this line touches the card until
            // 应用. Everything else in this tab is a live setting that repaints as you drag it.
            box.Children.Add(Rule());
            var applyNote = Label(Strings.T("board.applyNote"),
                                  11.5, TextDim, FontWeights.Normal);
            applyNote.TextWrapping = TextWrapping.Wrap;
            applyNote.Margin = new Thickness(0, 0, 0, 4);
            box.Children.Add(applyNote);
            box.Children.Add(Rule());
        }

        /// <summary>卡片: the whole-widget look -- scale, opacity, see-through, text shadow.</summary>
        void CardStop(StackPanel box)
        {
            box.Children.Add(Section(Strings.T("rail.card")));
            box.Children.Add(Row(Strings.T("lbl.scale"), Stepper(_cfg.Scale, 0.55, 2.2, 0.05, "0.00", delegate(double v)
            { _cfg.Scale = v; _card.ApplyCurrentScale(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.opacity"), Stepper(_cfg.Opacity, 0.25, 1.0, 0.02, "0.00", delegate(double v)
            { _cfg.Opacity = v; _card.ApplyCurrentOpacity(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.clearBg"), Toggle(_cfg.ClearBg, delegate(bool v)
            { _card.SetClearBg(v); _cfg.Save(); })));
            // Text shadow is a whole-card treatment (light wallpapers need it), so it sits with
            // the other card-wide switches instead of on one of the text stops.
            box.Children.Add(Row(Strings.T("lbl.textShadow"), Toggle(_cfg.TextShadow, delegate(bool v)
            { _cfg.TextShadow = v; _card.ApplyBackdropNow(); _cfg.Save(); })));
        }

        /// <summary>歌名: size, alignment, colour, and whether the artist shares its line.</summary>
        void TitleStop(StackPanel box)
        {
            box.Children.Add(Section(Strings.T("rail.title")));
            box.Children.Add(Row(Strings.T("lbl.titleSize"), Stepper(_cfg.TitleScale, 0.6, 1.6, 0.05, "0.00", delegate(double v)
            { _cfg.TitleScale = v; _card.ApplyLook(); _cfg.Save(); })));

            // Alignment lives on the module (layout.ini), not on the appearance settings: the box is
            // the text box, so "left/centre/right" is how the text sits inside it.
            box.Children.Add(Row(Strings.T("lbl.alignTitle"), Segmented(new string[] { Strings.T("align.left"), Strings.T("align.center"), Strings.T("align.right") },
                AlignIndex(ModuleKind.Title), delegate(int idx) { SetModuleAlign(ModuleKind.Title, idx); })));
            box.Children.Add(ColorRow(Strings.T("lbl.titleColor"), _cfg.TitleColor, delegate(string v)
            { _cfg.TitleColor = v; _card.ApplyLook(); _cfg.Save(); }));
            // Same line or two lines: the switch rewrites the template, so it belongs with the
            // title row rather than with the instant colours.
            box.Children.Add(Row(Strings.T("lbl.sameLine"), Toggle(SameLine(), delegate(bool on)
            {
                CardConfig m = _card.Model;
                if (m == null) return;
                m.TitleArtistInline = on;
                bool saved = m.Save(CardConfig.DefaultPath);
                LyricsService.Log("toggle inline=" + (on ? "1" : "0") + " saved=" + (saved ? "1" : "0"));
                _card.ReloadModel();
                _card.RefreshArtistLine();
                RebuildAll();
            })));
        }

        /// <summary>艺人: size, alignment and colour (its length cap is a template size).</summary>
        void ArtistStop(StackPanel box)
        {
            box.Children.Add(Section(Strings.T("rail.artist")));
            box.Children.Add(Row(Strings.T("lbl.artistSize"), Stepper(_cfg.ArtistScale, 0.6, 1.6, 0.05, "0.00", delegate(double v)
            { _cfg.ArtistScale = v; _card.ApplyLook(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.alignArtist"), Segmented(new string[] { Strings.T("align.left"), Strings.T("align.center"), Strings.T("align.right") },
                AlignIndex(ModuleKind.Artist), delegate(int idx) { SetModuleAlign(ModuleKind.Artist, idx); })));
            // the artist length cap now lives on the module itself (MaxChars in layout.ini)
            box.Children.Add(ColorRow(Strings.T("lbl.artistColor"), _cfg.ArtistColor, delegate(string v)
            { _cfg.ArtistColor = v; _card.ApplyLook(); _cfg.Save(); }));
        }

        /// <summary>歌词: size, alignment and colour.</summary>
        void LyricStop(StackPanel box)
        {
            box.Children.Add(Section(Strings.T("rail.lyrics")));
            box.Children.Add(Row(Strings.T("lbl.lyricSize"), Stepper(_cfg.LyricScale, 0.6, 1.6, 0.05, "0.00", delegate(double v)
            { _cfg.LyricScale = v; _card.ApplyLook(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.alignLyrics"), Segmented(new string[] { Strings.T("align.left"), Strings.T("align.center"), Strings.T("align.right") },
                AlignIndex(ModuleKind.Lyrics), delegate(int idx) { SetModuleAlign(ModuleKind.Lyrics, idx); })));
            box.Children.Add(ColorRow(Strings.T("lbl.lyricColor"), _cfg.LyricColor, delegate(string v)
            { _cfg.LyricColor = v; _card.ApplyLook(); _cfg.Save(); }));
        }

        /// <summary>进度条: thickness, track and fill (its length is a template size).</summary>
        void BarStop(StackPanel box)
        {
            box.Children.Add(Section(Strings.T("sec.bar")));
            box.Children.Add(Row(Strings.T("lbl.barThickness"), Stepper(_cfg.BarThickness, 2, 8, 0.5, "0.0", delegate(double v)
            { _cfg.BarThickness = v; _card.ApplyLook(); _cfg.Save(); })));
            box.Children.Add(ColorRow(Strings.T("lbl.barColor"), _cfg.BarColor, delegate(string v)
            { _cfg.BarColor = v; _card.ApplyLook(); _cfg.Save(); }));
            box.Children.Add(ColorRow(Strings.T("lbl.barFill"), _cfg.BarFillColor, delegate(string v)
            { _cfg.BarFillColor = v; _card.ApplyLook(); _cfg.Save(); }));
        }

        /// <summary>按钮: the colour of the transport row under the lyrics.</summary>
        void ButtonStop(StackPanel box)
        {
            box.Children.Add(Section(Strings.T("rail.buttons")));
            box.Children.Add(ColorRow(Strings.T("lbl.controlsColor"), _cfg.ControlsColor, delegate(string v)
            { _cfg.ControlsColor = v; _card.ApplyLook(); _cfg.Save(); }));
        }

        /// <summary>频谱: bar count, stroke, attack/release and colour (length/height are on 画板).</summary>
        void SpectrumStop(StackPanel box)
        {
            box.Children.Add(Section(Strings.T("sec.spectrum")));
            box.Children.Add(Row(Strings.T("lbl.barsCount"), Stepper(_cfg.BarsCount, 0, 96, 1, "0", delegate(double v)
            {
                _cfg.BarsCount = (int)Math.Round(v);
                _card.ApplyLook();
                _cfg.Save();
            })));
            box.Children.Add(Row(Strings.T("lbl.barsThickness"), Stepper(_cfg.BarsThickness, 2, 14, 1, "0", delegate(double v)
            {
                _cfg.BarsThickness = v;
                _card.ApplyLook();
                _cfg.Save();
            })));
            box.Children.Add(Row(Strings.T("lbl.barsAttack"), Stepper(_cfg.BarsAttack, 0.05, 1.0, 0.05, "0.00", delegate(double v)
            {
                _cfg.BarsAttack = v;
                _cfg.Save();
            })));
            box.Children.Add(Row(Strings.T("lbl.barsRelease"), Stepper(_cfg.BarsRelease, 0.02, 1.0, 0.02, "0.00", delegate(double v)
            {
                _cfg.BarsRelease = v;
                _cfg.Save();
            })));
            box.Children.Add(ColorRow(Strings.T("lbl.barsColor"), _cfg.BarsColor, delegate(string v)
            {
                _cfg.BarsColor = v;
                _card.ApplyLook();
                _cfg.Save();
            }));
        }

        /// <summary>封面: shape, spin, and the whole grading chain (size is a template size).</summary>
        void ArtStop(StackPanel box)
        {
            box.Children.Add(Section(Strings.T("rail.art")));
            box.Children.Add(Row(Strings.T("lbl.artshape"), Segmented(
                new string[] { Strings.T("artshape.circle"), Strings.T("artshape.square") },
                string.Equals(_cfg.ArtShape, "square", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                delegate(int i) { _card.SetArtShape(i == 1 ? "square" : "circle"); RebuildAll(); })));
            box.Children.Add(Row(Strings.T("lbl.spin"), Toggle(_cfg.ArtSpin, delegate(bool v)
            { _card.SetArtSpin(v); _cfg.Save(); })));
            if (_cfg.ArtTint)
                box.Children.Add(Label(Strings.T("artSat.off"), 11.5, TextDim, FontWeights.Normal));
            else
                box.Children.Add(Row(Strings.T("lbl.artSat"), Stepper(_cfg.ArtSat, 0, 1, 0.05, "0.00", delegate(double v)
                { _cfg.ArtSat = v; _card.RefreshArt(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.artHiCut"), Stepper(_cfg.ArtHiCut, 0, 0.6, 0.02, "0.00", delegate(double v)
            { _cfg.ArtHiCut = v; _card.RefreshArt(); _cfg.Save(); })));
            // Duotone sits right above the grading sliders: it decides the hue, they shape the tone.
            box.Children.Add(Row(Strings.T("lbl.artTint"), Toggle(_cfg.ArtTint, delegate(bool v)
            { _card.SetArtTint(v); RebuildAll(); })));
            box.Children.Add(Row(Strings.T("lbl.artTintPreset"), Segmented(
                new string[] { Strings.T("tint.warm"), Strings.T("tint.cool"), Strings.T("tint.violet") },
                TintPresetIndex(),
                delegate(int i)
                {
                    if (i == 0) { _cfg.ArtTintDark = "#3A0A0A"; _cfg.ArtTintLight = "#FFB4B4"; }
                    else if (i == 1) { _cfg.ArtTintDark = "#061826"; _cfg.ArtTintLight = "#A9D6FF"; }
                    else { _cfg.ArtTintDark = "#1A0A2E"; _cfg.ArtTintLight = "#D9B8FF"; }
                    _cfg.ArtTint = true;
                    _card.SetArtTint(true);
                    _cfg.Save();
                    RebuildAll();
                })));
            box.Children.Add(Row(Strings.T("lbl.artTintMix"), Stepper(_cfg.ArtTintMix, 0, 1, 0.05, "0.00", delegate(double v)
            { _cfg.ArtTintMix = v; _cfg.Save(); _card.RefreshArt(); })));
            box.Children.Add(Row(Strings.T("lbl.artBright"), Stepper(_cfg.ArtBrightness, 0.5, 1.6, 0.02, "0.00", delegate(double v)
            { _cfg.ArtBrightness = v; _card.RefreshArt(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.artContrast"), Stepper(_cfg.ArtContrast, 0.5, 1.8, 0.02, "0.00", delegate(double v)
            { _cfg.ArtContrast = v; _card.RefreshArt(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.artWarmth"), Stepper(_cfg.ArtWarmth, -1, 1, 0.05, "0.00", delegate(double v)
            { _cfg.ArtWarmth = v; _card.RefreshArt(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.artHue"), Stepper(_cfg.ArtHue, -180, 180, 5, "0", delegate(double v)
            { _cfg.ArtHue = v; _card.RefreshArt(); _cfg.Save(); })));
            box.Children.Add(Row(Strings.T("lbl.artVignette"), Stepper(_cfg.ArtVignette, 0, 0.8, 0.02, "0.00", delegate(double v)
            { _cfg.ArtVignette = v; _card.RefreshArt(); _cfg.Save(); })));
            // the cover glow was removed: the halo bled over the title and lyrics
        }

        /// <summary>预设: snapshot the whole look -- and the template -- under a name.</summary>
        void PresetStop(StackPanel box)
        {
            // The preset block is a page of its own: it snapshots the whole look, so it belongs
            // to no single group on the other stops.
            box.Children.Add(Section(Strings.T("rail.preset")));

            var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
            var nameBox = new TextBox
            {
                Width = 200,
                Height = 28,
                FontSize = 12,
                Padding = new Thickness(8, 3, 8, 3),
                Foreground = new SolidColorBrush(TextMain),
                Background = new SolidColorBrush(Color.FromArgb(255, 28, 28, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CaretBrush = new SolidColorBrush(TextMain),
                ToolTip = Strings.T("preset.prompt")
            };
            saveRow.Children.Add(nameBox);
            var savePill = Pill(Strings.T("preset.save"), delegate
            {
                string nm = (nameBox.Text == null) ? "" : nameBox.Text.Trim();
                if (nm.Length == 0) nm = Strings.F("preset.defaultName", _cfg.Presets.Count + 1);
                if (nm.Length > 14) nm = nm.Substring(0, 14);
                if (_cfg.Presets.Count >= 10) _cfg.Presets.RemoveAt(0);
                Config.LookPreset np = _cfg.CaptureLook(nm);
                string tmpl = ReadTemplateText();
                if (tmpl != null) np.Data += ";Template=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tmpl));
                _cfg.Presets.Add(np);
                _cfg.Save();
                SetPage("look");
            }, false);
            savePill.Margin = new Thickness(8, 0, 0, 0);
            saveRow.Children.Add(savePill);
            box.Children.Add(saveRow);

            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            if (_cfg.Presets.Count == 0) chips.Children.Add(Hint(Strings.T("preset.empty")));
            else for (int i = 0; i < _cfg.Presets.Count; i++) chips.Children.Add(PresetChip(_cfg.Presets[i]));
            box.Children.Add(chips);
        }
    }
}
