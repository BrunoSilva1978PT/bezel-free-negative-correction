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

        switch (e.Key)
        {
            case Key.Tab:
                state.CycleActive();
                e.Handled = true;
                break;

            case Key.Left:
                active.Overlap = Math.Max(0, active.Overlap - step);
                e.Handled = true;
                break;

            case Key.Right:
                active.Overlap += step;
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

            default:
                if (e.Key == state.HudHotkey)
                {
                    state.HudVisible = !state.HudVisible;
                    e.Handled = true;
                }
                break;
        }
    }
}
