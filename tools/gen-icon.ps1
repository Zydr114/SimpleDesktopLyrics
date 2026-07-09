# 生成 Assets/app.png 与 Assets/app.ico（PNG 条目的 ICO，Vista+ 支持）
# 仅开发期使用，一次性生成后产物入库。
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

# 圆形底
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point 0, 0),
    (New-Object System.Drawing.Point $size, $size),
    [System.Drawing.Color]::FromArgb(255, 64, 132, 255),
    [System.Drawing.Color]::FromArgb(255, 36, 84, 220))
$g.FillEllipse($bg, 10, 10, $size - 20, $size - 20)

# 白色八分音符
$white = [System.Drawing.Brushes]::White
$g.FillRectangle($white, 128, 58, 13, 116)          # 符干
$g.FillEllipse($white, 88, 158, 56, 40)             # 符头
$flag = New-Object System.Drawing.Drawing2D.GraphicsPath
$flag.AddBezier(141, 58, 186, 74, 196, 112, 166, 148)
$flag.AddBezier(166, 148, 186, 112, 172, 88, 141, 90)
$flag.CloseFigure()
$g.FillPath($white, $flag)
$g.Dispose()

# PNG
$msPng = New-Object System.IO.MemoryStream
$bmp.Save($msPng, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $msPng.ToArray()
[System.IO.File]::WriteAllBytes((Join-Path $assets "app.png"), $png)

# ICO（单条目，PNG 压缩，256x256 用 0 表示）
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([uint16]0)            # reserved
$bw.Write([uint16]1)            # type = icon
$bw.Write([uint16]1)            # count
$bw.Write([byte]0)              # width 256
$bw.Write([byte]0)              # height 256
$bw.Write([byte]0)              # palette
$bw.Write([byte]0)              # reserved
$bw.Write([uint16]1)            # planes
$bw.Write([uint16]32)           # bpp
$bw.Write([uint32]$png.Length)  # size
$bw.Write([uint32]22)           # offset
$bw.Write($png)
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets "app.ico"), $ms.ToArray())
$bmp.Dispose()
Write-Output "icons generated: $assets"
