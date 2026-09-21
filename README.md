# Remvora

**Remvora** is a native Windows desktop uninstaller and cleanup utility designed for Windows 11 x64. It prioritizes safety, transparency, and evidence-based software removal.

## Core Features
- **Accurate App Discovery**: Aggregates from 32-bit and 64-bit registry entries, Windows Installer (MSI), and modern packaged apps (AppX/MSIX) with multi-factor deduplication.
- **Evidence-Based Leftover Removal**: Employs an evidence and scoring pipeline to identify leftovers without aggressive string-matching.
- **Privilege Separation**: The UI runs unelevated by default; privileged tasks execute via an on-demand elevated worker over authenticated IPC.
- **Transparent Previews & Dry-Runs**: Review and individually select or deselect candidates with complete reason explanations before any modification.
- **Transaction-Backed Recovery**: Auditable actions with rollback journals, file/registry backups, and Windows System Restore integration.

## Technology Stack
- **Language**: C# 14 (.NET 10 LTS)
- **UI Framework**: WinUI 3 (Windows App SDK)
- **Architecture**: Clean Layered Architecture + CommunityToolkit.Mvvm
- **Data Access**: SQLite (`Microsoft.Data.Sqlite`) + Dapper
- **Native Interop**: Microsoft.Windows.CsWin32
- **Testing**: xUnit + FluentAssertions

## Building
```powershell
dotnet build Remvora.slnx
dotnet test Remvora.slnx
```
