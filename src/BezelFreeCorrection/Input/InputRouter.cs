using System;
using System.Windows;
using System.Windows.Input;
using BezelFreeCorrection.Calibration;

namespace BezelFreeCorrection.Input;

// Central key handling so the same bindings work whether the HUD window or
// a calibration window currently has keyboard focus.
public static class InputRouter
{
    public static void HandleKey(CalibrationState state, KeyEventArgs e)
    {
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        var active = state.ActiveJunction;

        // Arrow keys follow the *outer* monitor's visual direction of travel:
        // on the left junction the outer monitor is the LEFT panel, which
        // moves further left as Overlap grows, so ← widens the gap and →
        // narrows it. On the right junction the outer monitor is the RIGHT
        // panel, and the mapping flips.
        var leftWidens = state.Active == JunctionSide.Left;

        switch (e.Key)
        {
            case Key.Tab:
                state.CycleActive();
                e.Handled = true;
                break;

            case Key.Left:
                active.Overlap = leftWidens
                    ? active.Overlap + step
                    : Math.Max(0, active.Overlap - step);
                e.Handled = true;
                break;

            case Key.Right:
                active.Overlap = leftWidens
                    ? Math.Max(0, active.Overlap - step)
                    : active.Overlap + step;
                e.Handled = true;
                break;

            case Key.Up:
                active.VOffset += step;
                e.Handled = true;
                break;

            case Key.Down:
                active.VOffset -= step;
                e.Handled = true;
                break;

            case Key.P:
                state.CyclePattern();
                e.Handled = true;
                break;

            case Key.Escape:
                Application.Current.Shutdown();
                e.Handled = true;
                break;
        }
    }
}
