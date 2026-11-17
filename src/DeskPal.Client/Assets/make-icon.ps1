# 把 Assets/icon-source.png 收成 DeskPal.ico（exe、托盘、安装向导共用）和 installer/wizard-small-*.bmp。
# 图案是成稿，不在这个脚本里画。换图时覆盖 icon-source.png（四角留白），再运行：
#   powershell -File src/DeskPal.Client/Assets/make-icon.ps1 [-PreviewDir <目录>]
# 源图是白底合成的，缩放后再把圆角外的白底还原成透明，避免缩小时出一圈白边。
param([string]$PreviewDir)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$sourcePath = Join-Path $PSScriptRoot 'icon-source.png'
$out = Join-Path $PSScriptRoot 'DeskPal.ico'
if (-not (Test-Path $sourcePath)) { throw "missing $sourcePath" }

# 圆角边缘是深色底板和白底的混合。三个通道估出的透明度接近，才当成边缘。
function Convert-WhiteFringe([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    for ($y = 0; $y -lt $n; $y++) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $aR = (255 - $c.R) / 237.0
            $aG = (255 - $c.G) / 213.0
            $aB = (255 - $c.B) / 182.0
            $hi = [Math]::Max($aR, [Math]::Max($aG, $aB))
            $lo = [Math]::Min($aR, [Math]::Min($aG, $aB))
            $avg = ($aR + $aG + $aB) / 3.0
            if (($hi - $lo) -gt 0.12 -or $avg -ge 0.93) { continue }
            if ($avg -le 0.04) {
                $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
            }
            else {
                $ai = [int][Math]::Round([Math]::Min(1.0, $avg) * 255)
                $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($ai, 18, 42, 73))
            }
        }
    }
}

function New-ScaledBitmap([System.Drawing.Image]$source, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::White)
    $g.DrawImage($source, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
    $g.Dispose()
    Convert-WhiteFringe $bmp
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$n); $w.Write([int]($n * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]($n * $n * 4)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([math]::Ceiling($n / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $n)))
    $w.Flush()
    return $ms.ToArray()
}

$source = [System.Drawing.Image]::FromFile($sourcePath)
$images = foreach ($size in $sizes) {
    $bmp = New-ScaledBitmap $source $size
    if ($PreviewDir) {
        New-Item -ItemType Directory -Force $PreviewDir | Out-Null
        $bmp.Save((Join-Path $PreviewDir "icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    if ($size -ge 128) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $data = $ms.ToArray()
    }
    else {
        $data = Get-DibBytes $bmp
    }
    $bmp.Dispose()
    , @($size, $data)
}

$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $size = $img[0]; $data = [byte[]]$img[1]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$data.Length); $w.Write([int]$offset)
    $offset += $data.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())

$installer = Join-Path $PSScriptRoot '..\..\..\installer'
foreach ($size in 55, 69, 83, 110) {
    $icon = New-ScaledBitmap $source $size
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $g.DrawImage($icon, 0, 0, $size, $size)
    $g.Dispose()
    $bmp.Save((Join-Path $installer "wizard-small-$size.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    $icon.Dispose()
}
$source.Dispose()
"wrote $out ($($ms.Length) bytes)"