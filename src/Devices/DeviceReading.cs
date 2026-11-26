using System;

namespace KeyMouseBattery.Devices
{
    internal enum DeviceState
    {
        Unknown,
        Ok,
        Charging,
        Sleeping,
        Missing,
        Error
    }

    /// <summary>一次电量读取结果（不可变快照，供界面与托盘共享）。</summary>
    internal sealed class DeviceReading
    {
        public DeviceState State = DeviceState.Unknown;
        public int Percent = -1;
        public bool Charging;
        public bool Uncertain;
        public string Detail = "";
        public DateTime UpdatedAt = DateTime.Now;

        public bool HasPercent
        {
            get { return Percent >= 0 && (State == DeviceState.Ok || State == DeviceState.Charging); }
        }

        public static DeviceReading Missing(string detail)
        {
            var r = new DeviceReading();
            r.State = DeviceState.Missing;
            r.Detail = detail;
            return r;
        }

        public static DeviceReading Sleeping(string detail)
        {
            var r = new DeviceReading();
            r.State = DeviceState.Sleeping;
            r.Detail = detail;
            return r;
        }

        public static DeviceReading Error(string detail)
        {
            var r = new DeviceReading();
            r.State = DeviceState.Error;
            r.Detail = detail;
            return r;
        }
    }
}
