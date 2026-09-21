# Remvora v1.2.0 — Release Notes

## Overview
**Remvora v1.2.0** brings significant usability enhancements, a streamlined Windows 11 selection toolbar, dynamic assembly-driven version reporting across all UI and update subsystems, single-file turnkey installers, and complete multi-architecture distribution packages.

---

## What's New in v1.2.0

### 🎨 Refined Selection Toolbar & Fluent UX
- **Eliminated Duplicate Selection Glitch**: Removed confusing buttons styled as duplicate checkboxes.
- **Single Master Toggle**: A clean, native Windows 11 tri-state `Select all` checkbox with comprehensive summary `({selected} of {total} categories • {size})`.
- **Subtle Clear Action**: Replaced clunky buttons with a minimalist `Clear selection` action button on the right.
- **Unified Privacy & Junk Selection**: Consistent, intuitive selection UX across both Junk Cleaner and Privacy Cleaner tabs.

### 🔢 Dynamic & Magnitude-Based Versioning System
- **Proportional Version Bumping**: Adopted semantic versioning incremented based on the magnitude of changes (major architecture shifts = major, feature additions = minor, UI polish/fixes = patch).
- **Dynamic Assembly Versioning**: The About card in Settings and update service headers dynamically read compiled assembly informational version metadata, eliminating hardcoded string drift.
- **Installer & Launcher Parity**: Standalone Setup wizards and launchers dynamically inspect payload binaries and report matching version numbers in Windows Installed Apps registry entries.

### 🚀 Standalone Single-File Installers (`Setup.exe`)
- **Single Turnkey Binary**: Download `Remvora-Setup-v1.2.0-<arch>.exe` and run immediately without needing to unzip.
- **Embedded Self-Extracting Payload**: Complete self-contained .NET 10 LTS and Windows App SDK runtime embedded inside.
- **Desktop & Start Menu Shortcuts**: Seamless integration with Windows 11 Shell and Windows Settings > Apps.

### 🧹 Real-Time Asynchronous System Cleaner
- **Live Granular Progress**: Real-time progress bar, percentage indicator, and active file paths during scan and clean phases.
- **Safe Asynchronous Deletion**: Background thread pool execution (`Task.Run`) prevents UI freeze during large file purges.

### ⚡ Differential Delta Updates
- **Compact Incremental Packages**: ~1.5 MB delta archives download only modified binaries, slashing bandwidth and update time.

---

## Download Assets

| Distribution | Architecture | Type | File Name |
| :--- | :--- | :--- | :--- |
| **Windows x64** (Standard PC) | `x64` | Standalone Installer | `Remvora-Setup-v1.2.0-win-x64.exe` |
| **Windows x64** (Portable) | `x64` | Clean Archive | `Remvora-v1.2.0-win-x64.zip` |
| **Windows ARM64** (Copilot+ / Surface) | `arm64` | Standalone Installer | `Remvora-Setup-v1.2.0-win-arm64.exe` |
| **Windows ARM64** (Portable) | `arm64` | Clean Archive | `Remvora-v1.2.0-win-arm64.zip` |
| **Windows x86** (32-bit Legacy) | `x86` | Standalone Installer | `Remvora-Setup-v1.2.0-win-x86.exe` |
| **Windows x86** (Portable) | `x86` | Clean Archive | `Remvora-v1.2.0-win-x86.zip` |
| **Delta Update Package** | `win-x64` | Incremental | `Remvora-v1.2.0-win-x64-delta.zip` |

---

## Verification & Integrity
Verify the authenticity of your downloaded files using SHA-256:

```powershell
Get-FileHash -Algorithm SHA256 <FileName>
```

| File Name | Size | SHA-256 Checksum |
| :--- | :--- | :--- |
| `Remvora-Setup-v1.2.0-win-x64.exe` | 91.40 MB | `66e7094a5c08f4fa0d109167dfd54802cee2340a7ee96f4ed8771c0165f902c8` |
| `Remvora-v1.2.0-win-x64.zip` | 91.29 MB | `73c1e062bcaf454f90ea28855268091038d16f1c165a8c958ae8fea6d6f4217b` |
| `Remvora-v1.2.0-win-x64-delta.zip` | 1.57 MB | `2de1d92544dec03ec89f671f5a79b018c25401de6b00674e864031f36c58b0f2` |
| `Remvora-Setup-v1.2.0-win-arm64.exe` | 88.62 MB | `1302eed2f89ef097aec5811ae7cb37f8e8c08ead4c81b92915dab8ccc9d7f2fb` |
| `Remvora-v1.2.0-win-arm64.zip` | 88.51 MB | `a6594d756dcfe7044b3fa8e10f06167497f0ced48e3eff7e6c13d8e4f4b7df95` |
| `Remvora-v1.2.0-win-arm64-delta.zip` | 1.55 MB | `449d49f10789b38a26fee0bd294ae6509ca84563048c943c9c683118e4de105b` |
| `Remvora-Setup-v1.2.0-win-x86.exe` | 64.76 MB | `cc1eef5b6c4f5a87d9421e7aa9230b1886333c09f5abad12f01f8fc839e045d4` |
| `Remvora-v1.2.0-win-x86.zip` | 64.65 MB | `21380246ac258b30a4cc345ae6599fae589bbd3b7be716fb67d121517c16e4b7` |
| `Remvora-v1.2.0-win-x86-delta.zip` | 1.55 MB | `33dc9dd18f84987ceef8dc2076afcdeb92eca10601d76674454d60da9ada9552` |

