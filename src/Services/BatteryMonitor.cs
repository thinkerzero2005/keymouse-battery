using System;
using System.Threading;
using KeyMouseBattery.Devices;

namespace KeyMouseBattery.Services
{
    /// <summary>后台轮询线程：定时读取两只设备，结果通过 Updated 事件发布。</summary>
    internal sealed class BatteryMonitor
    {
        private readonly Settings _settings;
        private readonly LogitechG304 _mouseDriver = new LogitechG304();
        private readonly VgnKeyboard _keyboardDriver = new VgnKeyboard();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        private Thread _thread;
        private volatile bool _stopping;

        public DeviceReading Mouse = DeviceReading.Missing("等待读取");
        public DeviceReading Keyboard = DeviceReading.Missing("等待读取");
        public DateTime LastPoll = DateTime.MinValue;

        public bool IsPolling { get; private set; }

        public event EventHandler Updated;

        public BatteryMonitor(Settings settings)
        {
            _settings = settings;
        }

        public void Start()
        {
            if (_thread != null) return;

            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "BatteryMonitor";
            _thread.Start();
        }

        public void RefreshNow()
        {
            _wake.Set();
        }

        public void Stop()
        {
            _stopping = true;
            _wake.Set();

            Thread thread = _thread;
            if (thread != null)
            {
                try { thread.Join(5000); }
                catch (ThreadStateException) { }
            }

            if (thread == null || !thread.IsAlive)
            {
                _mouseDriver.Dispose();
                _keyboardDriver.Dispose();
            }
        }

        private void Loop()
        {
            while (!_stopping)
            {
                IsPolling = true;
                RaiseUpdated();

                try
                {
                    Mouse = _mouseDriver.Poll();
                    Keyboard = _keyboardDriver.Poll();
                    LastPoll = DateTime.Now;
                }
                catch (Exception)
                {
                    // 任何异常都不允许结束轮询线程，下一轮会重新打开设备
                    Mouse = DeviceReading.Error("读取异常");
                    Keyboard = DeviceReading.Error("读取异常");
                }
                finally
                {
                    IsPolling = false;
                }

                RaiseUpdated();
                _wake.WaitOne(Math.Max(15, _settings.IntervalSeconds) * 1000);
            }
        }

        private void RaiseUpdated()
        {
            EventHandler handler = Updated;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
