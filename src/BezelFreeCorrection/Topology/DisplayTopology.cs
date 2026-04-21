using System.Collections.Generic;
using System.Linq;

namespace BezelFreeCorrection.Topology;

public sealed class DisplayTopology
{
    public IReadOnlyList<Display> Displays { get; }
    public TopologyKind Kind { get; }
    public IReadOnlyList<double> JunctionXs { get; }
    public Display Primary => Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays[0];

    private DisplayTopology(IReadOnlyList<Display> displays, TopologyKind kind, IReadOnlyList<double> junctionXs)
    {
        Displays = displays;
        Kind = kind;
        JunctionXs = junctionXs;
    }

    public static DisplayTopology Detect()
    {
        var raw = MonitorEnumerator.Enumerate();
        var ordered = raw
            .OrderBy(m => m.Bounds.X)
            .Select((m, i) => new Display(i, m.DeviceName, m.Bounds, m.IsPrimary))
            .ToList();

        if (ordered.Count == 0)
        {
            return new DisplayTopology(ordered, TopologyKind.Separate, new List<double>());
        }

        var junctions = new List<double>();
        TopologyKind kind;

        if (ordered.Count == 1)
        {
            // Heuristic: a logical display whose aspect exceeds 3:1 is almost certainly
            // a Surround or Eyefinity span of three horizontal monitors. Single ultra-wide
            // panels (32:9) stay below this threshold.
            var d = ordered[0];
            var aspect = d.Bounds.Width / d.Bounds.Height;
            kind = aspect > 3.0 ? TopologyKind.Surround : TopologyKind.Separate;

            if (kind == TopologyKind.Surround)
            {
                junctions.Add(d.Bounds.X + d.Bounds.Width / 3.0);
                junctions.Add(d.Bounds.X + 2.0 * d.Bounds.Width / 3.0);
            }
        }
        else
        {
            kind = TopologyKind.Separate;
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                junctions.Add(ordered[i].Bounds.Right);
            }
        }

        return new DisplayTopology(ordered, kind, junctions);
    }
}
