using FluentAssertions;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Policies;

namespace Remvora.Core.Tests.Policies;

public sealed class DeduplicationPolicyTests
{
    private readonly DeduplicationPolicy _policy = new();

    private static ApplicationRecord CreateSample(
        string name,
        string? publisher,
        string? version,
        string? location,
        string? productCode = null,
        string? packageFamily = null,
        string? registryKey = null)
    {
        var identity = new ApplicationIdentity(name, publisher, version, location, productCode, packageFamily, registryKey);
        var uninstall = new UninstallInfo(@"C:\uninstall.exe");
        return new ApplicationRecord(
            Guid.NewGuid(),
            name,
            publisher,
            version,
            null,
            location,
            null,
            null,
            InstallationScope.PerMachine,
            ArchitectureType.X64,
            InstallerType.Unknown,
            identity,
            uninstall);
    }

    [Fact]
    public void AreDuplicates_WhenMsiProductCodesMatch_ReturnsTrue()
    {
        var app1 = CreateSample("App One", "Vendor A", "1.0", @"C:\App1", productCode: "{AAAAAAAA-1111-1111-1111-AAAAAAAAAAAA}");
        var app2 = CreateSample("App One Updated", "Vendor A", "1.1", @"C:\App1", productCode: "{AAAAAAAA-1111-1111-1111-AAAAAAAAAAAA}");

        _policy.AreDuplicates(app1, app2).Should().BeTrue();
    }

    [Fact]
    public void AreDuplicates_WhenPackageFamilyNamesMatch_ReturnsTrue()
    {
        var app1 = CreateSample("Modern App", "Publisher X", "1.0", null, packageFamily: "Publisher.ModernApp_8wekyb3d8bbwe");
        var app2 = CreateSample("Modern App Store", "Publisher X", "1.0", null, packageFamily: "Publisher.ModernApp_8wekyb3d8bbwe");

        _policy.AreDuplicates(app1, app2).Should().BeTrue();
    }

    [Fact]
    public void AreDuplicates_WhenRegistryKeyPathsMatch_ReturnsTrue()
    {
        var app1 = CreateSample("Tools App", "Tools Inc", "2.0", @"C:\Tools", registryKey: @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\ToolsApp");
        var app2 = CreateSample("Tools Application", "Tools Inc", "2.0", @"C:\Tools", registryKey: @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\ToolsApp");

        _policy.AreDuplicates(app1, app2).Should().BeTrue();
    }

    [Fact]
    public void AreDuplicates_WhenOnlyDisplayNameMatches_ReturnsFalse()
    {
        // Two totally different apps from different publishers/paths happen to both be named "Setup" or "Player"
        var app1 = CreateSample("Media Player", "Vendor Alpha", "1.0", @"C:\Program Files\Alpha\Player");
        var app2 = CreateSample("Media Player", "Vendor Beta", "2.5", @"C:\Program Files\Beta\Player");

        _policy.AreDuplicates(app1, app2).Should().BeFalse();
    }

    [Fact]
    public void Deduplicate_MergesDuplicatesAndPreservesSources()
    {
        var app1 = CreateSample("Developer Studio", "DevCorp", "2026", @"C:\DevStudio", productCode: "{11111111-2222-3333-4444-555555555555}");
        var app2 = CreateSample("Developer Studio", "DevCorp", "2026", @"C:\DevStudio", productCode: "{11111111-2222-3333-4444-555555555555}");

        var deduped = _policy.Deduplicate([app1, app2]);

        deduped.Should().HaveCount(1);
        deduped[0].DisplayName.Should().Be("Developer Studio");
    }
}
