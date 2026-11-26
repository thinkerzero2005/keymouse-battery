using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KeyMouseBattery.Devices;
using KeyMouseBattery.Services;

namespace KeyMouseBattery.UI
{
    /// <summary>主面板：两张设备卡片 + 常用设置。</summary>
    internal sealed class MainWindow : Window
    {
        private static readonly int[] ThresholdOptions = new int[] { 10, 15, 20, 30 };

        private readonly Settings _settings;
        private readonly BatteryMonitor _monitor;
        private readonly DeviceCard _mouseCard;
        private readonly DeviceCard _keyboardCard;
        private readonly TextBlock _intervalText;
        private readonly TextBlock _lastPollText;
        private readonly Border _thresholdPill;
        private readonly TextBlock _thresholdText;
        private readonly Border _autoStartPill;
        private readonly TextBlock _autoStartText;
        private readonly Border _refreshPill;
        private readonly TextBlock _refreshText;

        private bool _allowClose;

        public event EventHandler SettingsChanged;

        public MainWindow(Settings settings, BatteryMonitor monitor)
        {
            _settings = settings;
            _monitor = monitor;

            Title = "外设电量";
            Width = 420;
            Height = 470;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
            UseLayoutRounding = true;

            var shell = new Border
            {
                Margin = new Thickness(20),
                CornerRadius = new CornerRadius(16),
                Background = Palette.White,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 3,
                    Direction = 270,
                    Opacity = 0.16
                }
            };
            Content = shell;

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.Child = layout;

            // 标题栏
            _intervalText = new TextBlock
            {
                Text = "每 60 秒自动刷新",
                FontSize = 10.5,
                Foreground = Palette.TextTertiary,
                VerticalAlignment = VerticalAlignment.Center
            };
            _lastPollText = new TextBlock
            {
                Text = "尚未刷新",
                FontSize = 10.5,
                Foreground = Palette.TextTertiary,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            layout.Children.Add(BuildTitleBar());

            // 设备卡片
            var content = new StackPanel { Margin = new Thickness(20, 2, 20, 0) };
            _mouseCard = new DeviceCard("罗技 G304", "2.4G 无线");
            _mouseCard.Margin = new Thickness(0, 0, 0, 10);
            _keyboardCard = new DeviceCard("VGN N75 Pro", "2.4G 无线");
            content.Children.Add(_mouseCard);
            content.Children.Add(_keyboardCard);

            var footInfo = new Grid { Margin = new Thickness(2, 12, 2, 0) };
            footInfo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footInfo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_intervalText, 0);
            Grid.SetColumn(_lastPollText, 1);
            footInfo.Children.Add(_intervalText);
            footInfo.Children.Add(_lastPollText);
            content.Children.Add(footInfo);

            Grid.SetRow(content, 1);
            layout.Children.Add(content);

            // 底部设置
            var footer = new Border
            {
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 13, 20, 14),
                Margin = new Thickness(0, 14, 0, 0)
            };

            var footerStack = new StackPanel();

            var settingsRow = new StackPanel { Orientation = Orientation.Horizontal };
            settingsRow.Children.Add(new TextBlock
            {
                Text = "低电量提醒",
                FontSize = 12,
                Foreground = Palette.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            });
            TextBlock thresholdLabel;
            _thresholdPill = CreatePill("20%", Palette.AccentSoft, Palette.Accent, out thresholdLabel);
            _thresholdText = thresholdLabel;
            _thresholdPill.Margin = new Thickness(8, 0, 0, 0);
            _thresholdPill.ToolTip = "点击切换提醒阈值";
            _thresholdPill.MouseLeftButtonUp += delegate { CycleThreshold(); };
            settingsRow.Children.Add(_thresholdPill);

            settingsRow.Children.Add(new Border { Width = 18 });
            settingsRow.Children.Add(new TextBlock
            {
                Text = "开机自启",
                FontSize = 12,
                Foreground = Palette.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            });
            TextBlock autoStartLabel;
            _autoStartPill = CreatePill("已开启", Palette.GreenSoft, Palette.Green, out autoStartLabel);
            _autoStartText = autoStartLabel;
            _autoStartPill.Margin = new Thickness(8, 0, 0, 0);
            _autoStartPill.MouseLeftButtonUp += delegate { ToggleAutoStart(); };
            settingsRow.Children.Add(_autoStartPill);
            footerStack.Children.Add(settingsRow);

            var actionRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hint = new TextBlock
            {
                Text = "关闭窗口后继续在托盘运行",
                FontSize = 10.5,
                Foreground = Palette.TextTertiary,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock refreshLabel;
            _refreshPill = CreatePill("立即刷新", Palette.Dark, Palette.White, out refreshLabel);
            _refreshText = refreshLabel;
            _refreshPill.Padding = new Thickness(12, 5, 12, 6);
            _refreshPill.Cursor = Cursors.Hand;
            _refreshPill.MouseLeftButtonUp += delegate { _monitor.RefreshNow(); };

            Grid.SetColumn(hint, 0);
            Grid.SetColumn(_refreshPill, 1);
            actionRow.Children.Add(hint);
            actionRow.Children.Add(_refreshPill);
            footerStack.Children.Add(actionRow);

            footer.Child = footerStack;
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);

