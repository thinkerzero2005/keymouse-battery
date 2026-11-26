using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KeyMouseBattery.Services;

namespace KeyMouseBattery.UI
{
    /// <summary>使用情况窗口：7 天电量趋势、本周统计与最近事件。</summary>
    internal sealed class StatsWindow : Window
    {
        private readonly HistoryLog _history;
        private readonly Canvas _chart;
        private readonly StackPanel _events;
        private readonly TextBlock _mouseSummary;
        private readonly TextBlock _keyboardSummary;
        private readonly TextBlock _emptyHint;

        private UsageSummary _summary;

        public StatsWindow(HistoryLog history)
        {
            _history = history;

            Title = "使用情况";
            Width = 460;
            Height = 600;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
            UseLayoutRounding = true;

            var shell = new Border
            {
                Margin = new Thickness(18),
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

            layout.Children.Add(BuildTitleBar());

            var body = new StackPanel { Margin = new Thickness(20, 4, 20, 0) };

            // 趋势图
            var chartCard = new Border
            {
                Background = Palette.Card,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10, 12, 10)
            };
            var chartStack = new StackPanel();
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            legend.Children.Add(LegendDot(Palette.Green));
            legend.Children.Add(new TextBlock { Text = "G304", FontSize = 10, Foreground = Palette.TextSecondary, Margin = new Thickness(4, 0, 14, 0) });
            legend.Children.Add(LegendDot(Palette.Accent));
            legend.Children.Add(new TextBlock { Text = "N75 Pro", FontSize = 10, Foreground = Palette.TextSecondary, Margin = new Thickness(4, 0, 0, 0) });
            chartStack.Children.Add(legend);

            var chartHost = new Grid { Height = 118 };
            _chart = new Canvas();
            chartHost.Children.Add(_chart);
            _emptyHint = new TextBlock
            {
                Text = "暂无趋势数据，运行一段时间后自动生成",
                FontSize = 10.5,
                Foreground = Palette.TextTertiary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            chartHost.Children.Add(_emptyHint);
            chartStack.Children.Add(chartHost);

            var axis = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            axis.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            axis.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new TextBlock { Text = "7 天前", FontSize = 9.5, Foreground = Palette.TextTertiary };
            var right = new TextBlock { Text = "现在", FontSize = 9.5, Foreground = Palette.TextTertiary };
            Grid.SetColumn(right, 1);
            axis.Children.Add(left);
            axis.Children.Add(right);
            chartStack.Children.Add(axis);

            chartCard.Child = chartStack;
            body.Children.Add(chartCard);

            // 本周统计
            _mouseSummary = new TextBlock { FontSize = 10.5, Foreground = Palette.TextSecondary, TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
            _keyboardSummary = new TextBlock { FontSize = 10.5, Foreground = Palette.TextSecondary, TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
            body.Children.Add(BuildStatsCard("罗技 G304", _mouseSummary, new Thickness(0, 10, 0, 0)));
            body.Children.Add(BuildStatsCard("VGN N75 Pro", _keyboardSummary, new Thickness(0, 8, 0, 0)));

            // 最近事件
            var eventCard = BuildCard("最近事件", new Thickness(0, 10, 0, 0));
            _events = new StackPanel();
            ((StackPanel)eventCard.Child).Children.Add(_events);
            body.Children.Add(eventCard);

            Grid.SetRow(body, 1);
            layout.Children.Add(body);

            UIElement footer = BuildFooter();
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);
            _chart.SizeChanged += delegate { DrawChart(); };
        }

        public void ShowPanel()
        {
            Reload();
            if (!IsVisible) Show();
            Activate();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150)));
        }

        private void Reload()
        {
            _summary = UsageSummary.Load(_history);
            FillStats(_mouseSummary, _summary.Get("G304"), true);
            FillStats(_keyboardSummary, _summary.Get("N75 Pro"), false);
            FillEvents();
            DrawChart();
        }

