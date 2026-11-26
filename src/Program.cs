using System;
using System.IO;
using System.Threading;
using System.Windows;
using KeyMouseBattery.Services;
using KeyMouseBattery.UI;

namespace KeyMouseBattery
{
    internal static class Program
    {
        private const string MutexName = "Local\\KeyMouseBattery.SingleInstance";
        private const string ShowEventName = "Local\\KeyMouseBattery.Show";

        [STAThread]
        private static void Main(string[] args)
        {
            // 兜底日志：万一出现未处理异常，便于事后排查
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                LogCrash(e.ExceptionObject as Exception);
            };
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    SignalExistingInstance();
                    return;
                }

                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    Run(args, showEvent);
                }

                GC.KeepAlive(mutex);
            }
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using (EventWaitHandle existing = EventWaitHandle.OpenExisting(ShowEventName))
                    existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // 对方尚未创建事件，忽略即可
            }
        }

        private static void Run(string[] args, EventWaitHandle showEvent)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                LogCrash(e.Exception);
                e.Handled = true;
            };

            Settings settings = Settings.Load();
            if (settings.IsNew)
            {
                AutoStart.SetEnabled(settings.AutoStart);
                settings.Save();
            }

            var history = new HistoryLog();
            history.Note("程序启动", "");

            var monitor = new BatteryMonitor(settings, history);
            var window = new MainWindow(settings, monitor, history);

            TrayIcon tray = null;
            Action exit = delegate
            {
                window.AllowClose();
                monitor.Stop();
                if (tray != null) tray.Dispose();
                history.Note("程序退出", "");
                history.Dispose();
                window.Close();
                app.Shutdown();
            };

            tray = new TrayIcon(settings, monitor, history, window.ShowPanel, exit);
            window.SettingsChanged += delegate { tray.Reapply(); };
            tray.Changed += delegate { window.RefreshState(); };

            // 用户重复双击 exe 时唤出已经运行的面板
            var waiter = new Thread(delegate()
            {
                while (showEvent.WaitOne())
                    app.Dispatcher.BeginInvoke(new Action(window.ShowPanel));
            });
            waiter.IsBackground = true;
            waiter.Start();

            bool autoStart = Array.IndexOf(args, "--autostart") >= 0;

            app.Startup += delegate
            {
                monitor.Start();
                if (!autoStart) window.ShowPanel();
            };

            app.Run();
        }

        private static void LogCrash(Exception exception)
        {
            if (exception == null) return;
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyMouseBattery");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "error.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + exception + Environment.NewLine);
            }
            catch
            {
                // 日志写不进去也不能影响主流程
            }
        }
    }
}
