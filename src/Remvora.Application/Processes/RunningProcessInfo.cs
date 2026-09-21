namespace Remvora.Application.Processes;

/// <summary>
/// Information about a running process associated with an application.
/// </summary>
public sealed record RunningProcessInfo(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string? MainWindowTitle);
