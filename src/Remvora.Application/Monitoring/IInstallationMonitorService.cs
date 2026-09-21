using Remvora.Core.Domain.Monitoring;
using Remvora.Core.Domain.Results;

namespace Remvora.Application.Monitoring;

/// <summary>
/// Service responsible for recording system changes during software installation sessions.
/// </summary>
public interface IInstallationMonitorService
{
    /// <summary>
    /// Gets whether a monitoring session is currently capturing system state.
    /// </summary>
    bool IsMonitoringActive { get; }

    /// <summary>
    /// Gets the currently active session, or null if no session is running.
    /// </summary>
    InstallationSession? ActiveSession { get; }

    /// <summary>
    /// Captures a baseline snapshot of relevant filesystem and registry trees and starts monitoring.
    /// </summary>
    Task<InstallationSession> StartMonitoringAsync(
        string sessionName,
        string? installerPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the active session, captures a post-install snapshot, calculates differences, and saves the log.
    /// </summary>
    Task<InstallationSession> StopMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all previously saved installation monitoring sessions.
    /// </summary>
    Task<IReadOnlyList<InstallationSession>> GetSavedSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a saved installation session log.
    /// </summary>
    Task<OperationResult> DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports an installation log to a human-readable and machine-readable JSON file.
    /// </summary>
    Task<OperationResult> ExportSessionAsync(Guid sessionId, string targetFilePath, CancellationToken cancellationToken = default);
}
