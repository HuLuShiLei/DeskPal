# 生成 DeskPal.ico（exe、托盘和安装向导共用）和 installer/wizard-small-*.bmp。改了图案后重新运行：
#   powershell -File src/DeskPal.Client/Assets/make-icon.ps1 [-PreviewDir <目录>]
# 24px 及以下单独画一版：去掉支架和嘴，放大屏幕和眼睛，托盘里才看得清。
param([string]$PreviewDir)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$out = Join-Path $PSScriptRoot 'DeskPal.ico'

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $small = $size -le 24

    $white = [System.Drawing.Brushes]::White
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x23, 0x3A, 0x9E))
    $ring = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x35, 0x56, 0xD6))
    $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x22, 0xC5, 0x5E))

    if ($small) { $bx = 0; $br = 52 } else { $bx = 8; $br = 56 }
    $bw = 256 - 2 * $bx
    $bg = New-RoundRect $bx $bx $bw $bw $br
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 256, 256),
        [System.Drawing.Color]::FromArgb(0x6E, 0x96, 0xFF), [System.Drawing.Color]::FromArgb(0x3A, 0x5C, 0xE0))
    $g.FillPath($grad, $bg)

    if ($small) {
        $g.FillPath($white, (New-RoundRect 36 48 184 148 30))
        $g.FillEllipse($ink, 78, 96, 36, 48)
        $g.FillEllipse($ink, 142, 96, 36, 48)
        $dot = @{ X = 206; Y = 50; R = 40; Ring = 0 }
    }
    else {
        $g.FillPath($white, (New-RoundRect 44 62 168 112 24))
        $g.FillRectangle($white, 116, 170, 24, 22)
        $g.FillPath($white, (New-RoundRect 82 186 92 18 9))
        $g.FillEllipse($ink, 86, 90, 24, 30)
        $g.FillEllipse($ink, 146, 90, 24, 30)
        $pen = New-Object System.Drawing.Pen ($ink.Color), 10
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawArc($pen, 100, 104, 56, 40, 30, 120)
        $dot = @{ X = 202; Y = 66; R = 24; Ring = 9 }
    }

    if ($dot.Ring -gt 0) {
        $rr = $dot.R + $dot.Ring
        $g.FillEllipse($ring, $dot.X - $rr, $dot.Y - $rr, 2 * $rr, 2 * $rr)
    }
    $g.FillEllipse($green, $dot.X - $dot.R, $dot.Y - $dot.R, 2 * $dot.R, 2 * $dot.R)
    $g.Dispose()
    return $bmp
}

# 64px 及以下写成 BMP（DIB）条目，兼容只认 BMP 的旧读取方；128、256 写 PNG 以减小体积。
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

$images = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
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

# 安装向导右上角的小图，Inno 按 DPI 从这几张里选。向导底色是白色，所以铺白底存 24 位 BMP。
$installer = Join-Path $PSScriptRoot '..\..\..\installer'
foreach ($size in 55, 69, 83, 110) {
    $icon = New-IconBitmap $size
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $g.DrawImage($icon, 0, 0, $size, $size)
    $g.Dispose()
    $bmp.Save((Join-Path $installer "wizard-small-$size.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    $icon.Dispose()
}"wrote $out ($($ms.Length) bytes)"