# Architecture Decision Records (ADRs)

## ADR 001: Technology Stack Selection
- **Status**: Accepted
- **Context**: The product requires high-fidelity integration with Windows 11 system components (registry, MSI, packages, services, tasks, restart manager) and modern Windows design aesthetics.
- **Decision**: Locked to modern C# 14 on .NET 10 LTS, WinUI 3 (Windows App SDK), CsWin32 for native Win32 source generation, and SQLite for persistence.

## ADR 002: Process Model & Privilege Boundary
- **Status**: Accepted
- **Context**: Running the entire UI application elevated exposes a high-privilege attack surface to external data and third-party code.
- **Decision**: The main UI (`Remvora.exe`) runs as a standard unelevated user process. A dedicated worker (`Remvora.ElevatedWorker.exe`) is launched on-demand via UAC for privileged actions, communicating over authenticated named pipes using explicit, typed operation DTOs. Arbitrary shell commands are strictly prohibited.

## ADR 003: Solution Format
- **Status**: Accepted
- **Context**: .NET 10 introduces the modern XML-based `.slnx` solution file format supported by dotnet CLI and modern tools.
- **Decision**: Use `Remvora.slnx` as the primary solution definition.

## ADR 004: Deduplication Policy
- **Status**: Accepted
- **Context**: Software installers often register in multiple places (MSI APIs, 32-bit registry, 64-bit registry, AppX packages).
- **Decision**: Deduplicate using a prioritized multi-factor identity: exact MSI ProductCode > package family name > exact registry key path > normalized vendor + product name + version + location. Never merge records based solely on display name.
