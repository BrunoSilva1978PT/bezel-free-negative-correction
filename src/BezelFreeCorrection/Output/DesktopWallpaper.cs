using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace BezelFreeCorrection.Output;

// Thin managed wrapper over Windows' IDesktopWallpaper COM interface.
// Used to push per-monitor wallpaper files onto the live desktop without
// going through the legacy SPI_SETDESKWALLPAPER, which can't set different
// images per display on multi-monitor setups.
public static class DesktopWallpaper
{
    // Apply a gallery entry's PNG files to the current desktop, matching
    // each file to the live COM monitor by rectangular bounds. Works for
    // both Surround (one file → span) and Separate (three files →
    // individual monitors). Re-applying an older entry only succeeds
    // when the monitor layout still matches the bounds recorded in the
    // entry — we surface that mismatch with a clear error.
    public static void Apply(GalleryEntry entry)
    {
        var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
        try
        {
            dw.SetPosition(DesktopWallpaperPosition.Fill);

            var monitors = Enumerate(dw);
            foreach (var file in entry.Files)
            {
                var bounds = new Rect(file.BoundsX, file.BoundsY, file.BoundsWidth, file.BoundsHeight);
                var id = MatchMonitorId(monitors, bounds)
                    ?? throw new InvalidOperationException(
                        $"No active monitor matches the layout this entry was saved with "
                        + $"(role '{file.Role}', bounds {bounds}).");
                var filePath = GalleryStore.FullFilePath(entry, file);
                dw.SetWallpaper(id, filePath);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(dw);
        }
    }

    // Clear the wallpaper on any monitor that currently matches one of
    // the bounds stored in a gallery entry. Used when the user deletes an
    // entry — Windows falls back to the background colour (black by
    // default) so the user sees the wallpaper "go away" on the matching
    // screens instead of lingering after the files are removed.
    public static void ClearForEntry(GalleryEntry entry)
    {
        var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
        try
        {
            dw.SetBackgroundColor(0); // RGB(0,0,0)
            var monitors = Enumerate(dw);
            foreach (var file in entry.Files)
            {
                var bounds = new Rect(file.BoundsX, file.BoundsY, file.BoundsWidth, file.BoundsHeight);
                var id = MatchMonitorId(monitors, bounds);
                if (id == null) continue;
                try
                {
                    dw.SetWallpaper(id, string.Empty);
                }
                catch (COMException)
                {
                    // Not every driver accepts an empty string; ignore
                    // and move on rather than block the delete.
                }
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
