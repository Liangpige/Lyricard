// ---------------------------------------------------------------------------
// ColorLibraryWindow.cs
//
/// The optional palette window: 48 swatches plus a hex field, opened from the ... chip at
/// the end of any colour row.
// ---------------------------------------------------------------------------
// Colour library popup for the Look page.
//
// The row of nine swatches on the Look page covers "the obvious choices"; this window is the
// long tail: a grouped palette plus a hex box for anything else. It deliberately stays dumb --
// it has no idea which config key it is editing, it just hands the chosen "#RRGGBB" (or "" for
// "use the built-in colour") back to the caller.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Amc
{
    public class ColorLibraryWindow : Window
    {
        // Six per row, grouped so the eye can find a family instead of scanning 48 unrelated dots.
        static readonly string[][] Palette = new string[][]
        {
            new string[] { "#FFFFFF", "#F2F3F5", "#C7CBD1", "#8A9099", "#4A4F57", "#000000" },
            new string[] { "#FFE5E5", "#FFB3B3", "#E5484D", "#B02A30", "#FF8A3D", "#C2410C" },
            new string[] { "#FFF3C4", "#FFD166", "#FFB224", "#B7791F", "#C8A165", "#8B5E3C" },
            new string[] { "#E6F7EC", "#8FE3AF", "#3FB950", "#1F7A33", "#A7E8C4", "#2E7D5B" },
            new string[] { "#E0F2FF", "#8CC8FF", "#3B82F6", "#1D4ED8", "#A5B4FC", "#4F46E5" },
            new string[] { "#F3E8FF", "#C4A1FF", "#8B5CF6", "#6D28D9", "#FFC7E5", "#DB2777" },
            new string[] { "#F5EFE6", "#D9CBB8", "#7C8B99", "#46566B", "#6B7F6B", "#3F5D53" },
            new string[] { "#FF6B6B", "#FFA94D", "#FFD43B", "#51CF66", "#22B8CF", "#748FFC" }
        };

        readonly Action<string> _pick;
        WrapPanel _grid;
        readonly TextBox _hex = new TextBox();
        readonly TextBlock _warn = new TextBlock();
        readonly List<Border> _dots = new List<Border>();
        string _current = "";

        ColorLibraryWindow(string title, string current, Action<string> pick)
        {
            _pick = pick;
            _current = current ?? "";

            Title = title;
            Width = 470;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = new SolidColorBrush(Color.FromRgb(19, 19, 22));

            var root = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = Build()
            };
            Content = root;
        }

        /// <summary>Opens the library and reports the choice through <paramref name="pick"/>.
        /// Esc or the × closes it without touching anything.</summary>
        public static void Open(string title, string current, Action<string> pick)
        {
            LyricsService.Log("colour library open for " + title);
            try
            {
                var w = new ColorLibraryWindow(title, current, pick);
                // Setting Owner to a window that has not been shown yet throws, and with
                // --settings the app's "MainWindow" is not always the visible one.
                Window owner = Application.Current.MainWindow;
                if (owner != null && owner.IsLoaded && owner.IsVisible) w.Owner = owner;
                w.WindowStartupLocation = w.Owner == null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner;
                w.Show();
                w.Activate();
                LyricsService.Log("colour library shown " + w.ActualWidth.ToString("0") + "x" + w.ActualHeight.ToString("0") +
                    " at " + w.Left.ToString("0") + "," + w.Top.ToString("0"));
            }
            catch (Exception ex)
            {
                LyricsService.Log("colour library fail " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        UIElement Build()
        {
            var box = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var h = new TextBlock
            {
                Text = Strings.T("color.title"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(236, 238, 242))
            };
            head.Children.Add(h);
            var close = new Border
            {
                Width = 26,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                Child = new TextBlock
                {
                    Text = "\u00d7",
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 204, 210))
                }
            };
            close.MouseLeftButtonUp += delegate { Close(); };
            Grid.SetColumn(close, 1);
            head.Children.Add(close);
            head.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            box.Children.Add(head);

            var sub = new TextBlock
            {
                Text = Strings.T("color.hint"),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 14),
                Foreground = new SolidColorBrush(Color.FromRgb(140, 145, 152))
            };
            box.Children.Add(sub);

            _grid = new WrapPanel();
            foreach (string[] row in Palette)
            {
                foreach (string hex in row) _grid.Children.Add(Swatch(hex));
            }
            box.Children.Add(_grid);

            // hex entry -------------------------------------------------------
            var line = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _hex.Width = 140;
            _hex.Height = 28;
            _hex.Padding = new Thickness(8, 0, 8, 0);
            _hex.VerticalContentAlignment = VerticalAlignment.Center;
            _hex.FontSize = 12;
            _hex.CaretBrush = new SolidColorBrush(Color.FromRgb(236, 238, 242));
            _hex.Foreground = new SolidColorBrush(Color.FromRgb(236, 238, 242));
            _hex.Background = new SolidColorBrush(Color.FromRgb(28, 28, 32));
            _hex.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            _hex.BorderThickness = new Thickness(1);
            _hex.Text = string.IsNullOrEmpty(_current) ? "" : _current;
            _hex.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) ApplyHex();
            };
            line.Children.Add(_hex);

            var ok = Button(Strings.T("color.apply"), true);
            ok.Margin = new Thickness(8, 0, 0, 0);
            ok.MouseLeftButtonUp += delegate { ApplyHex(); };
            Grid.SetColumn(ok, 1);
            line.Children.Add(ok);

            var reset = Button(Strings.T("color.reset"), false);
            reset.Margin = new Thickness(8, 0, 0, 0);
            reset.MouseLeftButtonUp += delegate { Choose(""); };
            Grid.SetColumn(reset, 2);
            line.Children.Add(reset);

            box.Children.Add(line);

            _warn.Text = "";
            _warn.FontSize = 11;
            _warn.Margin = new Thickness(0, 8, 0, 0);
            _warn.Foreground = new SolidColorBrush(Color.FromRgb(196, 90, 84));
            box.Children.Add(_warn);

            RefreshRings();
            return box;
        }

        Border Swatch(string hex)
        {
            Color c;
            if (!Parse(hex, out c)) c = Colors.White;
            var b = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 10, 10),
                Background = new SolidColorBrush(c),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Cursor = Cursors.Hand,
                Tag = hex
            };
            b.MouseLeftButtonUp += delegate { Choose(hex); };
            // a light hover ring reads better than a background highlight on a coloured dot
            b.MouseEnter += delegate { b.Opacity = 0.82; };
            b.MouseLeave += delegate { b.Opacity = 1.0; };
            _dots.Add(b);
            return b;
        }

        Border Button(string text, bool primary)
        {
            var b = new Border
            {
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(primary
                    ? Color.FromRgb(236, 238, 242)
                    : Color.FromArgb(22, 255, 255, 255)),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(primary ? Color.FromRgb(18, 18, 20) : Color.FromRgb(236, 238, 242))
                }
            };
            return b;
        }

        void ApplyHex()
        {
            string t = (_hex.Text ?? "").Trim();
            Color c;
            if (!Parse(t, out c))
            {
                _warn.Text = Strings.T("color.bad");
                return;
            }
            Choose("#" + t.TrimStart('#').ToUpperInvariant());
        }

        void Choose(string hex)
        {
            _current = hex ?? "";
            LyricsService.Log("colour pick " + (_current.Length == 0 ? "(built-in)" : _current));
            _pick(_current);
            Close();
        }

        void RefreshRings()
        {
            foreach (Border b in _dots)
            {
                string hex = b.Tag as string;
                bool on = string.Equals(hex ?? "", _current ?? "", StringComparison.OrdinalIgnoreCase);
                b.BorderThickness = new Thickness(on ? 2 : 1);
                b.BorderBrush = new SolidColorBrush(on
                    ? Color.FromRgb(236, 238, 242)
                    : Color.FromArgb(40, 255, 255, 255));
            }
        }

        /// <summary>#RGB / #RRGGBB / RRGGBB, with or without the hash.</summary>
        public static bool Parse(string text, out Color c)
        {
            c = Colors.White;
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim().TrimStart('#');
            if (t.Length == 3) t = new string(new char[] { t[0], t[0], t[1], t[1], t[2], t[2] });
            if (t.Length != 6) return false;
            int v;
            if (!int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            c = Color.FromRgb((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
            return true;
        }
    }
}
