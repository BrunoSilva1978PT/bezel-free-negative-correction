using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BezelFreeCorrection.Calibration;

// Per-junction calibration values.
//   Overlap  — pixels of overlap between adjacent monitors, stored as a
//              non-negative magnitude. The correction concept is "negative
//              bezel", so the UI presents the value as a negative number.
//   VOffset  — vertical offset in pixels applied at this junction to
//              compensate for physical misalignment between monitors.
public sealed class JunctionState : INotifyPropertyChanged
{
    private int _overlap;
    private int _vOffset;

    public int Overlap
    {
        get => _overlap;
        set => Set(ref _overlap, Math.Max(0, value));
    }

    public int VOffset
    {
        get => _vOffset;
        set => Set(ref _vOffset, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
