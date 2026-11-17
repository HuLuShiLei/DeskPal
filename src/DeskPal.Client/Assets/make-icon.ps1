# 把 Assets/icon-source.png 收成 DeskPal.ico（exe、托盘、安装向导共用）和 installer/wizard-small-*.bmp。
# 图案是成稿，不在这个脚本里画。换图时覆盖 icon-source.png（四角留白），再运行：
#   powershell -File src/DeskPal.Client/Assets/make-icon.ps1 [-PreviewDir <目录>]
# 源图是白底合成的。只从四角向内去掉白边，字母本身的浅色不会被当成背景。
param([string]$PreviewDir)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$sourcePath = Join-Path $PSScriptRoot 'icon-source.png'
$out = Join-Path $PSScriptRoot 'DeskPal.ico'
if (-not (Test-Path $sourcePath)) { throw "missing $sourcePath" }

function Convert-WhiteFringe([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $fg = $null
    $mid = [int]($n / 2)
    for ($y = 0; $y -lt $n; $y++) {
        $c = $bmp.GetPixel($mid, $y)
        if ($c.R -lt 90 -and $c.G -lt 90 -and $c.B -lt 90) { $fg = $c; break }
    }
    if ($null -eq $fg) { return }
    $denR = [double][Math]::Max(1, 255 - $fg.R)
    $denG = [double][Math]::Max(1, 255 - $fg.G)
    $denB = [double][Math]::Max(1, 255 - $fg.B)

    $seen = New-Object 'bool[]' ($n * $n)
    $q = New-Object 'System.Collections.Generic.Queue[int]'
    foreach ($seed in @(0, ($n - 1), (($n - 1) * $n), (($n * $n) - 1))) {
        $seen[$seed] = $true
        $q.Enqueue($seed)
    }
    while ($q.Count -gt 0) {
        $i = $q.Dequeue()
        $x = $i % $n
        $y = [int][Math]::Floor($i / $n)
        $c = $bmp.GetPixel($x, $y)
        $aR = (255 - $c.R) / $denR
        $aG = (255 - $c.G) / $denG
        $aB = (255 - $c.B) / $denB
        $hi = [Math]::Max($aR, [Math]::Max($aG, $aB))
        $lo = [Math]::Min($aR, [Math]::Min($aG, $aB))
        $avg = ($aR + $aG + $aB) / 3.0
        if ((($hi - $lo) -gt 0.10) -or ($avg -ge 0.88)) { continue }
        if ($avg -le 0.05) {
            $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
        }
        else {
            $ai = [int][Math]::Round([Math]::Min(1.0, $avg) * 255)
            $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($ai, [int]$fg.R, [int]$fg.G, [int]$fg.B))
        }
        $next = New-Object System.Collections.Generic.List[int]
        if ($x -gt 0) { $next.Add($i - 1) }
        if ($x -lt ($n - 1)) { $next.Add($i + 1) }
        if ($y -gt 0) { $next.Add($i - $n) }
        if ($y -lt ($n - 1)) { $next.Add($i + $n) }
        foreach ($nb in $next) {
            if (-not $seen[$nb]) {
                $seen[$nb] = $true
                $q.Enqueue($nb)
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