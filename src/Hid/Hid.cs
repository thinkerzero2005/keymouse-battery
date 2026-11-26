using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace KeyMouseBattery.Hid
{
    /// <summary>一个 HID 接口（同一个物理接收器通常会暴露多个接口）。</summary>
    internal sealed class HidInterface
    {
        public string Path;
        public ushort VendorId;
        public ushort ProductId;
        public ushort UsagePage;
        public ushort Usage;
        public int InputReportLength;
        public int OutputReportLength;
        public string ProductName;

        public override string ToString()
        {
            return string.Format("VID_{0:X4}&PID_{1:X4} UP={2:X4} In={3} Out={4} {5}",
                VendorId, ProductId, UsagePage, InputReportLength, OutputReportLength, ProductName);
        }
    }

    internal static class HidEnumerator
    {
        public static string LastError = "";

        /// <summary>枚举系统当前所有 HID 接口。</summary>
        public static List<HidInterface> Enumerate()
        {
            LastError = "";
            var result = new List<HidInterface>();
            Guid guid = Native.HidInterfaceGuid;
            IntPtr set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
                Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == Native.InvalidHandle)
            {
                LastError = "SetupDiGetClassDevs 失败，错误码 " + Marshal.GetLastWin32Error();
                return result;
            }

            int total = 0;
            int failed = 0;
            try
            {
                var data = new Native.SpDeviceInterfaceData();
                data.Size = Marshal.SizeOf(typeof(Native.SpDeviceInterfaceData));

                for (int i = 0; ; i++)
                {
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                    total++;

                    string stage;
                    string path = GetInterfacePath(set, ref data, out stage);
                    if (path == null) { failed++; Note(stage); continue; }

                    HidInterface info = ReadInterfaceInfo(path, out stage);
                    if (info != null) result.Add(info);
                    else { failed++; Note(stage); }
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(set);
            }

            if (failed > 0)
                LastError = string.Format("共 {0} 个，失败 {1} 个。{2}", total, failed, Notes.ToString());
            return result;
        }

        private static readonly System.Text.StringBuilder Notes = new System.Text.StringBuilder();

        private static void Note(string message)
        {
            if (Notes.Length > 600) return;
            Notes.Append('[').Append(message).Append(']');
        }

        private static string GetInterfacePath(IntPtr set, ref Native.SpDeviceInterfaceData data, out string stage)
        {
            stage = "";
            int required = 0;
            Native.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, ref required, IntPtr.Zero);
            if (required <= 0)
            {
                stage = "取路径长度失败:" + Marshal.GetLastWin32Error();
                return null;
            }

            IntPtr buffer = Marshal.AllocHGlobal(required);
            try
            {
                // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize：x64 为 8，x86 为 6
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                int size = required;
                if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, ref size, IntPtr.Zero))
                {
                    stage = "取路径失败:" + Marshal.GetLastWin32Error();
                    return null;
                }

                // DevicePath 紧跟在 4 字节 cbSize 之后（x64 上有时按 8 字节对齐，做一次探测）
                long address = buffer.ToInt64();
                string path = Marshal.PtrToStringUni(new IntPtr(address + 4));
                if (path == null || !path.StartsWith("\\\\?\\"))
                    path = Marshal.PtrToStringUni(new IntPtr(address + 8));
                return path;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static HidInterface ReadInterfaceInfo(string path, out string stage)
        {
            stage = "";
            // 以零权限打开即可读取属性，不会干扰正在运行的程序
            IntPtr h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == IntPtr.Zero || h == Native.InvalidHandle)
            {
                stage = "打开失败:" + Marshal.GetLastWin32Error() + " " + path;
                return null;
            }

            try
            {
                var attributes = new Native.HiddAttributes();
                attributes.Size = Marshal.SizeOf(typeof(Native.HiddAttributes));
                if (!Native.HidD_GetAttributes(h, ref attributes))
                {
                    stage = "属性失败:" + Marshal.GetLastWin32Error();
                    return null;
                }

                IntPtr preparsed = IntPtr.Zero;
                if (!Native.HidD_GetPreparsedData(h, ref preparsed) || preparsed == IntPtr.Zero)
                {
                    stage = "预解析数据失败:" + Marshal.GetLastWin32Error();
                    return null;
                }

                try
                {
                    var caps = new Native.HidpCaps();
                    int status = Native.HidP_GetCaps(preparsed, ref caps);
                    if (status != unchecked((int)0x110000))
                    {
                        stage = "取能力失败:0x" + status.ToString("X8");
                        return null;
                    }

                    var info = new HidInterface();
                    info.Path = path;
                    info.VendorId = attributes.VendorId;
                    info.ProductId = attributes.ProductId;
                    info.UsagePage = caps.UsagePage;
                    info.Usage = caps.Usage;
                    info.InputReportLength = caps.InputReportByteLength;
                    info.OutputReportLength = caps.OutputReportByteLength;

                    var name = new System.Text.StringBuilder(256);
                    if (Native.HidD_GetProductString(h, name, 512))
                        info.ProductName = name.ToString().Trim();
                    return info;
                }
                finally
                {
                    Native.HidD_FreePreparsedData(preparsed);
                }
            }
            finally
            {
                Native.CloseHandle(h);
            }
        }
    }

    /// <summary>通过对路径的 HID 句柄收发报文；读用异步 OVERLAPPED 句柄（可超时），写用独立同步句柄。</summary>
    internal sealed class HidDevice : IDisposable
    {
        private readonly object _sync = new object();
        private IntPtr _readHandle;
        private IntPtr _writeHandle;
        private bool _disposed;

        public HidInterface Interface { get; private set; }

        private HidDevice() { }

        public static HidDevice Open(HidInterface info)
        {
            IntPtr read = Native.CreateFile(info.Path, Native.GENERIC_READ,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero,
                Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (read == IntPtr.Zero || read == Native.InvalidHandle) return null;

            IntPtr write = Native.CreateFile(info.Path, Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero,
                Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (write == IntPtr.Zero || write == Native.InvalidHandle)
            {
                Native.CloseHandle(read);
                return null;
            }

            var device = new HidDevice();
            device.Interface = info;
            device._readHandle = read;
            device._writeHandle = write;
            return device;
        }

        /// <summary>写一个完整的输出报文（首字节为 Report ID，长度必须等于 OutputReportByteLength）。</summary>
        public bool Write(byte[] report)
        {
            lock (_sync)
            {
                if (_disposed || _writeHandle == IntPtr.Zero) return false;
                int written;
                if (!Native.WriteFile(_writeHandle, report, report.Length, out written, IntPtr.Zero))
                    return false;
                return written == report.Length;
            }
        }

        /// <summary>等待一个输入报文，超时返回 null。</summary>
        public byte[] Read(int timeoutMs)
        {
            lock (_sync)
            {
                if (_disposed || _readHandle == IntPtr.Zero) return null;

                int length = Interface.InputReportLength > 0 ? Interface.InputReportLength : 64;
                byte[] buffer = new byte[length];
                IntPtr evt = Native.CreateEvent(IntPtr.Zero, true, false, null);
                if (evt == IntPtr.Zero) return null;

                try
                {
                    var overlapped = new Native.OverlappedIo();
                    overlapped.EventHandle = evt;
                    int read;

                    if (!Native.ReadFile(_readHandle, buffer, buffer.Length, out read, ref overlapped))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != Native.ERROR_IO_PENDING) return null;

                        if (Native.WaitForSingleObject(evt, (uint)timeoutMs) != 0)
                        {
                            // 超时：取消挂起的读，等它真正结束后再返回，避免句柄复用崩溃
                            Native.CancelIoEx(_readHandle, ref overlapped);
                            Native.WaitForSingleObject(evt, 150);
                            Native.GetOverlappedResult(_readHandle, ref overlapped, out read, false);
                            return null;
                        }

                        if (!Native.GetOverlappedResult(_readHandle, ref overlapped, out read, false))
                            return null;
                    }

                    if (read <= 0) return null;
                    byte[] result = new byte[read];
                    Array.Copy(buffer, result, read);
                    return result;
                }
                finally
                {
                    Native.CloseHandle(evt);
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                if (_writeHandle != IntPtr.Zero) { Native.CloseHandle(_writeHandle); _writeHandle = IntPtr.Zero; }
                if (_readHandle != IntPtr.Zero) { Native.CloseHandle(_readHandle); _readHandle = IntPtr.Zero; }
            }
        }
    }
}
