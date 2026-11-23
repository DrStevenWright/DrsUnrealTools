# Builds Assets\UETestTool.ico from the PNGs in Assets\IconSource.
# 256 and 128 px frames use the matching source PNGs; smaller frames are high-quality
# downscales of the 256 px source. Every frame is stored PNG-compressed (supported by
# Windows Vista+ and by WPF's icon decoder).
#
# Usage:  powershell -ExecutionPolicy Bypass -File Tools\UETestTool\Assets\make-icon.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = $PSScriptRoot
$src256 = Join-Path $assets 'IconSource\UE_Test_icon_256px.png'
$src128 = Join-Path $assets 'IconSource\UE_Test_icon_128px.png'
$out    = Join-Path $assets 'UETestTool.ico'

function Get-PngBytes([System.Drawing.Image] $image, [int] $size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $attr = New-Object System.Drawing.Imaging.ImageAttributes
        $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # avoids edge fringing
        $g.DrawImage($image, (New-Object System.Drawing.Rectangle 0, 0, $size, $size),
                     0, 0, $image.Width, $image.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
    } finally { $g.Dispose() }
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$img256 = [System.Drawing.Image]::FromFile($src256)
$img128 = [System.Drawing.Image]::FromFile($src128)
try {
    $frames = New-Object System.Collections.Generic.List[object]
    $frames.Add([pscustomobject]@{ Size = 256; Data = [System.IO.File]::ReadAllBytes($src256) })
    $frames.Add([pscustomobject]@{ Size = 128; Data = [System.IO.File]::ReadAllBytes($src128) })
    foreach ($s in 64, 48, 32, 24, 16) { $frames.Add([pscustomobject]@{ Size = $s; Data = (Get-PngBytes $img256 $s) }) }
} finally { $img256.Dispose(); $img128.Dispose() }

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
