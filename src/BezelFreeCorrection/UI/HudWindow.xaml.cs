using System;
using System.Collections.Generic;
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
        VersionLabel.Text = $"v{AppInfo.Version}";

        _state.PropertyChanged += OnStateChanged;
        _state.Left.PropertyChanged += OnStateChanged;
        _state.Right.PropertyChanged += OnStateChanged;

        Loaded += (_, _) =>
        {
            Refresh();
            RefreshGallery();
        };
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

        var target = WallpaperGenerator.TargetCanvas(_state);
        TargetLabel.Text = target.Width > 0
            ? $"{target.Width} × {target.Height}"
            : "— pick 3 monitors —";

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

    // Flat square button template, used by the gallery × button so the
    // glyph is fully visible and the hover state signals "destructive".
    private static ControlTemplate BuildFlatButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(0xB8, 0x2D, 0x2D)), "Bd"));
        template.Triggers.Add(hoverTrigger);
        return template;
    }

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

            var target = WallpaperGenerator.TargetCanvas(_state);
            if (target.Width > 0 &&
                (bmp.PixelWidth != target.Width || bmp.PixelHeight != target.Height))
            {
                WallpaperMeta.Text =
                    $"{bmp.PixelWidth} × {bmp.PixelHeight} " +
                    $"→ cover-fit to {target.Width} × {target.Height}";
            }
            else
            {
                WallpaperMeta.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight}";
            }
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
            ApplyStatus.Text = "Pick a source wallpaper first.";
            ApplyStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            return;
        }

        try
        {
            var entry = WallpaperGenerator.Generate(_state);
            DesktopWallpaper.Apply(entry);
            RefreshGallery();
            ApplyStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
            ApplyStatus.Text =
                $"✓ Applied at {DateTime.Now:HH:mm:ss}  ({entry.Topology}, {entry.Files.Count} file(s))";
        }
        catch (Exception ex)
        {
            ApplyStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            ApplyStatus.Text = "Apply failed: " + ex.Message;
        }
    }

    // Rebuilds the GALLERY strip from disk. Called on HUD load and after
    // each Apply. Newest entry appears on the left; empty state shows a
    // hint instead of the scroll view.
    private void RefreshGallery()
    {
        GalleryList.Children.Clear();
        var entries = GalleryStore.List();
        if (entries.Count == 0)
        {
            GalleryEmpty.Visibility = Visibility.Visible;
            GalleryHint.Visibility = Visibility.Collapsed;
            GalleryScroll.Visibility = Visibility.Collapsed;
            return;
        }
        GalleryEmpty.Visibility = Visibility.Collapsed;
        GalleryHint.Visibility = Visibility.Visible;
        GalleryScroll.Visibility = Visibility.Visible;
        foreach (var entry in entries)
        {
            GalleryList.Children.Add(BuildGalleryTile(entry));
        }
    }

    private FrameworkElement BuildGalleryTile(GalleryEntry entry)
    {
        var thumbPath = entry.FolderPath != null
            ? System.IO.Path.Combine(entry.FolderPath, GalleryStore.ThumbFile)
            : string.Empty;

        var thumb = new System.Windows.Controls.Image
        {
            Width = 120,
            Height = 28,
            Stretch = Stretch.UniformToFill,
        };
        if (!string.IsNullOrEmpty(thumbPath) && System.IO.File.Exists(thumbPath))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(thumbPath);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            thumb.Source = bmp;
        }

        var nameLabel = new TextBlock
        {
            Text = entry.DisplayName,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xEC)),
            FontSize = 10,
            MaxWidth = 120,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var dateLabel = new TextBlock
        {
            Text = entry.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x99)),
            FontSize = 9,
        };

        // Small pill badge colour-coded by topology so the user can tell
        // Surround entries from Separate ones at a glance.
        var isSurround = string.Equals(entry.Topology, "Surround", StringComparison.OrdinalIgnoreCase);
        var badge = new Border
        {
            Background = new SolidColorBrush(isSurround
                ? Color.FromRgb(0x1D, 0x3E, 0x2C)
                : Color.FromRgb(0x1F, 0x3A, 0x5C)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 3, 0, 0),
            Child = new TextBlock
            {
                Text = isSurround ? "SURROUND" : "SEPARATE",
                Foreground = new SolidColorBrush(isSurround
                    ? Color.FromRgb(0x7F, 0xE4, 0xA5)
                    : Color.FromRgb(0xBF, 0xDC, 0xFF)),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
            },
        };

        var content = new StackPanel();
        content.Children.Add(thumb);
        content.Children.Add(nameLabel);
        content.Children.Add(dateLabel);
        content.Children.Add(badge);

        // Dedicated delete button, positioned top-right of the tile so
        // it's obvious at a glance. Template is set to null so the
        // per-button colours below are the only thing that paints it —
        // the inherited Button template was eating the × glyph visually.
        var deleteBtn = new Button
        {
            Content = "✕",
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x8A, 0x1F, 0x1F)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(0),
            ToolTip = "Delete this entry and clear the wallpaper on its monitors",
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = BuildFlatButtonTemplate(),
        };
        deleteBtn.Click += (_, _) =>
        {
            var answer = MessageBox.Show(this,
                $"Delete '{entry.DisplayName}' and clear it from the desktop?",
                "Wallpaper Bezel Free Correction",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                DesktopWallpaper.ClearForEntry(entry);
            }
            catch (Exception)
            {
                // Desktop clearing is best-effort; still proceed to delete
                // the files even if Windows refused to reset the wallpaper.
            }
            GalleryStore.Delete(entry);
            RefreshGallery();
        };

        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(deleteBtn);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x21, 0x29)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x31, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 6, 0),
            Width = 132,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = grid,
            ToolTip = $"{entry.DisplayName}\n{entry.Topology}\n" +
                      "Click: load these settings back into the HUD for editing\n" +
                      "× button (top-right): delete this entry",
        };
        border.MouseLeftButtonDown += (_, args) =>
        {
            // Walk up the visual tree from the clicked element to check
            // whether the click came from inside the delete button. If it
            // did, leave the load alone so deleting never also loads.
            var src = args.OriginalSource as DependencyObject;
            while (src != null)
            {
                if (ReferenceEquals(src, deleteBtn)) return;
                src = System.Windows.Media.VisualTreeHelper.GetParent(src);
            }
            LoadEntryIntoState(entry);
        };
        return border;
    }

    // Brings a gallery entry's settings back into the live state so the
    // user can tweak them and press Apply to regenerate. The saved PNGs
    // themselves are not pushed to the desktop here — that only happens
    // when Apply is clicked. In Separate mode the saved per-file bounds
    // are matched back to the current displays so the role assignments
    // (Left / Centre / Right) snap back too. In Surround there is no
    // selection to restore — the span is always the single target.
    private void LoadEntryIntoState(GalleryEntry entry)
    {
        try
        {
            if (!string.IsNullOrEmpty(entry.SourcePath) && System.IO.File.Exists(entry.SourcePath))
                _state.SourceWallpaperPath = entry.SourcePath;

            _state.Left.Overlap = entry.LeftOverlap;
            _state.Right.Overlap = entry.RightOverlap;
            _state.Left.VOffset = entry.LeftVOffset;
            _state.Right.VOffset = entry.RightVOffset;

            if (_state.Topology.Kind == TopologyKind.Separate)
            {
                var displays = _state.Topology.Displays;
                foreach (var file in entry.Files)
                {
                    var pos = RoleToPosition(file.Role);
                    if (pos < 0) continue;
                    var idx = FindDisplayByBounds(displays,
                        file.BoundsX, file.BoundsY, file.BoundsWidth, file.BoundsHeight);
                    if (idx >= 0) _state.AssignMonitor(pos, idx);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                "Wallpaper Bezel Free Correction", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static int RoleToPosition(string role) => role?.ToLowerInvariant() switch
    {
        "left" => CalibrationState.LeftPosition,
        "center" => CalibrationState.CenterPosition,
        "right" => CalibrationState.RightPosition,
        _ => -1,
    };

    private static int FindDisplayByBounds(
        IReadOnlyList<Topology.Display> displays,
        double x, double y, double w, double h)
    {
        for (var i = 0; i < displays.Count; i++)
        {
            var b = displays[i].Bounds;
            if (Math.Abs(b.X - x) < 1 &&
                Math.Abs(b.Y - y) < 1 &&
                Math.Abs(b.Width - w) < 1 &&
                Math.Abs(b.Height - h) < 1)
                return i;
        }
        return -1;
    }
}
