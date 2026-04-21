using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BezelFreeCorrection.Calibration;
using BezelFreeCorrection.Topology;
using BezelFreeCorrection.UI;
using BezelFreeCorrection.Updates;

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

    // Named mutex held for the lifetime of the process. The auto-update
    // installer can race two relaunches at the tail of a silent update
    // (Restart Manager's restart AND its own [Run] entry); letting a
    // second instance actually boot meant three fullscreen calibration
    // windows were fighting for the same monitors and the centre one
    // was losing. The mutex makes the second instance exit immediately.
    private Mutex? _singleInstanceMutex;
    private const string SingleInstanceMutexName =
        @"Local\WallpaperBezelFreeCorrection.SingleInstance";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true,
            SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        // Show the splash first thing so the user gets immediate visual
        // feedback even while the heavy start-up work runs on this
        // thread. Dispatcher.DoEvents-style UpdateLayout forces a paint
        // before we start blocking on NVAPI / monitor enumeration.
        var splash = new SplashWindow();
        splash.Show();
        splash.UpdateLayout();

        try
        {
            splash.SetStatus("Detecting displays…");
            var topology = DisplayTopology.Detect();
            _state = new CalibrationState(topology);
            _state.PropertyChanged += OnStatePropertyChanged;

            splash.SetStatus("Opening calibration windows…");
            ReconcileCalibrationWindows();

            splash.SetStatus("Loading HUD…");
            _hud = new HudWindow(_state);
            var primary = topology.Primary;
            _hud.Left = primary.Bounds.X + (primary.Bounds.Width - _hud.Width) / 2.0;
            _hud.Top = primary.Bounds.Y + 80.0;
            _hud.Show();
            _hud.Activate();
        }
        finally
        {
            splash.Close();
        }

        // Background update check. Deliberately fire-and-forget so a
        // slow or unreachable GitHub API never blocks the HUD.
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var info = await UpdateChecker.CheckAsync(
                AppInfo.GitHubOwner, AppInfo.GitHubRepo, AppInfo.Version);
            if (info == null) return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (_hud == null) return;
                _hud.ShowUpdateAvailable(info.Version, () => _ = RunInstallAsync(info));
            });
        }
        catch
        {
            // Already caught inside UpdateChecker; outer guard so a
            // surprise (e.g. dispatcher disposed on shutdown) does not
            // crash the process.
        }
    }

    private async Task RunInstallAsync(UpdateChecker.ReleaseInfo info)
    {
        try
        {
            var proc = await UpdateChecker.DownloadAndLaunchInstallerAsync(info);
            if (proc != null)
            {
                // Installer will replace files; exit so it can.
                Dispatcher.Invoke(Shutdown);
                return;
            }

            // No installer asset — fall back to opening the release
            // page in the browser so the user can download manually.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = info.HtmlUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(_hud ?? (Window)MainWindow,
                    "Could not download the update: " + ex.Message,
                    AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_state != null) _state.PropertyChanged -= OnStatePropertyChanged;
        foreach (var w in _calibrationWindows.Values) w.Close();
        _calibrationWindows.Clear();
        _hud?.Close();
        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { /* already released */ }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
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
