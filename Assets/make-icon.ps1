# Generates Assets\NetMonitor.ico: the tide tile (deep-water square, shoal flood above the
# datum, ebb band below), drawn at each Windows icon size. Same drawing as TrayService.Tile.
# Run from the project root:  powershell -File Assets\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$out = Join-Path $PSScriptRoot 'NetMonitor.ico'

function New-Tile([int]$px) {
    $bmp = New-Object System.Drawing.Bitmap $px, $px
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Work on the tray's 32-unit grid; small sizes fill the frame, large ones keep a margin.
    $inset = if ($px -ge 48) { $px / 16 } else { 0 }
    $s = ($px - 2 * $inset) / 32
    function P([float]$x, [float]$y) { New-Object System.Drawing.PointF ($inset + $x * $s), ($inset + $y * $s) }

    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x0B, 0x1F, 0x33))), $inset, $inset, 32 * $s, 32 * $s)

    $flood = New-Object System.Drawing.Drawing2D.GraphicsPath
    $flood.AddLines([System.Drawing.PointF[]]@((P 2 20), (P 2 15), (P 8 12), (P 14 6), (P 19 9), (P 24 13), (P 30 11), (P 30 20)))
    $flood.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xA8, 0xCF, 0xE0))), $flood)

    $ebb = P 2 21
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x4E, 0x8F, 0x7C))), $ebb.X, $ebb.Y, 28 * $s, 5 * $s)

    $datum = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(0xF2, 0xF7, 0xFA)), ([Math]::Max(1, 2 * $s))
    $a = P 0 20.5; $b = P 32 20.5
    $g.DrawLine($datum, $a, $b)
    $g.Dispose()
    return $bmp
}

# 32-bit DIB entry (BITMAPINFOHEADER, bottom-up BGRA, then a 1-bit AND mask), the form every
# icon reader understands.
function Get-Dib([System.Drawing.Bitmap]$bmp) {
    $px = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $maskRow = [int]([Math]::Ceiling($px / 32.0) * 4)
    $w.Write([uint32]40); $w.Write([int32]$px); $w.Write([int32]($px * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($px * $px * 4 + $maskRow * $px))
    $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $px - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $px; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $w.Write((New-Object byte[] ($maskRow * $px))) # alpha carries transparency; mask stays clear
    $w.Flush()
    return , $ms.ToArray()
}

# Small sizes as DIB, 256 as PNG (the Vista+ convention that keeps the file small).
$pngs = foreach ($px in $sizes) {
    $bmp = New-Tile $px
    if ($px -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $data = $ms.ToArray()
    } else {
        $data = Get-Dib $bmp
    }
    $bmp.Dispose()
    , $data
}

$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $px = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($px -ge 256) { 0 } else { $px }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Close()
"wrote $out"
