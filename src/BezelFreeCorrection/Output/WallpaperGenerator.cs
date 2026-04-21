using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using BezelFreeCorrection.Calibration;
using BezelFreeCorrection.Topology;

namespace BezelFreeCorrection.Output;

// Bakes the active calibration into one or more PNG files ready to be set
// as Windows wallpapers. For Surround the span is one Windows monitor, so
// a single PNG is produced. For Separate each physical monitor owns its
// own PNG sized to that monitor's native resolution.
public static class WallpaperGenerator
{
    public readonly record struct Output(Display Display, string FilePath);

    public static IReadOnlyList<Output> Generate(CalibrationState state, string outputDirectory)
    {
        if (string.IsNullOrEmpty(state.SourceWallpaperPath))
            throw new InvalidOperationException("No source wallpaper selected.");

        Directory.CreateDirectory(outputDirectory);

        var source = LoadBgra32(state.SourceWallpaperPath);
        return state.Topology.Kind == TopologyKind.Surround
            ? GenerateSurround(state, source, outputDirectory)
            : GenerateSeparate(state, source, outputDirectory);
    }

    // A single output image matching the full Surround span. All three
    // panels are composited side by side into it using the same slice
    // geometry the preview uses, so what ships as wallpaper matches what
    // the user calibrated on screen.
    private static IReadOnlyList<Output> GenerateSurround(
        CalibrationState state,
        SourceImage source,
        string outputDirectory)
    {
        var topology = state.Topology;
        var spanDisplay = topology.Displays[0];
        var spanW = (int)Math.Round(spanDisplay.Bounds.Width);
        var spanH = (int)Math.Round(spanDisplay.Bounds.Height);

        var output = new byte[spanW * spanH * 4];

        if (topology.Mosaic != null && topology.Mosaic.Panels.Count == 3)
        {
            // Mosaic gives us the authoritative per-panel layout inside the
            // span. Every panel gets its own shift based on its position.
            var topRow = topology.Mosaic.Panels
                .Where(p => p.Row == 0)
                .OrderBy(p => p.Column)
                .ToList();
            for (var i = 0; i < topRow.Count; i++)
            {
                var panel = topRow[i];
                var panelW = (int)Math.Round(panel.BoundsInSpan.Width);
                var panelH = (int)Math.Round(panel.BoundsInSpan.Height);
                var panelX = (int)Math.Round(panel.BoundsInSpan.X);
                var panelY = (int)Math.Round(panel.BoundsInSpan.Y);
                var position = i; // left → centre → right by column order
                var xShift = state.XShiftForPosition(position);
                var yShift = state.YShiftForPosition(position);

                RenderSlice(
                    source,
                    output, spanW, spanH,
                    dstX: panelX, dstY: panelY, dstW: panelW, dstH: panelH,
                    virtualX0: panelX, virtualY0: panelY,
                    virtualCanvasW: spanW, virtualCanvasH: spanH,
                    xShift, yShift);
            }
        }
        else
        {
            // Fallback: treat the span as three equal thirds. Matches what
            // the preview does when Mosaic data is unavailable.
            var third = spanW / 3;
            for (var position = 0; position < 3; position++)
            {
                var dstX = position * third;
                var dstW = position == 2 ? spanW - 2 * third : third;
                var xShift = state.XShiftForPosition(position);
                var yShift = state.YShiftForPosition(position);

                RenderSlice(
                    source,
                    output, spanW, spanH,
                    dstX: dstX, dstY: 0, dstW: dstW, dstH: spanH,
                    virtualX0: dstX, virtualY0: 0,
                    virtualCanvasW: spanW, virtualCanvasH: spanH,
                    xShift, yShift);
            }
        }

        var outPath = Path.Combine(outputDirectory, "surround.png");
        SavePng(output, spanW, spanH, outPath);
        return new[] { new Output(spanDisplay, outPath) };
    }

