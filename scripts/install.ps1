<#
.SYNOPSIS
    Installs Remvora onto the local Windows machine.
.DESCRIPTION
    Builds and publishes the self-contained Remvora application, copies it to
    $env:LocalAppData\Programs\Remvora, creates Start Menu and Desktop shortcuts,
    and registers Remvora in Windows "Installed Apps" (Programs and Features).
.PARAMETER Scope
    User (default, no admin required, installs to %LocalAppData%\Programs\Remvora)
    or System (requires Run as Administrator, installs to %ProgramFiles%\Remvora).
#>
[CmdletBinding()]
param(
    [ValidateSet("User", "System")]
    [string]$Scope = "User",
    [switch]$SkipBuild,
    [switch]$CreateDesktopShortcut
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir

# 1. Determine target directory
if ($Scope -eq "System") {
    $installDir = Join-Path $env:ProgramFiles "Remvora"
    $uninstallRegKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Remvora"
    $startMenuDir = [System.IO.Path]::Combine($env:ProgramData, "Microsoft\Windows\Start Menu\Programs")
} else {
    $installDir = Join-Path $env:LOCALAPPDATA "Programs\Remvora"
    $uninstallRegKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Remvora"
    $startMenuDir = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs")
}

# 2. Publish if not skipped
$publishDir = Join-Path $rootDir "publish\Remvora"
if (-not $SkipBuild -or -not (Test-Path $publishDir)) {
    Write-Host "Publishing Remvora.App (Release, win-x64, self-contained)..." -ForegroundColor Cyan
    dotnet publish "$rootDir\src\Remvora.App\Remvora.App.csproj" -c Release -r win-x64 --self-contained -o $publishDir
    Write-Host "Publishing Remvora.Elevation..." -ForegroundColor Cyan
    dotnet publish "$rootDir\src\Remvora.Elevation\Remvora.Elevation.csproj" -c Release -r win-x64 --self-contained -o $publishDir
}

# 3. Create destination and copy files
Write-Host "Installing Remvora to: $installDir" -ForegroundColor Cyan
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

Copy-Item -Path "$publishDir\*" -Destination $installDir -Recurse -Force

# 4. Copy uninstall script to install directory
$installedUninstallScript = Join-Path $installDir "uninstall.ps1"
Copy-Item -Path "$scriptDir\uninstall.ps1" -Destination $installedUninstallScript -Force

# 5. Create Start Menu Shortcut
$wshShell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path $startMenuDir "Remvora.lnk"
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $installDir "Remvora.App.exe"
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = "$installDir\Assets\AppIcon.ico,0"
$shortcut.Description = "Remvora - Windows Uninstaller and Cleanup Utility"
$shortcut.Save()
Write-Host "Created Start Menu shortcut: $shortcutPath" -ForegroundColor Green

# 6. Optional Desktop Shortcut
if ($CreateDesktopShortcut) {
    $desktopDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
    $desktopShortcutPath = Join-Path $desktopDir "Remvora.lnk"
    $desktopShortcut = $wshShell.CreateShortcut($desktopShortcutPath)
    $desktopShortcut.TargetPath = Join-Path $installDir "Remvora.App.exe"
    $desktopShortcut.WorkingDirectory = $installDir
    $desktopShortcut.IconLocation = "$installDir\Assets\AppIcon.ico,0"
    $desktopShortcut.Description = "Remvora - Windows Uninstaller and Cleanup Utility"
    $desktopShortcut.Save()
    Write-Host "Created Desktop shortcut: $desktopShortcutPath" -ForegroundColor Green
}

# 7. Register in Windows Installed Apps
if (-not (Test-Path $uninstallRegKey)) {
    New-Item -Path $uninstallRegKey -Force | Out-Null
}

Set-ItemProperty -Path $uninstallRegKey -Name "DisplayName" -Value "Remvora"
Set-ItemProperty -Path $uninstallRegKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $uninstallRegKey -Name "Publisher" -Value "Remvora"
Set-ItemProperty -Path $uninstallRegKey -Name "InstallLocation" -Value $installDir
Set-ItemProperty -Path $uninstallRegKey -Name "DisplayIcon" -Value (Join-Path $installDir "Assets\AppIcon.ico")
Set-ItemProperty -Path $uninstallRegKey -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$installedUninstallScript`""
Set-ItemProperty -Path $uninstallRegKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallRegKey -Name "NoRepair" -Value 1 -Type DWord

Write-Host "Remvora registered in Windows Installed Apps registry." -ForegroundColor Green
Write-Host "Installation completed successfully!" -ForegroundColor Green
Write-Host "You can now launch Remvora from the Start Menu or run: & '$installDir\Remvora.App.exe'" -ForegroundColor Yellow
