using System;
using System.Collections.Generic;
using System.Windows;

namespace BezelFreeCorrection.Topology.Nvidia;

// Result of probing the NVIDIA driver for an active Surround / Mosaic grid.
// Panels are the physical monitors that compose a single logical span, with
// bounds expressed in span-local coordinates (origin at the span's top-left).
// Rows/Columns describe the Mosaic grid shape (e.g. 1×3 for a typical
// triple-monitor sim rig).
public sealed record NvMosaicPanel(int Row, int Column, Rect BoundsInSpan);

public sealed record NvMosaicLayout(
    uint SpanDisplayId,
    int Rows,
    int Columns,
    IReadOnlyList<NvMosaicPanel> Panels);

// Probes the NVIDIA driver for Mosaic / Surround information. All calls are
// best-effort: on non-NVIDIA systems, on GPUs without Mosaic configured, or
// when NVAPI is missing, callers get null back and should fall back to the
// plain Win32 monitor enumeration.
public static class NvMosaicDetector
{
    public static NvMosaicLayout? TryDetect()
    {
        if (!NvApi.TryInitialize()) return null;

        NvApiMosaic.GridTopoV2[] grids;
        try
        {
            grids = NvApiMosaic.EnumDisplayGrids();
        }
        catch (Exception)
        {
            // QueryInterface for a missing function id, or a driver that
            // refuses Mosaic calls on this SKU. Either way, fall back.
            return null;
        }

        // Pick the first multi-panel grid. Simulator setups only have one
        // Surround span, so we do not try to merge multiple grids here.
        foreach (var g in grids)
        {
            var panelCount = (int)g.DisplayCount;
            if (panelCount <= 1) continue;
            if (g.Rows == 0 || g.Columns == 0) continue;

            var firstDisplayId = g.Displays[0].DisplayId;
            NvApi.NvRect[] viewports;
            try
            {
                viewports = NvApiMosaic.GetDisplayViewports(firstDisplayId);
            }
            catch (Exception)
            {
                return null;
            }

            var panels = new List<NvMosaicPanel>(panelCount);
            for (var i = 0; i < panelCount; i++)
            {
                var v = viewports[i];
                var row = i / (int)g.Columns;
                var col = i % (int)g.Columns;
                var bounds = new Rect(
                    v.Left,
                    v.Top,
                    (double)v.Right - v.Left,
                    (double)v.Bottom - v.Top);
                panels.Add(new NvMosaicPanel(row, col, bounds));
            }

            return new NvMosaicLayout(firstDisplayId, (int)g.Rows, (int)g.Columns, panels);
        }

        return null;
    }
}
