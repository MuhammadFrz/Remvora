# Remvora v1.1.0 — Release Notes

## Overview
**Remvora v1.1.0** is a major feature update delivering standalone single-file installers, an overhauled asynchronous System Cleaner engine with live granular progress, batch application uninstallation with failure isolation, deep system scanning, delta updates, and refined Windows 11 Fluent UX.

---

## What's New in v1.1.0

### 🚀 Standalone Single-File Installers (`Setup.exe`)
- **Turnkey Setup Executable**: Download `Remvora-Setup-v1.1.0-<arch>.exe` and run it directly. No ZIP extraction required.
- **Embedded Payload Compression**: The entire self-contained application runtime is packaged directly inside the installer executable and extracts cleanly to `%LocalAppData%\Programs\Remvora`.
- **Desktop & Start Menu Integration**: Automatically creates modern app shortcuts and registers cleanly with Windows Settings > Apps.

### 🧹 System Cleaner Overhaul & Background Execution
- **Asynchronous Execution (`Task.Run`)**: Scanning and file deletion now execute on background thread pool threads without freezing the WinUI 3 UI thread.
- **Fluid Granular Progress**: Intermediate directory reporting updates file counts and paths in real-time as large directories (like `%TEMP%`) are enumerated.
- **Select All & Deselect All**: High-contrast selection toolbars added to both Junk Cleaner and Privacy Cleaner with tri-state master checkboxes and quick action buttons.
- **In-Place Cleanup Updates**: Cleaned categories update their counters in-place to `0 B` without flashing or erasing the cleanup success message.
- **Fixed Status String Formatting**: Eliminated the confusing `"Scanning Scan Complete..."` text collision.

### 📦 Batch Application Uninstaller
- **Multi-Select Uninstallation**: Select multiple installed applications or Windows Store packages and remove them in a single batch operation.
- **Failure Isolation**: If an uninstaller fails or encounters an issue, subsequent uninstalls proceed unaffected.
- **Batch Execution Report**: A detailed summary dialog displays succeeded and failed uninstall operations with direct logs.

### 🔍 Deep System Scan
- **Comprehensive Audit**: Discovers redundant Windows files, error dumps, application caches (Chrome, Edge, Discord, Spotify), orphaned remnants in `%AppData%` / `ProgramData`, and broken registry keys.
- **Granular Category Cleaning**: Choose precisely which scan findings to wipe or retain.

### ⚡ Delta Updates & Lifetime Statistics
- **Ultra-Fast Delta Updates**: Incremental update archives (~1.5 MB) download only changed binaries instead of re-downloading the entire 90+ MB package.
- **Lifetime Cleaning Metrics**: Dashboard cards and graphs track total lifetime disk space reclaimed and files purged.

### 🎨 Windows 11 Fluent UI Polish
- **View Toggles**: Grid View and List View options with responsive layout and fixed empty slot sizing.
- **High Contrast Borders**: Elevated visibility across cards, lists, and dialogs.

---

## Download Assets

| Distribution | Architecture | Type | File Name |
| :--- | :--- | :--- | :--- |
| **Windows x64** (Standard PC) | `x64` | Standalone Installer | `Remvora-Setup-v1.1.0-win-x64.exe` |
| **Windows x64** (Portable) | `x64` | Clean Archive | `Remvora-v1.1.0-win-x64.zip` |
| **Windows ARM64** (Copilot+ / Surface) | `arm64` | Standalone Installer | `Remvora-Setup-v1.1.0-win-arm64.exe` |
| **Windows ARM64** (Portable) | `arm64` | Clean Archive | `Remvora-v1.1.0-win-arm64.zip` |
| **Windows x86** (32-bit Legacy) | `x86` | Standalone Installer | `Remvora-Setup-v1.1.0-win-x86.exe` |
| **Windows x86** (Portable) | `x86` | Clean Archive | `Remvora-v1.1.0-win-x86.zip` |
| **Delta Update Package** | `win-x64` | Incremental | `Remvora-v1.1.0-win-x64-delta.zip` |

---

## Verification & Integrity
Verify the authenticity of your downloaded files using SHA-256:

```powershell
Get-FileHash -Algorithm SHA256 <FileName>
```

| File Name | Size | SHA-256 Checksum |
| :--- | :--- | :--- |
| `Remvora-Setup-v1.1.0-win-x64.exe` | 91.39 MB | `8bd2133802b3c1bac963c0043b6722cdeb29be4eaec1fabb65db5e6a62aa67f8` |
| `Remvora-v1.1.0-win-x64.zip` | 91.28 MB | `92ab2a25e9d42acd8504ea446d162bad253d85db8ed0348e0b298ab863d8af8d` |
| `Remvora-v1.1.0-win-x64-delta.zip` | 1.57 MB | `0a337a47a5532f754bd93a2116b2b0428ef7dd7238baca69522e7a07a5171ae5` |
| `Remvora-Setup-v1.1.0-win-arm64.exe` | 88.62 MB | `38a2ac9c0fad5ca7a2f4db0b51eaaa6765711fad8daeeb08375563896508bd62` |
| `Remvora-v1.1.0-win-arm64.zip` | 88.51 MB | `4e976aad26a9cac26c33a9ed430a597fd929127aa0df394e5ff1aebbc41c7ba8` |
| `Remvora-v1.1.0-win-arm64-delta.zip` | 1.55 MB | `91b13dcf2bbfc2057fc5727258fa118c304c6d6edf4c79def6b8c28663975b7a` |
| `Remvora-Setup-v1.1.0-win-x86.exe` | 64.76 MB | `422cedef5cfd3dcfb792a075a73006ea433ec9855ab09024756ea584a9d307cf` |
| `Remvora-v1.1.0-win-x86.zip` | 64.65 MB | `6301da9422862980a9a05d95d2e65b51b62f709bc56677b649caa31ed83ecdd5` |
| `Remvora-v1.1.0-win-x86-delta.zip` | 1.55 MB | `8e02ccd5b966871f932b9a29a3cccdff5d66ac8a62a99475c3d006f0d408edba` |
