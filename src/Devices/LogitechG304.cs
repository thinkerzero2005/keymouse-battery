using System;
using System.Collections.Generic;
using System.Text;
using KeyMouseBattery.Hid;

namespace KeyMouseBattery.Devices
{
    /// <summary>
    /// 罗技 G304（Lightspeed 接收器）电量读取。
    /// 协议：HID++ 2.0 长报文（Report ID 0x11），接收器按设备序号 1-6 转发。
    /// 优先使用特性 0x1001 UNIFIED_BATTERY（直接给百分比），
    /// 设备不支持时退回 0x1000 BATTERY_VOLTAGE（按电压估算）。
    /// </summary>
    internal sealed class LogitechG304 : IDisposable
    {
        public const ushort VendorId = 0x046D;
        public const ushort ProductId = 0xC53F;

        public const ushort FeatureUnifiedBattery = 0x1001;
        public const ushort FeatureBatteryVoltage = 0x1000;

        private HidDevice _device;
        private byte _deviceIndex;
        private byte _batteryFeatureIndex;
        private ushort _batteryFeatureId;
        private DateTime _lastConnectAttempt = DateTime.MinValue;

        public string LastRaw = "";
        public string LastError = "";

        public DeviceReading Poll()
        {
            if (_device == null)
            {
                if ((DateTime.UtcNow - _lastConnectAttempt).TotalSeconds < 3)
                    return DeviceReading.Missing("接收器重连中");
                _lastConnectAttempt = DateTime.UtcNow;
                if (!Connect())
                    return DeviceReading.Missing("未找到接收器");
            }

            byte[] response = _batteryFeatureId == FeatureUnifiedBattery
                ? Request(_batteryFeatureIndex, 1, null, 700)
                : Request(_batteryFeatureIndex, 0, null, 700);

            if (response == null)
            {
                // 鼠标静止几秒就会休眠，此时读不到响应属于正常现象
                Disconnect();
                return DeviceReading.Sleeping("已休眠，移动鼠标即可唤醒");
            }

            return Parse(response);
        }

        private DeviceReading Parse(byte[] response)
        {
            var reading = new DeviceReading();
            reading.UpdatedAt = DateTime.Now;

            if (_batteryFeatureId == FeatureUnifiedBattery)
            {
                int soc = response[4];
                int chargingStatus = response[6];

                if (soc < 0 || soc > 100)
                    return DeviceReading.Error("读数异常");

                reading.Percent = soc;
                reading.Charging = chargingStatus != 0;
                reading.State = reading.Charging ? DeviceState.Charging : DeviceState.Ok;
                reading.Detail = reading.Charging ? "正在充电" : "2.4G 无线";
                return reading;
            }

            // 0x1000：实测 G304 的第 4 字节即为电量百分比（0x5A = 90%），第 5 字节为电池状态
            if (response.Length < 7) return DeviceReading.Error("报文过短");
            int value = response[4];
            if (value >= 0 && value <= 100)
            {
                reading.Percent = value;
                reading.Charging = false;   // G304 使用 AA 电池，不支持充电
                reading.State = DeviceState.Ok;
                reading.Detail = "2.4G 无线";
                return reading;
            }

            // 少数机型在该特性下返回电压（mV），做一次兜底换算
            int millivolts = (response[4] << 8) | response[5];
            if (millivolts <= 0) return DeviceReading.Error("读数无效");

            double ratio = (millivolts - 1050.0) / (1550.0 - 1050.0);
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;

            reading.Percent = (int)Math.Round(ratio * 100);
            reading.Charging = (response[6] & 0x80) != 0;
            reading.Uncertain = true;
            reading.State = reading.Charging ? DeviceState.Charging : DeviceState.Ok;
            reading.Detail = string.Format("电压 {0:0.00} V（估算）", millivolts / 1000.0);
            return reading;
        }

        private bool Connect()
        {
            List<HidInterface> all = HidEnumerator.Enumerate();

            // 第一轮只找 HID++ 专用接口；找不到再放宽条件
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (HidInterface info in all)
                {
                    if (info.VendorId != VendorId || info.ProductId != ProductId) continue;
                    if (info.InputReportLength < 20 || info.OutputReportLength < 20) continue;
                    if (pass == 0 && info.UsagePage != 0xFF00) continue;

                    HidDevice device = HidDevice.Open(info);
                    if (device == null) continue;

                    for (byte index = 1; index <= 6; index++)
                    {
                        byte feature = GetFeature(device, index, FeatureUnifiedBattery, 350);
                        if (feature > 0)
                        {
                            Adopt(device, index, feature, FeatureUnifiedBattery);
                            return true;
                        }

                        feature = GetFeature(device, index, FeatureBatteryVoltage, 250);
                        if (feature > 0)
                        {
                            Adopt(device, index, feature, FeatureBatteryVoltage);
                            return true;
                        }
                    }

                    device.Dispose();
                }
            }

            LastError = "接收器存在但没有设备响应";
            return false;
        }

        private void Adopt(HidDevice device, byte index, byte featureIndex, ushort featureId)
        {
            _device = device;
            _deviceIndex = index;
            _batteryFeatureIndex = featureIndex;
            _batteryFeatureId = featureId;
            LastError = "";
        }

        private byte GetFeature(HidDevice device, byte index, ushort featureId, int timeoutMs)
        {
            byte[] parameters = new byte[] { (byte)(featureId >> 8), (byte)featureId };
            byte[] request = BuildReport(device.Interface.OutputReportLength, index, 0x00, 0x00, parameters);
            if (!device.Write(request)) return 0;

            byte[] response = WaitResponse(device, index, 0x00, 0x00, timeoutMs);
            if (response == null) return 0;
            return response[4];
        }

        private byte[] Request(byte featureIndex, byte function, byte[] parameters, int timeoutMs)
        {
            byte[] request = BuildReport(_device.Interface.OutputReportLength, _deviceIndex, featureIndex, function, parameters);
            if (!_device.Write(request))
            {
                LastError = "写入失败";
                return null;
            }

            byte[] response = WaitResponse(_device, _deviceIndex, featureIndex, function, timeoutMs);
            if (response == null) LastError = "等待响应超时";
            return response;
        }

        private byte[] WaitResponse(HidDevice device, byte index, byte featureIndex, byte function, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (true)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) return null;

                byte[] report = device.Read(remaining);
                if (report == null) return null;
                if (report.Length < 4) continue;
                if (report[0] != 0x11) continue;          // 非长报文
                if (report[1] != index) continue;         // 其它设备
                if (report[2] == 0xFF) continue;          // 错误报文（特性不支持/设备离线）
                if (report[2] != featureIndex) continue;  // 其它特性的通知
                if ((report[3] >> 4) != function) continue;

                LastRaw = Hex(report);
                return report;
            }
        }

        private static byte[] BuildReport(int outputLength, byte index, byte featureIndex, byte function, byte[] parameters)
        {
            byte[] report = new byte[outputLength];
            report[0] = 0x11;
            report[1] = index;
            report[2] = featureIndex;
            report[3] = (byte)((function << 4) | 0x01);
            if (parameters != null)
                Array.Copy(parameters, 0, report, 4, Math.Min(parameters.Length, outputLength - 4));
            return report;
        }

        private static string Hex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private void Disconnect()
        {
            if (_device != null)
            {
                _device.Dispose();
                _device = null;
            }
            _batteryFeatureId = 0;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
