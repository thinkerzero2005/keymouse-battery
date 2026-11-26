using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using KeyMouseBattery.Hid;

namespace KeyMouseBattery.Devices
{
    /// <summary>
    /// VGN N75 Pro（2.4G 接收器）电量读取。
    /// 协议：Usage Page 0xFF1C，64 字节报文，Report ID 0x04。
    /// 请求 04 00 00 1A 06 ...，回包第 3 字节为 0x1A，第 8 字节为电量百分比，第 9 字节非 0 表示充电中。
    /// 注意：必须用 WriteFile 写输出报文（HidD_SetOutputReport 会假成功），
    /// 且键盘休眠时会吞掉第一次查询，需要重发。
    /// </summary>
    internal sealed class VgnKeyboard : IDisposable
    {
        public const ushort VendorId = 0x320F;
        public const ushort ProductId = 0x5088;

        private HidDevice _device;
        private DateTime _lastConnectAttempt = DateTime.MinValue;
        private int _lastPercent = -1;

        public string LastRaw = "";

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

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!_device.Write(BuildRequest(_device.Interface.OutputReportLength)))
                    break;

                byte[] response = WaitResponse(450);
                if (response != null) return Parse(response);

                Thread.Sleep(60);
            }

            Disconnect();
            return DeviceReading.Sleeping("已休眠，按一下键盘即可唤醒");
        }

        private DeviceReading Parse(byte[] response)
        {
            var reading = new DeviceReading();
            reading.UpdatedAt = DateTime.Now;

            int percent = response[8];
            bool charging = response[9] != 0;

            if (percent < 0 || percent > 100)
            {
                reading.Percent = -1;
                reading.Uncertain = true;
                reading.State = DeviceState.Error;
                reading.Detail = "读数异常";
                return reading;
            }

            // 已知固件问题：ADC 读取失败时会固定返回 100，此时与上一次正常读数差距过大
            if (percent == 100 && !charging && _lastPercent >= 0 && _lastPercent < 90)
                reading.Uncertain = true;

            _lastPercent = percent;
            reading.Percent = percent;
            reading.Charging = charging;
            reading.State = charging ? DeviceState.Charging : DeviceState.Ok;
            reading.Detail = charging ? "正在充电" : "2.4G 无线";
            return reading;
        }

        private static byte[] BuildRequest(int outputLength)
        {
            byte[] request = new byte[outputLength];
            request[0] = 0x04;
            request[3] = 0x1A;
            request[4] = 0x06;
            return request;
        }

        private byte[] WaitResponse(int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (true)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) return null;

                byte[] report = _device.Read(remaining);
                if (report == null) return null;
                if (report.Length < 10) continue;
                if (report[0] != 0x04) continue;   // 非本协议报文（键盘按键等）
                if (report[3] != 0x1A) continue;

                LastRaw = Hex(report);
                return report;
            }
        }

        private bool Connect()
        {
            List<HidInterface> all = HidEnumerator.Enumerate();

            for (int pass = 0; pass < 2; pass++)
            {
                foreach (HidInterface info in all)
                {
                    if (info.VendorId != VendorId || info.ProductId != ProductId) continue;
                    if (info.OutputReportLength < 8) continue;
                    if (pass == 0 && info.UsagePage != 0xFF1C) continue;

                    HidDevice device = HidDevice.Open(info);
                    if (device == null) continue;

                    // 打开后立刻试一次，能应答的就是正确的接口
                    if (device.Write(BuildRequest(device.Interface.OutputReportLength)))
                    {
                        byte[] probe = ReadOnce(device, 300);
                        if (probe != null)
                        {
                            _device = device;
                            _lastPercent = -1;
                            LastRaw = Hex(probe);
                            return true;
                        }
                    }

                    device.Dispose();
                }
            }

            return false;
        }

        private static byte[] ReadOnce(HidDevice device, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) return null;

                byte[] report = device.Read(remaining);
                if (report == null) return null;
                if (report.Length < 10) continue;
                if (report[0] != 0x04 || report[3] != 0x1A) continue;
                return report;
            }
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
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
