# Remvora AI Agent Rules & Engineering Standards

> **Identity & Purpose**: Remvora is a modern, high-performance, and safe Windows 11 application uninstaller, deep system cleaner, and privacy maintenance utility built with .NET 10 LTS and Windows App SDK (WinUI 3).  
> Every AI agent, pair-programmer, or developer working on this codebase **MUST strictly follow these rules** to ensure safety, maximum performance, architectural integrity, and premium Windows 11 Fluent UX.

---

## 1. Magnitude-Based Semantic Versioning (Mandatory)

* **Proportional Version Bumping**: Whenever making changes, **always update the version number based on the magnitude of the changes**, never based on the number of edits or commits:
  * **Major (`X.0.0`)**: Fundamental architectural shifts, breaking cross-platform changes, or total redesigns.
  * **Minor (`x.Y.0`)**: Significant new capabilities, new screens/pages, major engine overhauls (e.g., Batch Uninstaller, System Cleaner, Update Center, Standalone Installer).
  * **Patch (`x.y.Z`)**: Bug fixes, performance optimizations, UI polish, text corrections, and casing fixes.
* **Synchronized Version Checklist**: When updating the version, all of the following locations **must be updated simultaneously**:
  1. `Directory.Build.props`: `<Version>`, `<AssemblyVersion>`, `<FileVersion>`
  2. `src/Remvora.App/Package.appxmanifest`: `<Identity Version="X.Y.Z.0" />`
  3. `src/Remvora.App/app.manifest`: `<assemblyIdentity version="X.Y.Z.0" />`
  4. `src/Remvora.App/ViewModels/SettingsViewModel.cs`: `CurrentVersion` default and fallback
  5. `src/Remvora.Windows/Updates/GitHubUpdateService.cs`: Fallback version string
  6. `scripts/Launcher.cs`: Assembly attributes and `FallbackVersion`
  7. `scripts/install.ps1`: `$detectedVersion` default
  8. `scripts/package-releases.ps1`: `$Version` parameter default
  9. `README.md`: Distribution folder and script examples
  10. `releases/manifest.json`: Release manifest payload version

---

## 2. Safety & Protective Invariants (Core Mission)

* **Unelevated UI by Default**: The primary WinUI 3 process (`Remvora.App.exe`) **must always run unelevated (standard user privileges)**.
* **On-Demand Elevation Worker**: High-privilege tasks (such as uninstalling system-wide software or cleaning protected system directories) are isolated into `Remvora.ElevatedWorker.exe`, invoked exclusively through named pipes / RPC with explicit user authorization.
* **Strict Path Whitelisting**:
  * Deletion engines must validate targets against approved root boundaries (`%TEMP%`, `C:\Windows\Temp`, `SoftwareDistribution\Download`, designated browser cache folders, error dump paths).
  * **Never touch or delete**: `C:\Windows\System32`, `C:\Windows\SysWOW64`, `C:\Windows\WinSxS`, or root system drive anchors.
* **Non-Fatal Error Handling**: File locks and permission errors are normal in Windows. Deletion engines must catch `IOException`, `UnauthorizedAccessException`, and `SecurityException` per file, skip safely, log the skip, and report clean metrics rather than crashing or halting the cleanup queue.
* **Batch Uninstall Isolation**: During batch uninstallation, each process execution is isolated. Failure of one uninstaller must never abort subsequent queue items.

---

## 3. Performance & Optimization Guidelines

* **Zero UI Thread Blocking**:
  * **Never** perform disk I/O, registry scans, network calls, SHA-256 hash calculations, or child process executions on the UI dispatcher thread.
  * Always wrap long-running operations in `await Task.Run(...)` on background thread pool threads.
* **Granular Progress Throttling**:
  * When enumerating or deleting thousands of files (e.g. `%TEMP%`), do not dispatch UI notifications on every single file.
  * Batch updates (e.g., every 25–50 items or throttle to ~50ms) to ensure smooth 60fps WinUI 3 animations and zero dispatcher starvation.
* **Streaming I/O & Memory Efficiency**:
  * Use buffered streams (recommended buffer size: `81920` / 80 KB) for SHA-256 computation, zip extraction, and file transfers to keep memory consumption flat (< 100 MB).
