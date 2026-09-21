using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Windows.Msi;

namespace Remvora.Windows.Tests.Msi;

public sealed class MsiApplicationSourceTests
{
    private sealed class FakeMsiAccessor : IMsiAccessor
    {
        public List<DiscoveredMsiProduct> Products { get; } = [];

        public IReadOnlyList<DiscoveredMsiProduct> GetInstalledProducts() => Products;
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsMsiApplicationsWithCorrectMetadata()
    {
        var fakeMsi = new FakeMsiAccessor();
        fakeMsi.Products.Add(new DiscoveredMsiProduct(
            "{12345678-ABCD-1234-ABCD-1234567890AB}",
            "Enterprise Database",
            "Database Corp",
            "15.2.0",
            @"C:\Program Files\EnterpriseDatabase",
            "20260915",
            IsPerMachine: true));

        var source = new MsiApplicationSource(fakeMsi, NullLogger<MsiApplicationSource>.Instance);

        var list = new List<ApplicationRecord>();
        await foreach (var app in source.DiscoverAsync(new DiscoveryFilter(), CancellationToken.None))
        {
            list.Add(app);
        }

        list.Should().HaveCount(1);
        var appRecord = list[0];
        appRecord.DisplayName.Should().Be("Enterprise Database");
        appRecord.Publisher.Should().Be("Database Corp");
        appRecord.InstallerType.Should().Be(InstallerType.Msi);
        appRecord.Identity.ProductCode.Should().Be("{12345678-ABCD-1234-ABCD-1234567890AB}");
        appRecord.Uninstall.UninstallString.Should().Be("MsiExec.exe /X{12345678-ABCD-1234-ABCD-1234567890AB}");
        appRecord.Uninstall.QuietUninstallString.Should().Be("MsiExec.exe /X{12345678-ABCD-1234-ABCD-1234567890AB} /qn");
        appRecord.Uninstall.IsMsi.Should().BeTrue();
        appRecord.Scope.Should().Be(InstallationScope.PerMachine);
    }
}
