using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Amc
{
    // Dark, see-through context menu. Built in code (no XAML) because the whole card is
    // code-built, and because the default WPF menu is a light system-chrome panel that looks
    // pasted on next to a borderless desktop widget.
    /// <summary>
    /// Dark, rounded, semi-transparent restyle for the card's ContextMenu, built in code because
    /// the project has no XAML. Every colour is a frozen brush so the popup stays cheap to redraw.
    /// </summary>
    static class MenuTheme
    {
        static readonly Brush MenuBg = Freeze(Color.FromArgb(214, 16, 16, 20));
        static readonly Brush MenuEdge = Freeze(Color.FromArgb(34, 255, 255, 255));
        static readonly Brush ItemText = Freeze(Color.FromArgb(246, 246, 247, 249));
        static readonly Brush ItemTextDim = Freeze(Color.FromArgb(198, 222, 224, 228));
        static readonly Brush Hover = Freeze(Color.FromArgb(30, 255, 255, 255));
        static readonly Brush RuleLine = Freeze(Color.FromArgb(26, 255, 255, 255));

        static Style _menuStyle;
        static Style _itemStyle;
        static Style _sepStyle;

        static Brush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static void Apply(ContextMenu m)
        {
            m.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            m.FontSize = 12.5;
            m.Style = MenuStyle();
            foreach (object o in m.Items)
            {
                var mi = o as MenuItem;
                if (mi != null) { mi.Style = ItemStyle(); continue; }
                var sp = o as Separator;
                if (sp != null) sp.Style = SeparatorStyle();
            }
        }

        static Style MenuStyle()
        {
            if (_menuStyle != null) return _menuStyle;

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, MenuBg);
            border.SetValue(Border.BorderBrushProperty, MenuEdge);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.PaddingProperty, new Thickness(0, 6, 0, 6));
            border.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

            var host = new FrameworkElementFactory(typeof(ItemsPresenter));
            border.AppendChild(host);

            var tmpl = new ControlTemplate(typeof(ContextMenu));
            tmpl.VisualTree = border;

            var st = new Style(typeof(ContextMenu));
            st.Setters.Add(new Setter(Control.TemplateProperty, tmpl));
            st.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            st.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            st.Setters.Add(new Setter(Control.MinWidthProperty, 208.0));
            _menuStyle = st;
            return st;
        }

        static Style SeparatorStyle()
        {
            if (_sepStyle != null) return _sepStyle;

            var line = new FrameworkElementFactory(typeof(Border));
            line.SetValue(FrameworkElement.HeightProperty, 1.0);
            line.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 6, 14, 6));
            line.SetValue(Border.BackgroundProperty, RuleLine);

            var tmpl = new ControlTemplate(typeof(Separator));
            tmpl.VisualTree = line;

            var st = new Style(typeof(Separator));
            st.Setters.Add(new Setter(Control.TemplateProperty, tmpl));
            _sepStyle = st;
            return st;
        }

        static Style ItemStyle()
        {
            if (_itemStyle != null) return _itemStyle;

            var row = new FrameworkElementFactory(typeof(Border), "Bg");
            row.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            row.SetValue(Border.MarginProperty, new Thickness(6, 0, 6, 0));
            row.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            row.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 14, 0));
            grid.SetValue(FrameworkElement.MinHeightProperty, 29.0);
            row.AppendChild(grid);

            var colCheck = new FrameworkElementFactory(typeof(ColumnDefinition));
            colCheck.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            grid.AppendChild(colCheck);
            var colText = new FrameworkElementFactory(typeof(ColumnDefinition));
            colText.SetValue(ColumnDefinition.WidthProperty, new GridLength(1.0, GridUnitType.Star));
            grid.AppendChild(colText);

            var check = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path), "Check");
            check.SetValue(System.Windows.Shapes.Path.DataProperty,
                Geometry.Parse("M 0,4.4 L 3.4,7.8 L 9.4,0.8"));
            check.SetValue(System.Windows.Shapes.Shape.StrokeProperty, ItemText);
            check.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.7);
            check.SetValue(System.Windows.Shapes.Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            check.SetValue(System.Windows.Shapes.Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            check.SetValue(System.Windows.Shapes.Shape.StrokeLineJoinProperty, PenLineJoin.Round);
            check.SetValue(FrameworkElement.WidthProperty, 10.0);
            check.SetValue(FrameworkElement.HeightProperty, 9.0);
            check.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 9, 0));
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
            check.SetValue(Grid.ColumnProperty, 0);
            grid.AppendChild(check);

            var hdr = new FrameworkElementFactory(typeof(ContentPresenter), "Hdr");
            hdr.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            hdr.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            hdr.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            hdr.SetValue(TextElement.ForegroundProperty, ItemTextDim);
            hdr.SetValue(TextElement.FontSizeProperty, 12.5);
            hdr.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(hdr);

            var tmpl = new ControlTemplate(typeof(MenuItem));
            tmpl.VisualTree = row;

            var hovered = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            hovered.Setters.Add(Named(Border.BackgroundProperty, "Bg", Hover));
            hovered.Setters.Add(Named(TextElement.ForegroundProperty, "Hdr", ItemText));
            tmpl.Triggers.Add(hovered);

            var checkedT = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            checkedT.Setters.Add(Named(UIElement.VisibilityProperty, "Check", Visibility.Visible));
            checkedT.Setters.Add(Named(TextElement.ForegroundProperty, "Hdr", ItemText));
            tmpl.Triggers.Add(checkedT);

            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(Named(UIElement.OpacityProperty, "Bg", 0.38));
            tmpl.Triggers.Add(off);

            var st = new Style(typeof(MenuItem));
            st.Setters.Add(new Setter(Control.TemplateProperty, tmpl));
            st.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            st.Setters.Add(new Setter(Control.ForegroundProperty, ItemTextDim));
            st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            st.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            _itemStyle = st;
            return st;
        }

        static Setter Named(DependencyProperty prop, string target, object value)
        {
            var s = new Setter(prop, value);
            s.TargetName = target;
            return s;
        }
    }
}
