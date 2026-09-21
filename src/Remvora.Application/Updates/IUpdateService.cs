using Remvora.Contracts.Updates;
using Remvora.Core.Domain.Results;

namespace Remvora.Application.Updates;

/// <summary>
/// Service responsible for discovering, downloading, verifying, and staging differential application updates.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Checks the remote distribution manifest for available updates.
    /// </summary>
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the delta (or full fallback) update package, verifies SHA-256 integrity, and unpacks to staging.
    /// </summary>
    Task<OperationResult> DownloadAndStageUpdateAsync(
        UpdateCheckResult update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Spawns the root launcher with update arguments and gracefully terminates the running application.
    /// </summary>
    void ApplyUpdateAndRestart();
}
