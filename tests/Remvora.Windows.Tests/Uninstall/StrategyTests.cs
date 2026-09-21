using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Application.Uninstall;
using Remvora.Core.Domain.Applications;
using Remvora.Windows.Packages;
using Remvora.Windows.Uninstall;

namespace Remvora.Windows.Tests.Uninstall;

public sealed class StrategyTests
{
    private sealed class FakePackageAccessor : IPackageAccessor
    {
        public List<DiscoveredPackageInfo> Packages { get; } = [];
        public List<string> RemovedPackages { get; } = [];

        public IReadOnlyList<DiscoveredPackageInfo> GetUserPackages() => Packages;

        public Task<PackageRemovalResult> RemovePackageAsync(string packageFullName, CancellationToken cancellationToken = default)
        {
            RemovedPackages.Add(packageFullName);
            return Task.FromResult(new PackageRemovalResult(true, 0, null));
        }
    }

    private static ApplicationRecord CreateApp(
        string name,
        InstallerType type,
        string? uninstallCmd = null,
        string? quietCmd = null,
        string? productCode = null,
        string? packageFamily = null,
        bool isMsi = false)
    {
        return new ApplicationRecord(
            id: Guid.NewGuid(),
            displayName: name,
            publisher: "Test Pub",
            displayVersion: "1.0",
            installDate: null,
            installLocation: @"C:\App",
            estimatedSizeBytes: 100,
            calculatedSizeBytes: null,
            scope: InstallationScope.PerMachine,
            architecture: ArchitectureType.X64,
            installerType: type,
            identity: new ApplicationIdentity(name, "Test Pub", "1.0", @"C:\App", productCode, packageFamily),
            uninstall: new UninstallInfo(uninstallCmd, quietCmd, IsMsi: isMsi));
    }

    [Fact]
    public void MsiUninstallStrategy_CanHandle_WhenProductCodePresent_ReturnsTrue()
    {
        var strategy = new MsiUninstallStrategy(NullLogger<MsiUninstallStrategy>.Instance);
        var app = CreateApp("MSI App", InstallerType.Msi, productCode: "{12345678-ABCD-1234-ABCD-1234567890AB}", isMsi: true);

        strategy.CanHandle(app).Should().BeTrue();
    }

    [Fact]
    public void MsiUninstallStrategy_CanHandle_WhenUninstallStringHasMsiExec_ReturnsTrue()
    {
        var strategy = new MsiUninstallStrategy(NullLogger<MsiUninstallStrategy>.Instance);
        var app = CreateApp("MSI App", InstallerType.Unknown, uninstallCmd: "MsiExec.exe /X{12345678-ABCD-1234-ABCD-1234567890AB}");

        strategy.CanHandle(app).Should().BeTrue();
    }

    [Fact]
    public void MsiUninstallStrategy_CanHandle_WhenStandardExe_ReturnsFalse()
    {
        var strategy = new MsiUninstallStrategy(NullLogger<MsiUninstallStrategy>.Instance);
        var app = CreateApp("Exe App", InstallerType.InnoSetup, uninstallCmd: @"C:\App\uninstall.exe");

        strategy.CanHandle(app).Should().BeFalse();
    }

    [Fact]
    public void RegistryCommandStrategy_CanHandle_WhenUninstallStringPresent_ReturnsTrue()
    {
        var strategy = new RegistryCommandStrategy(NullLogger<RegistryCommandStrategy>.Instance);
        var app = CreateApp("Inno App", InstallerType.InnoSetup, uninstallCmd: @"C:\App\unins000.exe");

        strategy.CanHandle(app).Should().BeTrue();
    }

    [Fact]
    public void RegistryCommandStrategy_CanHandle_WhenMsiOrStorePackage_ReturnsFalse()
    {
        var strategy = new RegistryCommandStrategy(NullLogger<RegistryCommandStrategy>.Instance);
        var msiApp = CreateApp("Msi App", InstallerType.Msi, productCode: "{12345678-ABCD-1234-ABCD-1234567890AB}", isMsi: true);
        var pkgApp = CreateApp("Store App", InstallerType.StorePackage, packageFamily: "App_12345");

        strategy.CanHandle(msiApp).Should().BeFalse();
        strategy.CanHandle(pkgApp).Should().BeFalse();
    }

    [Fact]
    public async Task RegistryCommandStrategy_ExecuteAsync_WhenProtectedPath_RefusesExecution()
    {
        var strategy = new RegistryCommandStrategy(NullLogger<RegistryCommandStrategy>.Instance);
        var maliciousApp = CreateApp("Malicious App", InstallerType.CustomExecutable, uninstallCmd: @"C:\Windows\System32\cmd.exe /c del *.*");

        var result = await strategy.ExecuteAsync(maliciousApp, UninstallOptions.Default);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("protected system directory");
    }

    [Fact]
    public void PackageUninstallStrategy_CanHandle_WhenStorePackage_ReturnsTrue()
    {
        var fakeAccessor = new FakePackageAccessor();
        var strategy = new PackageUninstallStrategy(fakeAccessor, NullLogger<PackageUninstallStrategy>.Instance);
        var app = CreateApp("Store App", InstallerType.StorePackage, packageFamily: "App_12345");

        strategy.CanHandle(app).Should().BeTrue();
    }

    [Fact]
    public async Task PackageUninstallStrategy_ExecuteAsync_RemovesPackageSuccessfully()
    {
        var fakeAccessor = new FakePackageAccessor();
        fakeAccessor.Packages.Add(new DiscoveredPackageInfo(
            "Vendor.App_1.0.0.0_x64__abc123",
            "Vendor.App_abc123",
            "Store App",
            "Vendor",
            "1.0.0.0",
            null,
            false,
            false,
            false));

        var strategy = new PackageUninstallStrategy(fakeAccessor, NullLogger<PackageUninstallStrategy>.Instance);
        var app = CreateApp("Store App", InstallerType.StorePackage, packageFamily: "Vendor.App_abc123");

        var result = await strategy.ExecuteAsync(app, UninstallOptions.Default);

        result.IsSuccess.Should().BeTrue();
        fakeAccessor.RemovedPackages.Should().Contain("Vendor.App_1.0.0.0_x64__abc123");
    }
}
