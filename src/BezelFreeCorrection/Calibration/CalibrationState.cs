using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using BezelFreeCorrection.Patterns;
using BezelFreeCorrection.Topology;

namespace BezelFreeCorrection.Calibration;

public sealed class CalibrationState : INotifyPropertyChanged
{
    public const int LeftPosition = 0;
    public const int CenterPosition = 1;
    public const int RightPosition = 2;

    private JunctionSide _active = JunctionSide.Left;
    private TestPattern _pattern = TestPattern.None;
    private string? _sourceWallpaperPath;

    // _positions[p] = natural slice/display index assigned to physical
    // position p (0 = left, 1 = centre, 2 = right). In Surround the index
    // is the panel column inside the Mosaic span; in Separate it indexes
    // into Topology.Displays. Always length 3 — Separate setups with more
    // than three monitors still pick exactly three roles.
    private int[] _positions;

    public DisplayTopology Topology { get; }
    public JunctionState Left { get; } = new();
    public JunctionState Right { get; } = new();

    public CalibrationState(DisplayTopology topology)
    {
        Topology = topology;
        _positions = PickInitialPositions(topology);
    }

    // Always three: one monitor (or panel) per physical position. When the
    // input topology has fewer panels the remaining slots get filled with
    // -1 so callers can detect a missing role rather than index out.
    public int MonitorCount => 3;

    public IReadOnlyList<int> Positions => _positions;

    public JunctionState ActiveJunction => _active == JunctionSide.Left ? Left : Right;

    public JunctionSide Active
    {
        get => _active;
        set => Set(ref _active, value);
    }

    public TestPattern Pattern
    {
        get => _pattern;
        set => Set(ref _pattern, value);
    }

    public string? SourceWallpaperPath
    {
        get => _sourceWallpaperPath;
        set => Set(ref _sourceWallpaperPath, value);
    }

    // Returns the physical position (0 = left, 1 = center, 2 = right) of the
    // monitor whose natural index is `monitorIndex`, or -1 if that monitor
    // is not currently assigned to any role.
    public int GetPositionForMonitor(int monitorIndex) =>
        Array.IndexOf(_positions, monitorIndex);

    // Natural display index currently assigned to the given physical
    // position, or -1 if unassigned.
    public int GetMonitorAtPosition(int position)
    {
        if (position < 0 || position >= _positions.Length) return -1;
        return _positions[position];
    }

    // Assign a specific natural display index to a role. Any other role
    // previously holding the same index is cleared to -1 so no monitor is
    // accidentally double-booked.
    public void AssignMonitor(int position, int displayIndex)
    {
        if (position < 0 || position >= _positions.Length) return;
        if (_positions[position] == displayIndex) return;

        if (displayIndex >= 0)
        {
            // Drop duplicates so no display is double-booked. -1 is the
            // sentinel for "no monitor", which many positions can share.
            for (var i = 0; i < _positions.Length; i++)
            {
                if (i == position) continue;
                if (_positions[i] == displayIndex) _positions[i] = -1;
            }
        }
        _positions[position] = displayIndex;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Positions)));
    }

    // Horizontal shift applied to the monitor at position `position`, anchored
    // on the center monitor so each junction's control only moves the outer
    // monitor of that junction. The outer monitors are pushed *outward* by
    // the overlap magnitude because a bezel-free lens (Asus Bezel Free Kit
    // and similar) already refracts content inward across the seam; the
    // software has to widen the gap in source space so the lens-merged
    // image ends up continuous.
    public double XShiftForPosition(int position) => position switch
    {
        LeftPosition => -Left.Overlap,
        CenterPosition => 0,
        RightPosition => Right.Overlap,
        _ => 0,
    };

    public double YShiftForPosition(int position) => position switch
    {
        LeftPosition => Left.VOffset,
        CenterPosition => 0,
        RightPosition => Right.VOffset,
        _ => 0,
    };

    public void SwapPositions(int positionA, int positionB)
    {
        if (positionA == positionB) return;
        if (positionA < 0 || positionA >= _positions.Length) return;
        if (positionB < 0 || positionB >= _positions.Length) return;

        (_positions[positionA], _positions[positionB]) = (_positions[positionB], _positions[positionA]);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Positions)));
    }

    public void CycleActive() =>
        Active = _active == JunctionSide.Left ? JunctionSide.Right : JunctionSide.Left;

    public void CyclePattern()
    {
        Pattern = _pattern switch
        {
            TestPattern.None => TestPattern.HorizontalLines,
            _ => TestPattern.None,
        };
    }

    public void Reset()
    {
        Left.Overlap = 0;
        Left.VOffset = 0;
        Right.Overlap = 0;
        Right.VOffset = 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Initial role assignment:
    //   * Surround — slots are panel columns (0, 1, 2).
    //   * Separate with exactly 3 monitors — keep desktop X order.
    //   * Separate with more monitors — centre takes the primary, left and
    //     right take the nearest monitors on each side by desktop X. Extras
    //     stay unassigned (the user picks them in the HUD if desired).
    private static int[] PickInitialPositions(DisplayTopology topology)
    {
        if (topology.Kind == TopologyKind.Surround)
            return new[] { 0, 1, 2 };

        var displays = topology.Displays;
        if (displays.Count == 0) return new[] { -1, -1, -1 };

        // Centre is always the primary monitor. Left and right are the
        // nearest monitors on either side of the primary by desktop X.
        // This works for 3 monitors (deterministic order) and for more
        // (user can re-pick in the HUD).
        var primaryIdx = -1;
        for (var i = 0; i < displays.Count; i++)
        {
            if (displays[i].IsPrimary) { primaryIdx = i; break; }
        }
        if (primaryIdx < 0) primaryIdx = displays.Count / 2;

        var primaryX = displays[primaryIdx].Bounds.X;
        var leftCandidate = -1;
        var rightCandidate = -1;
        var leftBestDist = double.PositiveInfinity;
        var rightBestDist = double.PositiveInfinity;
        for (var i = 0; i < displays.Count; i++)
        {
            if (i == primaryIdx) continue;
            var dx = displays[i].Bounds.X - primaryX;
            if (dx < 0 && -dx < leftBestDist) { leftBestDist = -dx; leftCandidate = i; }
            else if (dx > 0 && dx < rightBestDist) { rightBestDist = dx; rightCandidate = i; }
        }

        return new[] { leftCandidate, primaryIdx, rightCandidate };
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(Active))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveJunction)));
        }
    }
}
