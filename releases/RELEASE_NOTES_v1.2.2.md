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
| `Remvora-Setup-v1.2.2-win-x64.exe` | 91.40 MB | `b90fa22fb19651769683f49d37a80cfe4f1959ac8fccc2e46558143b68e70c0b` |
| `Remvora-v1.2.2-win-x64.zip` | 91.29 MB | `d333beff833fcafeacfcbb5b708231a5ab15e1099314fa08e5b09f972b7cc599` |
| `Remvora-v1.2.2-win-x64-delta.zip` | 1.58 MB | `6f51ae540d1e35af0156c1b08026504995bffd718fe70c0aee90cf3812961d9e` |
| `Remvora-Setup-v1.2.2-win-arm64.exe` | 88.63 MB | `357db0d0822161f935d8dcadf26a458197a06366634b03f5885ac7a31559ce7a` |
| `Remvora-v1.2.2-win-arm64.zip` | 88.52 MB | `047096dd4f1069adf96db04a617137cd5b8ed31df04e088ac8ea3b433b91c6ca` |
| `Remvora-v1.2.2-win-arm64-delta.zip` | 1.55 MB | `e0bf78c95eac194b853b8940b50514af1f050fcd3683874805127d92ae9d8fd3` |
| `Remvora-Setup-v1.2.2-win-x86.exe` | 64.77 MB | `ccc1a9a09c373300d29eda564f2dc9f9a76dbc3755d1c20b170b353de9d406f9` |
| `Remvora-v1.2.2-win-x86.zip` | 64.66 MB | `4cfe4b8dd61cb3b0016d4e0eba65095fe8437b5268ea2c81a2dd41b5ba65ae18` |
| `Remvora-v1.2.2-win-x86-delta.zip` | 1.55 MB | `95a0911e56517c7f3c4e244708b360655db6ff92706dc7ab1251eb261e86c3b6` |
