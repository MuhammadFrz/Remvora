# Remvora v1.2.1 — Release Notes

## Overview
**Remvora v1.2.1** is a high-priority stability and UX polish update. It delivers reliable update downloading with local fallback mechanisms, introduces the explicit **"Install Update & Restart"** action flow, hardens manifest JSON parsing across different casing styles, and establishes the foundational **Remvora AI Agent Rules & Engineering Standards (`AGENTS.md`)**.

---

## What's New in v1.2.1

### 🔄 Resilient Update Downloading & Verification
- **Local Package Fallback**: When offline, testing, or updating before a GitHub release goes public, Remvora automatically discovers matching delta `.zip` archives in local candidate directories.
- **Strict Cryptographic Integrity**: Full SHA-256 verification ensures every update archive is bit-for-bit authentic before staging.
- **Robust Path & Casing Handling**: Fixed JSON parsing and path normalization to support forward-slashed and escaped Windows paths seamlessly.

### ⚡ Seamless "Install Update & Restart" Experience
- **Clear Actionable Feedback**: Once an update finishes downloading, a prominent **"Update Ready to Install"** status card appears with a dedicated **"Install Update & Restart"** button (`\uE777`).
- **Atomic File Swapping**: Remvora terminates safely to release file locks, while the lightweight native launcher swaps updated files into place and relaunches the application in under 300ms.

### 🛡️ AI Agent Engineering Standards (`AGENTS.md`)
- **Magnitude-Based Versioning**: Formalized proportional semantic version bumping (Major / Minor / Patch) with a synchronized 10-point checklist.
- **Protective Safety Invariants**: Mandated unelevated UI execution, isolated elevation workers for high-privilege operations, and strict path whitelisting forbidding any touches to `System32` or core system anchors.
- **Performance & 60fps Optimization**: Mandated off-thread I/O (`Task.Run`), progress notification batching/throttling (~50ms) to prevent dispatcher starvation, and 80 KB streaming buffers for flat memory usage (< 100 MB).

---

## Download Assets

| Distribution | Architecture | Type | File Name |
| :--- | :--- | :--- | :--- |
| **Windows x64** (Standard PC) | `x64` | Standalone Installer | `Remvora-Setup-v1.2.1-win-x64.exe` |
| **Windows x64** (Portable) | `x64` | Clean Archive | `Remvora-v1.2.1-win-x64.zip` |
| **Windows ARM64** (Copilot+ / Surface) | `arm64` | Standalone Installer | `Remvora-Setup-v1.2.1-win-arm64.exe` |
| **Windows ARM64** (Portable) | `arm64` | Clean Archive | `Remvora-v1.2.1-win-arm64.zip` |
| **Windows x86** (32-bit Legacy) | `x86` | Standalone Installer | `Remvora-Setup-v1.2.1-win-x86.exe` |
| **Windows x86** (Portable) | `x86` | Clean Archive | `Remvora-v1.2.1-win-x86.zip` |
| **Delta Update Package** | `win-x64` | Incremental | `Remvora-v1.2.1-win-x64-delta.zip` |

---

## Verification & Integrity
Verify the authenticity of your downloaded files using SHA-256:

```powershell
Get-FileHash -Algorithm SHA256 <FileName>
```

| File Name | Size | SHA-256 Checksum |
| :--- | :--- | :--- |
| `Remvora-Setup-v1.2.1-win-x64.exe` | 91.40 MB | `098b2e67ec4ecae8a554f150c112434a5dc775364658c0291b23109bd01bf4da` |
| `Remvora-v1.2.1-win-x64.zip` | 91.29 MB | `d6d4fa0bc0f05c2f354dbe6be4b950257a98056f6692e734c1f22f17a47d41c5` |
| `Remvora-v1.2.1-win-x64-delta.zip` | 1.57 MB | `59aebf89630508830c1be2d5fff922282c588e13b7614df933eb3837b201a9b4` |
| `Remvora-Setup-v1.2.1-win-arm64.exe` | 88.62 MB | `88b9cfb0a06c7fb6f7e42f6f1439ac2412533aa326e5fb42708102509dcc779f` |
| `Remvora-v1.2.1-win-arm64.zip` | 88.51 MB | `26a877e01dcf079760a2ee76235275cb8817eccfe8f20f9cd8c5e4a3e2939302` |
| `Remvora-v1.2.1-win-arm64-delta.zip` | 1.55 MB | `fa0cf9d35404f250f0c1a7ab4c306931b44114dd79e966dc3a37058bb923cb9f` |
| `Remvora-Setup-v1.2.1-win-x86.exe` | 64.76 MB | `1cb43a683d3fe298e4f95e09f56e8e0b8656140a3364e3db55d1b9231b54cffe` |
| `Remvora-v1.2.1-win-x86.zip` | 64.65 MB | `76748053689585a69d0108528ff0dfc9c170cd3898376ffb0cabfb7702ae87f7` |
| `Remvora-v1.2.1-win-x86-delta.zip` | 1.55 MB | `5491854662cd0bf0bd59432f40fa2947530adbdc9e4d9240d47af1eac609f83c` |
