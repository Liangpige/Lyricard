using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Amc
{
    /// <summary>
    /// Stand-alone preview of the UI kit (`--uikit`). It exists so the theme and the control
    /// factory can be looked at without touching the real pages, and so palette changes can be
    /// judged side by side. Nothing here is used by the production UI.
    /// </summary>
    public sealed class UiKitDemo : Window
    {
        UiTheme _t;
        StackPanel _page;
        Border _shell;
        Border _bar;
        TextBlock _themeName;
        TextBlock _status;
        int _demoState;
        bool _isDark = true;

        public UiKitDemo()
        {
            _t = UiTheme.Dark();
            Title = "Lyricard · UI kit";
            Width = 720;
            Height = 640;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Build();
        }

        void Build()
        {
            _shell = new Border();
            _shell.Background = _t.Brush(_t.Window);
            _shell.BorderBrush = _t.Brush(_t.Border);
            _shell.BorderThickness = new Thickness(1);
            Content = _shell;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _shell.Child = grid;

            // ---- header: drag to move, X to close (Preview, so DragMove cannot eat the click) ----
            var head = new Border();
            head.Height = 46;
            head.Background = _t.Brush(_t.Window);
            head.BorderBrush = _t.Brush(_t.Border);
            head.BorderThickness = new Thickness(0, 0, 0, 1);
            // Drag from the header background only. Dragging from the whole header swallowed the
            // mouse-up that the theme picker needs -- the same way DragMove ate the close button in
            // the picker window.
            head.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (!ReferenceEquals(e.OriginalSource, head)) return;
                try { DragMove(); } catch (Exception) { }
            };
            grid.Children.Add(head);

            var headRow = new Grid();
            headRow.Margin = new Thickness(_t.Space4, 0, _t.Space3, 0);
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Child = headRow;

            var name = UiKit.Label(_t, "UI kit", 13.5, _t.Text, true);
            headRow.Children.Add(name);

            _themeName = UiKit.Label(_t, "主题", _t.FontCaption, _t.TextTertiary, false);
            _themeName.Margin = new Thickness(_t.Space3, 0, _t.Space2, 0);
            _themeName.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_themeName, 1);
            headRow.Children.Add(_themeName);

            // A two-segment picker rather than a switch: the switch shares its click with the
            // header's drag area and fired twice when the tree was rebuilt under the pointer.
            var sw = UiKit.Segmented(_t, new string[] { "深色", "浅色" }, _isDark ? 0 : 1,
                                     delegate(int pick)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    Guard(delegate
                    {
                        _isDark = pick == 0;
                        _t = _isDark ? UiTheme.Dark() : UiTheme.Light();
                        Build();
                    });
                }), System.Windows.Threading.DispatcherPriority.Background);
            });
            sw.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(sw, 2);
            headRow.Children.Add(sw);

            var close = UiKit.Label(_t, "\u00d7", 16, _t.TextTertiary, false);
            close.Cursor = Cursors.Hand;
            close.Margin = new Thickness(_t.Space4, 0, 0, 1);
            close.VerticalAlignment = VerticalAlignment.Center;
            close.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Close();
            };
            Grid.SetColumn(close, 3);
            headRow.Children.Add(close);

            // ---- body ----
            _page = new StackPanel();
            _page.Margin = new Thickness(_t.Space5, _t.Space4, _t.Space5, _t.Space5);
            var scroller = UiKit.Scroll(_t, _page);
            Grid.SetRow(scroller, 1);
            grid.Children.Add(scroller);

            var statusBar = new Border();
            statusBar.Padding = new Thickness(_t.Space4, 6, _t.Space4, 7);
            statusBar.BorderBrush = _t.Brush(_t.Border);
            statusBar.BorderThickness = new Thickness(0, 1, 0, 0);
            _status = UiKit.Label(_t, "就绪：点一点看看，出错会在这里显示", _t.FontCaption, _t.TextTertiary, false);
            _status.TextTrimming = TextTrimming.CharacterEllipsis;
            statusBar.Child = _status;
            Grid.SetRow(statusBar, 2);
            grid.Children.Add(statusBar);

            var cards = new Border[]
            {
                ButtonsCard(), SwitchesCard(), InputsCard(), RowsCard(), MotionCard()
            };
            for (int i = 0; i < cards.Length; i++) _page.Children.Add(cards[i]);

            // Stagger the cards by 45 ms: the eye reads them in order instead of seeing one slab
            // fade in as a block.
            UiMotion.Enter(_page, _t.MotionSlow + 120, 10);
            for (int i = 0; i < cards.Length; i++)
            {
                UiMotion.Enter(cards[i], _t.MotionSlow, 12, 60 + i * 45);
            }
        }

        Border ButtonsCard()
        {
            var row = new WrapPanel();
            var primary = UiKit.Button(_t, "保存设置", UiButtonKind.Primary, delegate { Bump(); });
            var ghost = UiKit.Button(_t, "取消", UiButtonKind.Ghost, delegate { Bump(); });
            var quiet = UiKit.Button(_t, "更多", UiButtonKind.Quiet, delegate { Bump(); });
            var danger = UiKit.Button(_t, "清除缓存", UiButtonKind.Danger, delegate { Bump(); });
            primary.Margin = new Thickness(0, 0, 8, 8);
            ghost.Margin = new Thickness(0, 0, 8, 8);
            quiet.Margin = new Thickness(0, 0, 8, 8);
            row.Children.Add(primary);
            row.Children.Add(ghost);
            row.Children.Add(quiet);
            row.Children.Add(danger);
            return UiKit.Card(_t, "按钮", "按下有缩放与涟漪，悬停渐变 " + _t.MotionFast + " ms", row);
        }

        Border SwitchesCard()
        {
            var stack = new StackPanel();
            stack.Children.Add(Row("开机自动启动", UiKit.Switch(_t, true, delegate { Bump(); })));
            stack.Children.Add(Row("有窗口全屏时隐藏卡片", UiKit.Switch(_t, false, delegate { Bump(); })));
            stack.Children.Add(Row("点击穿透（屏蔽卡片交互）", UiKit.Switch(_t, false, delegate { Bump(); })));
            return UiKit.Card(_t, "开关", "轨道变色 + 圆钮滑动 " + _t.MotionBase + " ms", stack);
        }

        Grid Row(string label, UIElement control)
        {
            var g = new Grid();
            g.Margin = new Thickness(0, 3, 0, 3);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tb = UiKit.Label(_t, label, _t.FontBody, _t.TextSecondary, false);
            g.Children.Add(tb);
            // label left, control right -- the way the real settings rows read
            var fe = control as FrameworkElement;
            if (fe != null) fe.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        Border InputsCard()
        {
            var stack = new StackPanel();

            var seg = UiKit.Segmented(_t, new string[] { "跟随系统", "中文", "English" }, 0, delegate { Bump(); });
            seg.Margin = new Thickness(0, 0, 0, _t.Space3);
            stack.Children.Add(seg);

            var step = UiKit.Stepper(_t, 3.0, 1.0, 12.0, 0.5, "0.0", delegate { Bump(); });
            step.Margin = new Thickness(0, 0, 0, _t.Space3);
            stack.Children.Add(Row("进度条粗细", step));

            var search = UiKit.Search(_t, "搜索歌曲…", delegate { });
            search.Margin = new Thickness(0, 2, 0, _t.Space3);
            stack.Children.Add(search);

            var chips = new WrapPanel();
            chips.Children.Add(UiKit.Chip(_t, "夜间模式", delegate { Bump(); }));
            chips.Children.Add(UiKit.Chip(_t, "Battlefield V Legacy Theme", delegate { Bump(); }));
            stack.Children.Add(chips);

            stack.Children.Add(UiKit.Divider(_t));

            var input = UiKit.Input(_t, "歌单名字…", "", delegate { Bump(); });
            input.Margin = new Thickness(0, _t.Space3, 0, _t.Space3);
            stack.Children.Add(input);

            stack.Children.Add(UiKit.Checkbox(_t, "只在该播放器出声时显示卡片", true, delegate(bool v) { Bump(); _status.Text = "复选 " + (v ? "开" : "关"); }));
            stack.Children.Add(UiKit.Checkbox(_t, "歌词淡出后保留一行", false, delegate(bool v) { Bump(); _status.Text = "复选 " + (v ? "开" : "关"); }));

            var slideValue = UiKit.Label(_t, "0.85", _t.FontCaption, _t.TextTertiary, false);
            var slider = UiKit.Slider(_t, 0.5, 1.5, 0.85, delegate(double v)
            {
                slideValue.Text = v.ToString("0.00");
                UiKit.SetProgress(_bar, (v - 0.5) / 1.0);
                Bump();
            });
            slider.Width = 200;
            var slideRow = new StackPanel();
            slideRow.Orientation = Orientation.Horizontal;
            slideRow.Margin = new Thickness(0, _t.Space3, 0, 0);
            slider.Margin = new Thickness(0, 0, _t.Space3, 0);
            slider.VerticalAlignment = VerticalAlignment.Center;
            slideValue.VerticalAlignment = VerticalAlignment.Center;
            slideRow.Children.Add(slider);
            slideRow.Children.Add(slideValue);
            stack.Children.Add(Row("整体缩放", slideRow));

            _bar = UiKit.ProgressBar(_t, 0.35);
            _bar.Margin = new Thickness(0, _t.Space2, 0, 0);
            stack.Children.Add(_bar);

            stack.Children.Add(UiKit.Divider(_t));
            stack.Children.Add(UiKit.Hint(_t, "分段的高亮块会滑过去；滑杆拖动时进度条跟着走。"));
            return UiKit.Card(_t, "输入与选择", "分段 / 步进 / 搜索 / 滑杆 / 复选 / 进度", stack);
        }

        Border MotionCard()
        {
            var stack = new StackPanel();

            var bar = new Border();
            bar.Height = 6;
            bar.CornerRadius = new CornerRadius(3);
            bar.Background = _t.Brush(_t.SurfaceAlt);
            var fill = new Border();
            fill.Height = 6;
            fill.CornerRadius = new CornerRadius(3);
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.Background = _t.Brush(_t.Solid);
            fill.Width = 120;
            var host = new Grid();
            host.Children.Add(bar);
            host.Children.Add(fill);
            host.Margin = new Thickness(0, 0, 0, _t.Space3);

            var replay = UiKit.Button(_t, "重播入场", UiButtonKind.Primary, delegate
            {
                Guard(delegate
                {
                    _demoState = (_demoState + 1) % 3;
                    var a = new System.Windows.Media.Animation.DoubleAnimation(
                        _demoState == 0 ? 120.0 : (_demoState == 1 ? 260.0 : 380.0),
                        TimeSpan.FromMilliseconds(_t.MotionSlow + 100));
                    a.EasingFunction = UiMotion.Out;
                    fill.BeginAnimation(WidthProperty, a);
                    UiMotion.Enter(stack, _t.MotionSlow, 10);
                });
            });
            replay.HorizontalAlignment = HorizontalAlignment.Left;

            stack.Children.Add(host);
            stack.Children.Add(replay);
            stack.Children.Add(UiKit.Hint(_t, "卡片与页面的入场：淡入 + 上浮 10 px，缓动 cubic-out。"));
            return UiKit.Card(_t, "动效", "同一套缓动常数，" + _t.MotionSlow + " ms 封顶", stack);
        }

        Border RowsCard()
        {
            var stack = new StackPanel();

            var badge = UiKit.Badge(_t, "运行中", UiTone.Ok);
            var sw = UiKit.Switch(_t, true, delegate { Bump(); });
            var tail = new StackPanel();
            tail.Orientation = Orientation.Horizontal;
            badge.Margin = new Thickness(0, 0, _t.Space3, 0);
            tail.Children.Add(badge);
            tail.Children.Add(sw);
            stack.Children.Add(UiKit.ListRow(_t, "Apple Music", "正在播放 · 勋章", tail));

            var sw2 = UiKit.Switch(_t, false, delegate { Bump(); });
            stack.Children.Add(UiKit.ListRow(_t, "Spotify", "已安装，未在播放", sw2));

            var sw3 = UiKit.Switch(_t, false, delegate { Bump(); });
            stack.Children.Add(UiKit.ListRow(_t, "Salt Player", "未检测到", sw3));

            var spin = UiKit.Spinner(_t, 18);
            var spinRow = new StackPanel();
            spinRow.Orientation = Orientation.Horizontal;
            spin.Margin = new Thickness(0, 0, _t.Space2, 0);
            spinRow.Children.Add(spin);
            var spinText = UiKit.Label(_t, "正在取歌词…", _t.FontCaption, _t.TextTertiary, false);
            spinText.VerticalAlignment = VerticalAlignment.Center;
            spinRow.Children.Add(spinText);
            var badgeInfo = UiKit.Badge(_t, "异步", UiTone.Info);
            badgeInfo.Margin = new Thickness(_t.Space2, 0, 0, 0);
            spinRow.Children.Add(badgeInfo);
            stack.Children.Add(UiKit.ListRow(_t, "歌词缓存", "274 首 · 460 KB", spinRow));

            stack.Children.Add(UiKit.Divider(_t));
            stack.Children.Add(UiKit.Notice(_t, UiTone.Info, "提示条：信息用中性色，注意用琥珀，出错用红字，不铺大色块。"));
            stack.Children.Add(UiKit.Notice(_t, UiTone.Warn, "「哔哩哔哩」不会上报播放位置，卡片只能按自己的秒表估。"));
            stack.Children.Add(UiKit.Notice(_t, UiTone.Danger, "取歌词失败：3 个源都没有可用时间轴。"));

            var badgeRow = new WrapPanel();
            badgeRow.Margin = new Thickness(0, _t.Space2, 0, 0);
            badgeRow.Children.Add(UiKit.Badge(_t, "≥ 0.88", UiTone.Ok));
            badgeRow.Children.Add(UiKit.Badge(_t, "0.70+", UiTone.Warn));
            badgeRow.Children.Add(UiKit.Badge(_t, "本地", UiTone.Info));
            var spacer = new Border();
            spacer.Width = _t.Space2;
            badgeRow.Children.Add(spacer);
            badgeRow.Children.Add(UiKit.Badge(_t, "未检测到", UiTone.Danger));
            stack.Children.Add(badgeRow);

            return UiKit.Card(_t, "列表与提示", "行 / 徽章 / 提示条 / 转圈", stack);
        }

        /// <summary>
        /// Anything that touches the visual tree from a click goes through here. A preview window
        /// that dies silently is worse than one that prints its stack trace.
        /// </summary>
        void Guard(Action a)
        {
            try { a(); }
            catch (Exception ex)
            {
                try
                {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uikit_crash.txt"),
                        DateTime.Now.ToString("HH:mm:ss") + " " + ex + "\r\n\r\n");
                }
                catch (Exception) { }
                if (_status != null) _status.Text = "出错：" + ex.GetType().Name + " " + ex.Message;
            }
        }

        void Bump()
        {
            if (_status != null) _status.Text = "已响应一次点击";
            // Touch the readout so a click is visibly acknowledged even though this window has
            // no real state to change.
            Title = "Lyricard · UI kit";
        }
    }
}
