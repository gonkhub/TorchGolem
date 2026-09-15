# Draws thunderstore/icon.png (256x256, required by Thunderstore). Rerun to regenerate.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = "AntiAlias"

function Brush($hex) { New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex)) }

# Night-sky background with a warm glow behind the golem.
$g.Clear([System.Drawing.ColorTranslator]::FromHtml("#161a22"))
$glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$glowPath.AddEllipse(8, 24, 240, 240)
$glow = New-Object System.Drawing.Drawing2D.PathGradientBrush $glowPath
$glow.CenterColor = [System.Drawing.Color]::FromArgb(170, 255, 140, 40)
$glow.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 22, 26, 34))
$g.FillPath($glow, $glowPath)

# Stone golem: body, head, arms, legs.
$stone = Brush "#6f6a63"
$stoneDark = Brush "#4d4944"
$g.FillEllipse($stoneDark, 78, 206, 100, 22)                 # shadow
$g.FillRectangle($stone, 96, 180, 24, 36)                    # left leg
$g.FillRectangle($stone, 136, 180, 24, 36)                   # right leg
$g.FillEllipse($stone, 76, 108, 104, 90)                     # body
$g.FillEllipse($stoneDark, 58, 124, 30, 58)                  # left arm
$g.FillEllipse($stoneDark, 166, 112, 30, 50)                 # right arm (raised)
$g.FillEllipse($stone, 94, 70, 68, 56)                       # head
$eyes = Brush "#ffb347"
$g.FillEllipse($eyes, 110, 90, 12, 12)
$g.FillEllipse($eyes, 134, 90, 12, 12)
$g.FillEllipse((Brush "#ffcf7a"), 118, 136, 20, 20)          # ember core in chest

# Torch held up in the right hand.
$g.FillRectangle((Brush "#7a4a25"), 181, 58, 9, 70)
$flameOuter = New-Object System.Drawing.Drawing2D.GraphicsPath
$flameOuter.AddBezier(185, 64, 158, 42, 178, 22, 186, 6)
$flameOuter.AddBezier(186, 6, 196, 26, 214, 42, 185, 64)
$g.FillPath((Brush "#ff7a1a"), $flameOuter)
$flameInner = New-Object System.Drawing.Drawing2D.GraphicsPath
$flameInner.AddBezier(185, 60, 172, 48, 180, 34, 186, 24)
$flameInner.AddBezier(186, 24, 192, 36, 200, 48, 185, 60)
$g.FillPath((Brush "#ffe08a"), $flameInner)

$out = Join-Path $PSScriptRoot "..\thunderstore\icon.png"
$bmp.Save((Resolve-Path (Split-Path $out)).Path + "\icon.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "Wrote $out"
