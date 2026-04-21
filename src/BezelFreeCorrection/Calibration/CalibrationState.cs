using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using BezelFreeCorrection.Patterns;
using BezelFreeCorrection.Topology;

namespace BezelFreeCorrection.Calibration;

public sealed class CalibrationState : INotifyPropertyChanged
{
    public const int LeftPosition = 0;
    public const int CenterPosition = 1;
    public const int RightPosition = 2;

    private JunctionSide _active = JunctionSide.Left;
    private TestPattern _pattern = TestPattern.HorizontalLines;
    private bool _hudVisible = true;
    private Key _hudHotkey = Key.H;
    private string? _sourceWallpaperPath;

    // _positions[p] is the topology index assigned to physical position p
    // (0 = left, 1 = center, 2 = right). Initialized from desktop X order and
    // can be reassigned by the user via the HUD.
    private int[] _positions;

    public DisplayTopology Topology { get; }
    public JunctionState Left { get; } = new();
    public JunctionState Right { get; } = new();

    public CalibrationState(DisplayTopology topology)
    {
        Topology = topology;
        _positions = Enumerable.Range(0, MonitorCount).ToArray();
    }

    public int MonitorCount =>
        Topology.Kind == TopologyKind.Surround
            ? Topology.JunctionXs.Count + 1
            : Topology.Displays.Count;

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

    public bool HudVisible
    {
        get => _hudVisible;
        set => Set(ref _hudVisible, value);
    }

    public Key HudHotkey
    {
        get => _hudHotkey;
        set => Set(ref _hudHotkey, value);
    }

    public string? SourceWallpaperPath
    {
        get => _sourceWallpaperPath;
        set => Set(ref _sourceWallpaperPath, value);
    }

    // Returns the physical position (0 = left, 1 = center, 2 = right) of the
    // monitor whose natural index is `monitorIndex`.
    public int GetPositionForMonitor(int monitorIndex) =>
        Array.IndexOf(_positions, monitorIndex);

    // Horizontal shift applied to the monitor at position `position`, anchored
    // on the center monitor so each junction's control only moves the outer
    // monitor of that junction.
    public double XShiftForPosition(int position)
    {
        if (MonitorCount != 3) return 0;
        return position switch
        {
            LeftPosition => Left.Overlap,
            CenterPosition => 0,
            RightPosition => -Right.Overlap,
            _ => 0,
        };
    }

    public double YShiftForPosition(int position)
    {
        if (MonitorCount != 3) return 0;
        return position switch
        {
            LeftPosition => Left.VOffset,
            CenterPosition => 0,
            RightPosition => Right.VOffset,
            _ => 0,
        };
    }

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
            TestPattern.HorizontalLines => TestPattern.Diagonals,
            TestPattern.Diagonals => TestPattern.Wallpaper,
            _ => TestPattern.HorizontalLines,
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
