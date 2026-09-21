using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.WindowsApps;

namespace Remvora.Application.WindowsApps;

/// <summary>
/// Service managing modern Windows Store/MSIX applications and pre-installed bloatware packages.
/// </summary>
public interface IWindowsAppsManager
{
    /// <summary>
    /// Enumerates modern packages on the machine, identifying user-installed, provisioned, and bloatware packages.
    /// </summary>
    Task<IReadOnlyList<WindowsAppPackage>> GetPackagesAsync(
        bool includeProvisioned = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a modern package for the current user or machine-wide for all users if provisioned.
    /// </summary>
    Task<OperationResult> RemovePackageAsync(
        WindowsAppPackage package,
        bool allUsers = false,
        CancellationToken cancellationToken = default);
}
