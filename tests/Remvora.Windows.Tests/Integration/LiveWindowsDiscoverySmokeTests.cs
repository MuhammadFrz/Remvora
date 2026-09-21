using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Windows.Msi;
using Remvora.Windows.Packages;
using Remvora.Windows.Registry;

namespace Remvora.Windows.Tests.Integration;

public sealed class LiveWindowsDiscoverySmokeTests
{
    [Fact]
    public async Task LiveRegistryDiscovery_DiscoversRealInstalledApplications()
    {
        var registryAccessor = new WindowsRegistryAccessor();
        var source = new RegistryApplicationSource(registryAccessor, NullLogger<RegistryApplicationSource>.Instance);

        var apps = new List<ApplicationRecord>();
        await foreach (var app in source.DiscoverAsync(new DiscoveryFilter(), CancellationToken.None))
        {
            apps.Add(app);
        }

        // Any typical Windows 11 machine will have installed applications
        apps.Should().NotBeEmpty();
        apps.All(a => !string.IsNullOrWhiteSpace(a.DisplayName)).Should().BeTrue();
    }

    [Fact]
    public async Task LivePackageDiscovery_DiscoversRealStorePackages()
    {
        var packageAccessor = new WindowsPackageAccessor();
        var source = new PackageApplicationSource(packageAccessor, NullLogger<PackageApplicationSource>.Instance);

        var apps = new List<ApplicationRecord>();
        await foreach (var app in source.DiscoverAsync(new DiscoveryFilter(), CancellationToken.None))
        {
            apps.Add(app);
        }

        // On Windows 11, standard user apps (Calculator, Notepad, etc.) exist
        apps.Should().NotBeEmpty();
        apps.All(a => a.InstallerType == InstallerType.StorePackage).Should().BeTrue();
    }

    [Fact]
    public async Task LiveMsiDiscovery_DoesNotCrash()
    {
        var msiAccessor = new WindowsMsiAccessor();
        var source = new MsiApplicationSource(msiAccessor, NullLogger<MsiApplicationSource>.Instance);

        var apps = new List<ApplicationRecord>();
        await foreach (var app in source.DiscoverAsync(new DiscoveryFilter(), CancellationToken.None))
        {
            apps.Add(app);
        }

        // Even if 0 MSI products exist on a clean VM, it should complete without throwing
        apps.Should().NotBeNull();
    }
}
