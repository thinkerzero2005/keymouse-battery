using System;
using System.Threading;
using KeyMouseBattery.Devices;
using KeyMouseBattery.Hid;

namespace KeyMouseBattery.Tools
{
    /// <summary>命令行探针：不启动界面，直接打印两台设备的原始读数，用于与旧软件对比。</summary>
    internal static class Probe
    {
        private static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Console.WriteLine("== HID 接口 ==");
            System.Collections.Generic.List<HidInterface> all = HidEnumerator.Enumerate();
            foreach (HidInterface info in all)
                Console.WriteLine("  " + info);
            Console.WriteLine(string.Format("共 {0} 个接口。{1}", all.Count, HidEnumerator.LastError));

            var mouse = new LogitechG304();
            var keyboard = new VgnKeyboard();

            for (int i = 0; i < 3; i++)
            {
                DeviceReading m = mouse.Poll();
                DeviceReading k = keyboard.Poll();

                Console.WriteLine(string.Format("[{0:HH:mm:ss}] 鼠标  {1}  {2}%  {3}", DateTime.Now, m.State, m.Percent, m.Detail));
                Console.WriteLine("           原始: " + mouse.LastRaw);
                Console.WriteLine(string.Format("           键盘  {0}  {1}%  {2}", k.State, k.Percent, k.Detail));
                Console.WriteLine("           原始: " + keyboard.LastRaw);

                if (i < 2) Thread.Sleep(1500);
            }

            mouse.Dispose();
            keyboard.Dispose();
        }
    }
}
