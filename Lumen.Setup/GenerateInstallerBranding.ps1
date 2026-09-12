param(
    [Parameter(Mandatory = $true)][string]$LogoPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $LogoPath)) {
    throw "LumenLogo.png was not found at $LogoPath"
}

$directory = Split-Path -Parent $OutputPath
if ($directory) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$bitmap = New-Object System.Drawing.Bitmap 493, 58, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$logo = [System.Drawing.Image]::FromFile($LogoPath)

try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # MSI's built-in banner title/subtitle are black. Keep the left side light and
    # text-free so the native installer text stays readable, with Lumen branding at right.
    $background = New-Object System.Drawing.Rectangle 0, 0, 493, 58
    $left = [System.Drawing.Color]::FromArgb(250, 249, 252)
    $right = [System.Drawing.Color]::FromArgb(239, 233, 250)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush $background, $left, $right, 0.0
    try {
        $graphics.FillRectangle($gradient, $background)
    } finally {
        $gradient.Dispose()
    }

    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(149, 103, 232))
    try {
        $graphics.FillRectangle($accent, 0, 55, 493, 3)
    } finally {
        $accent.Dispose()
    }

    $maxW = 42.0
    $maxH = 42.0
    $scale = [Math]::Min($maxW / $logo.Width, $maxH / $logo.Height)
    $width = [int]($logo.Width * $scale)
    $height = [int]($logo.Height * $scale)
    $x = 493 - $width - 14
    $y = [int]((55 - $height) / 2)
    $graphics.DrawImage($logo, $x, $y, $width, $height)

    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
} finally {
    $logo.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Host "Generated Lumen installer banner: $OutputPath"
