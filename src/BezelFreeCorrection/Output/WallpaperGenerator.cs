using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BezelFreeCorrection.Calibration;
using BezelFreeCorrection.Topology;

namespace BezelFreeCorrection.Output;

// Bakes the active calibration into one or more PNG files ready to be set
// as Windows wallpapers. For Surround the span is one Windows monitor, so
// a single PNG is produced. For Separate each physical monitor owns its
// own PNG sized to that monitor's native resolution. Every run writes to
// its own time-stamped subfolder plus a meta.json so the HUD gallery can
// list and re-apply previous results without regenerating them.
public static class WallpaperGenerator
{
    private const int ThumbWidth = 240;

    // Total canvas the user's wallpaper is expected to fill. For Surround
    // the span IS the canvas. For Separate the canvas is the sum of the
    // three chosen monitor widths by the tallest chosen height. Computed
    // in a single place so HUD hints and render scaling stay consistent.
    public static (int Width, int Height) TargetCanvas(CalibrationState state)
    {
        var topology = state.Topology;
        if (topology.Kind == TopologyKind.Surround)
        {
            if (topology.Displays.Count == 0) return (0, 0);
            var span = topology.Displays[0];
            return ((int)Math.Round(span.Bounds.Width), (int)Math.Round(span.Bounds.Height));
        }
        if (state.MonitorCount != 3) return (0, 0);
        var chosen = Enumerable.Range(0, 3)
            .Select(pos => state.Positions[pos])
            .Where(idx => idx >= 0 && idx < topology.Displays.Count)
            .Select(idx => topology.Displays[idx])
            .ToList();
        if (chosen.Count != 3) return (0, 0);
        var totalW = (int)Math.Round(chosen.Sum(d => d.Bounds.Width));
        var totalH = (int)Math.Round(chosen.Max(d => d.Bounds.Height));
        return (totalW, totalH);
    }

    public static GalleryEntry Generate(CalibrationState state)
    {
        if (string.IsNullOrEmpty(state.SourceWallpaperPath))
            throw new InvalidOperationException("No source wallpaper selected.");

        // If the current state already matches an existing entry (same
        // source + topology + bezel values), drop the older one so the
        // gallery doesn't pile up duplicates when the user re-applies
        // a loaded entry without changes.
        var topologyLabel = state.Topology.Kind == TopologyKind.Surround ? "Surround" : "Separate";
        var duplicate = GalleryStore.List().FirstOrDefault(e =>
            e.SourcePath == state.SourceWallpaperPath
            && e.Topology == topologyLabel
            && e.LeftOverlap == state.Left.Overlap
            && e.RightOverlap == state.Right.Overlap
            && e.LeftVOffset == state.Left.VOffset
            && e.RightVOffset == state.Right.VOffset);
        if (duplicate != null)
            GalleryStore.Delete(duplicate);

        var id = GalleryStore.NewEntryId();
        var folder = GalleryStore.EntryFolder(id);
        Directory.CreateDirectory(folder);

        // Pre-scale the source to the target canvas so the per-pixel loop
        // below samples 1:1 regardless of what the user fed in. This also
        // means a source with the wrong aspect ratio gets centre-cropped
        // to preserve its aspect instead of non-uniformly stretched, which
        // is what was producing the weird "crop + bad division" symptom
        // on non-Surround rigs whose monitor widths didn't sum exactly to
        // the source wallpaper width.
        var target = TargetCanvas(state);
        var source = LoadAndFitToTarget(state.SourceWallpaperPath, target.Width, target.Height);

        var entry = new GalleryEntry
        {
            Id = id,
            SourcePath = state.SourceWallpaperPath,
            DisplayName = Path.GetFileNameWithoutExtension(state.SourceWallpaperPath) ?? id,
            CreatedUtc = DateTime.UtcNow,
            Topology = state.Topology.Kind == TopologyKind.Surround ? "Surround" : "Separate",
            LeftOverlap = state.Left.Overlap,
            RightOverlap = state.Right.Overlap,
            LeftVOffset = state.Left.VOffset,
            RightVOffset = state.Right.VOffset,
            FolderPath = folder,
        };

        if (state.Topology.Kind == TopologyKind.Surround)
            GenerateSurround(state, source, folder, entry);
        else
            GenerateSeparate(state, source, folder, entry);

        WriteThumbnail(entry);
        GalleryStore.SaveMeta(entry);
        return entry;
    }

    // A single output image matching the full Surround span. All three
    // panels are composited side by side into it using the same slice
    // geometry the preview uses, so what ships as wallpaper matches what
    // the user calibrated on screen.
    private static void GenerateSurround(
        CalibrationState state,
        SourceImage source,
        string folder,
        GalleryEntry entry)
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

