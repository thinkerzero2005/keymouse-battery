using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KeyMouseBattery.Devices;

namespace KeyMouseBattery.Services
{
    internal sealed class LogRow
    {
        public DateTime Time;
        public string Device = "";
        public int Percent = -1;
        public bool? Charging;
        public string State = "";
        public string Event = "";
        public string Note = "";
    }

    /// <summary>
    /// 使用记录：按 ISO 周写入 CSV（logs\2026-W48.csv）。
    /// 每 5 分钟固定采样一条；充电开始/结束、休眠、离线、读数异常等状态变化即时记录。
    /// </summary>
    internal sealed class HistoryLog : IDisposable
    {
        private static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(5);

        private readonly object _sync = new object();
        private readonly string _folder;

        private StreamWriter _writer;
        private string _filePath;
        private DateTime _lastSample = DateTime.MinValue;
        private DeviceReading _lastMouse;
        private DeviceReading _lastKeyboard;

        public HistoryLog()
        {
            _folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KeyMouseBattery", "logs");
        }

        public string Folder
        {
            get { return _folder; }
        }

        public string CurrentFile
        {
            get { lock (_sync) { return _filePath; } }
        }

        /// <summary>每次轮询后调用：处理状态变化并按 5 分钟节奏采样。</summary>
        public void Process(string mouseName, DeviceReading mouse, string keyboardName, DeviceReading keyboard)
        {
            lock (_sync)
            {
                DateTime now = DateTime.Now;
                try
                {
                    DetectChanges(mouseName, _lastMouse, mouse, now);
                    DetectChanges(keyboardName, _lastKeyboard, keyboard, now);

                    if (now - _lastSample >= SampleInterval)
                    {
                        WriteRow(now, mouseName, mouse, "采样", "");
                        WriteRow(now, keyboardName, keyboard, "采样", "");
                        _lastSample = now;
                    }
                }
                catch
                {
                    // 记录失败不能影响电量监控
                }

                _lastMouse = mouse;
                _lastKeyboard = keyboard;
            }
        }

        public void Event(string device, DeviceReading reading, string eventName, string note)
        {
            lock (_sync)
            {
                try { WriteRow(DateTime.Now, device, reading, eventName, note); }
                catch { }
            }
        }

        public void Note(string eventName, string note)
        {
            lock (_sync)
            {
                try { WriteRow(DateTime.Now, "", null, eventName, note); }
                catch { }
            }
        }

        private void DetectChanges(string name, DeviceReading previous, DeviceReading current, DateTime now)
        {
            if (current == null) return;
            if (previous == null)
            {
                if (current.State == DeviceState.Sleeping) WriteRow(now, name, current, "设备休眠", "");
                else if (current.State == DeviceState.Missing) WriteRow(now, name, current, "设备离线", "");
                else if (current.State == DeviceState.Error) WriteRow(now, name, current, "读数异常", current.Detail);
                return;
            }

            if (current.Charging != previous.Charging && current.HasPercent)
                WriteRow(now, name, current, current.Charging ? "充电开始" : "充电结束", "");

            if (current.State == previous.State) return;

            switch (current.State)
            {
                case DeviceState.Sleeping:
                    WriteRow(now, name, current, "设备休眠", "");
                    break;
                case DeviceState.Missing:
                    WriteRow(now, name, current, "设备离线", "");
                    break;
                case DeviceState.Error:
                    WriteRow(now, name, current, "读数异常", current.Detail);
                    break;
                default:
                    if (previous.State == DeviceState.Sleeping || previous.State == DeviceState.Missing)
                        WriteRow(now, name, current, "设备恢复", "");
                    break;
            }

            if (current.Uncertain && !previous.Uncertain)
                WriteRow(now, name, current, "读数存疑", current.Detail);
        }

        private void WriteRow(DateTime time, string device, DeviceReading reading, string eventName, string note)
        {
            EnsureWriter(time);

            string percent = reading != null && reading.HasPercent
                ? reading.Percent.ToString(CultureInfo.InvariantCulture)
                : "";
            string charging = reading != null && reading.HasPercent
                ? (reading.Charging ? "1" : "0")
                : "";
            string state = reading == null ? "" : DescribeState(reading.State, reading.Uncertain);

            _writer.WriteLine(string.Join(",", new string[]
            {
                time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Csv(device),
                percent,
                charging,
                Csv(state),
                Csv(eventName),
                Csv(note)
            }));
            _writer.Flush();
        }

        private static string DescribeState(DeviceState state, bool uncertain)
        {
            switch (state)
            {
                case DeviceState.Ok: return uncertain ? "正常(存疑)" : "正常";
                case DeviceState.Charging: return "充电中";
                case DeviceState.Sleeping: return "已休眠";
                case DeviceState.Missing: return "未连接";
                case DeviceState.Error: return "异常";
                default: return "未知";
            }
        }

        private void EnsureWriter(DateTime now)
        {
            string path = Path.Combine(_folder, WeekFileName(now));
            if (_writer != null && path == _filePath) return;

            CloseWriter();
            Directory.CreateDirectory(_folder);

            bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, new UTF8Encoding(true));
            _filePath = path;

            if (isNew)
            {
                _writer.WriteLine("时间,设备,电量,充电,状态,事件,备注");
                _writer.Flush();
            }
        }

        /// <summary>ISO 周文件名，例如 2026-W48.csv（按星期四归属年，正确处理跨年）。</summary>
        public static string WeekFileName(DateTime date)
        {
            int dayOfWeek = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            DateTime thursday = date.AddDays(4 - dayOfWeek);
            int week = (thursday.DayOfYear - 1) / 7 + 1;
            return string.Format("{0}-W{1:00}.csv", thursday.Year, week);
        }

        public static List<LogRow> ReadFile(string path)
        {
            var rows = new List<LogRow>();
            if (!File.Exists(path)) return rows;

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string first = reader.ReadLine(); // 表头
                    if (first == null) return rows;

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        List<string> parts = SplitCsv(line);
                        if (parts.Count < 7) continue;

                        DateTime time;
                        if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) continue;

                        var row = new LogRow();
                        row.Time = time;
                        row.Device = parts[1];

                        int percent;
                        row.Percent = int.TryParse(parts[2], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out percent) ? percent : -1;

                        if (parts[3] == "1") row.Charging = true;
                        else if (parts[3] == "0") row.Charging = false;

                        row.State = parts[4];
                        row.Event = parts[5];
                        row.Note = parts[6];
                        rows.Add(row);
                    }
                }
            }
            catch
            {
                // 读取失败返回已解析部分
            }
            return rows;
        }

        private static List<string> SplitCsv(string line)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                        else quoted = false;
                    }
                    else current.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { parts.Add(current.ToString()); current.Length = 0; }
                else current.Append(c);
            }
            parts.Add(current.ToString());
            return parts;
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(new char[] { ',', '"', '\n', '\r' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private void CloseWriter()
        {
            if (_writer == null) return;
            try { _writer.Dispose(); }
            catch { }
            _writer = null;
        }

        public void Dispose()
        {
            lock (_sync) { CloseWriter(); }
        }
    }
}
