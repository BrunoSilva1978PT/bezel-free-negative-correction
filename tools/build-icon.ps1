# Builds src\BezelFreeCorrection\app.ico from scratch using
# System.Drawing. No external design tools required — the icon is a
# small glyph (three monitor rectangles with a bezel-free lens dot)
# rendered at four sizes and packed into a multi-resolution ICO file.

param(
    [string]$OutPath = "src\BezelFreeCorrection\app.ico"
)

Add-Type -AssemblyName System.Drawing

function New-MonitorIcon([int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        # Rounded dark background reminiscent of the HUD's header bar.
        $bg = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $r = [int]($size * 0.18)
        $bg.AddArc(0,0,$r*2,$r*2,180,90)
        $bg.AddArc($size-$r*2,0,$r*2,$r*2,270,90)
        $bg.AddArc($size-$r*2,$size-$r*2,$r*2,$r*2,0,90)
        $bg.AddArc(0,$size-$r*2,$r*2,$r*2,90,90)
        $bg.CloseAllFigures()
        $g.FillPath(
            [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(0xFF,0x1A,0x1D,0x24)),
            $bg)

        # Three monitors centred vertically, small gap between them to
        # suggest the bezel seams the app corrects.
        $count = 3
        $gap = [int]([math]::Max(1, $size * 0.03))
        $monW = ([int]([double]$size * 0.78) - $gap * ($count - 1)) / $count
        $monH = [int]($monW * 9 / 16)
        $totalW = $monW * $count + $gap * ($count - 1)
        $ox = [int](($size - $totalW) / 2)
        $oy = [int](($size - $monH) / 2) - [int]($size * 0.03)

        $frameBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(0xFF,0x4E,0xA1,0xFF))
        $faceBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(0xFF,0xBF,0xDC,0xFF))

        for ($i = 0; $i -lt $count; $i++) {
            $x = $ox + $i * ($monW + $gap)
            $g.FillRectangle($frameBrush, $x, $oy, $monW, $monH)
            $inset = [int]([math]::Max(1, $size * 0.01))
            $g.FillRectangle(
                $faceBrush,
                $x + $inset, $oy + $inset,
                $monW - 2 * $inset, $monH - 2 * $inset)
        }

        # Green "lens" dot in the lower right, echoing the topology
        # indicator on the HUD.
        $dotD = [int]($size * 0.16)
        $g.FillEllipse(
            [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(0xFF,0x4A,0xDE,0x80)),
            $size - $dotD - [int]($size * 0.12),
            $size - $dotD - [int]($size * 0.12),
            $dotD, $dotD)
    } finally {
        $g.Dispose()
    }
    return $bmp
}

$sizes = @(256, 128, 64, 48, 32, 16)

# Encode each bitmap as PNG in-memory; ICO supports PNG payloads for
# sizes >= 16, which keeps the file small and preserves alpha.
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-MonitorIcon $s
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,([System.Tuple]::Create($s, $ms.ToArray()))
    $bmp.Dispose()
    $ms.Dispose()
}

$dir = Split-Path -Parent $OutPath
if ($dir -and -not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir | Out-Null
}

$fs = [System.IO.File]::Create($OutPath)
$bw = [System.IO.BinaryWriter]::new($fs)

# ICONDIR header.
$bw.Write([UInt16]0)                   # reserved
$bw.Write([UInt16]1)                   # type: 1 = icon
$bw.Write([UInt16]$pngs.Count)

# ICONDIRENTRY per size.
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $s = $p.Item1
    $data = $p.Item2
    $bw.Write([byte]($s -band 0xFF))   # width  (0 = 256)
    $bw.Write([byte]($s -band 0xFF))   # height (0 = 256)
    $bw.Write([byte]0)                 # colors in palette
    $bw.Write([byte]0)                 # reserved
    $bw.Write([UInt16]1)               # planes
    $bw.Write([UInt16]32)              # bits per pixel
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}

foreach ($p in $pngs) {
    $bw.Write($p.Item2)
}
$bw.Flush()
$bw.Close()

Write-Host "Wrote $OutPath ($((Get-Item $OutPath).Length) bytes, $($pngs.Count) sizes)"
