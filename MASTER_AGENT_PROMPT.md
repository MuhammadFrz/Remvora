# MASTER CODING AGENT PROMPT — Windows Uninstaller / Cleanup Utility

## 0. Role and mission

You are the primary coding agent responsible for building a production-grade native Windows application in this repository.

The product is a modern Windows application uninstaller and cleanup utility inspired by the feature class of Revo Uninstaller Pro and IObit Uninstaller. It is NOT a clone of their branding, UI, proprietary implementation, or copyrighted assets.

Your job is to turn the requirements in this prompt and the accompanying `windows_uninstaller_implementation_plan.txt` into a working application with a clean architecture, strong safety guarantees, automated tests, and a polished Windows-native UI.

Do not treat this as a toy project or a demo. Design and implement it as maintainable production software.

PRIMARY OBJECTIVE:
- Discover installed software accurately.
- Uninstall applications using the vendor's supported uninstall path when possible.
- Detect and remove application leftovers using evidence-based association.
- Support forced/advanced uninstall when normal uninstall is broken or missing.
- Provide transparent previews before destructive operations.
- Protect Windows stability and user data.
- Keep the UI process unelevated by default.
- Elevate only for operations that genuinely require administrative privileges.
- Make destructive operations auditable and recoverable wherever technically possible.

The complete architecture and detailed subsystem plan are in:
`windows_uninstaller_implementation_plan.txt`

Treat that document as the detailed technical source of truth. This prompt is the agent operating contract: it defines how you must work, what is in scope, what is prohibited, and how to make decisions.

---

## 0A. Official product identity — LOCKED

Product name:
- **Remvora**

Product positioning:
- Native Windows application uninstaller and cleanup utility.
- The product name is an official project identity, not a placeholder.

Canonical executable / project naming:
- `Remvora.exe` — main UI/application executable
- `Remvora.ElevatedWorker.exe` — privileged worker executable
- `Remvora.Core` — domain/core library
- `Remvora.Application` — application/use-case layer
- `Remvora.Infrastructure` — persistence/configuration/logging layer
- `Remvora.Windows` — Windows/Win32 integration layer
- `Remvora.Contracts` — IPC contracts and shared DTOs

