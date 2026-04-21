using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BezelFreeCorrection.Calibration;
using BezelFreeCorrection.Input;
using BezelFreeCorrection.Output;
using BezelFreeCorrection.Patterns;
using BezelFreeCorrection.Topology;

namespace BezelFreeCorrection.UI;

public partial class HudWindow : Window
{
    private static readonly Brush ActiveBorder = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly Brush SurroundColor = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly Brush SeparateColor = new SolidColorBrush(Color.FromRgb(0x4E, 0xA1, 0xFF));

    private readonly CalibrationState _state;

    public HudWindow(CalibrationState state)
    {
        _state = state;
        InitializeComponent();

        _state.PropertyChanged += OnStateChanged;
        _state.Left.PropertyChanged += OnStateChanged;
        _state.Right.PropertyChanged += OnStateChanged;

        Loaded += (_, _) => Refresh();
        KeyDown += (_, e) => InputRouter.HandleKey(_state, e);

        // WPF sometimes drops a topmost window behind another topmost or
        // fullscreen window when focus moves. Flipping Topmost off then on
        // forces the window manager to re-stack this one at the top of
        // the always-on-top band. Cheap and reliable.
        Deactivated += (_, _) =>
        {
            Topmost = false;
            Topmost = true;
        };
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _state.PropertyChanged -= OnStateChanged;
        _state.Left.PropertyChanged -= OnStateChanged;
        _state.Right.PropertyChanged -= OnStateChanged;
        base.OnClosed(e);
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) =>
        Dispatcher.Invoke(Refresh);

    private void Refresh()
    {
        var t = _state.Topology;

        TopoLabel.Text = t.Kind == TopologyKind.Surround ? "NVIDIA Surround" : "Separate displays";
        TopoLabel.Foreground = t.Kind == TopologyKind.Surround ? SurroundColor : SeparateColor;

        if (t.Kind == TopologyKind.Surround && t.Displays.Count > 0)
        {
            var d = t.Displays[0];
            ResLabel.Text = $"{(int)d.Bounds.Width} × {(int)d.Bounds.Height} (logical)";
            OutLabel.Text = "1 file";
        }
        else
        {
            ResLabel.Text = string.Join(" · ", t.Displays.Select(d => $"{(int)d.Bounds.Width}×{(int)d.Bounds.Height}"));
            OutLabel.Text = $"{t.Displays.Count} files";
        }

        LeftOvl.Text = FormatNeg(_state.Left.Overlap);
        LeftVo.Text = _state.Left.VOffset + " px";
        RightOvl.Text = FormatNeg(_state.Right.Overlap);
        RightVo.Text = _state.Right.VOffset + " px";

        // Highlight the active overlay button so the user always sees which
        // guide is currently drawn on top of the wallpaper.
        PatNone.Style   = OverlayButtonStyle(_state.Pattern == TestPattern.None);
        PatHLines.Style = OverlayButtonStyle(_state.Pattern == TestPattern.HorizontalLines);

        var activeLeft = _state.Active == JunctionSide.Left;
        LeftBadge.Text = activeLeft ? "active" : "—";
        RightBadge.Text = activeLeft ? "—" : "active";
        LeftCard.BorderBrush = activeLeft ? ActiveBorder : Brushes.Transparent;
        RightCard.BorderBrush = activeLeft ? Brushes.Transparent : ActiveBorder;

        RefreshWallpaperSection();
        RefreshMonitorPicker();
    }

