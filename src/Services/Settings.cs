using System;
using System.Globalization;
using System.IO;

namespace KeyMouseBattery.Services
{
    /// <summary>极简键值配置，存放在 %APPDATA%\KeyMouseBattery\settings.ini。</summary>
    internal sealed class Settings
    {
        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyMouseBattery");

        private static readonly string FilePath = Path.Combine(Folder, "settings.ini");

        public int Threshold = 20;
        public int IntervalSeconds = 60;
        public bool AutoStart = true;
        public bool IsNew = true;

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return settings;

                settings.IsNew = false;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;

                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();

                    switch (key)
                    {
                        case "threshold":
                            settings.Threshold = Clamp(ParseInt(value, settings.Threshold), 5, 50);
                            break;
                        case "interval":
                            settings.IntervalSeconds = Clamp(ParseInt(value, settings.IntervalSeconds), 15, 600);
                            break;
                        case "autostart":
                            settings.AutoStart = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;
                    }
                }
            }
            catch
            {
                // 配置损坏时退回默认值，不影响使用
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(FilePath, new string[]
                {
                    "# 外设电量 配置文件",
                    "threshold=" + Threshold.ToString(CultureInfo.InvariantCulture),
                    "interval=" + IntervalSeconds.ToString(CultureInfo.InvariantCulture),
                    "autostart=" + (AutoStart ? "1" : "0")
                });
            }
            catch
            {
                // 忽略写入失败（例如磁盘只读）
            }
        }

        private static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
