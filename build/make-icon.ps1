<#
.SYNOPSIS
把 images\Logo.PNG 转成 src\WeatherHelper.App\app.ico（多尺寸 ICO）。
用法：在仓库根目录运行 .\build\make-icon.ps1

为什么要自己拼 ICO：仓库里没有、也不打算引入 ImageMagick 或任何 NuGet 包，
而 System.Drawing 只能存单尺寸图标。ICO 的容器格式很简单，直接手写。
换 Logo 时重跑本脚本即可；生成的 app.ico 要和代码一起提交。
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "images\Logo.PNG"
$target = Join-Path $root "src\WeatherHelper.App\app.ico"

# 小尺寸用 DIB（兼容性最好，System.Drawing.Icon 也一定认），256 用 PNG（Vista+ 的标准做法，省体积）
$dibSizes = @(16, 20, 24, 32, 40, 48, 64, 128)
$pngSize = 256

# 下面两个 Get-*Entry 都用 "return ,$ms.ToArray()" 收尾，那个逗号不能删：
# PowerShell 会把函数返回的字节数组拆成一串单个字节，调用方拿到的是 object[]
# 而不是 byte[] —— .Length 看着是对的，写进文件就废了。逗号把它整体包住。

if (-not (Test-Path $source)) { throw "找不到源图：$source" }

function Resize-One {
    param([System.Drawing.Image]$Image, [int]$Size)
    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.DrawImage($Image, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size))
    } finally {
        $g.Dispose()
    }
    return $bmp
}

function Convert-ToSize {
    # 逐级折半再收到目标尺寸：一步从 1508 缩到 16 会糊成一团。
    # 返回的 Bitmap 归调用方 Dispose。
    param([System.Drawing.Bitmap]$Square, [int]$Size)

    $steps = New-Object System.Collections.ArrayList
    $n = $Square.Width
    while ($n -gt $Size * 2) {
        $n = [Math]::Max($Size * 2, [int][Math]::Floor($n / 2))
        [void]$steps.Add($n)
    }
    [void]$steps.Add($Size)

    $prev = $Square
    $prevOwned = $false
    $result = $null
    foreach ($s in $steps) {
        $result = Resize-One -Image $prev -Size $s
        if ($prevOwned) { $prev.Dispose() }
        $prev = $result
        $prevOwned = $true
    }
    return $result
}

function Get-DibEntry {
    # 32bpp BGRA 的 DIB：BITMAPINFOHEADER + 自下而上的 XOR 位图 + 1bpp AND 掩码
    param([System.Drawing.Bitmap]$Bitmap)

    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $stride = $w * 4
    $maskStride = [int][Math]::Ceiling($w / 32.0) * 4   # 1bpp 的行也要按 4 字节对齐

    $rect = [System.Drawing.Rectangle]::new(0, 0, $w, $h)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                             [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = [byte[]]::new($stride * $h)
    try {
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    } finally {
        $Bitmap.UnlockBits($data)
    }

    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    try {
        $bw.Write([uint32]40)                       # biSize
        $bw.Write([int32]$w)                        # biWidth
        $bw.Write([int32]($h * 2))                  # biHeight = XOR 和 AND 两张图叠起来
        $bw.Write([uint16]1)                        # biPlanes
        $bw.Write([uint16]32)                       # biBitCount
        $bw.Write([uint32]0)                        # biCompression = BI_RGB
        $bw.Write([uint32]($stride * $h + $maskStride * $h))
        $bw.Write([int32]0); $bw.Write([int32]0)    # 分辨率留 0
        $bw.Write([uint32]0); $bw.Write([uint32]0)  # 调色板留 0

        for ($y = $h - 1; $y -ge 0; $y--) {         # DIB 是自下而上存的
            $bw.Write($pixels, $y * $stride, $stride)
        }

        # AND 掩码：alpha < 128 的位置置 1。素材本身是不透明的，这里基本全 0，
        # 但按 alpha 算才对 —— 将来换成带透明通道的 Logo 也不会画错。
        $mask = [byte[]]::new($maskStride * $h)
        for ($y = 0; $y -lt $h; $y++) {
            $srcRow = ($h - 1 - $y) * $stride
            $dstRow = $y * $maskStride
            for ($x = 0; $x -lt $w; $x++) {
                if ($pixels[$srcRow + $x * 4 + 3] -lt 128) {
                    $i = $dstRow + [int][Math]::Floor($x / 8)
                    $mask[$i] = $mask[$i] -bor [byte](0x80 -shr ($x % 8))
                }
            }
        }
        $bw.Write($mask)
    } finally {
        $bw.Dispose()
    }
    return ,$ms.ToArray()
}

function Get-PngEntry {
    param([System.Drawing.Bitmap]$Bitmap)
    $ms = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$ms.ToArray()
    } finally {
        $ms.Dispose()
    }
}

