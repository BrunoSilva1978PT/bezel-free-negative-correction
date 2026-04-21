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

        var activeLeft = _state.Active == JunctionSide.Left;
        LeftBadge.Text = activeLeft ? "active" : "—";
        RightBadge.Text = activeLeft ? "—" : "active";
        LeftCard.BorderBrush = activeLeft ? ActiveBorder : Brushes.Transparent;
        RightCard.BorderBrush = activeLeft ? Brushes.Transparent : ActiveBorder;

        Visibility = _state.HudVisible ? Visibility.Visible : Visibility.Hidden;

        RefreshWallpaperSection();
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
    private void SelectPatternHLines(object sender, RoutedEventArgs e) => _state.Pattern = TestPattern.HorizontalLines;
    private void SelectPatternDiag(object sender, RoutedEventArgs e) => _state.Pattern = TestPattern.Diagonals;
    private void SelectPatternWall(object sender, RoutedEventArgs e) => _state.Pattern = TestPattern.Wallpaper;

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
        // Wallpaper generation + IDesktopWallpaper invocation lands in a later iteration.
        MessageBox.Show(this, "Apply is not implemented yet.", "Bezel Correction", MessageBoxButton.OK);
    }
}