        const string fileName = "surround.png";
        SavePng(output, spanW, spanH, Path.Combine(folder, fileName));
        entry.Files.Add(new GalleryFile
        {
            Role = "surround",
            FileName = fileName,
            BoundsX = spanDisplay.Bounds.X,
            BoundsY = spanDisplay.Bounds.Y,
            BoundsWidth = spanDisplay.Bounds.Width,
            BoundsHeight = spanDisplay.Bounds.Height,
        });
    }

    // One output image per chosen monitor. The wallpaper is conceptually
    // a continuous panorama covering the sum of the three monitor widths;
    // each monitor crops out its own slice with the per-junction shifts
    // baked in.
    private static void GenerateSeparate(
        CalibrationState state,
        SourceImage source,
        string folder,
        GalleryEntry entry)
    {
        if (state.MonitorCount != 3)
            throw new InvalidOperationException(
                "Separate mode requires exactly 3 monitors to be picked before Apply.");

        var topology = state.Topology;
        for (var pos = 0; pos < 3; pos++)
        {
            var idx = state.Positions[pos];
            if (idx < 0 || idx >= topology.Displays.Count)
                throw new InvalidOperationException(
                    "One of the three monitor roles (Left / Centre / Right) is unassigned. " +
                    "Click a monitor tile in the HUD's MONITORS section to assign it.");
        }
        var chosen = Enumerable.Range(0, 3)
            .Select(pos => topology.Displays[state.Positions[pos]])
            .ToList();

        var totalW = (int)Math.Round(chosen.Sum(d => d.Bounds.Width));
        var totalH = (int)Math.Round(chosen.Max(d => d.Bounds.Height));

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

            var fileName = RoleFileName(position);
            SavePng(output, monW, monH, Path.Combine(folder, fileName));
            entry.Files.Add(new GalleryFile
            {
                Role = RoleName(position),
                FileName = fileName,
                BoundsX = display.Bounds.X,
                BoundsY = display.Bounds.Y,
                BoundsWidth = display.Bounds.Width,
                BoundsHeight = display.Bounds.Height,
            });
            offsetX += monW;
        }
    }

    private static string RoleFileName(int position) => position switch
    {
        CalibrationState.LeftPosition => "left.png",
        CalibrationState.CenterPosition => "center.png",
        CalibrationState.RightPosition => "right.png",
        _ => $"monitor{position}.png",
    };

    private static string RoleName(int position) => position switch
    {
        CalibrationState.LeftPosition => "left",
        CalibrationState.CenterPosition => "center",
        CalibrationState.RightPosition => "right",
        _ => $"monitor{position}",
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

    // Produces a small thumbnail that represents what the user will see
    // on their monitors: a horizontal composite of all output files.
    // Stored at thumb.png next to the full-resolution PNGs.
    private static void WriteThumbnail(GalleryEntry entry)
    {
        if (entry.FolderPath == null) return;
        var sources = new List<BitmapSource>();
        foreach (var f in entry.Files.OrderBy(f => f.Role switch
                 {
                     "left" => 0, "surround" => 1, "center" => 1, "right" => 2, _ => 3,
                 }))
        {
            var path = Path.Combine(entry.FolderPath, f.FileName);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = ThumbWidth;
            bmp.EndInit();
            bmp.Freeze();
            sources.Add(bmp);
        }
        if (sources.Count == 0) return;

        // Compose side by side at a uniform height proportional to the
        // tallest panel, then re-encode as a single PNG.
        var maxH = sources.Max(s => s.PixelHeight);
        var totalW = sources.Sum(s => s.PixelWidth);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            totalW, maxH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var dv = new System.Windows.Media.DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            double x = 0;
            foreach (var src in sources)
            {
                ctx.DrawImage(src, new System.Windows.Rect(x, 0, src.PixelWidth, src.PixelHeight));
                x += src.PixelWidth;
            }
        }
        rtb.Render(dv);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(Path.Combine(entry.FolderPath, GalleryStore.ThumbFile));
        encoder.Save(stream);
    }

    private sealed class SourceImage
    {
        public byte[] Pixels { get; init; } = Array.Empty<byte>();
        public int Width { get; init; }
        public int Height { get; init; }
        public int Stride => Width * 4;
    }

    // Loads the wallpaper and, only when needed, re-renders it to exactly
    // (targetW, targetH) using a cover fit. If the source already matches
    // the target dimensions, the pixels are copied directly — no rescale
    // round-trip, no chance of a RenderTargetBitmap quirk eating the
    // content. Cover fit preserves aspect when rescaling is required.
    private static SourceImage LoadAndFitToTarget(string path, int targetW, int targetH)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        var srcW = bmp.PixelWidth;
        var srcH = bmp.PixelHeight;
        if (targetW <= 0 || targetH <= 0)
        {
            targetW = srcW;
            targetH = srcH;
        }

        // Fast path: exact match. Decode straight into Bgra32 without
        // going through a RenderTargetBitmap (whose behaviour on giant
        // opaque images has bit us before).
        if (srcW == targetW && srcH == targetH)
        {
            var direct = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            var stride = targetW * 4;
            var pixels = new byte[targetH * stride];
            direct.CopyPixels(pixels, stride, 0);
            return new SourceImage { Pixels = pixels, Width = targetW, Height = targetH };
        }

        var scale = Math.Max((double)targetW / srcW, (double)targetH / srcH);
        var scaledW = srcW * scale;
        var scaledH = srcH * scale;
        var offsetX = (targetW - scaledW) / 2.0;
        var offsetY = (targetH - scaledH) / 2.0;

        var rtb = new RenderTargetBitmap(targetW, targetH, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.Fant);
        using (var ctx = dv.RenderOpen())
        {
            ctx.DrawRectangle(Brushes.Black, null, new Rect(0, 0, targetW, targetH));
            ctx.DrawImage(bmp, new Rect(offsetX, offsetY, scaledW, scaledH));
        }
        rtb.Render(dv);

        var converted = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        var stride2 = targetW * 4;
        var pixels2 = new byte[targetH * stride2];
        converted.CopyPixels(pixels2, stride2, 0);
        return new SourceImage { Pixels = pixels2, Width = targetW, Height = targetH };
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
