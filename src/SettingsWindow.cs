// ---------------------------------------------------------------------------
// SettingsWindow.cs
//
/// RETIRED. The old standalone settings dialog, kept only as the fallback that
/// CardWindow.OpenSettings() falls back to if the new panel fails to open.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Amc
{
    // Dark, card-styled settings panel. Built in code (no XAML) because the whole project
    // is compiled with the C# 5 csc that ships with .NET Framework.
    /// <summary>
    /// The "Appearance" panel: a borderless dark card that edits the Config live (every control
    /// calls back into CardWindow.Refresh/ApplyLook) and persists on "Done". Rows are built in code
    /// by the small helpers below, all labels come from Strings so the panel follows the language.
    /// </summary>
    public class SettingsWindow : Window
    {
        readonly Config _cfg;
        readonly CardWindow _card;
        readonly StackPanel _body;

        static readonly Color PanelBg = Color.FromArgb(246, 20, 20, 24);
        static readonly Color PanelEdge = Color.FromArgb(38, 255, 255, 255);
        static readonly Color TextMain = Color.FromArgb(246, 246, 247, 249);
        static readonly Color TextDim = Color.FromArgb(170, 222, 224, 228);
        static readonly Color HoverBg = Color.FromArgb(28, 255, 255, 255);
        static readonly Color AccentBg = Color.FromArgb(38, 255, 255, 255);

        static SolidColorBrush B(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        TextBlock _headTitle;
        TextBlock _footHint;
        const string Version = "v0.3.2";
        TextBlock _btnDoneText;
        TextBlock _btnResetText;
        Border _panel;

        /// <summary>English labels are 1.5~2x longer than Chinese, so the panel gets wider.</summary>
        static double PanelWidth()
        {
            return 460;
        }

        /// <summary>Re-render the whole panel after an interface-language change.</summary>
        public void Relang()
        {
            Title = Strings.T("settings.title");
            if (_headTitle != null) _headTitle.Text = Strings.T("settings.title");
            Width = PanelWidth();
            if (_panel != null) _panel.Width = PanelWidth();
            if (_footHint != null) _footHint.Text = Version + " · " + Strings.T("footer.live");
            if (_btnDoneText != null) _btnDoneText.Text = Strings.T("btn.done");
            if (_btnResetText != null) _btnResetText.Text = Strings.T("btn.reset");
            Rebuild();
        }

        public SettingsWindow(Config cfg, CardWindow card)
        {
            _cfg = cfg;
            _card = card;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 460;
            Topmost = true;
            Title = Strings.T("settings.title");

            var panel = new Border
            {
                Width = PanelWidth(),
                CornerRadius = new CornerRadius(16),
                Background = B(PanelBg),
                BorderBrush = B(PanelEdge),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0)
            };
            panel.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 6,
                Opacity = 0.45,
                Color = Colors.Black
            };

            _panel = panel;
            var stack = new StackPanel();
            panel.Child = stack;

            // ---- header ----
            var head = new Grid { Margin = new Thickness(20, 16, 14, 10) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = Strings.T("settings.title"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = B(TextMain),
                VerticalAlignment = VerticalAlignment.Center
            };
            _headTitle = title;
            head.Children.Add(title);

            var close = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = "\u2715",
                    FontSize = 12,
                    Foreground = B(TextDim),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            close.MouseLeftButtonDown += delegate { Close(); };
            close.MouseEnter += delegate { close.Background = B(HoverBg); };
            close.MouseLeave += delegate { close.Background = Brushes.Transparent; };
            Grid.SetColumn(close, 1);
            head.Children.Add(close);

            head.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            stack.Children.Add(head);

            // ---- scrollable body ----
            _body = new StackPanel { Margin = new Thickness(14, 0, 20, 0) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 620,
                Content = _body
            };
            stack.Children.Add(scroll);

            BuildSections();
            BuildFooter(stack);

            Content = panel;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
        }

        // ---------------- building blocks ----------------

        void Section(string text)
        {
            var t = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = B(TextDim),
                Margin = new Thickness(8, 14, 8, 6)
            };
            _body.Children.Add(t);
        }

        // Label on top, control underneath: English labels are far too long to sit in a
        // left column, and stacking keeps both languages readable at the same panel width.
        StackPanel Row()
        {
            return new StackPanel
            {
                Margin = new Thickness(10, 0, 10, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = B(TextDim),
                Margin = new Thickness(2, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            };
        }

        Border SmallButton(string text, Action act, double width)
        {
            var b = new Border
            {
                Width = width,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = B(AccentBg),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    Foreground = B(TextMain),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseLeftButtonDown += delegate { act(); };
            b.MouseEnter += delegate { b.Background = B(HoverBg); };
            b.MouseLeave += delegate { b.Background = B(AccentBg); };
            return b;
        }

        // A "- value +" stepper; keeps the panel visually consistent with the card menu.
        void StepRow(string label, double value, double min, double max, double step,
                     string fmt, Action<double> apply)
        {
            var g = Row();
            g.Children.Add(Label(label));

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 0, 0)
            };
            var val = new TextBlock
            {
                Text = value.ToString(fmt, CultureInfo.InvariantCulture),
                FontSize = 12,
                Width = 56,
                TextAlignment = TextAlignment.Center,
                Foreground = B(TextDim),
                VerticalAlignment = VerticalAlignment.Center
            };
            double cur = value;
            Action<double> set = delegate(double v)
            {
                if (v < min) v = min;
                if (v > max) v = max;
                cur = Math.Round(v, 4);
                val.Text = cur.ToString(fmt, CultureInfo.InvariantCulture);
                apply(cur);
            };
            var minus = SmallButton("\u2212", delegate { set(cur - step); }, 26);
            var plus = SmallButton("+", delegate { set(cur + step); }, 26);
            right.Children.Add(minus);
            right.Children.Add(val);
            right.Children.Add(plus);
            g.Children.Add(right);
            _body.Children.Add(g);
        }

        // Colour row: swatch + 8 quick presets + a "…" button that opens the system picker.
        void ColorRow(string label, string current, Action<string> apply)
        {
            var g = Row();
            g.Children.Add(Label(label));

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 0, 0)
            };

            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                BorderBrush = B(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Color? parsed = ParseHex(current);
            swatch.Background = parsed.HasValue ? B(parsed.Value) : B(Color.FromArgb(255, 255, 255, 255));
            right.Children.Add(swatch);

            string[] preset =
            {
                "#FFFFFFFF", "#FFD8DCE4", "#FF9AA3B2", "#FF2B2B33",
                "#FFFF5A5A", "#FFFFC65C", "#FF6ECB7B", "#FF71A7FF"
            };
            foreach (string hex in preset)
            {
                string fixedHex = hex;
                var cell = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = B(ColorOf(fixedHex)),
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                cell.MouseLeftButtonDown += delegate
                {
                    swatch.Background = B(ColorOf(fixedHex));
                    apply(fixedHex);
                };
                right.Children.Add(cell);
            }

            var pick = SmallButton("\u2026", delegate
            {
                var dlg = new System.Windows.Forms.ColorDialog();
                dlg.FullOpen = true;
                Color? now = ParseHex(current);
                if (now.HasValue) dlg.Color = System.Drawing.Color.FromArgb(now.Value.A, now.Value.R, now.Value.G, now.Value.B);
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var c = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
                    swatch.Background = B(c);
                    apply("#" + c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"));
                }
            }, 30);
            pick.Margin = new Thickness(4, 0, 0, 0);
            right.Children.Add(pick);

            g.Children.Add(right);
            _body.Children.Add(g);
        }

        // three-way segmented picker: 左 / 中 / 右
        void LangRow()
        {
            var g = Row();
            g.Children.Add(Label(Strings.T("lbl.lang")));

            var seg = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 0, 0)
            };
            string[] names = new string[]
            {
                Strings.T("lang.auto"), Strings.T("lang.zh"), Strings.T("lang.en")
            };
            var cells = new List<Border>();
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                bool on = (Strings.Preference == i);
                var cell = new Border
                {
                    Height = 24,
                    CornerRadius = new CornerRadius(6),
                    Background = on ? B(Color.FromArgb(255, 120, 190, 255)) : B(AccentBg),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                    Padding = new Thickness(10, 0, 10, 0),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = names[i],
                        FontSize = 12,
                        Foreground = on ? B(Color.FromArgb(255, 18, 18, 22)) : B(TextMain),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                cell.MouseLeftButtonDown += delegate
                {
                    _cfg.Lang = idx;
                    _cfg.Save();
                    Strings.Resolve(idx);
                    _card.Relang();
                    Relang();
                };
                cells.Add(cell);
                seg.Children.Add(cell);
            }
            g.Children.Add(seg);
            _body.Children.Add(g);
        }

        void AlignRow(string label, string value, Action<string> apply)
        {
            var g = Row();
            g.Children.Add(Label(label));

            var seg = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 0, 0)
            };
            string[] keys = new string[] { "left", "center", "right" };
            string[] names = new string[] { Strings.T("align.left"), Strings.T("align.center"), Strings.T("align.right") };
            var cells = new List<Border>();
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                var cell = new Border
                {
                    Width = 40,
                    Height = 24,
                    CornerRadius = new CornerRadius(6),
                    Background = (value == k) ? B(Color.FromArgb(255, 120, 190, 255)) : B(AccentBg),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = names[i],
                        FontSize = 12,
                        Foreground = (value == k) ? B(Color.FromArgb(255, 18, 18, 22)) : B(TextMain),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                cells.Add(cell);
                cell.MouseLeftButtonDown += delegate
                {
                    for (int j = 0; j < cells.Count; j++)
                    {
                        bool on = keys[j] == k;
                        cells[j].Background = on ? B(Color.FromArgb(255, 120, 190, 255)) : B(AccentBg);
                        ((TextBlock)cells[j].Child).Foreground = on ? B(Color.FromArgb(255, 18, 18, 22)) : B(TextMain);
                    }
                    apply(k);
                };
                seg.Children.Add(cell);
            }
            g.Children.Add(seg);
            _body.Children.Add(g);
        }

        void ToggleRow(string label, bool value, Action<bool> apply)
        {
            var g = Row();
            g.Children.Add(Label(label));

            var box = new Border
            {
                Width = 42,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = value ? B(Color.FromArgb(255, 120, 190, 255)) : B(Color.FromArgb(48, 255, 255, 255)),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            var knob = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = B(Color.FromArgb(255, 255, 255, 255)),
                HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            box.Child = knob;
            bool on = value;
            box.MouseLeftButtonDown += delegate
            {
                on = !on;
                knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                box.Background = on ? B(Color.FromArgb(255, 120, 190, 255)) : B(Color.FromArgb(48, 255, 255, 255));
                apply(on);
            };
            g.Children.Add(box);
            _body.Children.Add(g);
        }

        static Color? ParseHex(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string t = s.Trim();
            if (t.StartsWith("#")) t = t.Substring(1);
            try
            {
                if (t.Length == 6)
                    return Color.FromArgb(255, Convert.ToByte(t.Substring(0, 2), 16),
                                          Convert.ToByte(t.Substring(2, 2), 16), Convert.ToByte(t.Substring(4, 2), 16));
                if (t.Length == 8)
                    return Color.FromArgb(Convert.ToByte(t.Substring(0, 2), 16), Convert.ToByte(t.Substring(2, 2), 16),
                                          Convert.ToByte(t.Substring(4, 2), 16), Convert.ToByte(t.Substring(6, 2), 16));
            }
            catch { }
            return null;
        }

        static Color ColorOf(string hex)
        {
            Color? c = ParseHex(hex);
            return c.HasValue ? c.Value : Color.FromArgb(255, 255, 255, 255);
        }

        // ---------------- sections ----------------

        void BuildSections()
        {
            Section(Strings.T("sec.ui"));
            LangRow();

            Section(Strings.T("sec.text"));

            StepRow(Strings.T("lbl.titleSize"), _cfg.TitleScale, 0.6, 1.6, 0.05, "0.00", delegate(double v)
            {
                _cfg.TitleScale = v;
                _card.ApplyLook();
            });
            ColorRow(Strings.T("lbl.titleColor"), _cfg.TitleColor, delegate(string v)
            {
                _cfg.TitleColor = v;
                _card.ApplyLook();
            });

            StepRow(Strings.T("lbl.artistSize"), _cfg.ArtistScale, 0.6, 1.6, 0.05, "0.00", delegate(double v)
            {
                _cfg.ArtistScale = v;
                _card.ApplyLook();
            });
            StepRow(Strings.T("lbl.artistMax"), _cfg.ArtistMaxChars, 4, 40, 1, "0", delegate(double v)
            {
                _cfg.ArtistMaxChars = (int)v;
                _card.ApplyLook();
            });
            ColorRow(Strings.T("lbl.artistColor"), _cfg.ArtistColor, delegate(string v)
            {
                _cfg.ArtistColor = v;
                _card.ApplyLook();
            });

            StepRow(Strings.T("lbl.lyricSize"), _cfg.LyricScale, 0.6, 1.6, 0.05, "0.00", delegate(double v)
            {
                _cfg.LyricScale = v;
                _card.ApplyLook();
            });
            ColorRow(Strings.T("lbl.lyricColor"), _cfg.LyricColor, delegate(string v)
            {
                _cfg.LyricColor = v;
                _card.ApplyLook();
            });

            Section(Strings.T("sec.bar"));

            StepRow(Strings.T("lbl.barScale"), _cfg.BarScale, 0.5, 3.0, 0.05, "0%", delegate(double v)
            {
                _cfg.BarScale = v;
                _card.ApplyLook();
            });
            StepRow(Strings.T("lbl.barThickness"), _cfg.BarThickness, 2, 8, 0.5, "0.0", delegate(double v)
            {
                _cfg.BarThickness = v;
                _card.ApplyLook();
            });
            ColorRow(Strings.T("lbl.barColor"), _cfg.BarColor, delegate(string v)
            {
                _cfg.BarColor = v;
                _card.ApplyLook();
            });
            ColorRow(Strings.T("lbl.barFill"), _cfg.BarFillColor, delegate(string v)
            {
                _cfg.BarFillColor = v;
                _card.ApplyLook();
            });

            Section(Strings.T("sec.art"));

            StepRow(Strings.T("lbl.artSize"), _cfg.ArtSize, 56, 160, 4, "0", delegate(double v)
            {
                _cfg.ArtSize = v;
                _card.ApplyLook();
            });
            StepRow(Strings.T("lbl.artSat"), _cfg.ArtSat, 0, 1, 0.05, "0.00", delegate(double v) { _cfg.ArtSat = v; _card.RefreshArt(); });
            StepRow(Strings.T("lbl.artHiCut"), _cfg.ArtHiCut, 0, 0.4, 0.02, "0.00", delegate(double v) { _cfg.ArtHiCut = v; _card.RefreshArt(); });
            ToggleRow(Strings.T("lbl.artShadow"), _cfg.ArtShadow, delegate(bool v)
            {
                _cfg.ArtShadow = v;
                _card.RefreshArt();
            });
            ToggleRow(Strings.T("lbl.textShadow"), _cfg.TextShadow, delegate(bool v)
            {
                _cfg.TextShadow = v;
                _card.ApplyBackdropNow();
            });

            Section(Strings.T("sec.card"));

            StepRow(Strings.T("lbl.scale"), _cfg.Scale, 0.55, 2.2, 0.05, "0.00", delegate(double v)
            {
                _cfg.Scale = v;
                _card.ApplyCurrentScale();
            });
            StepRow(Strings.T("lbl.opacity"), _cfg.Opacity, 0.25, 1.0, 0.05, "0.00", delegate(double v)
            {
                _cfg.Opacity = v;
                _card.ApplyCurrentOpacity();
            });
            ColorRow(Strings.T("lbl.controlsColor"), _cfg.ControlsColor, delegate(string v)
            {
                _cfg.ControlsColor = v;
                _card.ApplyLook();
            });
            ToggleRow(Strings.T("lbl.clearBg"), _cfg.ClearBg, delegate(bool v) { _card.SetClearBg(v); });
            ToggleRow(Strings.T("lbl.spin"), _cfg.ArtSpin, delegate(bool v) { _card.SetArtSpin(v); });

            Section(Strings.T("sec.behavior"));
            ToggleRow(Strings.T("lbl.autostart"), _cfg.AutoStart, delegate(bool v)
            {
                _cfg.AutoStart = v;
                AutoStart.Set(v);
            });
            ToggleRow(Strings.T("lbl.listenAll"), _cfg.ListenAll, delegate(bool v) { _card.SetListenAll(v); });

            Section(Strings.T("sec.presets"));

            var saveRow = new Grid { Margin = new Thickness(6, 4, 6, 2) };
            saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBox = new TextBox
            {
                Height = 30,
                FontSize = 12.5,
                Foreground = B(TextMain),
                Background = B(Color.FromArgb(255, 26, 26, 31)),
                BorderBrush = B(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 0, 9, 2),
                CaretBrush = B(TextMain),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = Strings.T("preset.prompt")
            };
            Grid.SetColumn(nameBox, 0);
            saveRow.Children.Add(nameBox);

            var save = SmallButton(Strings.T("preset.save"), delegate
            {
                string nm = (nameBox.Text == null) ? "" : nameBox.Text.Trim();
                if (nm.Length == 0) nm = Strings.F("preset.defaultName", _cfg.Presets.Count + 1);
                if (nm.Length > 14) nm = nm.Substring(0, 14);
                if (_cfg.Presets.Count >= 10) _cfg.Presets.RemoveAt(0);
                _cfg.Presets.Add(_cfg.CaptureLook(nm));
                _cfg.Save();
                Rebuild();
            }, 76);
            save.Height = 30;
            Grid.SetColumn(save, 1);
            saveRow.Children.Add(save);
            _body.Children.Add(saveRow);

            var wrap = new WrapPanel { Margin = new Thickness(6, 4, 6, 2) };
            if (_cfg.Presets.Count == 0)
            {
                var hint = new TextBlock
                {
                    Text = Strings.T("preset.empty"),
                    FontSize = 11.5,
                    Foreground = B(TextDim),
                    Margin = new Thickness(0, 4, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                };
                wrap.Children.Add(hint);
            }
            for (int i = 0; i < _cfg.Presets.Count; i++)
            {
                wrap.Children.Add(PresetChip(_cfg.Presets[i]));
            }
            _body.Children.Add(wrap);
        }

        // one saved look: click the name to apply it, click the × to delete it
        Border PresetChip(Config.LookPreset p)
        {
            var b = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = B(AccentBg),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(12, 0, 8, 0)
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock
            {
                Text = p.Name,
                FontSize = 12,
                Foreground = B(TextMain),
                VerticalAlignment = VerticalAlignment.Center
            };
            var del = new TextBlock
            {
                Text = "\u00d7",
                FontSize = 14,
                Foreground = B(TextDim),
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            sp.Children.Add(name);
            sp.Children.Add(del);
            b.Child = sp;

            b.MouseEnter += delegate { b.Background = B(HoverBg); };
            b.MouseLeave += delegate { b.Background = B(AccentBg); };

            del.MouseLeftButtonDown += delegate
            {
                _cfg.Presets.Remove(p);
                _cfg.Save();
                Rebuild();
            };
            b.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                if (del.IsMouseOver) { args.Handled = true; return; }   // the × has its own handler
                _cfg.ApplyLookPreset(p);
                PushLook();
                Rebuild();
            };
            return b;
        }

        // push the current config values onto the live card
        void Rebuild()
        {
            _body.Children.Clear();
            BuildSections();
        }

        void PushLook()
        {
            _card.ApplyLook();
            _card.ApplyCurrentScale();
            _card.ApplyCurrentOpacity();
            _card.SetClearBg(_cfg.ClearBg);
            _card.SetArtSpin(_cfg.ArtSpin);
            _card.RefreshArt();
        }


        void BuildFooter(StackPanel stack)
        {
            var foot = new Grid { Margin = new Thickness(16, 12, 16, 16) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hint = new TextBlock
            {
                Text = Version + " · " + Strings.T("footer.live"),
                FontSize = 11,
                Foreground = B(TextDim),
                VerticalAlignment = VerticalAlignment.Center
            };
            _footHint = hint;
            foot.Children.Add(hint);

            var done = new Border
            {
                Height = 32,
                Width = 88,
                CornerRadius = new CornerRadius(8),
                Background = B(Color.FromArgb(255, 118, 176, 255)),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = Strings.T("btn.done"),
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = B(Color.FromArgb(255, 12, 16, 22)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            done.MouseLeftButtonDown += delegate
            {
                _cfg.Save();
                Close();
            };
            _btnDoneText = (TextBlock)done.Child;
            done.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(done, 2);
            foot.Children.Add(done);

            var reset = new Border
            {
                Height = 32,
                Width = 88,
                CornerRadius = new CornerRadius(8),
                Background = B(AccentBg),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = Strings.T("btn.reset"),
                    FontSize = 12.5,
                    Foreground = B(TextMain),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            _btnResetText = (TextBlock)reset.Child;
            reset.MouseLeftButtonDown += delegate
            {
                // back to the owner's tuned look, not some invented "factory" state
                _cfg.ResetLookDefaults();
                PushLook();
                Rebuild();
            };
            Grid.SetColumn(reset, 1);
            foot.Children.Add(reset);

            stack.Children.Add(foot);
        }
    }
}
