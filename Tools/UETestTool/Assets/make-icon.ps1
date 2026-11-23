# Builds Assets\UETestTool.ico from Assets\IconSource\UE_Test_icon_1024px.png.
#
# The logo is much wider than it is tall, so drawn into a square as-is it fills only about half the height.
# To use more of the icon, the logo's band (the rows with any content) is cropped and stretched vertically
# by $StretchY, keeping the full width, and centred.
#
# 256 and 128 px frames are rendered from the 1024 px artwork; smaller frames are downscaled from the
# 256 px frame (two steps give cleaner small sizes). Every frame is stored PNG-compressed (supported by
# Windows Vista+ and by WPF's icon decoder).
#
# Usage:  powershell -ExecutionPolicy Bypass -File Tools\UETestTool\Assets\make-icon.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = $PSScriptRoot
$source = Join-Path $assets 'IconSource\UE_Test_icon_1024px.png'
$out    = Join-Path $assets 'UETestTool.ico'

# Rows of the 1024 px artwork containing the logo (content spans about 246-778), with a little margin.
$BandTop = 238
$BandBottom = 786
$StretchY = 1.35

function New-Canvas([int] $size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    return $bmp, $g
}

function New-Attributes {
    $attr = New-Object System.Drawing.Imaging.ImageAttributes
    $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # avoids edge fringing
    return $attr
}

# The logo band, full width, stretched vertically and centred in a size x size square.
function Render-Logo([System.Drawing.Image] $art, [int] $size) {
    $bmp, $g = New-Canvas $size
    try {
        $scale = $size / $art.Width
        $height = [int][Math]::Round(($BandBottom - $BandTop) * $scale * $StretchY)
        $dest = New-Object System.Drawing.Rectangle 0, ([int](($size - $height) / 2)), $size, $height
        $g.DrawImage($art, $dest, 0, $BandTop, $art.Width, ($BandBottom - $BandTop), [System.Drawing.GraphicsUnit]::Pixel, (New-Attributes))
    } finally { $g.Dispose() }
    return $bmp
}

function Resize([System.Drawing.Image] $image, [int] $size) {
    $bmp, $g = New-Canvas $size
    try {
        $g.DrawImage($image, (New-Object System.Drawing.Rectangle 0, 0, $size, $size),
                     0, 0, $image.Width, $image.Height, [System.Drawing.GraphicsUnit]::Pixel, (New-Attributes))
    } finally { $g.Dispose() }
    return $bmp
}

function Get-PngBytes([System.Drawing.Image] $image) {
    $ms = New-Object System.IO.MemoryStream
    $image.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

$art = [System.Drawing.Image]::FromFile($source)
$frames = New-Object System.Collections.Generic.List[object]
try {
    $frame256 = Render-Logo $art 256
    $frame128 = Render-Logo $art 128
    $frames.Add([pscustomobject]@{ Size = 256; Data = (Get-PngBytes $frame256) })
    $frames.Add([pscustomobject]@{ Size = 128; Data = (Get-PngBytes $frame128) })
    foreach ($s in 64, 48, 32, 24, 16) {
        $small = Resize $frame256 $s
        $frames.Add([pscustomobject]@{ Size = $s; Data = (Get-PngBytes $small) })
        $small.Dispose()
    }
    $frame256.Dispose(); $frame128.Dispose()
} finally { $art.Dispose() }

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
