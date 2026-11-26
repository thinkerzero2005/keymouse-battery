using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyMouseBattery.Devices;
using KeyMouseBattery.Services;

namespace KeyMouseBattery.UI
{
    /// <summary>单一托盘图标：按最低电量变色，悬停显示两台设备读数，负责低电量气泡提醒。</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly Settings _settings;
        private readonly BatteryMonitor _monitor;
        private readonly NotifyIcon _notify;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly bool[] _lowNotified = new bool[2];

        private Icon _icon;
        private Color _iconColor;
        private bool _iconCharging;
        private bool _hasIcon;

        public event EventHandler Changed;

        public TrayIcon(Settings settings, BatteryMonitor monitor, Action showPanel, Action exit)
        {
            _settings = settings;
            _monitor = monitor;

            var menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Font = new Font("Microsoft YaHei UI", 9f);

            var open = new ToolStripMenuItem("打开主界面");
            open.Click += delegate { showPanel(); };
            menu.Items.Add(open);

            var refresh = new ToolStripMenuItem("立即刷新");
            refresh.Click += delegate { monitor.RefreshNow(); };
            menu.Items.Add(refresh);

            menu.Items.Add(new ToolStripSeparator());

            _autoStartItem = new ToolStripMenuItem("开机自启");
            _autoStartItem.CheckOnClick = true;
            _autoStartItem.Checked = AutoStart.IsEnabled();
            _autoStartItem.Click += delegate
            {
                AutoStart.SetEnabled(_autoStartItem.Checked);
                _settings.AutoStart = _autoStartItem.Checked;
                _settings.Save();
                EventHandler handler = Changed;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            menu.Items.Add(_autoStartItem);

            menu.Items.Add(new ToolStripSeparator());

            var quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { exit(); };
            menu.Items.Add(quit);

            _notify = new NotifyIcon();
            _notify.ContextMenuStrip = menu;
            _notify.Text = "外设电量";
            _notify.Visible = true;
            _notify.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) showPanel();
            };

            SetIcon(Color.FromArgb(0x9C, 0xA3, 0xAF), false);

            _monitor.Updated += delegate
            {
                if (System.Windows.Application.Current == null) return;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(Reapply));
            };
        }

        /// <summary>用当前读数重画托盘图标、提示文字并检查提醒。</summary>
        public void Reapply()
        {
            DeviceReading mouse = _monitor.Mouse;
            DeviceReading keyboard = _monitor.Keyboard;

            _notify.Text = BuildTooltip(mouse, keyboard);
            _autoStartItem.Checked = AutoStart.IsEnabled();

            int? lowest = Lowest(mouse, keyboard);
            bool charging = (mouse.HasPercent && mouse.Charging) || (keyboard.HasPercent && keyboard.Charging);
            Color color = lowest.HasValue
                ? ColorFor(lowest.Value, _settings.Threshold)
                : Color.FromArgb(0x9C, 0xA3, 0xAF);

            if (!_hasIcon || !color.Equals(_iconColor) || charging != _iconCharging)
                SetIcon(color, charging);

            CheckLow(mouse, "G304", "鼠标", 0);
            CheckLow(keyboard, "N75 Pro", "键盘", 1);
        }

        private void CheckLow(DeviceReading reading, string model, string label, int slot)
        {
            if (!reading.HasPercent) return;

            if (reading.Charging)
            {
                _lowNotified[slot] = false;
                return;
            }

            if (reading.Percent <= _settings.Threshold)
            {
                if (_lowNotified[slot]) return;
                _lowNotified[slot] = true;
                _notify.ShowBalloonTip(8000,
                    model + " 电量不足",
                    string.Format("{0}仅剩 {1}%，请及时更换电池。", label, reading.Percent),
                    ToolTipIcon.Warning);
            }
            else if (reading.Percent > _settings.Threshold + 5)
            {
                _lowNotified[slot] = false;
            }
        }

        private static string BuildTooltip(DeviceReading mouse, DeviceReading keyboard)
        {
            string text = "G304 " + Short(mouse) + " · N75 Pro " + Short(keyboard);
            if (text.Length > 63) text = text.Substring(0, 63);
            return text;
        }

        private static string Short(DeviceReading reading)
        {
            if (reading.HasPercent) return reading.Percent + "%";
            switch (reading.State)
            {
                case DeviceState.Sleeping: return "休眠";
                case DeviceState.Missing: return "未连接";
                case DeviceState.Error: return "异常";
                default: return "--";
            }
        }

        private static int? Lowest(DeviceReading mouse, DeviceReading keyboard)
        {
            int? result = null;
            if (mouse.HasPercent) result = mouse.Percent;
            if (keyboard.HasPercent) result = result.HasValue ? Math.Min(result.Value, keyboard.Percent) : keyboard.Percent;
            return result;
        }

        private static Color ColorFor(int percent, int threshold)
        {
            if (percent < threshold) return Color.FromArgb(0xDC, 0x26, 0x26);
            if (percent < threshold + 20) return Color.FromArgb(0xF5, 0x9E, 0x0B);
            return Color.FromArgb(0x16, 0xA3, 0x4A);
        }

        private void SetIcon(Color color, bool charging)
        {
            Icon icon = CreateIcon(color, charging);
            Icon previous = _icon;
            _icon = icon;
            _iconColor = color;
            _iconCharging = charging;
            _hasIcon = true;
            _notify.Icon = icon;
            if (previous != null) previous.Dispose();
        }

        private static Icon CreateIcon(Color color, bool charging)
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (var brush = new SolidBrush(color))
                    using (GraphicsPath body = RoundedRect(new RectangleF(3f, 8f, 21f, 16f), 4f))
                    using (GraphicsPath cap = RoundedRect(new RectangleF(24f, 13f, 4f, 6f), 1.5f))
                    {
                        g.FillPath(brush, body);
                        g.FillPath(brush, cap);
                    }

                    if (charging)
                    {
                        PointF[] bolt = new PointF[]
                        {
                            new PointF(17.5f, 7.5f), new PointF(10.5f, 17.5f), new PointF(15f, 17.5f),
                            new PointF(13f, 24.5f), new PointF(20.5f, 14.5f), new PointF(16f, 14.5f)
                        };
                        using (var white = new SolidBrush(Color.White))
                            g.FillPolygon(white, bolt);
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(handle))
                        return (Icon)temp.Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            float d = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public void Dispose()
        {
            _notify.Visible = false;
            _notify.Dispose();
            if (_icon != null)
            {
                _icon.Dispose();
                _icon = null;
            }
        }
    }
}
