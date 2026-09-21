<#
.SYNOPSIS
    Builds clean, professional Remvora release packages for all Windows architectures.
.DESCRIPTION
    Creates clean distributions where all DLLs, runtimes, and satellite folders
    are encapsulated inside an 'app' subfolder, leaving only the clean Remvora.exe
    launcher, install.ps1, and uninstall.ps1 in the root of the ZIP archive.
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
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$launcherSrc = Join-Path $scriptDir "Launcher.cs"
$iconPath = Join-Path $rootDir "src\Remvora.App\Assets\AppIcon.ico"

if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
}

$checksums = @()

foreach ($arch in $Architectures) {
    Write-Host "`n========================================================" -ForegroundColor Cyan
    Write-Host "Packaging clean Remvora v$Version distribution: $arch" -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan

    $stagingDir = Join-Path $rootDir "publish\$arch"
    $appStagingDir = Join-Path $stagingDir "app"

    if (Test-Path $stagingDir) {
        Remove-Item -Path $stagingDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $appStagingDir -Force | Out-Null

    # 1. Publish Remvora.App to app/
    Write-Host "Publishing Remvora.App ($arch) into app/..." -ForegroundColor Yellow
    dotnet publish "$rootDir\src\Remvora.App\Remvora.App.csproj" `
        -c Release `
        -r $arch `
        --self-contained `
        -p:PublishReadyToRun=false `
        -p:PublishTrimmed=false `
        -o $appStagingDir

    # 2. Publish Remvora.Elevation to app/
    Write-Host "Publishing Remvora.Elevation ($arch) into app/..." -ForegroundColor Yellow
    dotnet publish "$rootDir\src\Remvora.Elevation\Remvora.Elevation.csproj" `
        -c Release `
        -r $arch `
        --self-contained `
        -o $appStagingDir

    # 3. Compile clean root launcher Remvora.exe
    $targetLauncher = Join-Path $stagingDir "Remvora.exe"
    Write-Host "Compiling root launcher Remvora.exe..." -ForegroundColor Yellow
    if (Test-Path $csc) {
        & $csc /target:winexe "/win32icon:$iconPath" "/out:$targetLauncher" "$launcherSrc" | Out-Null
    }

    # 4. Copy installation and uninstallation scripts to root
    Copy-Item -Path "$scriptDir\install.ps1" -Destination $stagingDir -Force
    Copy-Item -Path "$scriptDir\uninstall.ps1" -Destination $stagingDir -Force

    # 5. Create clean ZIP archive
    $zipFileName = "Remvora-v$Version-$arch.zip"
    $zipFilePath = Join-Path $releasesDir $zipFileName

    if (Test-Path $zipFilePath) {
        Remove-Item -Path $zipFilePath -Force
    }

    Write-Host "Compressing to $zipFileName..." -ForegroundColor Green
    Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipFilePath -CompressionLevel Optimal

    # 6. Compute SHA-256 Checksum
    $hash = (Get-FileHash -Path $zipFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksums += "$hash  $zipFileName"
    Write-Host "SHA-256: $hash" -ForegroundColor Gray
}

# 7. Write checksums file
$checksumPath = Join-Path $releasesDir "checksums-sha256.txt"
$checksums | Set-Content -Path $checksumPath -Encoding UTF8

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "All clean distributions packaged! Output in: $releasesDir" -ForegroundColor Green
Get-ChildItem -Path $releasesDir | ForEach-Object {
    Write-Host "  - $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor Yellow
}
Write-Host "========================================================" -ForegroundColor Green