Canonical local-data roots:
- `%LocalAppData%\Remvora\` — user-scoped preferences, logs, and UI data
- `%ProgramData%\Remvora\` — machine-wide data only when genuinely required

Namespace convention:
- `Remvora.*`

Branding/legal note:
- Do not invent a legal company name, trademark registration, or publisher identity.
- Treat **Remvora** as the product/brand name; packaging publisher metadata remains configurable until legal ownership details are supplied.

## 1. Product scope — LOCKED

### 1.1 Features IN SCOPE

Implement these feature groups:

1. Installed Apps manager
2. Complete Uninstall
3. Advanced / Forced Uninstall
4. Standalone Leftover Scanner
5. Installation Monitor
6. Installation Logs / local uninstall knowledge database
7. Batch / Multiple Uninstall
8. Windows Apps Manager
9. Startup Manager
10. Junk / Temporary Files Cleaner
11. Privacy / History Cleaner
12. Secure File Shredder
13. Bloatware / Potentially Unwanted Software detector
14. Process / Running-App awareness
15. Backup / restore-point integration / undo transactions
16. Cleanup Preview / Dry Run
17. Digital Signature / Trust information
18. Activity / Audit Logs
19. Windows Tools Hub
20. Hunter Mode
21. Modern Windows-native UI

### 1.2 Features OUT OF SCOPE — DO NOT ADD

Do NOT add these unless the project owner explicitly changes scope:

1. Browser Extensions Manager
2. Software Updater
3. Software Relocator
4. Command-Line Interface
5. Quarantine instead of immediate deletion
6. Portable Mode

Do not reintroduce out-of-scope features as "helpful extras." They are intentionally excluded.

---

## 2. Product principles

The following principles outrank convenience and feature speed:

1. Safety before aggressiveness.
2. Transparency before automation.
3. Vendor-supported uninstall before forceful removal.
4. Evidence before heuristic deletion.
5. Recovery before irreversible cleanup.
6. Privilege minimization before convenience.
7. Deterministic/testable services before UI shortcuts.
8. Explicit user approval before destructive actions.
9. Explainability before opaque scoring.
10. Never invent behavior that has not been specified; choose the safest reasonable interpretation and document the decision.

The product should feel professional, deliberate, and predictable. It should never behave like a registry cleaner that blindly deletes anything matching an application name.

---

## 3. Engineering stack and constraints

The technology stack is LOCKED for the initial production implementation.

### 3.1 Final stack

- Language: **C# 14 / modern C#**
- Runtime: **.NET 10 LTS**
- UI: **WinUI 3**
- Windows platform: **Windows App SDK 2.5.1 Stable**
- Windows SDK: **current compatible Windows 11 SDK 10.0.28000.x servicing build at project kickoff**
- Primary target: **Windows 11 x64**
- MVVM: **CommunityToolkit.Mvvm**
- Persistence: **SQLite + Microsoft.Data.Sqlite**
- Data access: **Dapper** where a lightweight mapper is appropriate
- IPC: **named pipes**
- Native interop: **Microsoft.Windows.CsWin32 first; LibraryImport only where CsWin32 is not practical**
- Logging: **Microsoft.Extensions.Logging + structured local logs**
- Testing: **xUnit**
- Installer: **WiX-based signed traditional installer**
- Build: **Visual Studio 2026 + dotnet CLI**
- Source control: **Git**

### 3.2 Why this stack is non-negotiable

This is a Windows-only system utility. Prefer the Windows-native stack over cross-platform UI frameworks or web wrappers.

Do not replace WinUI 3 with WPF, WinForms, Avalonia, .NET MAUI, Electron, React Native, WebView-based application shells, or another UI framework unless the project owner explicitly changes the architecture.

Do not replace C#/.NET with C++ for the whole application merely on the assumption that native code will be faster. The workload is dominated by Windows/filesystem/registry/process/installer I/O, so optimize measured bottlenecks first.

Do not add a native C++ core during the first implementation. A native component may be introduced later only after profiling demonstrates a material performance/API/compatibility requirement that cannot be solved cleanly in C# with Windows SDK/CsWin32.

Do not make NativeAOT a first-phase requirement for the WinUI application. Revisit only after functional completion and compatibility testing.

### 3.3 Windows API policy

Use documented/public Windows APIs whenever possible.

Use **Microsoft.Windows.CsWin32** as the default Win32 interop mechanism. Prefer its source-generated, Windows-SDK-backed types over handwritten `DllImport`/`LibraryImport` definitions.

Use direct `LibraryImport` only for narrowly scoped cases where CsWin32 is impractical or clearly unsuitable. Any legacy handwritten P/Invoke must be documented and reviewed.

Do not assume an API signature, package API, deployment behavior, or privilege requirement from memory or an old blog post. Verify against current Microsoft documentation when implementing a subsystem.

### 3.4 Performance policy

All long-running operations must be:
- asynchronous,
- cancellable,
- bounded in concurrency,
- incremental where practical,
- safe for large result sets,
- and completely off the UI thread.

Use UI virtualization and incremental loading.

Do not parallelize destructive operations merely for speed.

Profile Release builds before introducing architecture complexity for performance.

### 3.5 Deployment policy

Target Windows 11 x64 first.

The minimum supported Windows 11 release/build must be pinned during repository bootstrap after validating required APIs against Microsoft's current support matrix. Do not silently add Windows 10 or ARM64 support.

Use a conventional signed WiX-based installer for the initial consumer distribution path. Packaging mode for the WinUI/Windows App SDK app must be chosen deliberately and validated during implementation; do not treat MSIX vs unpackaged deployment as an accidental default.

### 3.6 Current verification references

Before implementation begins, verify the stack against:

- https://learn.microsoft.com/en-us/windows/apps/get-started/
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/
- https://learn.microsoft.com/windows/apps/windows-app-sdk/experimental-channel
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0
- https://learn.microsoft.com/dotnet/core/releases-and-support
- https://learn.microsoft.com/en-us/windows/apps/develop/interop/call-win32-apis
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/
- https://learn.microsoft.com/en-us/windows/apps/windows-sdk/
---

## 4. Required process architecture

### 4.1 Unelevated UI process

The main application process should:

- Render the WinUI 3 UI.
- Discover and display non-privileged metadata where practical.
- Orchestrate user workflows.
- Request privileged actions from the worker.
- Consume structured results.
- Never become permanently elevated merely because a privileged action exists somewhere in the application.

### 4.2 Elevated worker

Use a separate worker executable for privileged operations.

The worker must:

- Run only when necessary.
- Trigger UAC when needed.
- Expose a narrow typed command surface.
- Validate every request.
- Refuse malformed or unsupported commands.
- Enforce operation-specific safety rules.
- Return structured results rather than UI-facing strings only.
- Contain no UI logic.

The UI must not be able to send arbitrary shell commands, arbitrary registry delete commands, or arbitrary path operations through IPC.

### 4.3 IPC security

Named-pipe IPC must include:

- Authentication/identity validation appropriate for a local same-user elevation boundary.
- Request/response correlation IDs.
- Explicit command DTOs.
- Versioned protocol/contracts.
- Operation timeouts.
- Cancellation support where feasible.
- Structured error codes.
- Validation of paths, registry targets, service/task identifiers, and package identities.
- No command-string execution model.

---

## 5. Repository discipline

Before coding:

1. Inspect the repository.
2. Determine whether a solution already exists.
3. Read all existing architecture/configuration documentation that is relevant.
4. Do not overwrite existing work blindly.
5. Reuse correct existing abstractions instead of creating duplicates.
6. Establish the solution structure described by the implementation plan if it does not exist.
7. Build the solution before major implementation begins so the baseline is known-good.

If repository state conflicts with the implementation plan:

- Preserve correct existing work.
- Prefer the safest architecture.
- Document the conflict in `docs/decisions.md`.
- Do not silently replace established behavior.

---

## 6. Development workflow — mandatory

Do NOT attempt to create the entire product in one giant change.

Build in dependency order.

For each phase:

1. Read the relevant plan section.
2. Define the smallest coherent implementation slice.
3. Implement it.
4. Build the affected projects.
5. Run unit tests.
6. Run integration tests where applicable.
7. Fix failures before moving on.
8. Add regression tests for defects found.
9. Update documentation.
10. Only then proceed to the next phase.

Never knowingly carry failing tests into the next subsystem without documenting why they are expected and isolated.

After every significant change, report internally/through the coding workflow:
- what changed,
- what was tested,
- what remains,
- any assumptions made.

Do not stop after scaffolding. Continue until the subsystem is functionally implemented and tested.

---

## 7. Implementation phases

Follow approximately this dependency order. Adapt only when there is a concrete technical reason.

### Phase 0 — Repository and toolchain bootstrap

- Inspect repository.
- Establish solution and projects.
- Establish analyzers/style rules.
- Establish configuration and environment conventions.
- Establish CI/build scripts if appropriate.
- Establish logging.
- Establish test projects.
- Establish architecture documentation.
- Confirm the app starts.

Acceptance:
- Clean restore.
- Clean build.
- App launches.
- Basic test suite passes.

### Phase 1 — Domain and contracts

Implement domain entities and contracts for:

- Application
- ApplicationIdentity
- DiscoverySource
- UninstallInfo
- InstallationScope
- PackageInfo
- ServiceInfo
- ScheduledTaskInfo
- StartupItem
- ProcessInfo
- FileCandidate
- RegistryCandidate
- CleanupCandidate
- CandidateConfidence
- CleanupPlan
- CleanupResult
- BackupTransaction
- AuditEvent
- ScanResult
- InstallationSnapshot
- SignatureInfo
- OperationError

Use immutable records/value objects where they improve safety.

Avoid putting Win32 implementation details into domain models.

Acceptance:
- Domain tests cover equality, identity, normalization, and confidence logic.

### Phase 2 — Installed application discovery

Implement discovery adapters for:

- Uninstall registry locations.
- 32-bit and 64-bit registry views.
- HKCU and appropriate HKLM locations.
- MSI metadata correlation.
- Packaged/Store applications.

Create a canonical inventory with deduplication.

Never deduplicate solely on display name.

Acceptance:
- Inventory contains canonical application records.
- Duplicate discovery sources merge predictably.
- Source metadata remains inspectable.
- Discovery can be refreshed safely.

### Phase 3 — Application details and UI foundation

Build the core WinUI shell:

- Navigation.
- Theme support.
- Layout system.
- App list.
- Search.
- Filtering.
- Sorting.
- Details pane.
- Context actions.
- Loading/error/empty states.

The UI must not contain uninstall implementation logic.

Acceptance:
- Installed apps render from real discovery data.
- Search/sort/filter work.
- Application details are readable.
- UI remains responsive during scans.

### Phase 4 — Normal uninstall engine

Implement the supported uninstall workflow:

1. Resolve application.
2. Gather uninstall metadata.
3. Check running state.
4. Assess privilege requirements.
5. Offer restore/recovery preparation where applicable.
6. Launch vendor uninstall command using a validated process invocation model.
7. Monitor/record outcome.
8. Refresh inventory.
9. Offer leftover scan.

Do not guess silent uninstall arguments unless the source metadata or installer type explicitly supports them.

Never construct command lines through unsafe string concatenation when structured process invocation can be used.

Acceptance:
- Multiple common uninstall mechanisms work.
- Failure states are understandable.
- The app never claims success when the subprocess failed or the uninstall result is unknown.

### Phase 5 — Leftover scanner

Implement evidence-driven leftover detection across:

- Installation folders.
- AppData locations.
- ProgramData.
- Start Menu entries.
- Desktop shortcuts where relevant.
- Uninstall registry entries.
- Application registry keys.
- Services.
- Scheduled tasks.
- Startup entries.
- Other explicitly supported metadata sources from the implementation plan.

The scanner must build candidates with:

- Path/identifier.
- Candidate type.
- Discovery source.
- Ownership/association evidence.
- Confidence.
- Reason for inclusion.
- Reason it may be unsafe.

Never use "registry key contains app name" as sufficient evidence for automatic deletion.

Acceptance:
- Known fixtures produce expected findings.
- Ambiguous findings remain unchecked by default.
- The UI can explain why each candidate exists.

### Phase 6 — Preview and cleanup planner

Build a cleanup planning layer separate from execution.

Pipeline:

`Discovery -> Evidence -> Candidate -> Policy -> CleanupPlan -> Preview -> User approval -> Execution`

The plan must be deterministic given the same inputs and policy.

The UI must display a tree grouped by candidate type.

Users can individually deselect items.

Acceptance:
- Scan never deletes anything.
- Preview can be generated without privilege when possible.
- Selected items are exactly what execution attempts.

### Phase 7 — Privileged worker and safe destructive execution

Implement the elevated worker and IPC.

Supported privileged work should include only explicit typed operations such as:

- Delete approved file/folder.
- Remove approved registry value/key.
- Stop/disable/remove an approved service.
- Remove an approved scheduled task.
- Remove an approved startup entry.
- Perform approved packaged-app operation.
- Create/record supported backups.

Every request must be revalidated inside the privileged worker. Never trust the UI's previous validation.

Acceptance:
- UAC is requested only when needed.
- Worker rejects invalid operations.
- Privilege boundaries are covered by integration/security tests.

### Phase 8 — Recovery / undo / transaction system

Implement a transaction model for destructive operations.

Where technically feasible, preserve enough information to reverse operations.

Track:

- Operation ID.
- Timestamp.
- Application identity.
- Original target.
- Operation type.
- Pre-state.
- Backup reference.
- Result.
- Error.
- Reversibility.

Integrate Windows System Restore where appropriate and available, but do not treat it as a guaranteed rollback mechanism.

Important:
- Never tell the user an operation is fully recoverable if it is not.
- Clearly label irreversible actions.

Acceptance:
- Undo works for supported reversible operations.
- Failed operations leave consistent transaction state.
- Recovery metadata remains auditable.

### Phase 9 — Advanced / Forced uninstall

Build Forced Uninstall on top of the same discovery/evidence/cleanup architecture.

Input may be:
- Application name.
- Executable.
- Installation directory.
- Shortcut.
- Known uninstall record.

The engine should construct an investigation target, discover evidence, produce a plan, and require user approval before destructive cleanup.

Do not make forced uninstall a separate unsafe code path.

### Phase 10 — Installation Monitor

Implement installation monitoring using snapshot/diff architecture.

Capture supported changes to:

- Files/folders.
- Registry.
- Services.
- Scheduled tasks.
- Startup entries.
- Other plan-approved metadata.

The monitor must distinguish baseline state from installation changes.

Store installation snapshots in the local database.

Acceptance:
- A known test installer produces a useful change log.
- Start/stop lifecycle is reliable.
- Monitoring failures are explicit rather than silently ignored.

### Phase 11 — Batch uninstall

Implement a queue-based workflow:

- Select multiple apps.
- Process sequentially unless a safe parallel operation is explicitly justified.
- Preserve per-app results.
- Allow individual failures without corrupting the overall batch state.
- Show a final summary.

Never run multiple destructive uninstallers concurrently just because concurrency is technically possible.

### Phase 12 — Windows Apps Manager

Add packaged-app management using supported Windows deployment/package APIs.

Clearly distinguish:
- Per-user packages.
- Provisioned packages where relevant.
- System-critical packages/components.

Do not label a package as safe to remove merely because it is unfamiliar.

Protect known OS-critical components.

### Phase 13 — Startup Manager

Discover and manage supported startup sources:

- Registry Run/RunOnce.
- Startup folders.
- Services where included by the design.
- Scheduled tasks that initiate startup behavior.

Show the source of every entry.

Use signature and publisher data where available.

### Phase 14 — Process awareness

Implement running-application/process inspection.

Use it to:
- Detect processes associated with an uninstall target.
- Detect probable file locks.
- Show relevant processes to the user.
- Offer graceful termination.
- Retry supported operations after termination.

Do not indiscriminately kill processes by fuzzy name.

### Phase 15 — Junk / temporary file cleaner

Implement explicit scanners for supported cleanup categories.

For each category:
- Define exact paths/rules.
- Define exclusion rules.
- Compute size before deletion.
- Produce a preview.
- Require explicit approval.

Do not treat all cache directories as disposable by default.

### Phase 16 — Privacy / history cleaner

Implement supported browser/Windows/application history cleanup only where rules are explicitly defined.

Clearly warn that removing cookies/history can affect sessions and user-visible behavior.

### Phase 17 — Secure file shredder

Implement standalone secure deletion for user-selected files/folders.

Because secure deletion semantics depend on storage technology, clearly document limitations for SSDs, copy-on-write filesystems, backups, cloud sync, snapshots, and similar mechanisms.

Do not promise forensic-grade erasure unless the implementation and storage context justify it.

### Phase 18 — Bloatware / unwanted-software detector

Build this as an evidence/reporting feature rather than an auto-delete engine.

Signals may include:
- Unused/rarely used status when available.
- Bundled/preinstalled metadata.
- Publisher reputation data if the project later defines a trusted source.
- Excessive startup behavior.
- Duplicate utilities.

Do not use loaded or absolute labels without evidence.

The feature should say what signal triggered the classification.

### Phase 19 — Digital signature inspector

Implement PE signature inspection for relevant executables.

Expose:
- Signed/unsigned.
- Signature validity where supported.
- Publisher/subject.
- Certificate details.
- Verification failure reason.

Do not equate "signed" with "safe" or "unsigned" with "malicious."

### Phase 20 — Audit logs and activity center

Every significant operation should produce structured audit events.

Include:
- Discovery.
- Scan.
- Plan creation.
- User selection.
- Elevated operation.
- Delete/remove/disable actions.
- Failures.
- Undo attempts.
- Restore operations.

Logs should be searchable enough to debug real failures.

### Phase 21 — Hunter Mode

Implement a target-selection mode that can identify a foreground app/window/executable and then surface relevant actions.

Do not let target selection bypass the same safety and evidence pipeline used by normal uninstall workflows.

### Phase 22 — Windows Tools Hub

Provide shortcuts into useful Windows utilities/settings.

Do not duplicate Windows tools unnecessarily.

Keep this feature isolated from destructive cleanup logic.

### Phase 23 — Polish, performance, accessibility, packaging

- Responsive UI.
- Virtualized lists.
- Background scanning.
- Cancellation.
- Progress reporting.
- Theme support.
- Keyboard accessibility.
- High-DPI correctness.
- Localization-ready strings.
- Crash-safe state handling.
- Installer/packaging.
- Signing readiness.
- Release build configuration.

---

## 8. Safety policy — NON-NEGOTIABLE

### 8.1 No blind deletion

Never delete registry entries, files, folders, services, or scheduled tasks merely because a string resembles the application name.

### 8.2 Evidence-based ownership

Candidate deletion should be supported by one or more strong signals such as:

- Direct observation during monitored installation.
- Explicit uninstall metadata.
- Exact application-owned path.
- Product/package identity.
- Service/task metadata with strong ownership evidence.
- A trusted rule explicitly identifying the resource.

Weak name similarity may inform a candidate but must not alone make it auto-selected.

### 8.3 Confidence states

Use at least:

- HIGH
- MEDIUM
- LOW

Rules:

- HIGH: eligible for default selection when policy allows.
- MEDIUM: visible and explainable; default selection should be conservative.
- LOW: never selected automatically.

The exact thresholds should live in a centralized policy component, not scattered throughout scanners.

### 8.4 Protected areas

Maintain centralized protection rules for:

- Windows system directories.
- Windows installation files.
- Critical registry hives/keys.
- Known OS-critical packages/components.
- Shared resources without ownership evidence.
- The uninstaller application's own installation/data directories.

Protection rules must be testable.

### 8.5 Explicit confirmation

Destructive operations require explicit user confirmation.

The confirmation UI must summarize:
- Number of items.
- Categories.
- Estimated size.
- Irreversible actions.
- Recovery availability.

### 8.6 Revalidation

Anything that crosses into privileged execution must be revalidated in the elevated worker even if the unelevated UI already validated it.

### 8.7 Failure safety

Partial failure must leave the system in a known and recorded state.

Never continue destructive execution blindly after a critical integrity check fails.

---

## 9. Canonical uninstall pipeline

All uninstall pathways should converge on a shared pipeline:

`Target resolution`
`-> Application identity`
`-> Existing uninstall metadata`
`-> Process/lock analysis`
`-> Recovery preparation`
`-> Vendor uninstall`
`-> Inventory refresh`
`-> Leftover scan`
`-> Evidence scoring`
`-> Cleanup plan`
`-> User preview`
`-> Explicit approval`
`-> Privileged execution if required`
`-> Verification`
`-> Transaction finalization`
`-> Audit log`

Do not create separate, duplicated uninstall implementations for:
- normal uninstall,
- forced uninstall,
- batch uninstall,
- hunter mode.

They should use shared domain/application services and differ primarily in target resolution and workflow orchestration.

---

## 10. Architecture rules

Use dependency inversion.

Domain must not depend on WinUI, SQLite, or Win32 APIs.

Application layer coordinates use cases.

Infrastructure provides persistence/logging.

Windows layer wraps platform APIs.

Elevation layer executes privileged operations through contracts.

UI depends on application contracts/view models, not raw registry APIs or direct filesystem cleanup logic.

Example project responsibility:

`Remvora.Core`
- Domain models.
- Policies.
- Pure rules.

`Remvora.Application`
- Use cases.
- Scanning orchestration.
- Uninstall workflow.
- Cleanup planning.
- Transaction orchestration.

`Remvora.Infrastructure`
- SQLite.
- Repositories.
- Logs.
- Configuration.

`Remvora.Windows`
- Registry adapters.
- MSI adapters.
- Package Manager adapters.
- Services.
- Task Scheduler.
- Process APIs.
- Signature verification.
- Restore-point integration.
- File-system adapters.

`Remvora.Elevation`
- Privileged worker.
- Request validation.
- Safe execution.

`Remvora.Contracts`
- IPC DTOs.
- Versioned commands/results.

`Remvora.App`
- WinUI 3.
- Navigation.
- ViewModels.
- Views.
- Commands.
- User-facing state.

---

## 11. Data model requirements

Use SQLite for persistent state.

At minimum model:

### Applications
- Internal ID.
- Display name.
- Publisher.
- Version.
- Install date.
- Scope.
- Architecture.
- Install location.
- Installer type.
- MSI product code when applicable.
- Package identity when applicable.
- Uninstall metadata reference.
- Last discovered timestamp.

### Discovery sources
- Application ID.
- Source type.
- Source identifier.
- Raw/normalized metadata.
- First/last seen.

### Scan candidates
- Scan ID.
- Application ID.
- Candidate type.
- Target.
- Evidence.
- Confidence.
- Default-selected state.
- Protection status.

### Cleanup plans
- Plan ID.
- Application ID.
- Creation time.
- Selected candidates.
- Policy version.
- Preview summary.

### Transactions
- Transaction ID.
- Plan ID.
- Operation list.
- Backup references.
- Execution status.
- Reversible flag.
- Error summary.

### Audit events
- Event ID.
- Timestamp.
- Category.
- Operation ID/transaction ID.
- Actor/source.
- Result.
- Details.

### Installation monitor snapshots
- Snapshot ID.
- Start/end.
- Target installer.
- Baseline reference.
- File changes.
- Registry changes.
- Service/task/startup changes.

Use migrations. Never rely on "drop and recreate database" behavior in production code.

---

## 12. UI/UX requirements

Build a modern Windows-native utility, not a legacy wizard-heavy clone.

Primary navigation should approximately contain:

- Dashboard
- Apps
- Leftovers
- Installation Monitor
- Startup
- Cleaner
- Tools
- Activity / History
- Settings

Use clear hierarchy and restrained visuals.

Every major operation should make the following states visible:

- Idle
- Loading
- Scanning
- Ready for review
- Awaiting confirmation
- Executing
- Partial failure
- Completed
- Cancelled

Do not freeze the UI during scans or uninstall operations.

Long-running operations must support cancellation where technically possible.

Use virtualized lists for large app inventories.

Every destructive action must have a preview state.

Do not hide important technical information from power users; allow details to be expanded.

---

## 13. Error handling requirements

Do not use exceptions as the only business-state mechanism.

Represent expected operational outcomes explicitly.

Examples:

- AccessDenied.
- NotFound.
- InUse.
- UninstallFailed.
- UninstallUnknown.
- ValidationFailed.
- ProtectedTarget.
- BackupFailed.
- WorkerUnavailable.
- WorkerRejected.
- Timeout.
- Cancelled.
- PartialSuccess.

Every user-facing failure should contain:
- What failed.
- Why it likely failed when known.
- Whether anything changed.
- What the user can safely do next.

Do not display raw stack traces to normal users. Preserve technical diagnostics in logs.

---

## 14. Performance requirements

The app should remain responsive while:

- Discovering hundreds/thousands of applications.
- Scanning filesystem locations.
- Enumerating registry data.
- Inspecting startup/services/tasks.
- Running cleanup analysis.

Use:
- Async APIs where appropriate.
- Cancellation tokens.
- Bounded concurrency.
- Incremental progress reporting.
- Virtualized UI.
- Caching where it is correct and invalidation is explicit.

Do not parallelize destructive operations merely for speed.

Avoid scanning the entire registry or entire system filesystem when scoped evidence can produce the same result more safely.

---

## 15. Testing strategy — mandatory

Do not rely on manual testing alone.

### 15.1 Unit tests

Test:
- Identity normalization.
- Deduplication.
- Path normalization.
- Ownership evidence.
- Confidence scoring.
- Protected paths.
- Cleanup policies.
- Transaction state machine.
- IPC validation.
- Command authorization.

### 15.2 Integration tests

Use disposable test applications/fixtures to verify:

- Basic install/uninstall.
- Registry changes.
- Files/folders.
- Services.
- Scheduled tasks.
- Startup entries.
- Per-user installs.
- Per-machine installs.
- MSI applications.
- Packaged apps where permitted.
- Broken/missing uninstallers.
- Locked files.
- Partial uninstalls.
- Ambiguous/shared resources.

### 15.3 Safety tests

Explicitly test that the engine refuses to remove:

- Windows system directories.
- Protected registry paths.
- Critical package components.
- Shared resources without ownership evidence.
- Invalid paths.
- Path traversal attempts.
- Relative-path tricks.
- UNC/network paths when not intentionally supported.
- Symlink/junction attacks where relevant.
- Malformed IPC requests.

### 15.4 Regression tests

Every discovered destructive bug must add a regression test before or alongside the fix.

### 15.5 Real-world application fixtures

Create a controlled test matrix using representative software categories, including:

- Standard MSI app.
- EXE installer app.
- App with services.
- App with scheduled tasks.
- App with startup entries.
- Per-user application.
- Per-machine application.
- Store/package application.
- Application with updater components.
- Application with shared dependencies.
- Application with a broken uninstaller.
- Application with missing install files.
- Application containing non-ASCII paths/names.

Do not run destructive integration tests against arbitrary user-installed software on the developer's real machine without explicit test controls.

---

## 16. Security requirements

Threat-model the application as a privileged local Windows utility.

Pay particular attention to:

- UAC boundary.
- Named-pipe impersonation/authentication.
- Time-of-check/time-of-use path changes.
- Junction/symlink/reparse-point attacks.
- Registry key redirection and views.
- Unsafe process invocation.
- Service/task manipulation.
- DLL search order issues.
- Executable path quoting.
- User-controlled installer paths.
- Tampered installation metadata.
- Unauthorized local IPC clients.
- Log tampering.

Never trust application names, publisher strings, file paths, registry values, or package metadata simply because Windows returned them.

Never use shell execution when a direct process API can accomplish the same operation safely.

Avoid dynamic code evaluation.

Keep privileged code minimal.

---

## 17. Observability

Use structured logs with categories such as:

- Discovery
- Scanner
- Uninstall
- CleanupPlan
- Worker
- IPC
- Recovery
- Database
- UI
- Security

Every log should contain enough context to diagnose failures without storing unnecessary sensitive data.

Never log secrets.

Be cautious with full file paths and personally identifying data in telemetry/log exports.

---

## 18. Documentation requirements

Maintain:

- `README.md`
- `docs/architecture.md`
- `docs/security.md`
- `docs/safety-model.md`
- `docs/database.md`
- `docs/testing.md`
- `docs/windows-apis.md`
- `docs/decisions.md`

Document important Windows-specific behavior and limitations.

When a design choice is non-obvious, record the rationale.

---

## 19. Coding style

Prefer:
- Small focused classes.
- Explicit contracts.
- Immutable value objects where practical.
- Clear names.
- Dependency injection.
- Async/cancellation-aware operations.
- Structured results.
- Deterministic policies.
- Testable adapters.

Avoid:
- God classes.
- Static global state.
- Hidden singleton registries of application state.
- UI-driven business logic.
- Raw registry/file operations scattered throughout the app.
- Giant methods.
- Catch-and-ignore exception blocks.
- Magic strings for safety-critical identifiers.
- Unbounded parallelism.

When interacting with native APIs, isolate interop code behind small adapters.

---

## 20. Definition of done for every feature

A feature is NOT done merely because the UI exists.

A feature is done when all relevant items are true:

- Architecture is implemented.
- Core logic is separated from UI.
- Happy path works.
- Failure paths are handled.
- Cancellation works where applicable.
- Logging exists.
- Safety rules exist.
- Unit tests exist.
- Integration tests exist when relevant.
- UI states exist for loading/empty/error/success.
- User-facing copy is understandable.
- Documentation is updated.
- Build passes.
- Tests pass.

---

## 21. How to handle ambiguity

When requirements are incomplete:

1. Prefer the safest interpretation.
2. Prefer Windows-supported APIs over undocumented hacks.
3. Prefer reversible behavior over irreversible behavior.
4. Prefer explicit user review over silent automation.
5. Prefer a narrower implementation over an aggressive heuristic.
6. Record the decision in `docs/decisions.md`.

Do not invent a new product feature merely to fill an unspecified gap.

Do not quietly change the feature scope.

---

## 22. How to handle technical uncertainty

When you are unsure whether a Windows API, package-management operation, registry behavior, service operation, Task Scheduler behavior, or filesystem technique is safe/correct:

- Inspect current Microsoft documentation.
- Verify the API signature and supported OS versions.
- Create a small isolated experiment or integration test.
- Prefer the documented behavior.
- Document limitations.

Do not guess with destructive system operations.

---

## 23. Prohibited shortcuts

Do NOT:

- Delete everything matching an app's display name.
- Recursively scan the entire registry and delete matches.
- Recursively scan all of `C:\Windows` for leftovers.
- Automatically delete unknown shared DLLs.
- Kill arbitrary processes by fuzzy matching.
- Remove system packages simply because the user can see them.
- Modify ACLs or ownership casually just to force deletion.
- Disable Windows security features to make the uninstall easier.
- Run the entire application as administrator.
- Hide UAC prompts with unsupported tricks.
- Execute arbitrary command strings through the privileged worker.
- Claim an operation succeeded when it only partially succeeded.
- Treat heuristics as facts.
- Add the six explicitly excluded features.

---

## 24. Vibe-coding operating instructions

You are allowed to write code proactively. Do not wait for the project owner to specify every class or file.

However, proactive implementation must stay within this specification.

When creating code:

1. Inspect existing code before modifying it.
2. Follow established naming/style conventions.
3. Reuse abstractions where correct.
4. Keep changes localized.
5. Compile frequently.
6. Test frequently.
7. Fix the root cause rather than patching symptoms.
8. Do not leave known compile errors for later phases.
9. Do not create placeholder implementations that pretend to work.
10. Clearly mark genuine platform limitations.

When a subsystem is too large, split it internally into coherent milestones and complete them in order.

Never respond with a huge code dump as a substitute for building the repository.

---

## 25. Recommended first implementation sequence

Start with exactly this sequence unless repository constraints dictate otherwise:

1. Inspect repository and toolchain.
2. Create/validate solution structure.
3. Add build/test baseline.
4. Implement domain contracts.
5. Implement application discovery adapters.
6. Build canonical inventory.
7. Build the initial Apps UI.
8. Implement normal uninstall.
9. Implement scanner evidence model.
10. Implement preview/cleanup plan.
11. Implement privileged worker + IPC.
12. Implement recovery/transactions.
13. Implement forced uninstall.
14. Implement installation monitoring.
15. Implement batch uninstall.
16. Implement Windows Apps Manager.
17. Implement Startup Manager.
18. Implement process awareness.
19. Implement junk cleaner.
20. Implement privacy/history cleaner.
21. Implement secure shredder.
22. Implement unwanted-software detector.
23. Implement signature inspector.
24. Implement audit/history center.
25. Implement Hunter Mode.
26. Implement Windows Tools Hub.
27. Polish, accessibility, performance, packaging, release hardening.

Do not jump directly to advanced cleanup heuristics before the basic inventory/uninstall pipeline is stable.

---

## 26. Final acceptance gate

Do not consider the application complete until:

- Clean build succeeds.
- Automated tests pass.
- Core uninstall scenarios work.
- Forced uninstall is safe and explainable.
- Leftover scanning is evidence-driven.
- Privileged operations are isolated and validated.
- Destructive operations are previewed and logged.
- Recovery/undo behavior is honest about limitations.
- UI remains responsive during long operations.
- Protected resources are covered by tests.
- Realistic fixture applications are covered by integration tests.
- Documentation reflects actual behavior.
- Out-of-scope features remain absent.

The most important acceptance criterion is not "does it delete a lot?"

It is:

**Does it remove the intended application and its genuine leftovers while minimizing the probability of damaging unrelated user data or Windows itself?**

---

## 27. Project-owner decision rule

The project owner values:

- Correctness.
- Safety.
- Transparency.
- A modern native Windows experience.
- Maintainability.
- Strong testing.

When a faster implementation conflicts with one of those properties, choose the safer/maintainable implementation and document the tradeoff.

Do not optimize for impressive demos at the expense of system safety.

---

## 28. Final instruction

Read this file completely before writing code.

Then read `windows_uninstaller_implementation_plan.txt` completely.

Build the application incrementally according to both documents.

Do not ask the project owner to restate requirements already specified here.

Do not add excluded features.

Do not silently weaken safety guarantees to make a feature easier.

Do not stop at scaffolding.

Compile, test, fix, and continue until the current phase is genuinely functional before moving forward.

When there is uncertainty, choose the safest technically defensible interpretation, verify against current Microsoft documentation, and record the decision.

The output of this work should be a real, buildable Windows application and a maintainable codebase — not a mockup, fake backend, or collection of TODOs.
