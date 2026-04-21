using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace BezelFreeCorrection.Topology;

// Wraps Win32 EnumDisplayMonitors + GetMonitorInfoW.
// Bounds are returned in physical pixels; the app currently assumes 100% DPI
// so these are used directly as WPF DIP coordinates.
internal static class MonitorEnumerator
{
    internal readonly record struct RawMonitor(string DeviceName, Rect Bounds, bool IsPrimary);

    private const int CCHDEVICENAME = 32;
    private const uint MONITORINFOF_PRIMARY = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    public static IReadOnlyList<RawMonitor> Enumerate()
    {
        var result = new List<RawMonitor>();

        bool Callback(IntPtr hMonitor, IntPtr _, ref RECT __, IntPtr ___)
        {
            var info = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoW(hMonitor, ref info))
            {
                var r = info.rcMonitor;
                result.Add(new RawMonitor(
                    info.szDevice,
                    new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top),
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return result;
    }
}