Write-Host ">>> 读取 $source" -ForegroundColor Cyan
$src = [System.Drawing.Image]::FromFile($source)
$square = $null
$entries = New-Object System.Collections.ArrayList
try {
    # 源图 1512x1508，差那 4px 中心裁掉，缩放出方图
    $side = [Math]::Min($src.Width, $src.Height)
    $square = [System.Drawing.Bitmap]::new($side, $side, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($square)
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($src,
            [System.Drawing.Rectangle]::new(0, 0, $side, $side),
            [int](($src.Width - $side) / 2), [int](($src.Height - $side) / 2), $side, $side,
            [System.Drawing.GraphicsUnit]::Pixel)
    } finally {
        $g.Dispose()
    }

    foreach ($s in $dibSizes) {
        $bmp = Convert-ToSize -Square $square -Size $s
        try { [void]$entries.Add(@{ W = $s; Bytes = (Get-DibEntry -Bitmap $bmp) }) }
        finally { $bmp.Dispose() }
    }
    $bmp = Convert-ToSize -Square $square -Size $pngSize
    try { [void]$entries.Add(@{ W = $pngSize; Bytes = (Get-PngEntry -Bitmap $bmp) }) }
    finally { $bmp.Dispose() }
} finally {
    if ($square) { $square.Dispose() }
    $src.Dispose()
}

# ── 拼 ICO 容器：ICONDIR + 目录项 + 各尺寸数据 ──
$count = $entries.Count
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)
try {
    $bw.Write([uint16]0)          # reserved
    $bw.Write([uint16]1)          # type = icon
    $bw.Write([uint16]$count)

    $offset = 6 + 16 * $count
    foreach ($e in $entries) {
        $dim = if ($e.W -ge 256) { 0 } else { $e.W }   # ICO 里 256 用 0 表示
        $bw.Write([byte]$dim)     # bWidth
        $bw.Write([byte]$dim)     # bHeight
        $bw.Write([byte]0)        # bColorCount：32bpp 不用调色板
        $bw.Write([byte]0)        # bReserved
        $bw.Write([uint16]1)      # wPlanes
        $bw.Write([uint16]32)     # wBitCount
        $bw.Write([uint32]$e.Bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $e.Bytes.Length
    }
    foreach ($e in $entries) { $bw.Write($e.Bytes) }
} finally {
    $bw.Dispose()
}
$bytes = $ms.ToArray()
$ms.Dispose()
[System.IO.File]::WriteAllBytes($target, $bytes)

Write-Host ">>> 写入 $target（$count 个尺寸，$([math]::Round($bytes.Length / 1KB, 1)) KB）" -ForegroundColor Green
foreach ($e in $entries) {
    $kind = if ($e.W -ge 256) { "PNG" } else { "DIB" }
    Write-Host ("    {0,3}x{0,-3} {1}  {2,7} 字节" -f $e.W, $kind, $e.Bytes.Length)
}
