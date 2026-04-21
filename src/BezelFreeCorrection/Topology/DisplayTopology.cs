using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BezelFreeCorrection.Topology.Nvidia;

namespace BezelFreeCorrection.Topology;

public sealed class DisplayTopology
{
    public IReadOnlyList<Display> Displays { get; }
    public TopologyKind Kind { get; }
    public IReadOnlyList<double> JunctionXs { get; }
    public Display Primary => Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays[0];

    // Physical panel layout reported by NVAPI when a Surround / Mosaic span
    // is active. Null when Mosaic could not be queried (non-NVIDIA GPU,
    // driver without NVAPI, or Mosaic not configured). When present, the
    // calibration UI uses the exact per-panel widths instead of splitting
    // the span by equal fractions.
    public NvMosaicLayout? Mosaic { get; }

    private DisplayTopology(
        IReadOnlyList<Display> displays,
        TopologyKind kind,
        IReadOnlyList<double> junctionXs,
        NvMosaicLayout? mosaic)
    {
        Displays = displays;
        Kind = kind;
        JunctionXs = junctionXs;
        Mosaic = mosaic;
    }

    public static DisplayTopology Detect()
    {
        var raw = MonitorEnumerator.Enumerate();
        if (raw.Count == 0)
        {
            return new DisplayTopology(
                new List<Display>(), TopologyKind.Separate, new List<double>(), null);
        }

        // Authoritative path: ask the NVIDIA driver for the current Mosaic
        // grid. When present, only the span itself is exposed as a Display
        // so downstream code never spreads onto auxiliary (non-Surround)
        // monitors that Windows also reports.
        var mosaic = NvMosaicDetector.TryDetect();
        if (mosaic != null)
        {
            var surround = BuildSurroundTopology(raw, mosaic);
            if (surround != null) return surround;
        }

        // Fallback: no Mosaic. Treat every Windows monitor as a separate
        // display in left-to-right order. This path does not try to guess
        // Surround from aspect ratio any more — when the driver reports no
        // Mosaic, we trust that.
        var ordered = raw
            .OrderBy(m => m.Bounds.X)
            .Select((m, i) => new Display(i, m.DeviceName, m.Bounds, m.IsPrimary))
            .ToList();

        var junctions = new List<double>();
        for (var i = 0; i < ordered.Count - 1; i++)
            junctions.Add(ordered[i].Bounds.Right);

        return new DisplayTopology(ordered, TopologyKind.Separate, junctions, null);
    }

    // Builds a Surround topology anchored on the Windows monitor that hosts
    // the Mosaic span. The span itself remains a single Display; per-panel
    // geometry is carried in Mosaic so the calibration window can split
    // itself along the real panel boundaries (which may be asymmetric).
    private static DisplayTopology? BuildSurroundTopology(
        IReadOnlyList<MonitorEnumerator.RawMonitor> raw,
        NvMosaicLayout mosaic)
    {
        // The Mosaic span is the monitor with the largest logical area —
        // by construction, any other Windows monitor is smaller, since the
        // span itself is the sum of its physical panels.
        var spanMonitor = raw.OrderByDescending(m => m.Bounds.Width * m.Bounds.Height).First();

        var spanDisplay = new Display(
            Index: 0,
            DeviceName: spanMonitor.DeviceName,
            Bounds: spanMonitor.Bounds,
            IsPrimary: spanMonitor.IsPrimary);

        // Horizontal junctions along the top row, in desktop X coordinates.
        // Multi-row Mosaic grids (e.g. 2×2) still expose only row-0
        // junctions here — the calibration pass this UI supports is
        // horizontal, and extending to vertical junctions is a follow-up.
        var junctions = mosaic.Panels
            .Where(p => p.Row == 0 && p.Column < mosaic.Columns - 1)
            .OrderBy(p => p.Column)
            .Select(p => spanMonitor.Bounds.X + p.BoundsInSpan.Right)
            .ToList();

        return new DisplayTopology(
            new[] { spanDisplay },
            TopologyKind.Surround,
            junctions,
            mosaic);
    }
}
