// ---------------------------------------------------------------------------
// CandidateWindow.cs
//
/// The lyric picker: shows what each source returned for the current track, scored and
/// ranked, so the user can override a bad match in one click.
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
    /// <summary>
    /// The manual lyric picker: every candidate the searchers returned, with the match score that
    /// the matcher used. The card itself can only show one line, so a wrong pick is invisible
    /// there; this window makes the choice, and the reason for it, explicit.
    /// </summary>
    public class CandidateWindow : Window
    {
        readonly StackPanel _list = new StackPanel();
        readonly TextBlock _status = new TextBlock();
        public Action<LyricChoice> Pick;

        static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(19, 19, 22));
        static readonly Brush Edge = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255));
        static readonly Brush Main = new SolidColorBrush(Color.FromRgb(242, 242, 247));
        static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(150, 152, 160));

        public CandidateWindow(string title, string subtitle)
        {
            Title = title;
            Width = 470;
            Height = 430;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            // The card is a topmost, non-activating window; a plain window opened from it can end
            // up behind whatever the user is working in, so keep the picker on top and focus it.
            Topmost = true;
            // Borderless: the native caption would clash with the dark single-colour card style.
            // Dragging is handled by the header row below.
            WindowStyle = WindowStyle.None;
            Background = Bg;
            SnapsToDevicePixels = true;

            var root = new Border
            {
                Background = Bg,
                BorderBrush = Edge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0)
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(root);
            Grid.SetRowSpan(root, 3);

            var head = new StackPanel { Margin = new Thickness(20, 16, 20, 6) };
            var line = new Grid();
            var t = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Main,
                VerticalAlignment = VerticalAlignment.Center
            };
            line.Children.Add(t);
            var close = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand,
                Child = new Path
                {
                    Data = Geometry.Parse("M0,0 L8,8 M8,0 L0,8"),
                    Stroke = Dim,
                    StrokeThickness = 1.4,
                    Stretch = Stretch.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            // DragMove captures the mouse until the button comes up, which swallows exactly the Up
            // event this button waits for -- so the X did nothing on the small windows. Take the
            // press in the tunnel phase, close right there, and hand nothing down to the title bar.
            close.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Close();
            };
            line.Children.Add(close);
            head.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            head.Children.Add(line);
            head.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11.5,
                Foreground = Dim,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            _status.Text = Strings.T("cand.loading");
            _status.FontSize = 11.5;
            _status.Foreground = Dim;
            _status.Margin = new Thickness(0, 8, 0, 0);
            head.Children.Add(_status);
            Grid.SetRow(head, 0);
            grid.Children.Add(head);

            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(12, 8, 12, 14),
                Content = _list
            };
            Grid.SetRow(scroller, 2);
            grid.Children.Add(scroller);

            Content = grid;
        }

        public void SetCandidates(List<LyricChoice> list)
        {
            _list.Children.Clear();
            if (list == null || list.Count == 0)
            {
                _status.Text = Strings.T("cand.empty");
                return;
            }
            _status.Text = Strings.F("cand.count", list.Count);
            foreach (LyricChoice c in list) _list.Children.Add(Row(c));
        }

        UIElement Row(LyricChoice c)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int pct = (int)Math.Round(c.Score * 100);
            // Colour bands say the same thing the log scores say: green is a confident match,
            // amber is "usable but check it", red means we would normally not take it at all.
            Color band = c.Score >= 0.88 ? Color.FromRgb(63, 185, 80)
                       : c.Score >= 0.70 ? Color.FromRgb(219, 171, 74)
                       : Color.FromRgb(196, 90, 84);
            var badge = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromArgb(38, band.R, band.G, band.B)),
                Child = new TextBlock
                {
                    Text = pct.ToString(CultureInfo.InvariantCulture) + "%",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(band)
                }
            };
            g.Children.Add(badge);

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = c.Title,
                FontSize = 12.5,
                Foreground = Main,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            string sub = (string.IsNullOrEmpty(c.Artist) ? Strings.T("cand.unknownArtist") : c.Artist)
                + "  ·  " + Fmt(c.Dur) + "  ·  " + c.Source.ToUpperInvariant();
            text.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 10.5,
                Foreground = Dim,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            row.Child = g;

            row.MouseEnter += delegate { row.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)); };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.MouseLeftButtonUp += delegate
            {
                if (Pick != null) Pick(c);
                Close();
            };
            return row;
        }

        static string Fmt(double seconds)
        {
            if (seconds <= 1) return "--:--";
            int s = (int)Math.Round(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
