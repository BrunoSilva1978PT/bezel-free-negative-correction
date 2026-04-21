using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using BezelFreeCorrection.Topology;

namespace BezelFreeCorrection.Output;

// Thin managed wrapper over Windows' IDesktopWallpaper COM interface.
// Used to push per-monitor wallpaper files onto the live desktop without
// going through the legacy SPI_SETDESKWALLPAPER, which can't set different
// images per display on multi-monitor setups.
public static class DesktopWallpaper
{
    // Apply the per-display assignments produced by WallpaperGenerator.
    // Each entry pairs a Display (as we detected it) with the PNG file
    // that should appear on it. The Display's bounds are matched to the
    // COM-side monitor device path by rectangle, which works for both
    // Surround (one logical monitor) and Separate setups.
    public static void Apply(IReadOnlyList<(Display Display, string FilePath)> assignments)
    {
        var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
        try
        {
            // Fill so the generated bitmap covers the target monitor 1:1
            // without re-scaling; we already emitted at the exact pixel
            // dimensions so Fill behaves as identity.
            dw.SetPosition(DesktopWallpaperPosition.Fill);

            var monitors = Enumerate(dw);
            foreach (var (display, filePath) in assignments)
            {
                var id = MatchMonitorId(monitors, display.Bounds)
                    ?? throw new InvalidOperationException(
                        $"No COM monitor path matches display '{display.DeviceName}' with bounds {display.Bounds}.");
                dw.SetWallpaper(id, filePath);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(dw);
        }
    }

    private static string? MatchMonitorId(IReadOnlyList<(string Id, Rect Bounds)> monitors, Rect target)
    {
        foreach (var (id, bounds) in monitors)
        {
            if (Math.Abs(bounds.X - target.X) < 1 &&
                Math.Abs(bounds.Y - target.Y) < 1 &&
                Math.Abs(bounds.Width - target.Width) < 1 &&
                Math.Abs(bounds.Height - target.Height) < 1)
            {
                return id;
            }
        }
        return null;
    }

    private static IReadOnlyList<(string Id, Rect Bounds)> Enumerate(IDesktopWallpaper dw)
    {
        dw.GetMonitorDevicePathCount(out var count);
        var list = new List<(string, Rect)>((int)count);
        for (uint i = 0; i < count; i++)
        {
            dw.GetMonitorDevicePathAt(i, out var path);
            // Skip inactive / detached monitors — GetMonitorRECT throws on
            // them. Active monitors return their bounds in desktop coords.
            try
            {
                dw.GetMonitorRECT(path, out var r);
                list.Add((path, new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)));
            }
            catch (COMException)
            {
                // Monitor path exists in the registry but is not currently
                // attached; the driver would reject SetWallpaper on it too.
            }
        }
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private enum DesktopWallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5,
    }

    // Vtable order must match the C++ declaration in ShObjIdl_core.h.
    // Only the members actually called are bound; the rest are reserved
    // so the runtime still produces a valid vtable.
    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)] string monitorID,
            [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        void GetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)] string monitorID,
            [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);

        void GetMonitorDevicePathAt(
            uint monitorIndex,
            [MarshalAs(UnmanagedType.LPWStr)] out string monitorID);

        void GetMonitorDevicePathCount(out uint count);

        void GetMonitorRECT(
            [MarshalAs(UnmanagedType.LPWStr)] string monitorID,
            out RECT displayRect);

        void SetBackgroundColor(uint color);
        void GetBackgroundColor(out uint color);
        void SetPosition(DesktopWallpaperPosition position);
        void GetPosition(out DesktopWallpaperPosition position);
        // SetSlideshow, GetSlideshow, etc. omitted; unused.
    }

    [ComImport]
    [Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }
}
