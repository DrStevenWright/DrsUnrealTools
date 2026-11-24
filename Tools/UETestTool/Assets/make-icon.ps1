# Builds Assets\UETestTool.ico from Assets\IconSource\UE_Test_Tool_Icon_Master.png (1024 x 1024).
#
# The master artwork is a rounded tile on solid black, with blue light spilling above and below it. The black
# around the tile is made transparent, so the icon doesn't show as a black square on light backgrounds:
#   - Pixels reachable from the image's border without crossing the tile's silver frame are "outside". The frame
#     is found by its red channel: the black and the blue glow have little red, the silver frame a lot.
#   - An outside pixel's opacity comes from its brightness (its colour is un-premultiplied against the black it
#     was drawn on), so the glow stays as translucent blue light and the frame's antialiased edge stays smooth.
#   - Everything inside the frame stays opaque, including the tile's near-black background.
#
# Two crops of the processed artwork:
#   - Tile (64 px and up: Explorer's larger views, Start, Alt+Tab): cropped to the tile, so it touches the edges.
#   - Ring (48 px and down: taskbar, title bar, Explorer lists): just the ring and play triangle, cut out as a
#     circle, so the mark is about a third bigger where the icon is small.
# Frames of 48 px and up are scaled straight from the 1024 px artwork; smaller ones from a 256 px rendering of
# the ring (two steps give cleaner small sizes). Every frame is stored PNG-compressed (supported by Windows
# Vista+ and by WPF's icon decoder).
#
# IconSource\UE_Test_Tool_Icon_Layered.svg holds the same artwork (as an embedded PNG) plus a hidden, editable
# vector draft of it; it isn't used here.
#
# Usage:  powershell -ExecutionPolicy Bypass -File Tools\UETestTool\Assets\make-icon.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = $PSScriptRoot
$source = Join-Path $assets 'IconSource\UE_Test_Tool_Icon_Master.png'
$out    = Join-Path $assets 'UETestTool.ico'

# Pixels with less red than this can be outside the tile (black or blue glow); the silver frame has more.
$FrameRedThreshold = 40

# The tile spans x 32-991, y 29-965 of the master: a 960 px square centred on it.
$TileCrop = New-Object System.Drawing.Rectangle 32, 17, 960, 960

# The ring (with its glow) is centred at 512,500 and reaches about 385 px out; cut out with a little margin.
$RingCentreX = 512; $RingCentreY = 500; $RingRadius = 392
$RingCrop = New-Object System.Drawing.Rectangle ($RingCentreX - $RingRadius), ($RingCentreY - $RingRadius), (2 * $RingRadius), (2 * $RingRadius)

# Frames up to this size show the ring. It has to cover 48 px: WPF gives Windows the 48 px frame as the window's
# main icon at 150% display scaling (32 px x 1.5), and the taskbar shrinks that frame rather than using a small one.
$RingMaxSize = 48

# Sizes Windows asks for: 16/20/24/32/48 at 100%, plus 30/40 (125%), 36/72 (150%) and 64/96/128/256 for
# Explorer's larger views. Missing sizes are scaled from the nearest larger frame, which blurs them.
$Sizes = 256, 128, 96, 72, 64, 48, 40, 36, 32, 30, 24, 20, 16

