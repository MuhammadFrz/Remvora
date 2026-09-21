using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Remvora.Application.Uninstall;
using Remvora.Core.Domain.Applications;
using Remvora.Windows.Packages;

namespace Remvora.Windows.Uninstall;

/// <summary>
/// Executes uninstallation for modern Windows Store and MSIX/AppX packaged applications using Windows.Management.Deployment.PackageManager.
/// </summary>
public sealed partial class PackageUninstallStrategy : IUninstallStrategy
{
    private readonly IPackageAccessor _packageAccessor;
    private readonly ILogger<PackageUninstallStrategy> _logger;

    public PackageUninstallStrategy(
        IPackageAccessor packageAccessor,
        ILogger<PackageUninstallStrategy> logger)
    {
        _packageAccessor = packageAccessor ?? throw new ArgumentNullException(nameof(packageAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public InstallerType SupportedType => InstallerType.StorePackage;

    public bool CanHandle(ApplicationRecord application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.InstallerType == InstallerType.StorePackage ||
               !string.IsNullOrWhiteSpace(application.Identity.PackageFamilyName);
    }

    public async Task<UninstallExecutionResult> ExecuteAsync(
        ApplicationRecord application,
        UninstallOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();

        // Find package full name
        var packages = _packageAccessor.GetUserPackages();
        DiscoveredPackageInfo? targetPackage = null;

        if (!string.IsNullOrWhiteSpace(application.Identity.PackageFamilyName))
        {
            targetPackage = packages.FirstOrDefault(p =>
                string.Equals(p.FamilyName, application.Identity.PackageFamilyName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.FullName, application.Identity.PackageFamilyName, StringComparison.OrdinalIgnoreCase));
        }

        if (targetPackage is null)
        {
            targetPackage = packages.FirstOrDefault(p =>
                string.Equals(p.DisplayName, application.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        if (targetPackage is null)
        {
            stopwatch.Stop();
            var msg = $"Could not locate installed package for '{application.DisplayName}' (Family: {application.Identity.PackageFamilyName ?? "N/A"}).";
            LogPackageNotFound(_logger, msg);
            return UninstallExecutionResult.Failure(-1, msg, stopwatch.Elapsed);
        }

        progress?.Report($"Removing package: {targetPackage.FullName}");
        LogRemovingPackage(_logger, targetPackage.FullName);

        var removalResult = await _packageAccessor.RemovePackageAsync(targetPackage.FullName, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        if (removalResult.IsSuccess)
        {
            LogPackageRemoved(_logger, targetPackage.FullName, stopwatch.ElapsedMilliseconds);
            return UninstallExecutionResult.Success(0, stopwatch.Elapsed, message: $"Package '{targetPackage.DisplayName}' removed successfully.");
        }

        var errorMsg = removalResult.ErrorText ?? $"Failed to remove package (HRESULT: 0x{removalResult.ErrorCode:X8}).";
        LogPackageRemovalFailed(_logger, targetPackage.FullName, errorMsg);
        return UninstallExecutionResult.Failure(removalResult.ErrorCode, errorMsg, stopwatch.Elapsed);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogPackageNotFound(ILogger logger, string message);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Removing package: {PackageFullName}")]
    private static partial void LogRemovingPackage(ILogger logger, string packageFullName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Successfully removed package {PackageFullName} in {ElapsedMs}ms")]
    private static partial void LogPackageRemoved(ILogger logger, string packageFullName, long elapsedMs);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failed removing package {PackageFullName}: {Error}")]
    private static partial void LogPackageRemovalFailed(ILogger logger, string packageFullName, string error);
}
