using System.Collections.Generic;
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
    private readonly List<CalibrationWindow> _calibrationWindows = new();
    private HudWindow? _hud;
    private CalibrationState? _state;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var topology = DisplayTopology.Detect();
        _state = new CalibrationState(topology);

        foreach (var display in topology.Displays)
        {
            var window = new CalibrationWindow(_state, display);
            window.Show();
            _calibrationWindows.Add(window);
        }

        _hud = new HudWindow(_state);
        var primary = topology.Primary;
        _hud.Left = primary.Bounds.X + (primary.Bounds.Width - _hud.Width) / 2.0;
        _hud.Top = primary.Bounds.Y + 80.0;
        _hud.Show();
        _hud.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var w in _calibrationWindows) w.Close();
        _hud?.Close();
        base.OnExit(e);
    }
}