Add-Type -ReferencedAssemblies System.Drawing, System.Drawing.Primitives -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class IconAlpha
{
    public static Bitmap MakeSurroundTransparent(Bitmap source, int frameRedThreshold)
    {
        int w = source.Width, h = source.Height;
        var argb = new int[w * h];
        var rect = new Rectangle(0, 0, w, h);
        using (var copy = source.Clone(rect, PixelFormat.Format32bppArgb))
        {
            var data = copy.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, argb, y * w, w);
            copy.UnlockBits(data);
        }

        // Flood fill the outside from the border, through pixels with little red.
        var outside = new bool[w * h];
        var queue = new Queue<int>();
        Func<int, bool> canPass = i => ((argb[i] >> 16) & 0xFF) < frameRedThreshold;
        for (int x = 0; x < w; x++) { Seed(x, outside, queue, canPass); Seed((h - 1) * w + x, outside, queue, canPass); }
        for (int y = 0; y < h; y++) { Seed(y * w, outside, queue, canPass); Seed(y * w + w - 1, outside, queue, canPass); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
            if (x > 0) Seed(i - 1, outside, queue, canPass);
            if (x < w - 1) Seed(i + 1, outside, queue, canPass);
            if (y > 0) Seed(i - w, outside, queue, canPass);
            if (y < h - 1) Seed(i + w, outside, queue, canPass);
        }
        if (outside[(h / 2) * w + w / 2])
            throw new InvalidOperationException("The outside reached the centre: the frame has a gap at this threshold.");

        for (int i = 0; i < argb.Length; i++)
        {
            if (!outside[i]) { argb[i] |= unchecked((int)0xFF000000); continue; }
            int r = (argb[i] >> 16) & 0xFF, g = (argb[i] >> 8) & 0xFF, b = argb[i] & 0xFF;
            int a = Math.Max(r, Math.Max(g, b));
            argb[i] = a == 0 ? 0 : (a << 24) | (r * 255 / a << 16) | (g * 255 / a << 8) | (b * 255 / a);
        }

        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var outData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++) Marshal.Copy(argb, y * w, outData.Scan0 + y * outData.Stride, w);
        result.UnlockBits(outData);
        return result;
    }

    /// <summary>A copy with everything outside the circle made transparent (antialiased over `feather` px).</summary>
    public static Bitmap CutCircle(Bitmap source, double cx, double cy, double r, double feather)
    {
        int w = source.Width, h = source.Height;
        var rect = new Rectangle(0, 0, w, h);
        var result = source.Clone(rect, PixelFormat.Format32bppArgb);
        var data = result.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var row = new int[w];
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w);
            for (int x = 0; x < w; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                double m = Math.Max(0, Math.Min(1, (r - Math.Sqrt(dx * dx + dy * dy)) / feather + 0.5));
                if (m >= 1) continue;
                int a = (int)(((row[x] >> 24) & 0xFF) * m);
                row[x] = (a << 24) | (row[x] & 0xFFFFFF);
            }
            Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, w);
        }
        result.UnlockBits(data);
        return result;
    }

    private static void Seed(int i, bool[] outside, Queue<int> queue, Func<int, bool> canPass)
    {
        if (outside[i] || !canPass(i)) return;
        outside[i] = true;
        queue.Enqueue(i);
    }
}
"@

function New-Canvas([int] $size) {
    # Premultiplied alpha, so scaling doesn't drag dark fringes in from transparent pixels.
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    return $bmp, $g
}

# Scales the crop rectangle of an image (the whole image if none) to a size x size frame.
function Resize([System.Drawing.Image] $image, [int] $size, $crop = $null) {
    if ($null -eq $crop) { $crop = New-Object System.Drawing.Rectangle 0, 0, $image.Width, $image.Height }
    $bmp, $g = New-Canvas $size
    try {
        $attr = New-Object System.Drawing.Imaging.ImageAttributes
        $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # avoids edge fringing
        $g.DrawImage($image, (New-Object System.Drawing.Rectangle 0, 0, $size, $size),
                     $crop.X, $crop.Y, $crop.Width, $crop.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
    } finally { $g.Dispose() }
    return $bmp
}

function Get-PngBytes([System.Drawing.Image] $image) {
    $ms = New-Object System.IO.MemoryStream
    $image.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

$master = [System.Drawing.Bitmap]::FromFile($source)
$frames = New-Object System.Collections.Generic.List[object]
try {
    $art = [IconAlpha]::MakeSurroundTransparent($master, $FrameRedThreshold)
    $ringArt = [IconAlpha]::CutCircle($art, $RingCentreX, $RingCentreY, $RingRadius, 3)
    $ring256 = Resize $ringArt 256 $RingCrop
    foreach ($s in $Sizes) {
        $frame = if ($s -gt $RingMaxSize) { Resize $art $s $TileCrop }       # tile, straight from the artwork
                 elseif ($s -ge 48) { Resize $ringArt $s $RingCrop }         # ring, straight from the artwork
                 else { Resize $ring256 $s }                                 # small ring, via 256 px
        $frames.Add([pscustomobject]@{ Size = $s; Data = (Get-PngBytes $frame) })
        $frame.Dispose()
    }
    $ring256.Dispose(); $ringArt.Dispose(); $art.Dispose()
} finally { $master.Dispose() }

# ICONDIR + ICONDIRENTRY[] + image data
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
try {
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $size = $f.Size; $data = [byte[]]$f.Data
        $dim = if ($size -ge 256) { 0 } else { $size }
        $w.Write([byte]$dim); $w.Write([byte]$dim)
        $w.Write([byte]0); $w.Write([byte]0)            # palette, reserved
        $w.Write([UInt16]1); $w.Write([UInt16]32)       # planes, bpp
        $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
        $offset += $data.Length
    }
    foreach ($f in $frames) { $w.Write([byte[]]$f.Data) }
} finally { $w.Dispose() }

Write-Host "Wrote $out ($((Get-Item $out).Length) bytes, sizes: $(($frames | ForEach-Object Size) -join ', '))"
