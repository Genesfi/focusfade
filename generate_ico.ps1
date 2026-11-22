Add-Type -AssemblyName System.Drawing

function Create-FocusFadeBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.05))
    $rSize = $size - ($pad * 2)
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, $rSize, $rSize)
    $radius = [Math]::Max(2, [int]($size * 0.22))

    # Rounded path
    $r2 = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $r2, $r2, 180, 90)
    $path.AddArc($rect.Right - $r2, $rect.Y, $r2, $r2, 270, 90)
    $path.AddArc($rect.Right - $r2, $rect.Bottom - $r2, $r2, $r2, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $r2, $r2, $r2, 90, 90)
    $path.CloseFigure()

    # Gradient background
    $c1 = [System.Drawing.Color]::FromArgb(99, 102, 241)
    $c2 = [System.Drawing.Color]::FromArgb(168, 85, 247)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $path)

    # Subtle inner border
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(216, 180, 254), [Math]::Max(1.0, $size * 0.03))
    $g.DrawPath($pen, $path)

    # 4 Soundwave bars (white)
    $barBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $barW = [Math]::Max(2, [int]($size * 0.09))
    $gap = [Math]::Max(1, [int]($size * 0.06))
    $totalBarsW = (4 * $barW) + (3 * $gap)
    $startX = [int](($size - $totalBarsW) / 2)
    $centerY = [int]($size / 2)

    $heightPcts = @(0.35, 0.78, 0.55, 0.28)
    for ($i = 0; $i -lt 4; $i++) {
        $bHeight = [Math]::Max(3, [int]($size * 0.65 * $heightPcts[$i]))
        $bx = $startX + ($i * ($barW + $gap))
        $by = $centerY - [int]($bHeight / 2)
        
        $bRadius = [Math]::Min($barW / 2, 3)
        if ($bRadius -gt 1) {
            $bPath = New-Object System.Drawing.Drawing2D.GraphicsPath
            $br2 = $bRadius * 2
            $bRect = New-Object System.Drawing.Rectangle($bx, $by, $barW, $bHeight)
            $bPath.AddArc($bRect.X, $bRect.Y, $br2, $br2, 180, 90)
            $bPath.AddArc($bRect.Right - $br2, $bRect.Y, $br2, $br2, 270, 90)
            $bPath.AddArc($bRect.Right - $br2, $bRect.Bottom - $br2, $br2, $br2, 0, 90)
            $bPath.AddArc($bRect.X, $bRect.Bottom - $br2, $br2, $br2, 90, 90)
            $bPath.CloseFigure()
            $g.FillPath($barBrush, $bPath)
            $bPath.Dispose()
        } else {
            $g.FillRectangle($barBrush, $bx, $by, $barW, $bHeight)
        }
    }

    $barBrush.Dispose()
    $pen.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $g.Dispose()

    return $bmp
}

$sizes = @(16, 32, 48, 64, 128, 256)
$pngStreams = @()

foreach ($sz in $sizes) {
    $bmp = Create-FocusFadeBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngStreams += ,@($sz, $ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}

$icoFile = "C:\Users\Gusti N\.gemini\antigravity-ide\scratch\FocusFade\app.ico"
$fs = [System.IO.File]::Create($icoFile)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([UInt16]0) # Reserved
$bw.Write([UInt16]1) # Type 1 = ICO
$bw.Write([UInt16]$pngStreams.Count) # Count

$offset = 6 + (16 * $pngStreams.Count)

foreach ($entry in $pngStreams) {
    $sz = $entry[0]
    $data = $entry[1]

    $w = if ($sz -ge 256) { 0 } else { [byte]$sz }
    $h = if ($sz -ge 256) { 0 } else { [byte]$sz }

    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)   # Color count
    $bw.Write([byte]0)   # Reserved
    $bw.Write([UInt16]1) # Color planes
    $bw.Write([UInt16]32)# Bit count
    $bw.Write([UInt32]$data.Length) # BytesInRes
    $bw.Write([UInt32]$offset)      # ImageOffset

    $offset += $data.Length
}

foreach ($entry in $pngStreams) {
    $data = $entry[1]
    $bw.Write($data)
}

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Host "Created app.ico successfully with $($pngStreams.Count) resolutions!"
