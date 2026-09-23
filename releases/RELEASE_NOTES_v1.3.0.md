# Remvora v1.3.0 — Release Notes

## Overview
**Remvora v1.3.0** introduces interactive selective scanning in Deep Scan, resolves selection synchronization conflicts across all cleaning tabs, offloads deletion operations to non-blocking background workers with live progress indicators and full-screen translucent scrims, redesigns the completion dialog with native Windows 11 Fluent dark aesthetics, and fixes visual clipping in the Settings expander header.

---

## What's New in v1.3.0

### 🎯 Selective Deep System Scanning
- **Category Selection Options**: Users can now select which specific areas to inspect prior to scanning:
  - *Redundant Files* (Crash dumps, temporary data, Windows Update download cache)
  - *Application & Browser Caches* (Chrome, Edge, Firefox, Brave, Discord, Spotify, VS Code)
  - *Orphaned Leftovers* (Lingering AppData, LocalAppData, ProgramData, dead desktop shortcuts)
  - *Damaged App Registrations* (Orphaned registry uninstall keys pointing to missing software)
- **Dynamic Action Button**: Scan button updates reactively based on selection (e.g. `"Scan Orphaned Leftovers"`, `"Scan Selected Categories (2)"`, `"Start Full System Scan"`).
- **Scanner Engine Optimization**: `ISystemScanService.ScanSystemAsync` conditionally runs only requested modules in the background, accelerating scan completion times.

### 📁 Clickable Directories in Scan Results
- **Direct File Explorer Navigation**: Click any scanned target path in the results list to instantly open Windows File Explorer focused on that directory or file.
- **Interactive Visual Feedback**: Styled as a clean `HyperlinkButton` with an open-external glyph (`\uED25`), accent hover styling, and informational tooltip.

### ☑️ Unified Selection Toolbar Pattern
- **Fixed CheckBox Synchronization**: Resolved a two-way binding conflict where CheckBox clicks were accidentally inverted by redundant `Command` handlers, restoring instant group and item selection.
- **Clean Streamlined Toolbar**: Consolidated selection on Deep Scan and System Cleaner into a unified master `CheckBox` (`"Select all"`) and adjacent live summary `(X of Y items • Size)`.
- **Eliminated Redundant Controls**: Removed the duplicate `"Clear selection"` button, keeping all multi-selection controls intuitive and removing awkward empty spacing across the toolbar.
- **Fixed Empty Space Visual Bug**: Applied explicit `MinWidth="0"` and `Padding="0"` to all contentless CheckBoxes in category cards and result items, preventing WinUI 3's default 120px placeholder margin from squishing content and truncating card text.

### ⚡ Non-Blocking Deletion & Live Progress Overlays (Rule 3)
- **Zero UI Dispatcher Freezing**: All file system deletions and directory purges now execute strictly inside background worker tasks (`Task.Run`), leaving the WinUI 3 message loop fluid at 60fps.
- **Full Scrim Progress Card**: Live deletion shows an acrylic dark overlay (`#CC0B0D14`), spinning `ProgressRing`, real-time percentage indicators, and active file paths being purged.

### ✨ Redesigned Fluent Dark Completion Modal
- **Native Fluent Aesthetics**: Completely restyled completion modal matching Remvora's dark color identity (`#181A22` container, `#282C38` subtle card borders, `#B3080A0F` backdrop scrim).
- **Emerald Status Badge**: Circular glowing badge (`#10382B` with emerald `#34D399` icon) providing clear, confidence-inspiring feedback.
- **Symmetrical Metric Tiles**: Clean dark tiles (`#1F2330`) highlighting total items cleaned and exact storage space reclaimed in `#34D399` and `#38BDF8`.

