using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using BezelFreeCorrection.Calibration;
using BezelFreeCorrection.Topology;
using BezelFreeCorrection.UI;

namespace BezelFreeCorrection;

// NOTE: The skeleton currently assumes 100% DPI scaling on all monitors.
// Per-monitor DPI awareness will be addressed in a later iteration because
// triple-monitor sim setups commonly run at 100%, and the extra conversion
// work is not required to validate the overall UI flow.
public partial class App : Application
{
    // Calibration windows keyed by the natural display index they cover.
    // Keeping a map lets us reconcile quickly when the user reassigns a
    // role in the HUD — we close windows for deselected monitors, open
    // new ones for newly selected ones, and leave the rest alone.
    private readonly Dictionary<int, CalibrationWindow> _calibrationWindows = new();
    private HudWindow? _hud;
    private CalibrationState? _state;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var topology = DisplayTopology.Detect();
        _state = new CalibrationState(topology);
        _state.PropertyChanged += OnStatePropertyChanged;

        ReconcileCalibrationWindows();

        _hud = new HudWindow(_state);
        var primary = topology.Primary;
        _hud.Left = primary.Bounds.X + (primary.Bounds.Width - _hud.Width) / 2.0;
        _hud.Top = primary.Bounds.Y + 80.0;
        _hud.Show();
        _hud.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_state != null) _state.PropertyChanged -= OnStatePropertyChanged;
        foreach (var w in _calibrationWindows.Values) w.Close();
        _calibrationWindows.Clear();
        _hud?.Close();
        base.OnExit(e);
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only the Positions change affects which monitors host calibration
        // windows. Everything else is redrawn inside the existing windows.
        if (e.PropertyName == nameof(CalibrationState.Positions))
        {
            Dispatcher.Invoke(ReconcileCalibrationWindows);
        }
    }

    // Opens / closes calibration windows so exactly the set of displays
    // currently referenced by state.Positions has a live window. In
    // Surround the span is always shown (one Display, always assigned).
    // In Separate unassigned roles (-1) simply contribute no window.
    private void ReconcileCalibrationWindows()
    {
        if (_state == null) return;

        var topology = _state.Topology;
        var required = new HashSet<int>();

        if (topology.Kind == TopologyKind.Surround)
        {
            // The Surround topology exposes a single span Display; the three
            // panel roles live inside it as slices, so one window covers all.
            if (topology.Displays.Count > 0) required.Add(0);
        }
        else
        {
            foreach (var idx in _state.Positions)
            {
                if (idx >= 0 && idx < topology.Displays.Count) required.Add(idx);
            }
        }

        // Close windows that are no longer part of the selection.
        foreach (var existing in _calibrationWindows.Keys.ToList())
        {
            if (!required.Contains(existing))
            {
                _calibrationWindows[existing].Close();
                _calibrationWindows.Remove(existing);
            }
        }

        // Open windows for newly required monitors.
        foreach (var idx in required)
        {
            if (_calibrationWindows.ContainsKey(idx)) continue;
            var display = topology.Displays[idx];
            var window = new CalibrationWindow(_state!, display);
            window.Show();
            _calibrationWindows[idx] = window;
        }
    }
}
