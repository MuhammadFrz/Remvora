<#
.SYNOPSIS
    Installs Remvora onto the local Windows machine.
.DESCRIPTION
    Installs the clean Remvora distribution to:
      %LocalAppData%\Programs\Remvora (User Scope)
    or
      %ProgramFiles%\Remvora (System Scope)
    Creates Start Menu and Desktop shortcuts and registers Remvora in Windows "Installed Apps".
.PARAMETER Scope
    User (default, per-user, no admin required) or System (machine-wide, requires Admin).
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

Write-Host "Installing Remvora to: $installDir" -ForegroundColor Cyan
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

$appInstallDir = Join-Path $installDir "app"
if (-not (Test-Path $appInstallDir)) {
    New-Item -ItemType Directory -Path $appInstallDir -Force | Out-Null
}

# 2. Check if running from pre-packaged distribution or source repository
$localAppDir = Join-Path $scriptDir "app"
if (Test-Path $localAppDir) {
    # Running from extracted distribution zip
    Write-Host "Copying pre-packaged application files..." -ForegroundColor Gray
    Copy-Item -Path "$localAppDir\*" -Destination $appInstallDir -Recurse -Force
    if (Test-Path (Join-Path $scriptDir "Remvora.exe")) {
        Copy-Item -Path (Join-Path $scriptDir "Remvora.exe") -Destination $installDir -Force
    }
} else {
    # Running from source code
    $publishStaging = Join-Path $rootDir "publish\win-x64\app"
    if (-not $SkipBuild -or -not (Test-Path $publishStaging)) {
        Write-Host "Publishing Remvora.App (Release, win-x64)..." -ForegroundColor Cyan
        dotnet publish "$rootDir\src\Remvora.App\Remvora.App.csproj" -c Release -r win-x64 --self-contained -o $publishStaging
        Write-Host "Publishing Remvora.Elevation..." -ForegroundColor Cyan
        dotnet publish "$rootDir\src\Remvora.Elevation\Remvora.Elevation.csproj" -c Release -r win-x64 --self-contained -o $publishStaging
    }

    Copy-Item -Path "$publishStaging\*" -Destination $appInstallDir -Recurse -Force

    # Compile root launcher
    $csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $launcherSrc = Join-Path $rootDir "scripts\Launcher.cs"
    $iconPath = Join-Path $rootDir "src\Remvora.App\Assets\AppIcon.ico"
    $targetLauncher = Join-Path $installDir "Remvora.exe"
    if (Test-Path $csc) {
        Write-Host "Compiling root launcher Remvora.exe..." -ForegroundColor Gray
        & $csc /target:winexe "/win32icon:$iconPath" "/out:$targetLauncher" "$launcherSrc" | Out-Null
    }
}

# 3. Copy uninstall script
$installedUninstallScript = Join-Path $installDir "uninstall.ps1"
$srcUninstall = if (Test-Path (Join-Path $scriptDir "uninstall.ps1")) { Join-Path $scriptDir "uninstall.ps1" } else { Join-Path $rootDir "scripts\uninstall.ps1" }
if (Test-Path $srcUninstall) {
    Copy-Item -Path $srcUninstall -Destination $installedUninstallScript -Force
}

# 4. Shortcut target
$primaryExe = if (Test-Path (Join-Path $installDir "Remvora.exe")) {
    Join-Path $installDir "Remvora.exe"
} else {
    Join-Path $appInstallDir "Remvora.App.exe"
}

$iconSource = if (Test-Path (Join-Path $installDir "Remvora.exe")) {
    "$installDir\Remvora.exe,0"
} else {
    "$(Join-Path $appInstallDir 'Assets\AppIcon.ico'),0"
}

# 5. Create Start Menu Shortcut
$wshShell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path $startMenuDir "Remvora.lnk"
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $primaryExe
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = $iconSource
$shortcut.Description = "Remvora - Windows Uninstaller and Cleanup Utility"
$shortcut.Save()
Write-Host "Created Start Menu shortcut: $shortcutPath" -ForegroundColor Green

# 6. Optional Desktop Shortcut
if ($CreateDesktopShortcut) {
    $desktopDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
    $desktopShortcutPath = Join-Path $desktopDir "Remvora.lnk"
    $desktopShortcut = $wshShell.CreateShortcut($desktopShortcutPath)
    $desktopShortcut.TargetPath = $primaryExe
    $desktopShortcut.WorkingDirectory = $installDir
    $desktopShortcut.IconLocation = $iconSource
    $desktopShortcut.Description = "Remvora - Windows Uninstaller and Cleanup Utility"
    $desktopShortcut.Save()
    Write-Host "Created Desktop shortcut: $desktopShortcutPath" -ForegroundColor Green
}

# 7. Register in Windows Installed Apps
if (-not (Test-Path $uninstallRegKey)) {
    New-Item -Path $uninstallRegKey -Force | Out-Null
}

$displayIcon = if (Test-Path (Join-Path $appInstallDir "Assets\AppIcon.ico")) {
    Join-Path $appInstallDir "Assets\AppIcon.ico"
} else {
    $primaryExe
}

$detectedVersion = "1.2.1"
if (Test-Path $primaryExe) {
    try {
        $fileVer = (Get-Item $primaryExe).VersionInfo.ProductVersion
        if (-not [string]::IsNullOrWhiteSpace($fileVer)) {
            $detectedVersion = $fileVer.Split('+')[0]
        }
    } catch {}
}

Set-ItemProperty -Path $uninstallRegKey -Name "DisplayName" -Value "Remvora"
Set-ItemProperty -Path $uninstallRegKey -Name "DisplayVersion" -Value $detectedVersion
Set-ItemProperty -Path $uninstallRegKey -Name "Publisher" -Value "Remvora"
Set-ItemProperty -Path $uninstallRegKey -Name "InstallLocation" -Value $installDir
Set-ItemProperty -Path $uninstallRegKey -Name "DisplayIcon" -Value $displayIcon
Set-ItemProperty -Path $uninstallRegKey -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$installedUninstallScript`""
Set-ItemProperty -Path $uninstallRegKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallRegKey -Name "NoRepair" -Value 1 -Type DWord

Write-Host "Remvora successfully registered in Windows Installed Apps." -ForegroundColor Green
Write-Host "Installation completed! Launch from Start Menu or: & '$primaryExe'" -ForegroundColor Yellow
