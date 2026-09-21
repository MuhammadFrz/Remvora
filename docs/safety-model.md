# Remvora Safety Model

## Non-Negotiable Invariants

1. **No Blind Deletion**:
   - String matching or fuzzy similarity alone never justifies automated or default deletion.
   - Every leftover candidate must carry verifiable evidence (installation log, uninstall registry metadata, direct executable path correlation, or verified service/task targets).

2. **Protected Targets**:
   - System directories (`%SystemRoot%`, `%SystemRoot%\System32`, `%SystemRoot%\WinSxS`, etc.) are unconditionally protected.
   - Critical registry roots and system component hives are protected against modification and deletion.
   - OS-critical packages and Remvora's own installation binaries and database files are protected.

3. **Multi-State Confidence Model**:
   - **HIGH**: Eligible for default selection if policy allows (e.g. verified install monitor match, exact install path).
   - **MEDIUM**: Visible and explained with reasons; deselected by default unless explicitly configured.
   - **LOW**: Ambiguous findings; strictly unchecked by default; requires explicit user selection.

4. **Independent Revalidation**:
   - The elevated worker (`Remvora.ElevatedWorker.exe`) never trusts validation previously performed by the unelevated UI.
   - All targets, canonical paths, registry hives, and IDs are re-evaluated against the protected target policy immediately prior to execution.

5. **Reversibility & Auditability**:
   - Destructive operations are journaled in transactions before execution begins.
   - Small files and registry values are backed up when technically feasible.
   - System Restore points are initiated where permitted by Windows.
   - The UI clearly differentiates between fully reversible, partially reversible, and irreversible actions.
