# Builds a multi-resolution .ico from the source PNG.
# Sizes <= 64 are written as 32-bit BMP/DIB entries (the most widely compatible form);
# 128 and 256 are written as PNG entries, which is what Windows expects at those sizes.

param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngSizes = @(128, 256)

$src = [System.Drawing.Bitmap]::FromFile($Source)

# --- trim transparent padding so the art fills the icon frame ---
$minX = $src.Width; $minY = $src.Height; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $src.Height; $y++) {
    for ($x = 0; $x -lt $src.Width; $x++) {
        if ($src.GetPixel($x, $y).A -gt 8) {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$cropW = $maxX - $minX + 1
$cropH = $maxY - $minY + 1

# Square canvas with a small uniform margin so the art never touches the edge.
$margin = [int]([Math]::Max($cropW, $cropH) * 0.03)
$side = [Math]::Max($cropW, $cropH) + (2 * $margin)

$master = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($master)
$g.Clear([System.Drawing.Color]::Transparent)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$destRect = New-Object System.Drawing.Rectangle(
    [int](($side - $cropW) / 2), [int](($side - $cropH) / 2), $cropW, $cropH)
$srcRect = New-Object System.Drawing.Rectangle($minX, $minY, $cropW, $cropH)
$g.DrawImage($src, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$src.Dispose()

Write-Output "Trimmed to ${cropW}x${cropH}, master canvas ${side}x${side} (margin $margin)"

# --- render each size ---
function Resize-Master([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gr = [System.Drawing.Graphics]::FromImage($bmp)
    $gr.Clear([System.Drawing.Color]::Transparent)
    $gr.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gr.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gr.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $gr.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $gr.DrawImage($master, (New-Object System.Drawing.Rectangle(0, 0, $n, $n)))
    $gr.Dispose()
    return $bmp
}

# 32-bit BMP/DIB payload: BITMAPINFOHEADER + bottom-up BGRA + 1bpp AND mask.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms

    $bw.Write([uint32]40)          # biSize
    $bw.Write([int32]$n)           # biWidth
    $bw.Write([int32]($n * 2))     # biHeight: XOR + AND mask
    $bw.Write([uint16]1)           # biPlanes
    $bw.Write([uint16]32)          # biBitCount
    $bw.Write([uint32]0)           # biCompression BI_RGB
    $bw.Write([uint32]($n * $n * 4))
    $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)

    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G)
            $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }

    # AND mask: all zero (opaque). Modern Windows uses the alpha channel instead,
    # but the mask must still be present and row-padded to 4 bytes.
    $maskRow = [int][Math]::Floor(($n + 31) / 32) * 4
    $zeros = New-Object byte[] ($maskRow * $n)
    $bw.Write($zeros)

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return , $bytes
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return , $bytes
}

$payloads = @()
foreach ($n in $sizes) {
    $bmp = Resize-Master $n
    if ($pngSizes -contains $n) { $data = Get-PngBytes $bmp; $kind = "PNG" }
    else { $data = Get-DibBytes $bmp; $kind = "BMP" }
    $payloads += [pscustomobject]@{ Size = $n; Data = $data; Kind = $kind }
    $bmp.Dispose()
    Write-Output ("  {0,3}x{0,-3} {1}  {2,7} bytes" -f $n, $kind, $data.Length)
}
$master.Dispose()

# --- write the ICO container ---
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out

$w.Write([uint16]0)                  # reserved
$w.Write([uint16]1)                  # type: icon
$w.Write([uint16]$payloads.Count)

$offset = 6 + (16 * $payloads.Count)
foreach ($p in $payloads) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }   # 0 means 256
    $w.Write([byte]$dim)             # width
    $w.Write([byte]$dim)             # height
    $w.Write([byte]0)                # colour count (0 = truecolour)
    $w.Write([byte]0)                # reserved
    $w.Write([uint16]1)              # planes
    $w.Write([uint16]32)             # bits per pixel
    $w.Write([uint32]$p.Data.Length)
    $w.Write([uint32]$offset)
    $offset += $p.Data.Length
}
foreach ($p in $payloads) { $w.Write($p.Data) }

$w.Flush()
[System.IO.File]::WriteAllBytes($Destination, $out.ToArray())
$w.Dispose(); $out.Dispose()

Write-Output "Wrote $Destination ($((Get-Item $Destination).Length) bytes, $($payloads.Count) images)"