    // One output image per chosen monitor. The wallpaper is conceptually
    // a continuous panorama covering the sum of the three monitor widths;
    // each monitor crops out its own slice with the per-junction shifts
    // baked in.
    private static IReadOnlyList<Output> GenerateSeparate(
        CalibrationState state,
        SourceImage source,
        string outputDirectory)
    {
        if (state.MonitorCount != 3)
            throw new InvalidOperationException(
                "Separate mode requires exactly 3 monitors to be picked before Apply.");

        var topology = state.Topology;
        var chosen = Enumerable.Range(0, 3)
            .Select(pos => topology.Displays[state.Positions[pos]])
            .ToList();

        var totalW = (int)Math.Round(chosen.Sum(d => d.Bounds.Width));
        var totalH = (int)Math.Round(chosen.Max(d => d.Bounds.Height));

        var results = new List<Output>();
        int offsetX = 0;
        for (var position = 0; position < 3; position++)
        {
            var display = chosen[position];
            var monW = (int)Math.Round(display.Bounds.Width);
            var monH = (int)Math.Round(display.Bounds.Height);
            var xShift = state.XShiftForPosition(position);
            var yShift = state.YShiftForPosition(position);

            var output = new byte[monW * monH * 4];
            RenderSlice(
                source,
                output, monW, monH,
                dstX: 0, dstY: 0, dstW: monW, dstH: monH,
                virtualX0: offsetX, virtualY0: 0,
                virtualCanvasW: totalW, virtualCanvasH: totalH,
                xShift, yShift);

            var outPath = Path.Combine(outputDirectory, PositionFileName(position));
            SavePng(output, monW, monH, outPath);
            results.Add(new Output(display, outPath));
            offsetX += monW;
        }

        return results;
    }

    private static string PositionFileName(int position) => position switch
    {
        CalibrationState.LeftPosition => "left.png",
        CalibrationState.CenterPosition => "center.png",
        CalibrationState.RightPosition => "right.png",
        _ => $"monitor{position}.png",
    };

    // Core sampling loop: every destination pixel is mapped back to a
    // position in a virtual total canvas, then to a source pixel. Shifts
    // displace that mapping horizontally and vertically so bezel overlap
    // and vertical offset are baked in. Pixels outside the source land on
    // transparent black; given the source is stretched to cover the full
    // virtual canvas this only happens when a shift samples off-edge.
    private static void RenderSlice(
        SourceImage source,
        byte[] output, int outputW, int outputH,
        int dstX, int dstY, int dstW, int dstH,
        int virtualX0, int virtualY0,
        int virtualCanvasW, int virtualCanvasH,
        double xShift, double yShift)
    {
        var srcW = source.Width;
        var srcH = source.Height;
        var scaleX = (double)srcW / virtualCanvasW;
        var scaleY = (double)srcH / virtualCanvasH;

        for (var y = 0; y < dstH; y++)
        {
            var dstRow = dstY + y;
            if (dstRow < 0 || dstRow >= outputH) continue;
            var virtY = virtualY0 + y - yShift;
            var srcYf = virtY * scaleY;

            for (var x = 0; x < dstW; x++)
            {
                var dstCol = dstX + x;
                if (dstCol < 0 || dstCol >= outputW) continue;
                var virtX = virtualX0 + x - xShift;
                var srcXf = virtX * scaleX;

                byte b = 0, g = 0, r = 0;
                byte a = 0xFF;
                var sx = (int)srcXf;
                var sy = (int)srcYf;
                if (sx >= 0 && sx < srcW && sy >= 0 && sy < srcH)
                {
                    var si = sy * source.Stride + sx * 4;
                    b = source.Pixels[si];
                    g = source.Pixels[si + 1];
                    r = source.Pixels[si + 2];
                    a = source.Pixels[si + 3];
                }

                var di = (dstRow * outputW + dstCol) * 4;
                output[di] = b;
                output[di + 1] = g;
                output[di + 2] = r;
                output[di + 3] = a;
            }
        }
    }

    private sealed class SourceImage
    {
        public byte[] Pixels { get; init; } = Array.Empty<byte>();
        public int Width { get; init; }
        public int Height { get; init; }
        public int Stride => Width * 4;
    }

    private static SourceImage LoadBgra32(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        var converted = new FormatConvertedBitmap(
            bmp, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var w = converted.PixelWidth;
        var h = converted.PixelHeight;
        var stride = w * 4;
        var pixels = new byte[h * stride];
        converted.CopyPixels(pixels, stride, 0);

        return new SourceImage { Pixels = pixels, Width = w, Height = h };
    }

    private static void SavePng(byte[] pixels, int width, int height, string path)
    {
        var stride = width * 4;
        var source = BitmapSource.Create(
            width, height,
            96, 96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
