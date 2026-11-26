using System;
using System.Reflection;
using Microsoft.Win32;

namespace KeyMouseBattery.Services
{
    /// <summary>通过 HKCU 的 Run 项实现开机自启，无需管理员权限。</summary>
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "KeyMouseBattery";

        public static string ExecutablePath
        {
            get
            {
                Assembly assembly = Assembly.GetEntryAssembly();
                return assembly == null ? "" : assembly.Location;
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return;
                    if (enabled)
                        key.SetValue(ValueName, "\"" + ExecutablePath + "\" --autostart");
                    else
                        key.DeleteValue(ValueName, false);
                }
            }
            catch
            {
                // 注册表不可写时忽略，程序照常运行
            }
        }
    }
}
