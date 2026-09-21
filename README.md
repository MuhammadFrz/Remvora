# Remvora

**Remvora** is a modern, native Windows 11 desktop uninstaller and system cleanup utility engineered with WinUI 3 and .NET 10. It prioritizes safety, transparency, transaction-backed recovery, and evidence-based software removal.

---

## Navigation Menus Overview

Remvora includes a comprehensive sidebar navigation with dedicated tools:

| Menu | Description |
| :--- | :--- |
| **Dashboard** | System overview displaying installed applications count, detected junk cache size, startup load impact, and rapid-action buttons. |
| **Installed Apps** | Main uninstaller grid aggregating 32-bit, 64-bit, and MSI installations. Supports **Standard**, **Complete**, and **Forced** uninstallation flows with System Restore checkpoints and interactive leftover candidate review. |
| **Install Monitor** | **Installation Monitor menu** — A real-time tracking engine for monitoring third-party software setups. Captures pre- and post-installation snapshots, identifies every file, directory, and registry key written, and saves the session for 100% clean future uninstallation. |
| **Windows Apps** | Discovery and clean removal of modern Universal Windows Platform (UWP) and MSIX / AppX packages. |
| **Startup Manager** | Comprehensive autorun manager inspecting Registry Run keys (`HKCU`/`HKLM`), Windows Startup folders, and Task Scheduler triggers with toggle enable/disable controls. |
| **System Cleaner** | Evidence-based junk file cleaner (temporary directories, crash dumps, thumbnail caches) and privacy cleaner (Windows MRU lists, Explorer history). |
| **Hunter Mode** | Interactive desktop targeting crosshair overlay. Drag onto any open window or desktop shortcut to identify the parent executable, terminate rogue processes, or trigger instant targeted uninstallation. |
| **Windows Tools** | Quick-launch command hub providing one-click access to 16 native Windows administrative utilities (Event Viewer, Services, Task Manager, Disk Management, Registry Editor, etc.). |
| **Audit Log** | Chronological timeline and transaction log of every uninstall, cleanup, backup, and rollback operation, backed by SQLite. |

---

## How to Install Remvora on Windows

You have multiple options to install and run Remvora:

### Option 1: One-Click Local Installation (Recommended)

Run the included PowerShell installation script. This script builds and publishes Remvora as a self-contained application, installs it into your user profile (`%LocalAppData%\Programs\Remvora`), creates a Start Menu shortcut, and registers Remvora in Windows "Installed Apps" with a clean uninstaller:

```powershell
# From the repository root (in PowerShell):
.\scripts\install.ps1
```

*Optional Flags:*
- `-CreateDesktopShortcut`: Adds a shortcut on your Windows Desktop.
- `-Scope System`: Installs machine-wide to `%ProgramFiles%\Remvora` (requires running PowerShell as Administrator).
- `-SkipBuild`: Installs an already published build without rebuilding.

To cleanly uninstall Remvora at any time, run:
```powershell
.\scripts\uninstall.ps1
```
Or open **Windows Settings > Apps > Installed Apps** and select **Uninstall** next to Remvora.

---

### Option 2: Run Directly (Development Mode)

If you have the .NET SDK installed, you can launch Remvora directly without installation:

```powershell
dotnet run --project src/Remvora.App/Remvora.App.csproj
```

---

### Option 3: Manual Self-Contained Publish

To create a standalone portable directory containing all required runtimes:

```powershell
# 1. Publish Remvora.App
dotnet publish src/Remvora.App/Remvora.App.csproj -c Release -r win-x64 --self-contained -o ./publish/Remvora

# 2. Publish Remvora.ElevatedWorker (required for privileged operations)
dotnet publish src/Remvora.Elevation/Remvora.Elevation.csproj -c Release -r win-x64 --self-contained -o ./publish/Remvora

# 3. Run the executable
.\publish\Remvora\Remvora.App.exe
```

---

## Testing & Quality

To run the automated test suite across all architectural layers:

```powershell
dotnet test Remvora.slnx
```

---

## Architecture & Security

- **Privilege Separation**: Remvora's UI runs unelevated (standard user). When root-level actions are required (e.g. deleting locked services or `HKLM` keys), an isolated out-of-process worker (`Remvora.ElevatedWorker.exe`) is spawned on-demand via authenticated IPC with cryptographic nonce verification.
- **Rollback Engine**: Backs up registry keys to `.reg` format and files to an isolated backup store before deletion, enabling one-click transaction rollbacks.
- **Safety First**: Protected system paths (`Windows`, `System32`, critical boot keys) are shielded by hardcoded safety guardrails.
