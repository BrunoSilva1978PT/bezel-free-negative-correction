using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BezelFreeCorrection.Calibration;
using BezelFreeCorrection.Input;
using BezelFreeCorrection.Patterns;
using BezelFreeCorrection.Topology;

namespace BezelFreeCorrection.UI;

public partial class CalibrationWindow : Window
{
    private static readonly Brush PatternBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush JunctionBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0x4E, 0xA1, 0xFF));
    private static readonly Brush ActiveJunctionBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x6B, 0x6B));
    private static readonly Brush OverlapBandBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x6B, 0x6B));

    private readonly CalibrationState _state;
    private readonly Display _display;
    private readonly List<Slice> _slices = new();

    private BitmapImage? _wallpaperCache;
    private string? _wallpaperCacheKey;

    public CalibrationWindow(CalibrationState state, Display display)
    {
        _state = state;
        _display = display;
        InitializeComponent();

        Left = display.Bounds.X;
        Top = display.Bounds.Y;
        Width = display.Bounds.Width;
        Height = display.Bounds.Height;

        _state.PropertyChanged += OnStateChanged;
        _state.Left.PropertyChanged += OnStateChanged;
        _state.Right.PropertyChanged += OnStateChanged;

        Loaded += (_, _) =>
        {
            BuildSlices();
            Render();
        };
        SizeChanged += (_, _) =>
        {
            BuildSlices();
            Render();
        };
        KeyDown += (_, e) => InputRouter.HandleKey(_state, e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _state.PropertyChanged -= OnStateChanged;
        _state.Left.PropertyChanged -= OnStateChanged;
        _state.Right.PropertyChanged -= OnStateChanged;
        base.OnClosed(e);
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) =>
        Dispatcher.Invoke(Render);

    // A slice represents a single physical monitor rendered inside this window.
    // In Separate mode a window hosts one slice; in Surround mode one window
    // hosts one slice per physical monitor covered by the logical display.
    private sealed class Slice
    {
        public int MonitorIndex;
        public Rect Bounds;
        public Canvas Host = new();
    }

    private void BuildSlices()
    {
        foreach (var s in _slices) Root.Children.Remove(s.Host);
        _slices.Clear();

        var t = _state.Topology;

        if (t.Kind == TopologyKind.Surround)
        {
            // Prefer exact per-panel bounds reported by NVAPI. Without the
            // Mosaic layout we fall back to an equal-width split which only
            // matches reality when all physical panels have the same size.
            if (t.Mosaic != null && t.Mosaic.Panels.Count > 0)
            {
                var topRow = t.Mosaic.Panels.Where(p => p.Row == 0).OrderBy(p => p.Column).ToList();
                for (var i = 0; i < topRow.Count; i++)
                {
                    var b = topRow[i].BoundsInSpan;
                    _slices.Add(new Slice
                    {
                        MonitorIndex = i,
                        Bounds = new Rect(b.X, 0, b.Width, ActualHeight),
                    });
                }
            }
            else
            {
                var monitorCount = t.JunctionXs.Count + 1;
                if (monitorCount < 1) monitorCount = 1;
                var sliceWidth = ActualWidth / monitorCount;
                for (var i = 0; i < monitorCount; i++)
                {
                    _slices.Add(new Slice
                    {
                        MonitorIndex = i,
                        Bounds = new Rect(i * sliceWidth, 0, sliceWidth, ActualHeight),
                    });
                }
            }
        }
        else
        {
            _slices.Add(new Slice
            {
                MonitorIndex = _display.Index,
                Bounds = new Rect(0, 0, ActualWidth, ActualHeight),
            });
        }

        foreach (var s in _slices)
        {
            s.Host.Width = s.Bounds.Width;
            s.Host.Height = s.Bounds.Height;
            s.Host.ClipToBounds = true;
            Canvas.SetLeft(s.Host, s.Bounds.X);
            Canvas.SetTop(s.Host, s.Bounds.Y);
            Root.Children.Add(s.Host);
        }
    }

    private void Render()
    {
        if (_slices.Count == 0) return;

        foreach (var slice in _slices)
        {
            var position = _state.GetPositionForMonitor(slice.MonitorIndex);
            var xShift = _state.XShiftForPosition(position);
            var yShift = _state.YShiftForPosition(position);
            RenderSlice(slice, xShift, yShift);
            DrawPositionControl(slice, position);
        }

        DrawJunctionOverlays();
    }

    private static string PositionLabel(int position) => position switch
    {
        CalibrationState.LeftPosition => "LEFT",
        CalibrationState.CenterPosition => "CENTER",
        CalibrationState.RightPosition => "RIGHT",
        _ => "?",
    };

    private void DrawPositionControl(Slice slice, int position)
    {
        var label = new TextBlock
        {
            Text = PositionLabel(position),
            FontSize = Math.Max(48, slice.Bounds.Height / 10.0),
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false,
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, (slice.Bounds.Width - label.DesiredSize.Width) / 2.0);
        Canvas.SetTop(label, 24);
        slice.Host.Children.Add(label);

        var hint = new TextBlock
        {
            Text = "I am the",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false,
        };
        hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var hintY = 24 + label.DesiredSize.Height + 12;
        Canvas.SetLeft(hint, (slice.Bounds.Width - hint.DesiredSize.Width) / 2.0);
        Canvas.SetTop(hint, hintY);
        slice.Host.Children.Add(hint);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        for (var p = 0; p < _state.MonitorCount; p++)
        {
            buttons.Children.Add(BuildPositionButton(position, p));
        }
        buttons.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(buttons, (slice.Bounds.Width - buttons.DesiredSize.Width) / 2.0);
        Canvas.SetTop(buttons, hintY + hint.DesiredSize.Height + 4);
        slice.Host.Children.Add(buttons);
    }

    private FrameworkElement BuildPositionButton(int currentPosition, int targetPosition)
    {
        var isActive = currentPosition == targetPosition;
        var border = new Border
        {
            Background = new SolidColorBrush(isActive
                ? Color.FromArgb(0x99, 0x1F, 0x3A, 0x5C)
                : Color.FromArgb(0x66, 0x23, 0x26, 0x2D)),
            BorderBrush = new SolidColorBrush(isActive
                ? Color.FromArgb(0xFF, 0x4E, 0xA1, 0xFF)
                : Color.FromArgb(0x99, 0x2D, 0x31, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(3, 0, 3, 0),
            Cursor = isActive ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand,
            Child = new TextBlock
            {
                Text = PositionLabel(targetPosition),
                Foreground = new SolidColorBrush(isActive
                    ? Color.FromRgb(0xBF, 0xDC, 0xFF)
                    : Color.FromRgb(0xE6, 0xE8, 0xEC)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            },
        };

        if (!isActive)
        {
            border.MouseLeftButtonDown += (_, _) =>
                _state.SwapPositions(currentPosition, targetPosition);
        }

        return border;
    }

    private void RenderSlice(Slice slice, double xShift, double yShift)
    {
        slice.Host.Children.Clear();

        // Wallpaper — or its gradient fallback if none is loaded — is the
        // permanent base layer. Any calibration overlay is drawn on top so
        // the user can toggle between guides without losing the preview of
        // the real wallpaper.
        DrawWallpaper(slice, xShift, yShift);

        if (_state.Pattern == TestPattern.HorizontalLines)
            DrawHorizontalLines(slice, xShift, yShift);
    }

    private static void DrawHorizontalLines(Slice slice, double xShift, double yShift)
    {
        const double spacing = 60.0;
        var startY = yShift % spacing;
        if (startY > 0) startY -= spacing;

        for (var y = startY; y < slice.Bounds.Height + spacing; y += spacing)
        {
            slice.Host.Children.Add(new Line
            {
                X1 = 0,
                X2 = slice.Bounds.Width,
                Y1 = y,
                Y2 = y,
                Stroke = PatternBrush,
                StrokeThickness = 1,
            });
        }

        _ = xShift; // horizontal lines are uniform across X; shift is intentionally unused
    }

    private void DrawWallpaper(Slice slice, double xShift, double yShift)
    {
        var path = _state.SourceWallpaperPath;
        BitmapImage? bmp = string.IsNullOrEmpty(path) ? null : LoadWallpaper(path);
        if (bmp is null)
        {
            slice.Host.Children.Add(new Rectangle
            {
                Width = slice.Bounds.Width,
                Height = slice.Bounds.Height,
                Fill = BuildGradientBrush(),
            });
            return;
        }

        var position = _state.GetPositionForMonitor(slice.MonitorIndex);
        slice.Host.Children.Add(BuildWallpaperContent(slice, bmp, position, xShift, yShift));
    }

    // Scales the wallpaper to span the full multi-monitor desktop, then
    // positions it so each slice samples its own portion. VOffset is
    // covered by stretching the image vertically only when this slice has
    // a non-zero yShift, avoiding black bars on the shifted slice without
    // disturbing the centre slice.
    private FrameworkElement BuildWallpaperContent(
        Slice slice,
        BitmapImage bmp,
        int position,
        double xShift,
        double yShift)
    {
        var (totalW, _) = ComputeAssumedCanvas();
        var monitorX0 = MonitorOffsetX(position);

        var scaleW = totalW / bmp.PixelWidth;
        var neededH = slice.Bounds.Height + 2 * Math.Abs(yShift);
        var scaleY = Math.Max(scaleW, neededH / bmp.PixelHeight);

        var scaledW = bmp.PixelWidth * scaleW;
        var scaledH = bmp.PixelHeight * scaleY;

        var image = new Image
        {
            Source = bmp,
            Width = scaledW,
            Height = scaledH,
            Stretch = Stretch.Fill,
        };
        Canvas.SetLeft(image, -monitorX0 + xShift);
        Canvas.SetTop(image, (slice.Bounds.Height - scaledH) / 2.0 + yShift);

        var host = new Canvas
        {
            Width = slice.Bounds.Width,
            Height = slice.Bounds.Height,
            ClipToBounds = true,
            Background = Brushes.Black,
        };
        host.Children.Add(image);
        return host;
    }

    private BitmapImage? LoadWallpaper(string path)
    {
        if (_wallpaperCacheKey == path && _wallpaperCache is not null) return _wallpaperCache;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _wallpaperCache = bmp;
            _wallpaperCacheKey = path;
            return bmp;
        }
        catch
        {
            _wallpaperCache = null;
            _wallpaperCacheKey = null;
            return null;
        }
    }

    private (double width, double height) ComputeAssumedCanvas()
    {
        // Use the same canvas the generator commits to disk so the on-
        // screen preview lines up 1:1 with the Apply output. Summing ALL
        // topology.Displays here was the bug: on rigs with more than
        // three monitors (a six-monitor sim) the preview stretched the
        // wallpaper across the entire desktop width instead of just the
        // three roles the user picked, so each chosen monitor appeared
        // to show only a tiny compressed slice of the image.
        var target = Output.WallpaperGenerator.TargetCanvas(_state);
        if (target.Width > 0 && target.Height > 0)
            return (target.Width, target.Height);

        // Fallback if roles aren't assigned yet — keep the preview drawable
        // by using the whole desktop so the slice isn't zero-sized.
        var t = _state.Topology;
        var width = t.Displays.Sum(d => d.Bounds.Width);
        var height = t.Displays.Count > 0 ? t.Displays.Max(d => d.Bounds.Height) : 1;
        return (width, height);
    }

    private double MonitorOffsetX(int position)
    {
        var t = _state.Topology;
        if (t.Kind == TopologyKind.Surround && t.Displays.Count > 0)
        {
            if (t.Mosaic != null && t.Mosaic.Panels.Count > 0)
            {
                var topRow = t.Mosaic.Panels.Where(p => p.Row == 0).OrderBy(p => p.Column).ToList();
                if (position >= 0 && position < topRow.Count)
                    return topRow[position].BoundsInSpan.X;
            }
            return position * t.Displays[0].Bounds.Width / _state.MonitorCount;
        }
        double offset = 0;
        for (var p = 0; p < position; p++)
        {
            var natural = _state.Positions[p];
            if (natural >= 0 && natural < t.Displays.Count)
                offset += t.Displays[natural].Bounds.Width;
        }
        return offset;
    }

    private double MonitorWidth(int position)
    {
        var t = _state.Topology;
        if (t.Kind == TopologyKind.Surround && t.Displays.Count > 0)
        {
            if (t.Mosaic != null && t.Mosaic.Panels.Count > 0)
            {
                var topRow = t.Mosaic.Panels.Where(p => p.Row == 0).OrderBy(p => p.Column).ToList();
                if (position >= 0 && position < topRow.Count)
                    return topRow[position].BoundsInSpan.Width;
            }
            return t.Displays[0].Bounds.Width / _state.MonitorCount;
        }
        var natural = _state.Positions[position];
        if (natural >= 0 && natural < t.Displays.Count)
            return t.Displays[natural].Bounds.Width;
        return 0;
    }

    private static Brush BuildGradientBrush()
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x20, 0x33), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x0E, 0x13, 0x20), 1));
        return gradient;
    }

    private void DrawJunctionOverlays()
    {
        for (var i = Root.Children.Count - 1; i >= 0; i--)
        {
            if (Root.Children[i] is FrameworkElement fe && fe.Tag as string == "junction")
                Root.Children.RemoveAt(i);
        }

        // Seams are only visible inside a Surround window. In Separate mode
        // each window covers one monitor, so the seam falls between windows.
        if (_state.Topology.Kind != TopologyKind.Surround) return;

        var junctionXs = ComputeLocalJunctionXs();
        for (var j = 0; j < junctionXs.Count; j++)
        {
            var localX = junctionXs[j];
            var isLeftJunction = j == 0;
            var isActive = (isLeftJunction && _state.Active == JunctionSide.Left) ||
                           (!isLeftJunction && _state.Active == JunctionSide.Right);
            var overlap = isLeftJunction ? _state.Left.Overlap : _state.Right.Overlap;
            AddJunctionVisuals(localX, overlap, isActive);
        }
    }

    // Window-local X positions of the seams between physical panels. Uses
    // the real panel boundaries from the Mosaic layout when available so the
    // overlay marker sits exactly on the bezel, even when panels differ in
    // width. Falls back to an equal-width split otherwise.
    private IReadOnlyList<double> ComputeLocalJunctionXs()
    {
        var t = _state.Topology;
        if (t.Mosaic != null && t.Mosaic.Panels.Count > 0)
        {
            return t.Mosaic.Panels
                .Where(p => p.Row == 0 && p.Column < t.Mosaic.Columns - 1)
                .OrderBy(p => p.Column)
                .Select(p => p.BoundsInSpan.Right)
                .ToList();
        }

        var result = new List<double>();
        var monitorCount = _state.MonitorCount;
        for (var j = 0; j < monitorCount - 1; j++)
            result.Add((j + 1) * ActualWidth / monitorCount);
        return result;
    }

    private void AddJunctionVisuals(double localX, int overlap, bool isActive)
    {
        if (overlap > 0)
        {
            var band = new Rectangle
            {
                Width = overlap,
                Height = ActualHeight,
                Fill = OverlapBandBrush,
                Tag = "junction",
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(band, localX - overlap / 2.0);
            Canvas.SetTop(band, 0);
            Root.Children.Add(band);
        }

        var marker = new Rectangle
        {
            Width = 2,
            Height = ActualHeight,
            Fill = isActive ? ActiveJunctionBrush : JunctionBrush,
            Tag = "junction",
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(marker, localX - 1);
        Canvas.SetTop(marker, 0);
        Root.Children.Add(marker);
    }
}
