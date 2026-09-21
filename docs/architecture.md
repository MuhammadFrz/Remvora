# Remvora Architecture

## Overview

Remvora is an uninstaller and cleanup utility for Windows 11 x64 built on modern .NET 10 LTS and WinUI 3. It enforces strict separation of concerns, safety-first policies, and an isolated privilege boundary.

## Process Architecture

```
+-------------------------------------------------------------+
|                      Remvora.App                            |
|             (Unelevated Standard User Process)              |
|                                                             |
|   +-----------------------------------------------------+   |
|   |                      WinUI 3 UI                     |   |
|   |         CommunityToolkit.Mvvm / Views / ViewModels   |   |
|   +-----------------------------------------------------+   |
|                              |                              |
|   +-----------------------------------------------------+   |
|   |                  Remvora.Application                |   |
|   |         Use Cases / Orchestration / Workflows       |   |
|   +-----------------------------------------------------+   |
|          |                         |             |          |
|   +--------------+      +-------------------+    |          |
|   | Remvora.Core |      | Remvora.Contracts |    |          |
|   | Pure Domain  |      | DTOs / Messages   |    |          |
|   +--------------+      +-------------------+    |          |
|          ^                         |             |          |
|   +----------------------+         |      +----------------+|
|   |Remvora.Infrastructure|         |      | Remvora.Windows||
|   | SQLite / Repositories|         |      | Non-privileged ||
|   +----------------------+         |      +----------------+|
+------------------------------------|------------------------+
                                     | Authenticated IPC
                                     | (Named Pipe + Nonce)
+------------------------------------v------------------------+
|                 Remvora.ElevatedWorker                      |
|                  (Elevated UAC Worker)                      |
|                                                             |
|   - Revalidates all operations against strict safety policy |
|   - Re-checks protected paths, registry keys, and targets   |
|   - Executes only explicit typed actions                    |
|   - No arbitrary command string execution                   |
|   - Returns structured progress and execution results       |
+-------------------------------------------------------------+
```

## Projects & Responsibilities

1. **Remvora.Core**:
   - Pure domain models (`ApplicationRecord`, `CleanupCandidate`, `CleanupPlan`, `OperationTransaction`, `AuditEvent`).
   - Domain policies (`ProtectedPathsPolicy`, `CandidateScoringPolicy`, `DeduplicationPolicy`).
   - Result patterns and operation error types.
   - Zero dependencies on UI, SQLite, or Win32 interop.

2. **Remvora.Contracts**:
   - IPC contracts, command requests, response payloads, progress notifications.
   - Nonce-based session authentication tokens.
   - Versioned protocol models.

3. **Remvora.Application**:
   - Application use cases (discovery orchestration, uninstall workflow, leftover analysis, transaction coordinator).
   - Coordinates domain models, contracts, and repository interfaces.

4. **Remvora.Infrastructure**:
   - SQLite data access using `Microsoft.Data.Sqlite` and Dapper.
   - Schema versioning and migrations.
   - Structured local audit logging and configuration storage.

5. **Remvora.Windows**:
   - Direct Windows platform adapters (Registry 32/64-bit views, MSI enumeration via `MsiEnumProductsEx`, AppX/MSIX package management via `PackageManager`).
   - SCM service adapters, Task Scheduler wrappers, Restart Manager lock detection, Authenticode signature verification.
   - Microsoft.Windows.CsWin32 for safe Win32 interop.

6. **Remvora.Elevation (`Remvora.ElevatedWorker.exe`)**:
   - Privileged worker process launched via UAC (`runas`) only when destructive or privileged actions are required.
   - Listens on a restrictive named pipe server.
   - Independently revalidates every incoming command; rejects unauthorized, protected, or malformed requests.

7. **Remvora.App**:
   - WinUI 3 desktop presentation layer.
   - NavigationView shell, virtualized application grids, real-time filtering, search, and detail panes.
   - Consumes Application layer services and ViewModels; never contains direct deletion or raw registry logic.
