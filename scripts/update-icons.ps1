<#
.SYNOPSIS
    Converts a source icon PNG into all Windows App SDK and WinUI icon assets.
#>
[CmdletBinding()]
param(
    [string]$SourceImage = "icon.png"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$rootDir = Split-Path -Parent $PSScriptRoot
$sourceFullPath = Join-Path $rootDir $SourceImage
$assetsDir = Join-Path $rootDir "src\Remvora.App\Assets"
$docsAssetsDir = Join-Path $rootDir "docs\assets"

if (-not (Test-Path $docsAssetsDir)) {
    New-Item -ItemType Directory -Path $docsAssetsDir -Force | Out-Null
}

# Copy high-res icon to docs/assets and app assets
Copy-Item $sourceFullPath (Join-Path $docsAssetsDir "icon.png") -Force
Copy-Item $sourceFullPath (Join-Path $assetsDir "icon.png") -Force

$srcBitmap = [System.Drawing.Bitmap]::FromFile($sourceFullPath)

function Resize-Bitmap($bmp, [int]$width, [int]$height) {
    $dest = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($dest)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($bmp, 0, 0, $width, $height)
    $g.Dispose()
    return $dest
}

function Save-Png($bmp, [string]$path) {
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

# 1. Resize specific WinUI assets
$sizes = @{
    "Square44x44Logo.png" = @{w=44; h=44}
    "Square44x44Logo.targetsize-24_altform-unplated.png" = @{w=24; h=24}
    "Square44x44Logo.targetsize-48_altform-lightunplated.png" = @{w=48; h=48}
    "Square150x150Logo.scale-200.png" = @{w=300; h=300}
    "StoreLogo.png" = @{w=50; h=50}
}

foreach ($entry in $sizes.GetEnumerator()) {
    $targetPath = Join-Path $assetsDir $entry.Key
    $resized = Resize-Bitmap $srcBitmap $entry.Value.w $entry.Value.h
    Save-Png $resized $targetPath
    $resized.Dispose()
    Write-Host "Updated $($entry.Key)" -ForegroundColor Gray
}

# 2. Build multi-resolution .ico (256, 128, 64, 48, 32, 16)
$icoSizes = @(256, 128, 64, 48, 32, 16)
$pngBytesList = @()

foreach ($s in $icoSizes) {
    $resized = Resize-Bitmap $srcBitmap $s $s
    $ms = New-Object System.IO.MemoryStream
    $resized.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytesList += ,@($s, $ms.ToArray())
    $ms.Dispose()
    $resized.Dispose()
}

$srcBitmap.Dispose()

# Write ICO binary format
$icoPath = Join-Path $assetsDir "AppIcon.ico"
$fs = New-Object System.IO.FileStream $icoPath, ([System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter $fs

# ICONDIR
$bw.Write([uint16]0) # Reserved
$bw.Write([uint16]1) # Type 1 = Icon
$bw.Write([uint16]$pngBytesList.Count) # Image count

$offset = 6 + ($pngBytesList.Count * 16)

# Write Entries
foreach ($item in $pngBytesList) {
    $s = $item[0]
    $data = $item[1]
    $bWidth = if ($s -ge 256) { 0 } else { [byte]$s }
    $bHeight = if ($s -ge 256) { 0 } else { [byte]$s }
    $bw.Write([byte]$bWidth)
    $bw.Write([byte]$bHeight)
    $bw.Write([byte]0) # Color count
    $bw.Write([byte]0) # Reserved
    $bw.Write([uint16]1) # Planes
    $bw.Write([uint16]32) # Bit count
    $bw.Write([uint32]$data.Length) # Bytes in resource
    $bw.Write([uint32]$offset) # Image offset
    $offset += $data.Length
}

# Write PNG data chunks
foreach ($item in $pngBytesList) {
    $bw.Write($item[1])
}

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Host "AppIcon.ico generated successfully at: $icoPath" -ForegroundColor Green
