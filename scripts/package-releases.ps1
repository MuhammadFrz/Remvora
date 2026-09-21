<#
.SYNOPSIS
    Builds and packages Remvora release bundles for all supported Windows architectures.
.DESCRIPTION
    Compiles and publishes self-contained Release distributions for:
      - win-x64   (64-bit Intel & AMD)
      - win-arm64 (64-bit ARM / Snapdragon X Elite / Surface Pro)
      - win-x86   (32-bit Legacy Windows)
    Bundles the main UI executable, elevated worker, icons, and installation scripts
    into .zip archives inside the /releases directory with SHA-256 checksums.
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string[]]$Architectures = @("win-x64", "win-arm64", "win-x86")
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$releasesDir = Join-Path $rootDir "releases"

if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
}

$checksums = @()

foreach ($arch in $Architectures) {
    Write-Host "`n========================================================" -ForegroundColor Cyan
    Write-Host "Packaging Remvora v$Version for architecture: $arch" -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan

    $stagingDir = Join-Path $rootDir "publish\$arch"
    if (Test-Path $stagingDir) {
        Remove-Item -Path $stagingDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    # 1. Publish Remvora.App
    Write-Host "Publishing Remvora.App ($arch)..." -ForegroundColor Yellow
    dotnet publish "$rootDir\src\Remvora.App\Remvora.App.csproj" `
        -c Release `
        -r $arch `
        --self-contained `
        -p:PublishReadyToRun=false `
        -p:PublishTrimmed=false `
        -o $stagingDir

    # 2. Publish Remvora.Elevation
    Write-Host "Publishing Remvora.Elevation ($arch)..." -ForegroundColor Yellow
    dotnet publish "$rootDir\src\Remvora.Elevation\Remvora.Elevation.csproj" `
        -c Release `
        -r $arch `
        --self-contained `
        -o $stagingDir

    # 3. Include installation scripts
    Copy-Item -Path "$scriptDir\install.ps1" -Destination $stagingDir -Force
    Copy-Item -Path "$scriptDir\uninstall.ps1" -Destination $stagingDir -Force

    # 4. Create ZIP archive
    $zipFileName = "Remvora-v$Version-$arch.zip"
    $zipFilePath = Join-Path $releasesDir $zipFileName

    if (Test-Path $zipFilePath) {
        Remove-Item -Path $zipFilePath -Force
    }

    Write-Host "Compressing to $zipFileName..." -ForegroundColor Green
    Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipFilePath -CompressionLevel Optimal

    # 5. Compute SHA-256 Checksum
    $hash = (Get-FileHash -Path $zipFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksums += "$hash  $zipFileName"
    Write-Host "SHA-256: $hash" -ForegroundColor Gray
}

# 6. Write checksums file
$checksumPath = Join-Path $releasesDir "checksums-sha256.txt"
$checksums | Set-Content -Path $checksumPath -Encoding UTF8

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "Release packaging complete! Artifacts created in: $releasesDir" -ForegroundColor Green
Get-ChildItem -Path $releasesDir | ForEach-Object {
    Write-Host "  - $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor Yellow
}
Write-Host "========================================================" -ForegroundColor Green
