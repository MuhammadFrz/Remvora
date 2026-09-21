# Remvora — Walkthrough

## Completed Phases

### Phase 0: Repository & Toolchain Bootstrap
- **Git Repository**: Initialized with comprehensive `.gitignore`.
- **Solution & Projects**: Set up `Remvora.slnx` containing all 11 core, platform, and test projects:
  - `Remvora.Core` (`net10.0`)
  - `Remvora.Contracts` (`net10.0`)
  - `Remvora.Application` (`net10.0`)
  - `Remvora.Infrastructure` (`net10.0`)
  - `Remvora.Windows` (`net10.0-windows10.0.26100.0`)
  - `Remvora.Elevation` (`net10.0-windows10.0.26100.0`)
  - Test suites: `Remvora.Core.Tests`, `Remvora.Contracts.Tests`, `Remvora.Application.Tests`, `Remvora.Infrastructure.Tests`, `Remvora.Windows.Tests`.
- **Compiler & Code Analysis**: `Directory.Build.props` configured for C# 14, Nullable reference types, and `latest-recommended` code quality analysis.
- **Documentation**: Initial architectural guides (`docs/architecture.md`, `docs/safety-model.md`, `docs/decisions.md`, `README.md`).

### Phase 1: Domain Models, Safety Policies & IPC Contracts
- **Domain Entities** ([`ApplicationRecord`](file:///d:/Github/Remvora/src/Remvora.Core/Domain/Applications/ApplicationRecord.cs)):
  - Canonical application model with display metadata, size estimates, installation scope, architecture, installer type, and uninstall commands.
  - Multi-factor [`ApplicationIdentity`](file:///d:/Github/Remvora/src/Remvora.Core/Domain/Applications/ApplicationIdentity.cs) with string and path normalization routines.
  - Leftover candidate models ([`CleanupCandidate`](file:///d:/Github/Remvora/src/Remvora.Core/Domain/Leftovers/CleanupCandidate.cs), [`CleanupPlan`](file:///d:/Github/Remvora/src/Remvora.Core/Domain/Leftovers/CleanupPlan.cs), [`CleanupResult`](file:///d:/Github/Remvora/src/Remvora.Core/Domain/Leftovers/CleanupPlan.cs)).
  - Auditable transaction models ([`OperationTransaction`](file:///d:/Github/Remvora/src/Remvora.Core/Domain/Transactions/OperationTransaction.cs), [`TransactionItem`](file:///d:/Github/Remvora/src/Remvora.Core/Domain/Transactions/OperationTransaction.cs)) and [`AuditEvent`](file:///d:/Github/Remvora/src/Remvora.Core/Domain/Auditing/AuditEvent.cs).
  - Functional [`OperationResult<T>`](file:///d:/Github/Remvora/src/Remvora.Core/Domain/Results/OperationResult.cs) and structured [`ErrorCode`](file:///d:/Github/Remvora/src/Remvora.Core/Domain/Results/OperationResult.cs).
- **Domain Policies**:
  - [`ProtectedPathsPolicy`](file:///d:/Github/Remvora/src/Remvora.Core/Policies/ProtectedPathsPolicy.cs): Strictly protects Windows root, System32, WinSxS, bootloader targets, system hives (`HKLM\SYSTEM`, `HKLM\SECURITY`, etc.), and Remvora itself. Canonicalizes paths and defends against traversal attacks (`..`).
  - [`CandidateScoringPolicy`](file:///d:/Github/Remvora/src/Remvora.Core/Policies/CandidateScoringPolicy.cs): Computes evidence-backed scores, classifies candidates into `High`, `Medium`, and `Low` confidence, and assigns risk levels.
  - [`DeduplicationPolicy`](file:///d:/Github/Remvora/src/Remvora.Core/Policies/DeduplicationPolicy.cs): Implements prioritized multi-factor deduplication (MSI product code > package family > registry key > composite attributes). Never merges solely on display name.
- **IPC Contracts** in `Remvora.Contracts`:
  - Typed operation requests ([`ElevatedOperationRequest`](file:///d:/Github/Remvora/src/Remvora.Contracts/Operations/ElevatedOperations.cs)), progress streaming, and results.
  - Nonce-authenticated handshake contracts ([`WorkerHandshakeRequest`](file:///d:/Github/Remvora/src/Remvora.Contracts/Handshake/WorkerHandshake.cs)).
  - Explicit allowlisted [`ElevatedCommandType`](file:///d:/Github/Remvora/src/Remvora.Contracts/IpcConstants.cs) prohibiting arbitrary shell commands.

---

## Verification Results

### Automated Tests
- Command: `dotnet test Remvora.slnx`
- Total Tests: **47 passed, 0 failed, 0 skipped**.
  - `Remvora.Core.Tests`: 45 passed (ApplicationRecord tests, ProtectedPathsPolicy tests with traversal defense, CandidateScoringPolicy tests, DeduplicationPolicy tests, OperationResult tests).
  - `Remvora.Contracts.Tests`: 2 passed (Handshake and Operation JSON serialization roundtrip tests).
- Build Status: 0 Warnings, 0 Errors with `latest-recommended` Roslyn analyzers enabled.

---

## Next Steps
- **Phase 2: Installed Application Discovery Engine**
  - Implement registry discovery adapter (HKLM, HKCU, 32-bit `WOW6432Node`, 64-bit view).
  - Implement MSI discovery adapter (`MsiEnumProductsEx`).
  - Implement AppX/MSIX Package discovery adapter (`PackageManager`).
  - Implement application discovery orchestrator and repository.
