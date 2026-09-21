using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.WindowsApps;
using Remvora.Windows.Packages;
using Remvora.Windows.WindowsApps;

namespace Remvora.Windows.Tests.WindowsApps;

public sealed class WindowsPackageAppManagerTests
{
    private sealed class FakePackageAccessor : IPackageAccessor
    {
        public List<DiscoveredPackageInfo> Packages { get; set; } = [];
        public List<string> RemovedPackages { get; } = [];
        public bool FailRemoval { get; set; }

        public IReadOnlyList<DiscoveredPackageInfo> GetUserPackages() => Packages;

        public Task<PackageRemovalResult> RemovePackageAsync(string packageFullName, CancellationToken cancellationToken = default)
        {
            if (FailRemoval)
            {
                return Task.FromResult(new PackageRemovalResult(false, 1, "Failed to remove"));
            }

            RemovedPackages.Add(packageFullName);
            return Task.FromResult(new PackageRemovalResult(true, 0, null));
        }
    }

    [Fact]
    public async Task GetPackagesAsync_ClassifiesBloatwareAndSystemProtection()
    {
        // Arrange
        var fakeAccessor = new FakePackageAccessor
        {
            Packages =
            [
                new DiscoveredPackageInfo(
                    FullName: "Microsoft.WindowsStore_8wekyb3d8bbwe_neutral",
                    FamilyName: "Microsoft.WindowsStore_8wekyb3d8bbwe",
                    DisplayName: "Microsoft Store",
                    Publisher: "Microsoft Corporation",
                    Version: "1.0.0.0",
                    InstallLocation: "C:\\Program Files\\WindowsApps\\Store",
                    IsFramework: false,
                    IsResourcePackage: false,
                    IsBundle: false),
                new DiscoveredPackageInfo(
                    FullName: "Microsoft.BingNews_8wekyb3d8bbwe_neutral",
                    FamilyName: "Microsoft.BingNews_8wekyb3d8bbwe",
                    DisplayName: "Microsoft News",
                    Publisher: "Microsoft Corporation",
                    Version: "1.0.0.0",
                    InstallLocation: "C:\\Program Files\\WindowsApps\\BingNews",
                    IsFramework: false,
                    IsResourcePackage: false,
                    IsBundle: false),
                new DiscoveredPackageInfo(
                    FullName: "King.com.CandyCrushSaga_12345",
                    FamilyName: "King.com.CandyCrushSaga_12345",
                    DisplayName: "Candy Crush Saga",
                    Publisher: "King.com",
                    Version: "1.0.0.0",
                    InstallLocation: "C:\\Program Files\\WindowsApps\\CandyCrush",
                    IsFramework: false,
                    IsResourcePackage: false,
                    IsBundle: false)
            ]
        };

        var manager = new WindowsPackageAppManager(fakeAccessor, NullLogger<WindowsPackageAppManager>.Instance);

        // Act
        var result = await manager.GetPackagesAsync();

        // Assert
        var store = result.First(p => p.PackageFamilyName == "Microsoft.WindowsStore_8wekyb3d8bbwe");
        store.IsSystemProtected.Should().BeTrue();
        store.IsBloatware.Should().BeFalse();

        var news = result.First(p => p.PackageFamilyName == "Microsoft.BingNews_8wekyb3d8bbwe");
        news.IsSystemProtected.Should().BeFalse();
        news.IsBloatware.Should().BeTrue();
        news.BloatwareType.Should().Be(BloatwareCategory.Sponsored);

        var candy = result.First(p => p.DisplayName == "Candy Crush Saga");
        candy.IsSystemProtected.Should().BeFalse();
        candy.IsBloatware.Should().BeTrue();
        candy.BloatwareType.Should().Be(BloatwareCategory.Sponsored);
    }

    [Fact]
    public async Task RemovePackageAsync_WhenSystemProtected_BlocksRemoval()
    {
        // Arrange
        var fakeAccessor = new FakePackageAccessor();
        var manager = new WindowsPackageAppManager(fakeAccessor, NullLogger<WindowsPackageAppManager>.Instance);

        var protectedPkg = new WindowsAppPackage(
            packageFullName: "Microsoft.WindowsStore_8wekyb3d8bbwe_neutral",
            packageFamilyName: "Microsoft.WindowsStore_8wekyb3d8bbwe",
            displayName: "Microsoft Store",
            publisher: "Microsoft Corporation",
            version: "1.0",
            isSystemProtected: true);

        // Act
        var result = await manager.RemovePackageAsync(protectedPkg);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.ProtectedTarget);
        fakeAccessor.RemovedPackages.Should().BeEmpty();
    }

    [Fact]
    public async Task RemovePackageAsync_WhenUnprotectedBloatware_RemovesSuccessfully()
    {
        // Arrange
        var fakeAccessor = new FakePackageAccessor();
        var manager = new WindowsPackageAppManager(fakeAccessor, NullLogger<WindowsPackageAppManager>.Instance);

        var bloatPkg = new WindowsAppPackage(
            packageFullName: "King.com.CandyCrushSaga_12345",
            packageFamilyName: "King.com.CandyCrushSaga_12345",
            displayName: "Candy Crush Saga",
            publisher: "King.com",
            version: "1.0",
            isSystemProtected: false,
            bloatwareType: BloatwareCategory.Sponsored);

        // Act
        var result = await manager.RemovePackageAsync(bloatPkg);

        // Assert
        result.IsSuccess.Should().BeTrue();
        fakeAccessor.RemovedPackages.Should().ContainSingle(p => p == bloatPkg.PackageFullName);
    }
}
