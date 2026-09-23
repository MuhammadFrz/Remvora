# Remvora v1.2.2 — Release Notes

## Overview
**Remvora v1.2.2** is a critical security and privacy hardening update. It eliminates local privilege escalation vectors by securing IPC communication between unelevated and elevated processes, hardens Windows command execution against argument injection, enforces mandatory cryptographic hash verification on all application updates, and reinforces system boundary and registry safeguards.

---

## What's New in v1.2.2

### 🛡️ Hardened IPC & Client Identity Verification
- **Kernel-Level PID Resolution**: `Remvora.ElevatedWorker` now calls `GetNamedPipeClientProcessId` via Windows `kernel32.dll` to obtain the kernel-verified process ID of connecting clients.
- **Parent Process Verification**: The worker validates the incoming client PID against the parent process (`Remvora.App.exe`) passed at launch (`--parent-pid`). Any unauthorized caller or rogue process attempting to connect to the named pipe is immediately disconnected with `403 Forbidden`.
- **Restricted Pipe DACL**: Configured `NamedPipeServerStreamAcl` to grant pipe access strictly to the active user's Windows SID and `BuiltinAdministratorsSid`, mitigating pipe squatting and unauthorized impersonation.

### 🔒 Elevated Command Injection & Service Protection
- **Critical Service Shield**: Blacklisted stopping or disabling critical Windows system services including Windows Defender (`WinDefend`), Windows Firewall (`mpssvc`), Event Log (`EventLog`), RPC (`RpcSs`), Cryptographic Services (`CryptSvc`), Windows Update (`wuauserv`), and `DcomLaunch`.
- **Protected Scheduled Tasks**: Blacklisted system task modifications under `\Microsoft\Windows\` and system-critical branches.
- **Parameter Regex Whitelisting**: Service names and task paths are strictly validated against `^[a-zA-Z0-9_\-\. ]{1,256}$` before execution via `net.exe`, `sc.exe`, or `schtasks.exe`, preventing command injection and shell escape attacks.

### 📦 Mandatory SHA-256 Verification & Release Asset Parsing
- **Zero-Tolerance Update Gate**: Eliminated bypass conditions in `GitHubUpdateService`; any package without a valid cryptographic hash or failing hash comparison aborts staging immediately.
- **Release Asset Fallback**: Automatically downloads and parses `checksums-sha256.txt` directly from GitHub release assets to guarantee verification even when raw manifests are unavailable.
- **Environment Gating**: Probing of local developer repositories is strictly restricted to `#if DEBUG` configurations, preventing local file probing in production builds.

### 🗂️ Protected Paths & Registry Safeguards
- **User Anchor Protection**: Added protections in `ProtectedPathsPolicy` preventing uninstaller or cleaner operations from deleting `%USERPROFILE%`, `Desktop`, `Documents`, `Downloads`, `AppData\Local\Microsoft`, and `ProgramData\Microsoft`.
- **Core System Registry Safeguards**: Hardened registry policies to protect root system subtrees including `HKCR`, `Classes`, `CurrentVersion\Policies`, `CurrentVersion\Explorer`, and `CurrentVersion\Component Based Servicing`.

---

## Download Assets

| Distribution | Architecture | Type | File Name |
| :--- | :--- | :--- | :--- |
| **Windows x64** (Standard PC) | `x64` | Standalone Installer | `Remvora-Setup-v1.2.2-win-x64.exe` |
| **Windows x64** (Portable) | `x64` | Clean Archive | `Remvora-v1.2.2-win-x64.zip` |
| **Windows ARM64** (Copilot+ / Surface) | `arm64` | Standalone Installer | `Remvora-Setup-v1.2.2-win-arm64.exe` |
| **Windows ARM64** (Portable) | `arm64` | Clean Archive | `Remvora-v1.2.2-win-arm64.zip` |
| **Windows x86** (32-bit Legacy) | `x86` | Standalone Installer | `Remvora-Setup-v1.2.2-win-x86.exe` |
| **Windows x86** (Portable) | `x86` | Clean Archive | `Remvora-v1.2.2-win-x86.zip` |
| **Delta Update Package** | `win-x64` | Incremental | `Remvora-v1.2.2-win-x64-delta.zip` |

---

## Verification & Integrity
Verify the authenticity of your downloaded files using SHA-256:

```powershell
Get-FileHash -Algorithm SHA256 <FileName>
```

| File Name | Size | SHA-256 Checksum |
| :--- | :--- | :--- |
| `Remvora-Setup-v1.2.2-win-x64.exe` | 91.41 MB | `14d3069b900a798b329b4a9889f5b2729fbdce5e2e71b5fe71c2b216240772c4` |
| `Remvora-v1.2.2-win-x64.zip` | 91.30 MB | `2bff2d713f9163bc3e1e816ebe9cce44741e1d4a545f9b721bfd4992c5a29010` |
| `Remvora-v1.2.2-win-x64-delta.zip` | 1.59 MB | `17edf9543c1d9028bc025ba6a9bf7df142b8f96dbb928aca87043eceb2dcf115` |
| `Remvora-Setup-v1.2.2-win-arm64.exe` | 88.64 MB | `bc7038bbdd08f8118d58019b8ce1dbbfbfcf0b21bab634210a7414f6c0fe38e1` |
| `Remvora-v1.2.2-win-arm64.zip` | 88.53 MB | `454f6f9067ee0509b7904006ddd3dffa8560b60a0016fdb95e4d930f7954b36e` |
| `Remvora-v1.2.2-win-arm64-delta.zip` | 1.57 MB | `14ec3d46b7986de9c2ebcce6249929be2021884d79cd13697e0e4dee8bcc73fb` |
| `Remvora-Setup-v1.2.2-win-x86.exe` | 64.78 MB | `246d9c8671a596eee382424aa3562b59d6443e1b45cf3cdaec36c85b673928e5` |
| `Remvora-v1.2.2-win-x86.zip` | 64.67 MB | `482529295d420a51bb87f5e80f0a1c963f59861092b36ae204934dd289a9bd1a` |
| `Remvora-v1.2.2-win-x86-delta.zip` | 1.56 MB | `b159bc39bcae5a64037df6ace6bb65689e5c4fd2aa8b6c0c00d503c2c809f232` |
