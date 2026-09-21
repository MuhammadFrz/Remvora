<#
.SYNOPSIS
    Uninstalls Remvora from the local Windows machine.
#>
[CmdletBinding()]
param(
    [ValidateSet("User", "System")]
    [string]$Scope = "User"
)

$ErrorActionPreference = "SilentlyContinue"

if ($Scope -eq "System") {
    $installDir = Join-Path $env:ProgramFiles "Remvora"
    $uninstallRegKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Remvora"
    $startMenuDir = [System.IO.Path]::Combine($env:ProgramData, "Microsoft\Windows\Start Menu\Programs")
} else {
    $installDir = Join-Path $env:LOCALAPPDATA "Programs\Remvora"
    $uninstallRegKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Remvora"
    $startMenuDir = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs")
}

# 1. Stop any running Remvora processes
Get-Process -Name "Remvora.App", "Remvora.ElevatedWorker" -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Remove shortcuts
$startShortcut = Join-Path $startMenuDir "Remvora.lnk"
if (Test-Path $startShortcut) { Remove-Item $startShortcut -Force }

$desktopDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$desktopShortcut = Join-Path $desktopDir "Remvora.lnk"
if (Test-Path $desktopShortcut) { Remove-Item $desktopShortcut -Force }

# 3. Remove registry entry
if (Test-Path $uninstallRegKey) {
    Remove-Item -Path $uninstallRegKey -Recurse -Force
}

# 4. Remove installed directory
if (Test-Path $installDir) {
    # If running from inside installDir, wait briefly then delete
    Start-Sleep -Milliseconds 500
    Remove-Item -Path $installDir -Recurse -Force
}

Write-Host "Remvora has been cleanly uninstalled from your machine." -ForegroundColor Green
