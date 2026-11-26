using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyMouseBattery.UI
{
    /// <summary>圆角电量条：轨道 + 填充。</summary>
    internal sealed class BatteryBar : Grid
    {
        private readonly Border _fill;
        private double _fraction;

        public BatteryBar()
        {
            Height = 8;
            Background = Brushes.Transparent;

            Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = Palette.Track
            });

            _fill = new Border
            {
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Palette.Green
            };
            Children.Add(_fill);

            SizeChanged += delegate { Apply(); };
        }

        public void Set(double fraction, Brush brush)
        {
            _fraction = fraction < 0 ? 0 : (fraction > 1 ? 1 : fraction);
            _fill.Background = brush;
            Apply();
        }

        private void Apply()
        {
            double width = ActualWidth * _fraction;
            if (_fraction > 0 && width < 6) width = 6;
            _fill.Width = Math.Max(0, width);
        }
    }
}
