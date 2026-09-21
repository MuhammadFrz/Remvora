using Remvora.Core.Domain.Applications;

namespace Remvora.Application.Processes;

/// <summary>
/// Abstraction for detecting and managing running processes associated with an application.
/// </summary>
public interface IProcessDetector
{
    /// <summary>
    /// Detects all active processes associated with the specified application record.
    /// </summary>
    Task<IReadOnlyList<RunningProcessInfo>> DetectProcessesAsync(
        ApplicationRecord application,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to terminate the specified process, optionally forcing termination if graceful closure fails.
    /// </summary>
    Task<bool> TerminateProcessAsync(
        int processId,
        bool force = false,
        CancellationToken cancellationToken = default);
}
