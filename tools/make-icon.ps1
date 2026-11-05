# 用 avatar.png 生成 uutime.ico（多尺寸 PNG 帧）：整张头像裁成圆形，边缘抗锯齿。
param(
    [string]$Avatar = (Join-Path $PSScriptRoot "..\avatar.png"),
    [string]$Out = (Join-Path $PSScriptRoot "..\uutime.ico")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile([IO.Path]::GetFullPath($Avatar))
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
    # 先高质量缩放，再用纹理画刷填圆，圆边才是抗锯齿的
    $scaled = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($scaled)
    $g.InterpolationMode = "HighQualityBicubic"
    $g.PixelOffsetMode = "HighQuality"
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose()

    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.PixelOffsetMode = "HighQuality"
    $brush = New-Object System.Drawing.TextureBrush $scaled
    $g.FillEllipse($brush, 0, 0, $s, $s)
    $g.Dispose(); $brush.Dispose(); $scaled.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , @($s, $ms.ToArray())
}
$src.Dispose()

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
