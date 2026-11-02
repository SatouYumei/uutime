# 生成 uutime.ico（多尺寸 PNG 帧），图案和托盘图标一致：蓝色圆盘 + 白色时针分针。
param([string]$Out = (Join-Path $PSScriptRoot "..\uutime.ico"))
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.PixelOffsetMode = "HighQuality"
    $u = $s / 32.0
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x3B, 0x82, 0xF6))
    $g.FillEllipse($fill, 1 * $u, 1 * $u, 30 * $u, 30 * $u)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, 3.2 * $u))
    $pen.StartCap = "Round"; $pen.EndCap = "Round"
    $g.DrawLine($pen, 16 * $u, 16 * $u, 16 * $u, 7 * $u)
    $g.DrawLine($pen, 16 * $u, 16 * $u, 23 * $u, 20 * $u)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , @($s, $ms.ToArray())
}

$buf = New-Object System.IO.MemoryStream   # 注意：PowerShell 变量不分大小写，别和 $Out 重名
$w = New-Object System.IO.BinaryWriter $buf
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Flush()
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Out), $buf.ToArray())
"wrote $Out"