### 🛠️ Settings Expander Responsive Layout Fix
- **Eliminated Header Clipping**: Removed hardcoded `Width="700"` from the GitHub Private Repository & Token Configuration expander header.
- **Responsive Flex Layout**: Text block now employs `TextTrimming="CharacterEllipsis"` and concise status badges (`"Configured"`, `"Active (Env)"`, `"Active (Local .env)"`, `"Public Access"`), preventing badge collision with the expander dropdown chevron.

---

## Download Assets

| Distribution | Architecture | Type | File Name | Size |
| :--- | :--- | :--- | :--- | :--- |
| **Windows x64** (Standard PC) | `x64` | Standalone Installer | `Remvora-Setup-v1.3.0-win-x64.exe` | 91.42 MB |
| **Windows x64** (Portable) | `x64` | Clean Archive | `Remvora-v1.3.0-win-x64.zip` | 91.31 MB |
| **Windows ARM64** (Copilot+ / Surface) | `arm64` | Standalone Installer | `Remvora-Setup-v1.3.0-win-arm64.exe` | 88.64 MB |
| **Windows ARM64** (Portable) | `arm64` | Clean Archive | `Remvora-v1.3.0-win-arm64.zip` | 88.53 MB |
| **Windows x86** (32-bit Legacy) | `x86` | Standalone Installer | `Remvora-Setup-v1.3.0-win-x86.exe` | 64.78 MB |
| **Windows x86** (Portable) | `x86` | Clean Archive | `Remvora-v1.3.0-win-x86.zip` | 64.67 MB |
| **Delta Patch (x64)** | `win-x64` | Incremental | `Remvora-v1.3.0-win-x64-delta.zip` | 1.59 MB |
| **Delta Patch (ARM64)** | `win-arm64` | Incremental | `Remvora-v1.3.0-win-arm64-delta.zip` | 1.57 MB |
| **Delta Patch (x86)** | `win-x86` | Incremental | `Remvora-v1.3.0-win-x86-delta.zip` | 1.57 MB |

---

## Verification & Integrity
Verify the authenticity of your downloaded files using SHA-256:

```powershell
Get-FileHash -Algorithm SHA256 <FileName>
```

| File Name | Size | SHA-256 Checksum |
| :--- | :--- | :--- |
| `Remvora-Setup-v1.3.0-win-x64.exe` | 91.42 MB | `1ac3bf3de54b301453948a25113d4a0f13868adf51ed119b91d95d5fc3e24758` |
| `Remvora-v1.3.0-win-x64.zip` | 91.31 MB | `b8fa5819267a9d3cb34cb179ae2ca02d47539cc2d59094f4d8737ef471909b42` |
| `Remvora-v1.3.0-win-x64-delta.zip` | 1.59 MB | `5930cccbba3fbe39c965593841b5702b5b7b0141c500cbee5713cd76cbc10fd4` |
| `Remvora-Setup-v1.3.0-win-arm64.exe` | 88.64 MB | `fbb81320500e700c14181a41b5430f839c6fef1e044b7cbc2d9215fd2cfda539` |
| `Remvora-v1.3.0-win-arm64.zip` | 88.53 MB | `a8b30c075d2eecf22a1426ef36658f80a33e9e4c2ab0586a0ee87e8fa7dacac4` |
| `Remvora-v1.3.0-win-arm64-delta.zip` | 1.57 MB | `7b25dcc0d7b369190d75cba05833157bd29b2918d68f32cf9d8bf91435dfc6ba` |
| `Remvora-Setup-v1.3.0-win-x86.exe` | 64.78 MB | `8ea1baafeb72426c6798b8628354f41780a1a5e883967138b39dfe57f76a9913` |
| `Remvora-v1.3.0-win-x86.zip` | 64.67 MB | `cceea155366c06ed2585b13070d3b1fbb479a53281c719b8443268e312d69153` |
| `Remvora-v1.3.0-win-x86-delta.zip` | 1.57 MB | `c5cde27eaf53ef4de25e62904a6c8e29f9c7fbc0fcf31b31621a3caec7a6069c` |