* **In-Place UI Updates**:
  * After cleaning junk or privacy traces, **update the existing observable models in place** (e.g. set sizes to 0 B, mark items unselected) instead of triggering a full re-scan.
  * Never wipe the user's completion message or progress bar instantly.
* **Differential Updates First**:
  * The update engine must always favor downloading the lightweight ~1.5 MB differential delta archive (`*-delta.zip`) over the 95 MB full distribution whenever the installed version satisfies `MinDeltaVersion`.

---

## 4. Windows 11 Fluent UI & UX Standards

* **Native WinUI 3 Controls**: Use standard Windows 11 controls. **Never create fake controls** (e.g. styling standard buttons with checkbox icons `\uE73A` and `\uE739` next to real checkboxes).
* **Selection Toolbar Pattern**:
  * Single master `CheckBox` with label `"Select all"` and adjacent concise summary `(X of Y items • Size)`.
  * Do not add redundant `"Clear selection"` buttons; toggling the master `CheckBox` handles both select-all and deselect-all directly without empty space or duplicate controls.
* **High Contrast & Dark Mode**:
  * Use `HighContrastCardStrokeBrush` for visible card borders.
  * Avoid pure `#000000` or `#FFFFFF` contrast harshness; use tailored dark mode tones (e.g. `#181A22`, `#282C38`, `#38BDF8`).
* **Clear User Feedback**:
  * Update cards should clearly state **"Update Ready to Install"** and provide an explicit **"Install Update & Restart"** action button once downloaded.

---

## 5. Update Center & Serialization Architecture

* **Case-Insensitive JSON Deserialization**:
  * All JSON deserialization across the solution **must configure**:
    ```csharp
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    ```
  * Manifest DTO records (`UpdateManifest`, `UpdatePackageInfo`, `UpdateArchiveInfo`) must avoid the strict C# 11 `required` modifier and provide default values (`= string.Empty;`) to prevent crashes when reading external JSON.
* **Multi-Tier Manifest Discovery**:
  The update checker must gracefully check fallbacks in order:
  1. Remote raw GitHub manifest (`https://raw.githubusercontent.com/.../releases/manifest.json`)
  2. GitHub Releases API (`https://api.github.com/repos/.../releases/latest`)
  3. Local installed application release directory (`%LocalAppData%\Programs\Remvora\releases\manifest.json`)
  4. Local developer workspace repository (`D:\Github\Remvora\releases\manifest.json`)
* **Local Package Download Fallback**:
  * If the update download URL fails (e.g., repository is private, release is drafting, or offline), the update service must check for matching `.zip` archives located in the resolved local manifest directory before failing.

---

## 6. Standalone Single-File Setup & Deployment

* **Embedded Payload Architecture**:
  * `Remvora-Setup-vX.Y.Z-<arch>.exe` embeds the complete self-contained application runtime inside its `.NET` resource stream (`RemvoraPayload`).
  * Extracts cleanly to `%LocalAppData%\Programs\Remvora` with zero external dependencies.
* **Clean Portable ZIP Structure**:
  * Release ZIPs must maintain a clean root: `Remvora.exe` (launcher), `install.ps1`, `uninstall.ps1`, and all DLL clutter isolated inside an `app/` subfolder.
* **Shortcut & Registry Standards**:
  * Automatically creates Start Menu and Desktop shortcuts.
  * Registers cleanly with Windows Settings > Apps (`HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Remvora`).

---

## 7. Verification & Quality Gates

* **Zero Warning Tolerance**: `TreatWarningsAsErrors = true` is enforced in Release mode.
* **100% Test Pass Gate**: All unit tests in `tests/` across Core, Contracts, Application, Infrastructure, and Windows suites **must pass without failures or skips**:
  ```powershell
  dotnet test Remvora.slnx -c Release
  ```
* **Packaging Verification**:
  * Releases must be compiled and verified using the official packaging script:
    ```powershell
    powershell -ExecutionPolicy Bypass -File scripts\package-releases.ps1 -Version "<Version>"
    ```
  * `checksums-sha256.txt` and `manifest.json` must be regenerated and validated for every release.
