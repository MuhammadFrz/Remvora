using Remvora.Core.Domain.Results;

namespace Remvora.Application.RestorePoint;

/// <summary>
/// Service abstraction for interacting with Windows System Restore.
/// </summary>
public interface IRestorePointService
{
    /// <summary>
    /// Checks whether System Restore is supported and enabled on the current system.
    /// </summary>
    Task<bool> IsRestorePointSupportedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to create a System Restore point prior to executing a major uninstall operation.
    /// </summary>
    Task<OperationResult<RestorePointResult>> CreateRestorePointAsync(
        string description,
        CancellationToken cancellationToken = default);
}
