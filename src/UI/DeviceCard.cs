using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeyMouseBattery.Devices;

namespace KeyMouseBattery.UI
{
    /// <summary>单个设备的电量卡片。</summary>
    internal sealed class DeviceCard : Border
    {
        private readonly TextBlock _percent;
        private readonly TextBlock _status;
        private readonly TextBlock _time;
        private readonly BatteryBar _bar;

        public DeviceCard(string name, string transport)
        {
            Background = Palette.Card;
            BorderBrush = Palette.Border;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(12);
            Padding = new Thickness(16, 14, 16, 14);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 第一行：名称 + 连接方式 / 百分比
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Palette.TextPrimary
            });

            var chip = new Border
            {
                Background = Palette.Chip,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
                Child = new TextBlock
                {
                    Text = transport,
                    FontSize = 10.5,
                    Foreground = Palette.TextSecondary
                }
            };
            left.Children.Add(chip);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _percent = new TextBlock
            {
                Text = "--",
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = Palette.Gray
            };
            var unit = new TextBlock
            {
                Text = "%",
                FontSize = 13,
                Foreground = Palette.TextTertiary,
                Margin = new Thickness(3, 8, 0, 0)
            };
            right.Children.Add(_percent);
            right.Children.Add(unit);

            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            header.Children.Add(left);
            header.Children.Add(right);

            // 第二行：电量条
            _bar = new BatteryBar();
            _bar.Margin = new Thickness(0, 13, 0, 0);
            Grid.SetRow(_bar, 1);

            // 第三行：状态 / 更新时间
            var footer = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _status = new TextBlock
            {
                Text = "等待读取",
                FontSize = 11.5,
                Foreground = Palette.TextSecondary,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _time = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = Palette.TextTertiary,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(_status, 0);
            Grid.SetColumn(_time, 1);
            footer.Children.Add(_status);
            footer.Children.Add(_time);
            Grid.SetRow(footer, 2);

            root.Children.Add(header);
            root.Children.Add(_bar);
            root.Children.Add(footer);
            Child = root;
        }

        public void Apply(DeviceReading reading, int threshold)
        {
            bool hasPercent = reading.HasPercent;
            Brush color = hasPercent ? Palette.ForPercent(reading.Percent, threshold) : Palette.Gray;

            _percent.Text = hasPercent ? reading.Percent.ToString() : "--";
            _percent.Foreground = color;
            _bar.Set(hasPercent ? reading.Percent / 100.0 : 0, color);

            _status.Text = Describe(reading);
            _status.Foreground = StatusBrush(reading);
            _time.Text = reading.UpdatedAt.Year > 2000
                ? "更新于 " + reading.UpdatedAt.ToString("HH:mm:ss")
                : "";
        }

        private static string Describe(DeviceReading reading)
        {
            switch (reading.State)
            {
                case DeviceState.Ok:
                    return reading.Uncertain ? "运行正常 · 读数存疑" : "运行正常";
                case DeviceState.Charging:
                    return "充电中";
                case DeviceState.Sleeping:
                    return reading.Detail.Length > 0 ? reading.Detail : "设备已休眠";
                case DeviceState.Missing:
                    return reading.Detail.Length > 0 ? reading.Detail : "未检测到接收器";
                case DeviceState.Error:
                    return reading.Detail.Length > 0 ? reading.Detail : "读取失败";
                default:
                    return "等待读取";
            }
        }

        private static Brush StatusBrush(DeviceReading reading)
        {
            switch (reading.State)
            {
                case DeviceState.Charging:
                    return Palette.Accent;
                case DeviceState.Error:
                    return Palette.Red;
                case DeviceState.Sleeping:
                case DeviceState.Missing:
                    return Palette.TextTertiary;
                default:
                    return Palette.TextSecondary;
            }
        }
    }
}