    private static readonly Brush PickerLeftFill   = new SolidColorBrush(Color.FromRgb(0x4E, 0xA1, 0xFF));
    private static readonly Brush PickerCenterFill = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly Brush PickerRightFill  = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x57));
    private static readonly Brush PickerUnusedFill = new SolidColorBrush(Color.FromRgb(0x2D, 0x31, 0x3A));

    // Builds a Windows-style mini layout of every detected monitor and
    // hooks up click handlers so the user can cycle Left / Right roles.
    // The centre is implicit (the primary monitor) and not user-editable.
    // Hidden entirely in Surround mode where the span is self-assigning.
    private void RefreshMonitorPicker()
    {
        var t = _state.Topology;
        var showPicker = t.Kind == TopologyKind.Separate && t.Displays.Count > 0;

        MonitorPickerSection.Visibility = showPicker ? Visibility.Visible : Visibility.Collapsed;
        MonitorPickerSeparator.Visibility = showPicker ? Visibility.Visible : Visibility.Collapsed;
        MonitorPickerCanvas.Children.Clear();

        if (!showPicker) return;

        // Compute the bounding rect of all monitor rects, then scale so
        // the widest axis fits inside the canvas with a small margin.
        var minX = t.Displays.Min(d => d.Bounds.X);
        var minY = t.Displays.Min(d => d.Bounds.Y);
        var maxX = t.Displays.Max(d => d.Bounds.Right);
        var maxY = t.Displays.Max(d => d.Bounds.Bottom);
        var worldW = Math.Max(1.0, maxX - minX);
        var worldH = Math.Max(1.0, maxY - minY);

        MonitorPickerCanvas.UpdateLayout();
        var canvasW = MonitorPickerCanvas.ActualWidth > 0 ? MonitorPickerCanvas.ActualWidth : 316;
        var canvasH = MonitorPickerCanvas.Height;

        const double margin = 6;
        var scale = Math.Min(
            (canvasW - 2 * margin) / worldW,
            (canvasH - 2 * margin) / worldH);
        var drawnW = worldW * scale;
        var drawnH = worldH * scale;
        var offsetX = (canvasW - drawnW) / 2.0 - minX * scale;
        var offsetY = (canvasH - drawnH) / 2.0 - minY * scale;

        for (var i = 0; i < t.Displays.Count; i++)
        {
            var display = t.Displays[i];
            var role = _state.GetPositionForMonitor(i);
            var tile = BuildMonitorTile(display, i, role);

            Canvas.SetLeft(tile, display.Bounds.X * scale + offsetX);
            Canvas.SetTop(tile, display.Bounds.Y * scale + offsetY);
            tile.Width = display.Bounds.Width * scale;
            tile.Height = display.Bounds.Height * scale;
            MonitorPickerCanvas.Children.Add(tile);
        }
    }

    private Border BuildMonitorTile(Topology.Display display, int displayIndex, int role)
    {
        var fill = role switch
        {
            CalibrationState.LeftPosition => PickerLeftFill,
            CalibrationState.CenterPosition => PickerCenterFill,
            CalibrationState.RightPosition => PickerRightFill,
            _ => PickerUnusedFill,
        };

        var label = role switch
        {
            CalibrationState.LeftPosition => "LEFT",
            CalibrationState.CenterPosition => "CENTER",
            CalibrationState.RightPosition => "RIGHT",
            _ => (displayIndex + 1).ToString(),
        };

        var primaryBadge = display.IsPrimary
            ? new TextBlock
            {
                Text = "primary",
                Foreground = new SolidColorBrush(Color.FromArgb(0xBB, 0x10, 0x10, 0x18)),
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            }
            : null;

        var roleLabel = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0x10, 0x10, 0x18)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var stack = new Grid();
        stack.Children.Add(roleLabel);
        if (primaryBadge != null) stack.Children.Add(primaryBadge);

        var border = new Border
        {
            Background = fill,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x0C, 0x0D, 0x12)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = stack,
            Cursor = display.IsPrimary ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand,
            ToolTip = $"{display.DeviceName}\n{(int)display.Bounds.Width}×{(int)display.Bounds.Height}" +
                      (display.IsPrimary ? "\n(primary — centre)" : ""),
        };

        if (!display.IsPrimary)
        {
            border.MouseLeftButtonDown += (_, _) => CycleMonitorRole(displayIndex);
        }

        return border;
    }

    // Click on a non-primary monitor cycles its role through
    // Unassigned → Left → Right → Unassigned. The centre stays on the
    // primary at all times, matching the "assume primary is centre" rule.
    private void CycleMonitorRole(int displayIndex)
    {
        var current = _state.GetPositionForMonitor(displayIndex);
        var next = current switch
        {
            -1 => CalibrationState.LeftPosition,
            CalibrationState.LeftPosition => CalibrationState.RightPosition,
            _ => -1,
        };

        if (next == -1)
        {
            // Clear whichever side was holding this monitor; assigning -1
            // keeps the other side intact because AssignMonitor only drops
            // duplicates of the incoming index, which is now a sentinel.
            if (current != -1) _state.AssignMonitor(current, -1);
        }
        else
        {
            _state.AssignMonitor(next, displayIndex);
        }
    }

    private Style OverlayButtonStyle(bool active) =>
        (Style)FindResource(active ? "PrimaryButton" : typeof(Button));

    private void RefreshWallpaperSection()
    {
        var path = _state.SourceWallpaperPath;
        if (string.IsNullOrEmpty(path))
        {
            WallpaperName.Text = "No wallpaper chosen";
            WallpaperMeta.Text = "Pick a panoramic image (spans all monitors)";
            return;
        }

        WallpaperName.Text = Path.GetFileName(path);
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            WallpaperMeta.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight}";
        }
        catch
        {
            WallpaperMeta.Text = "(unable to read image)";
        }
    }

    private static string FormatNeg(int v) => v == 0 ? "0 px" : "-" + v + " px";

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void SelectLeft(object sender, MouseButtonEventArgs e) => _state.Active = JunctionSide.Left;
    private void SelectRight(object sender, MouseButtonEventArgs e) => _state.Active = JunctionSide.Right;
    private void SelectPatternNone(object sender, RoutedEventArgs e) => _state.Pattern = TestPattern.None;
    private void SelectPatternHLines(object sender, RoutedEventArgs e) => _state.Pattern = TestPattern.HorizontalLines;

    private void Reset_Click(object sender, RoutedEventArgs e) => _state.Reset();
    private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void PickWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose panoramic wallpaper",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            _state.SourceWallpaperPath = dialog.FileName;
        }
    }

    private void ClearWallpaper_Click(object sender, RoutedEventArgs e) =>
        _state.SourceWallpaperPath = null;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_state.SourceWallpaperPath))
        {
            MessageBox.Show(this, "Pick a source wallpaper first.",
                "Wallpaper Bezel Free Correction", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var outDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallpaperBezelFreeCorrection",
                "output");

            var results = WallpaperGenerator.Generate(_state, outDir);
            DesktopWallpaper.Apply(
                results.Select(r => (r.Display, r.FilePath)).ToList());

            MessageBox.Show(this,
                $"Applied {results.Count} file(s) from:\n{outDir}",
                "Wallpaper Bezel Free Correction",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                "Wallpaper Bezel Free Correction", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