        private static void FillStats(TextBlock target, DeviceWeeklyStats stats, bool isMouse)
        {
            if (stats == null || !stats.HasData)
            {
                target.Text = "本周还没有记录";
                return;
            }

            string drain = stats.DrainPerDay > 0.01
                ? "-" + stats.DrainPerDay.ToString("0.0") + "% / 天"
                : "无明显耗电";
            string daysLeft = !double.IsInfinity(stats.DaysLeft) && stats.DaysLeft < 999 && stats.DrainPerDay > 0.01
                ? "约 " + Math.Floor(stats.DaysLeft) + " 天"
                : "—";
            string min = stats.MinPercent >= 0
                ? stats.MinPercent + "%（" + stats.MinAt.ToString("MM-dd HH:mm") + "）"
                : "—";

            string line1 = "当前 " + (stats.LastPercent >= 0 ? stats.LastPercent + "%" : "—")
                + "   ·   日均 " + drain
                + "   ·   预计可用 " + daysLeft;
            string line2 = "最低 " + min + "   ·   异常 " + stats.ErrorCount + " 次   ·   离线 " + stats.OfflineCount + " 次";

            if (isMouse)
            {
                target.Text = line1 + Environment.NewLine + line2;
            }
            else
            {
                string charge = "充电 " + stats.ChargeCount + " 次";
                if (stats.ChargeDuration.TotalMinutes >= 1)
                    charge += "（" + FormatDuration(stats.ChargeDuration) + "）";
                target.Text = line1 + Environment.NewLine + line2 + "   ·   " + charge;
            }
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1) return ((int)span.TotalHours) + " 小时 " + span.Minutes + " 分";
            return Math.Max(1, span.Minutes) + " 分钟";
        }

        private void FillEvents()
        {
            _events.Children.Clear();
            if (_summary == null || _summary.RecentEvents.Count == 0)
            {
                _events.Children.Add(new TextBlock
                {
                    Text = "本周暂无异常或提醒事件",
                    FontSize = 10.5,
                    Foreground = Palette.TextTertiary
                });
                return;
            }

            int shown = 0;
            foreach (LogRow row in _summary.RecentEvents)
            {
                if (shown++ >= 7) break;

                Brush color = Palette.TextSecondary;
                if (row.Event == "读数异常" || row.Event == "设备离线") color = Palette.Red;
                else if (row.Event == "低电量提醒") color = Palette.Amber;

                string text = row.Time.ToString("MM-dd HH:mm") + "   ";
                if (row.Device.Length > 0) text += row.Device + " · ";
                text += row.Event;
                if (row.Event == "低电量提醒" && row.Note.Length > 0) text += "（" + row.Note + "）";

                _events.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 10.5,
                    Foreground = color,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, 5)
                });
            }
        }

        private void DrawChart()
        {
            if (_chart == null) return;
            _chart.Children.Clear();

            double width = _chart.ActualWidth;
            double height = _chart.ActualHeight;
            if (width < 40 || height < 40) return;

            for (int i = 0; i <= 4; i++)
            {
                double y = height * i / 4.0;
                _chart.Children.Add(new Line
                {
                    X1 = 0, X2 = width, Y1 = y, Y2 = y,
                    Stroke = Palette.Track, StrokeThickness = 1
                });
            }

            DateTime end = DateTime.Now;
            DateTime start = end.AddDays(-7);
            bool drawn = AddSeries("G304", Palette.Green, start, end, width, height);
            drawn |= AddSeries("N75 Pro", Palette.Accent, start, end, width, height);
            _emptyHint.Visibility = drawn ? Visibility.Collapsed : Visibility.Visible;
        }

        private bool AddSeries(string name, Brush brush, DateTime start, DateTime end, double width, double height)
        {
            if (_summary == null) return false;
            DeviceWeeklyStats stats = _summary.Get(name);
            if (stats == null || stats.Trend.Count < 2) return false;

            double total = (end - start).TotalSeconds;
            var points = new PointCollection();
            foreach (TrendPoint point in stats.Trend)
            {
                double x = (point.Time - start).TotalSeconds / total * width;
                if (x < 0) x = 0;
                if (x > width) x = width;
                double y = height - point.Percent / 100.0 * height;
                points.Add(new Point(x, y));
            }

            _chart.Children.Add(new Polyline
            {
                Points = points,
                Stroke = brush,
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round
            });
            return true;
        }

        private UIElement BuildTitleBar()
        {
            var bar = new Grid { Height = 50, Background = Brushes.Transparent };
            bar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); }
                    catch (InvalidOperationException) { }
                }
            };

            var titles = new StackPanel { Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock
            {
                Text = "使用情况",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Palette.TextPrimary
            });
            titles.Children.Add(new TextBlock
            {
                Text = "最近 7 天 · " + HistoryLog.WeekFileName(DateTime.Now),
                FontSize = 10.5,
                Foreground = Palette.TextTertiary,
                Margin = new Thickness(0, 1, 0, 0)
            });

            var closeLabel = new TextBlock
            {
                Text = "\u2715",
                FontSize = 11,
                Foreground = Palette.TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var close = new Border
            {
                Width = 28,
                Height = 24,
                CornerRadius = new CornerRadius(7),
                Background = Brushes.Transparent,
                Child = closeLabel,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 14, 12, 0)
            };
            close.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; };
            close.MouseEnter += delegate { close.Background = Palette.Hover; };
            close.MouseLeave += delegate { close.Background = Brushes.Transparent; };
            close.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Close();
            };

            bar.Children.Add(titles);
            bar.Children.Add(close);
            return bar;
        }

        private static Border BuildStatsCard(string title, TextBlock lines, Thickness margin)
        {
            Border card = BuildCard(title, margin);
            var stack = (StackPanel)card.Child;
            stack.Children.Add(lines);
            return card;
        }

        private static Border BuildCard(string title, Thickness margin)
        {
            var stack = new StackPanel();
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            header.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Palette.TextPrimary
            });
            stack.Children.Add(header);
            return new Border
            {
                Background = Palette.Card,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = margin,
                Child = stack
            };
        }

        private UIElement BuildFooter()
        {
            var footer = new Border
            {
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 12, 20, 14),
                Margin = new Thickness(0, 12, 0, 0)
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var path = new TextBlock
            {
                Text = "日志：logs\\" + HistoryLog.WeekFileName(DateTime.Now),
                FontSize = 10,
                Foreground = Palette.TextTertiary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var open = new Border
            {
                Background = Palette.AccentSoft,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 5, 10, 6),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "打开日志文件夹",
                    FontSize = 10.5,
                    Foreground = Palette.Accent,
                    FontWeight = FontWeights.Medium
                }
            };
            open.MouseLeftButtonUp += delegate { OpenLogFolder(); };

            Grid.SetColumn(path, 0);
            Grid.SetColumn(open, 1);
            row.Children.Add(path);
            row.Children.Add(open);
            footer.Child = row;
            return footer;
        }

        private void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(_history.Folder);
                System.Diagnostics.Process.Start("explorer.exe", _history.Folder);
            }
            catch
            {
                // 打开失败时不影响窗口
            }
        }

        private static Border LegendDot(Brush brush)
        {
            return new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = brush,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
    }
}
