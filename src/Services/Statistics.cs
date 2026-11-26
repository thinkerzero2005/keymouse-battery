using System;
using System.Collections.Generic;
using System.IO;

namespace KeyMouseBattery.Services
{
    internal struct TrendPoint
    {
        public DateTime Time;
        public int Percent;
        public bool Charging;

        public TrendPoint(DateTime time, int percent, bool charging)
        {
            Time = time;
            Percent = percent;
            Charging = charging;
        }
    }

    internal sealed class DeviceWeeklyStats
    {
        public string Name = "";
        public int LastPercent = -1;
        public double DrainPerDay;
        public double DaysLeft = double.PositiveInfinity;
        public int MinPercent = -1;
        public DateTime MinAt;
        public int ChargeCount;
        public TimeSpan ChargeDuration;
        public int ErrorCount;
        public int OfflineCount;
        public int SampleCount;
        public readonly List<TrendPoint> Trend = new List<TrendPoint>();

        public bool HasData
        {
            get { return SampleCount > 0; }
        }
    }

    /// <summary>从按周 CSV 聚合出的使用情况（只读快照）。</summary>
    internal sealed class UsageSummary
    {
        public readonly Dictionary<string, DeviceWeeklyStats> Devices =
            new Dictionary<string, DeviceWeeklyStats>(StringComparer.Ordinal);
        public readonly List<LogRow> RecentEvents = new List<LogRow>();
        public DateTime LastRecord = DateTime.MinValue;

        public DeviceWeeklyStats Get(string name)
        {
            DeviceWeeklyStats stats;
            return Devices.TryGetValue(name, out stats) ? stats : null;
        }

        public static UsageSummary Load(HistoryLog log)
        {
            DateTime now = DateTime.Now;
            DateTime windowStart = now.AddDays(-7);

            var currentWeek = HistoryLog.ReadFile(Path.Combine(log.Folder, HistoryLog.WeekFileName(now)));
            var previousWeek = HistoryLog.ReadFile(Path.Combine(log.Folder, HistoryLog.WeekFileName(now.AddDays(-7))));

            var summary = new UsageSummary();
            var weekRows = currentWeek;
            var trendRows = new List<LogRow>(previousWeek.Count + currentWeek.Count);
            trendRows.AddRange(previousWeek);
            trendRows.AddRange(currentWeek);
            trendRows.Sort(delegate(LogRow a, LogRow b) { return a.Time.CompareTo(b.Time); });

            // 本周统计：只统计当前周文件
            foreach (LogRow row in weekRows)
            {
                if (row.Time > summary.LastRecord) summary.LastRecord = row.Time;
                if (string.IsNullOrEmpty(row.Device)) continue;

                DeviceWeeklyStats stats;
                if (!summary.Devices.TryGetValue(row.Device, out stats))
                {
                    stats = new DeviceWeeklyStats();
                    stats.Name = row.Device;
                    summary.Devices[row.Device] = stats;
                }
                Accumulate(stats, row);
            }

            // 7 天趋势：上周 + 本周
            foreach (LogRow row in trendRows)
            {
                if (row.Percent < 0 || row.Time < windowStart) continue;
                DeviceWeeklyStats stats;
                if (!summary.Devices.TryGetValue(row.Device, out stats))
                {
                    stats = new DeviceWeeklyStats();
                    stats.Name = row.Device;
                    summary.Devices[row.Device] = stats;
                }
                stats.Trend.Add(new TrendPoint(row.Time, row.Percent, row.Charging == true));
            }

            foreach (DeviceWeeklyStats stats in summary.Devices.Values)
                CalculateDrain(stats);

            // 近期事件：本周非采样记录，最新在前
            for (int i = weekRows.Count - 1; i >= 0 && summary.RecentEvents.Count < 14; i--)
            {
                LogRow row = weekRows[i];
                if (row.Event == "采样" || row.Event.Length == 0) continue;
                summary.RecentEvents.Add(row);
            }

            return summary;
        }

        private static void Accumulate(DeviceWeeklyStats stats, LogRow row)
        {
            switch (row.Event)
            {
                case "读数异常":
                    stats.ErrorCount++;
                    break;
                case "设备离线":
                    stats.OfflineCount++;
                    break;
            }

            if (row.Event == "充电开始") stats.ChargeCount++;

            if (row.Percent >= 0)
            {
                stats.SampleCount++;
                stats.LastPercent = row.Percent;
                if (stats.MinPercent < 0 || row.Percent < stats.MinPercent)
                {
                    stats.MinPercent = row.Percent;
                    stats.MinAt = row.Time;
                }
            }
        }

        /// <summary>
        /// 用相邻采样间的下降量估算日均耗电：跳过充电区间与超过 20 分钟的断档（休眠/离线），
        /// 只反映活跃使用时段的耗电速度。
        /// </summary>
        private static double CalculateDrain(DeviceWeeklyStats stats)
        {
            DateTime? previousTime = null;
            int previousPercent = -1;
            bool previousCharging = false;
            double totalDrop = 0;
            double totalHours = 0;

            foreach (TrendPoint point in stats.Trend)
            {
                if (previousTime == null)
                {
                    previousTime = point.Time;
                    previousPercent = point.Percent;
                    previousCharging = point.Charging;
                    continue;
                }

                double minutes = (point.Time - previousTime.Value).TotalMinutes;
                bool valid = minutes >= 1 && minutes <= 20 && !point.Charging && !previousCharging;
                if (valid)
                {
                    totalHours += minutes / 60.0;
                    if (point.Percent < previousPercent)
                        totalDrop += previousPercent - point.Percent;
                }

                previousTime = point.Time;
                previousPercent = point.Percent;
                previousCharging = point.Charging;
            }

            if (totalHours > 0 && totalDrop > 0)
            {
                stats.DrainPerDay = totalDrop / totalHours * 24.0;
                if (stats.LastPercent >= 0)
                    stats.DaysLeft = stats.LastPercent / stats.DrainPerDay;
            }
            return stats.DrainPerDay;
        }
    }
}
