using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;

namespace Remvora.Windows.Msi;

/// <summary>
/// Discovers installed Windows Installer (MSI) applications using standard MSI APIs.
/// </summary>
public sealed class MsiApplicationSource : IApplicationDiscoverySource
{
    private readonly IMsiAccessor _msiAccessor;
    private readonly ILogger<MsiApplicationSource> _logger;

    public DiscoverySourceType SourceType => DiscoverySourceType.MsiDatabase;
    public string DisplayName => "Windows Installer (MSI)";

    public MsiApplicationSource(IMsiAccessor msiAccessor, ILogger<MsiApplicationSource> logger)
    {
        _msiAccessor = msiAccessor ?? throw new ArgumentNullException(nameof(msiAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<ApplicationRecord> DiscoverAsync(
        DiscoveryFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        filter ??= new DiscoveryFilter();

        var products = _msiAccessor.GetInstalledProducts();

        foreach (var product in products)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(product.ProductName))
                continue;

            if (product.IsPerMachine && !filter.IncludePerMachine)
                continue;

            if (!product.IsPerMachine && !filter.IncludePerUser)
                continue;

            if (!string.IsNullOrWhiteSpace(filter.SearchQuery) &&
                !product.ProductName.Contains(filter.SearchQuery, StringComparison.OrdinalIgnoreCase) &&
                !(product.Publisher?.Contains(filter.SearchQuery, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            DateTimeOffset? installDate = null;
            if (!string.IsNullOrWhiteSpace(product.InstallDate))
            {
                if (DateTime.TryParseExact(product.InstallDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    installDate = new DateTimeOffset(dt, TimeSpan.Zero);
            }

            var identity = new ApplicationIdentity(
                product.ProductName,
                product.Publisher,
                product.Version,
                product.InstallLocation,
                product.ProductCode,
                null,
                null);

            var uninstallInfo = new UninstallInfo(
                UninstallString: $"MsiExec.exe /X{product.ProductCode}",
                QuietUninstallString: $"MsiExec.exe /X{product.ProductCode} /qn",
                ModifyPath: $"MsiExec.exe /I{product.ProductCode}",
                IsMsi: true,
                RequiresElevation: product.IsPerMachine);

            var metadata = new Dictionary<string, string>
            {
                ["ProductCode"] = product.ProductCode,
                ["IsPerMachine"] = product.IsPerMachine.ToString()
            };

            if (!string.IsNullOrEmpty(product.InstallLocation))
                metadata["InstallLocation"] = product.InstallLocation;

            var discoverySource = new DiscoverySource(
                DiscoverySourceType.MsiDatabase,
                product.ProductCode,
                DateTimeOffset.UtcNow,
                metadata);

            var record = new ApplicationRecord(
                Guid.NewGuid(),
                product.ProductName,
                product.Publisher,
                product.Version,
                installDate,
                product.InstallLocation,
                null,
                null,
                product.IsPerMachine ? InstallationScope.PerMachine : InstallationScope.PerUser,
                ArchitectureType.Neutral,
                InstallerType.Msi,
                identity,
                uninstallInfo,
                [discoverySource],
                null,
                RunningStatus.Unknown,
                false);

            yield return record;
            await Task.Yield();
        }
    }
}