            UpdateThresholdPill();
            UpdateAutoStartPill();

            _monitor.Updated += delegate
            {
                Dispatcher.BeginInvoke(new Action(RefreshState));
            };

            RefreshState();
        }

        public void ShowPanel()
        {
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;

            BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150)));
        }

        public void AllowClose()
        {
            _allowClose = true;
        }

        /// <summary>根据当前配置与读数刷新界面（也可能由托盘菜单触发）。</summary>
        public void RefreshState()
        {
            _mouseCard.Apply(_monitor.Mouse, _settings.Threshold);
            _keyboardCard.Apply(_monitor.Keyboard, _settings.Threshold);

            _intervalText.Text = string.Format("每 {0} 秒自动刷新", _settings.IntervalSeconds);
            _lastPollText.Text = _monitor.LastPoll.Year > 2000
                ? "上次更新 " + _monitor.LastPoll.ToString("HH:mm:ss")
                : "尚未刷新";

            _refreshText.Text = _monitor.IsPolling ? "刷新中…" : "立即刷新";
            _refreshPill.Opacity = _monitor.IsPolling ? 0.6 : 1.0;

            UpdateThresholdPill();
            UpdateAutoStartPill();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }
            base.OnClosing(e);
        }

        private UIElement BuildTitleBar()
        {
            var bar = new Grid { Height = 50, Background = Brushes.Transparent, Cursor = Cursors.Arrow };
            bar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); }
                    catch (InvalidOperationException) { }
                }
            };

            var mark = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(8),
                Background = Palette.Accent,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0)
            };
            mark.Child = new Viewbox
            {
                Margin = new Thickness(7),
                Child = new Path
                {
                    Data = Geometry.Parse("M 1,3 H 13 V 11 H 1 Z M 14,5 H 16 V 9 H 14 Z"),
                    Fill = Palette.White
                }
            };

            var titles = new StackPanel
            {
                Margin = new Thickness(48, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titles.Children.Add(new TextBlock
            {
                Text = "外设电量",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Palette.TextPrimary
            });
            titles.Children.Add(new TextBlock
            {
                Text = "罗技 G304 · VGN N75 Pro",
                FontSize = 10.5,
                Foreground = Palette.TextTertiary,
                Margin = new Thickness(0, 1, 0, 0)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 14, 12, 0)
            };
            buttons.Children.Add(CreateWindowButton("\u2013", 14, delegate { HideToTray(); }));
            buttons.Children.Add(CreateWindowButton("\u2715", 11, delegate { HideToTray(); }));

            bar.Children.Add(mark);
            bar.Children.Add(titles);
            bar.Children.Add(buttons);
            return bar;
        }

        private UIElement CreateWindowButton(string glyph, double size, Action onClick)
        {
            var label = new TextBlock
            {
                Text = glyph,
                FontSize = size,
                Foreground = Palette.TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var button = new Border
            {
                Width = 28,
                Height = 24,
                CornerRadius = new CornerRadius(7),
                Background = Brushes.Transparent,
                Child = label,
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 0, 0)
            };
            // 阻止按下事件冒泡到标题栏，否则会触发 DragMove 并吞掉随后的点击
            button.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; };
            button.MouseEnter += delegate { button.Background = Palette.Hover; };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            button.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                onClick();
            };
            return button;
        }

        private static Border CreatePill(string text, Brush background, Brush foreground, out TextBlock label)
        {
            label = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = foreground,
                FontWeight = FontWeights.Medium
            };
            return new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 4, 9, 5),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = label
            };
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void CycleThreshold()
        {
            int current = _settings.Threshold;
            int next = ThresholdOptions[0];
            for (int i = 0; i < ThresholdOptions.Length; i++)
            {
                if (ThresholdOptions[i] == current)
                {
                    next = ThresholdOptions[(i + 1) % ThresholdOptions.Length];
                    break;
                }
            }

            _settings.Threshold = next;
            _settings.Save();
            UpdateThresholdPill();
            RefreshState();
            RaiseSettingsChanged();
        }

        private void ToggleAutoStart()
        {
            bool enable = !AutoStart.IsEnabled();
            AutoStart.SetEnabled(enable);
            _settings.AutoStart = enable;
            _settings.Save();
            UpdateAutoStartPill();
            RaiseSettingsChanged();
        }

        private void UpdateThresholdPill()
        {
            _thresholdText.Text = _settings.Threshold + "%";
        }

        private void UpdateAutoStartPill()
        {
            bool enabled = AutoStart.IsEnabled();
            _autoStartText.Text = enabled ? "已开启" : "已关闭";
            _autoStartPill.Background = enabled ? Palette.GreenSoft : Palette.Chip;
            _autoStartText.Foreground = enabled ? Palette.Green : Palette.TextSecondary;
        }

        private void RaiseSettingsChanged()
        {
            EventHandler handler = SettingsChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
