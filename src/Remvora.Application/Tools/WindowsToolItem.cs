namespace Remvora.Application.Tools;

/// <summary>
/// Categories for Windows administrative and diagnostic tools.
/// </summary>
public enum ToolCategory
{
    SystemAdministration,
    HardwareDiagnostics,
    StorageMaintenance,
    NetworkTerminal
}

/// <summary>
/// Represents an official Windows built-in administrative or diagnostic utility.
/// </summary>
public sealed record WindowsToolItem(
    string Id,
    string Name,
    string Description,
    ToolCategory Category,
    string Executable,
    string? Arguments = null,
    bool RequiresElevation = false,
    string IconGlyph = "\uE713"
);
