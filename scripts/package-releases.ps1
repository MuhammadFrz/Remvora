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
$packagesManifest = @{}

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

    # 3. Compile clean root launcher Remvora.exe and Setup.exe alias
    $targetLauncher = Join-Path $stagingDir "Remvora.exe"
    $setupLauncher = Join-Path $stagingDir "Setup.exe"
    Write-Host "Compiling root launcher Remvora.exe & Setup.exe..." -ForegroundColor Yellow
    if (Test-Path $csc) {
        & $csc /target:winexe /r:System.IO.Compression.dll "/win32icon:$iconPath" "/out:$targetLauncher" "$launcherSrc" | Out-Null
        Copy-Item -Path $targetLauncher -Destination $setupLauncher -Force
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

    # 6. Compute Full Package SHA-256 Checksum
    $hash = (Get-FileHash -Path $zipFilePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $fullSize = (Get-Item $zipFilePath).Length
    $checksums += "$hash  $zipFileName"
    Write-Host "Full SHA-256: $hash" -ForegroundColor Gray

    # 6b. Compile Standalone Single-File Installer (Setup.exe containing entire payload embedded)
    $setupExeName = "Remvora-Setup-v$Version-$arch.exe"
    $setupExePath = Join-Path $releasesDir $setupExeName
    if (Test-Path $setupExePath) { Remove-Item -Path $setupExePath -Force }
    Write-Host "Compiling standalone single-file installer $setupExeName..." -ForegroundColor Green
    $setupHash = ""
    $setupSize = 0
    if (Test-Path $csc) {
        & $csc /target:winexe /r:System.IO.Compression.dll "/win32icon:$iconPath" "/resource:$zipFilePath,RemvoraPayload" "/out:$setupExePath" "$launcherSrc" | Out-Null
        $setupHash = (Get-FileHash -Path $setupExePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $setupSize = (Get-Item $setupExePath).Length
        $checksums += "$setupHash  $setupExeName"
        Write-Host "Standalone Installer SHA-256: $setupHash ($([math]::Round($setupSize / 1MB, 2)) MB)" -ForegroundColor Gray
    }

    # 7. Create Delta Archive (only Remvora assemblies and assets ~2-3 MB)
    $deltaStagingDir = Join-Path $rootDir "publish\$arch-delta"
    if (Test-Path $deltaStagingDir) { Remove-Item -Path $deltaStagingDir -Recurse -Force }
    New-Item -ItemType Directory -Path $deltaStagingDir -Force | Out-Null

    Get-ChildItem -Path $appStagingDir -Filter "Remvora*.*" | Copy-Item -Destination $deltaStagingDir -Force
    if (Test-Path (Join-Path $appStagingDir "Assets")) {
        Copy-Item -Path (Join-Path $appStagingDir "Assets") -Destination $deltaStagingDir -Recurse -Force
    }
    if (Test-Path (Join-Path $appStagingDir "resources.pri")) {
        Copy-Item -Path (Join-Path $appStagingDir "resources.pri") -Destination $deltaStagingDir -Force
    }

    $deltaZipName = "Remvora-v$Version-$arch-delta.zip"
    $deltaZipPath = Join-Path $releasesDir $deltaZipName
    if (Test-Path $deltaZipPath) { Remove-Item -Path $deltaZipPath -Force }

    Write-Host "Compressing Delta package to $deltaZipName..." -ForegroundColor Cyan
    Compress-Archive -Path "$deltaStagingDir\*" -DestinationPath $deltaZipPath -CompressionLevel Optimal
    Remove-Item -Path $deltaStagingDir -Recurse -Force

    $deltaHash = (Get-FileHash -Path $deltaZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $deltaSize = (Get-Item $deltaZipPath).Length
    $checksums += "$deltaHash  $deltaZipName"
    Write-Host "Delta SHA-256: $deltaHash ($([math]::Round($deltaSize / 1MB, 2)) MB)" -ForegroundColor Gray

    $packagesManifest[$arch] = @{
        installerExe = @{
            url = "https://github.com/MuhammadFrz/Remvora/releases/download/v$Version/$setupExeName"
            sha256 = $setupHash
            sizeBytes = $setupSize
        }
        fullPackage = @{
            url = "https://github.com/MuhammadFrz/Remvora/releases/download/v$Version/$zipFileName"
            sha256 = $hash
            sizeBytes = $fullSize
        }
        deltaPackage = @{
            url = "https://github.com/MuhammadFrz/Remvora/releases/download/v$Version/$deltaZipName"
            sha256 = $deltaHash
            sizeBytes = $deltaSize
        }
    }
}

# 8. Write checksums file
$checksumPath = Join-Path $releasesDir "checksums-sha256.txt"
[System.IO.File]::WriteAllLines($checksumPath, $checksums, [System.Text.Encoding]::UTF8)

# 9. Write manifest.json
$manifestObj = @{
    version = $Version
    releaseDate = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    releaseNotes = "- Overhauled System Cleaner with deep scanning and safe file deletion`n- Added real-time progress bar with live percentage and file counters`n- Added standalone single-file installer (Setup.exe) with embedded payload"
    minDeltaVersion = "1.0.0"
    packages = $packagesManifest
}
$manifestJson = $manifestObj | ConvertTo-Json -Depth 6
$manifestPath = Join-Path $releasesDir "manifest.json"
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.Encoding]::UTF8)

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "All clean distributions packaged! Output in: $releasesDir" -ForegroundColor Green
Get-ChildItem -Path $releasesDir | ForEach-Object {
    Write-Host "  - $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor Yellow
}
Write-Host "========================================================" -ForegroundColor Green
