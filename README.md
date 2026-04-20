# Bezel-Free Negative Correction

A Windows desktop utility that applies **negative bezel correction** to the desktop wallpaper for users running triple-monitor setups with bezel-free lens kits (such as the ASUS ROG Bezel-Free Kit).

## Why

Triple-monitor setups with optical bezel-free lenses refract the edge pixels of adjacent monitors so that the physical bezels are visually hidden. In racing simulators the user can compensate in-engine by applying a **negative bezel value**, which makes the viewports overlap so the lens-merged seam appears continuous.

On the Windows desktop this option does not exist. NVIDIA Surround and every existing tool only expose **positive** bezel correction (gap compensation), never negative (overlap compensation). Wallpapers and desktop content break at the seams.

This utility fills that gap for the wallpaper case.

## Status

Early work in progress.

## Planned scope

- Generate a wallpaper with per-junction negative overlap baked into pixel space.
- Support both NVIDIA Surround enabled (single logical display) and Surround disabled (three independent displays) topologies, auto-detected.
- Live on-screen preview for interactive calibration — user drags sliders until a straight line is continuous across the lenses.
- Independent overlap values per junction (left and right), because real-world setups are rarely perfectly symmetric.
- Apply the result via the official Windows `IDesktopWallpaper` COM API.

## Non-goals (for now)

- Live desktop correction (windows, cursor, taskbar).
- Per-application correction.
- Support for non-Windows platforms.

## License

TBD.
