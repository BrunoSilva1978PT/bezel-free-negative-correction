using System.Windows;

namespace BezelFreeCorrection.Topology;

public sealed record Display(
    int Index,
    string DeviceName,
    Rect Bounds,
    bool IsPrimary);
