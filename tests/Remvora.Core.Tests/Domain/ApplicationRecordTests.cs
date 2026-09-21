using FluentAssertions;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Signatures;

namespace Remvora.Core.Tests.Domain;

public sealed class ApplicationRecordTests
{
    [Fact]
    public void Constructor_WithValidArguments_InitializesCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var identity = new ApplicationIdentity(
            "Test App",
            "Test Publisher",
            "1.0.0",
            @"C:\Program Files\TestApp",
            "{12345678-ABCD-1234-ABCD-1234567890AB}",
            null,
            @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\TestApp");

        var uninstall = new UninstallInfo(
            @"C:\Program Files\TestApp\uninstall.exe",
            @"C:\Program Files\TestApp\uninstall.exe /S",
            null,
            false,
            true);

        // Act
        var app = new ApplicationRecord(
            id,
            "Test App",
            "Test Publisher",
            "1.0.0",
            DateTimeOffset.UtcNow,
            @"C:\Program Files\TestApp",
            1024 * 1024 * 50,
            null,
            InstallationScope.PerMachine,
            ArchitectureType.X64,
            InstallerType.InnoSetup,
            identity,
            uninstall);

        // Assert
        app.Id.Should().Be(id);
        app.DisplayName.Should().Be("Test App");
        app.Publisher.Should().Be("Test Publisher");
        app.Uninstall.CanUninstall.Should().BeTrue();
        app.Uninstall.CanQuietUninstall.Should().BeTrue();
        app.Scope.Should().Be(InstallationScope.PerMachine);
        app.Architecture.Should().Be(ArchitectureType.X64);
        app.Identity.ProductCode.Should().Be("{12345678-ABCD-1234-ABCD-1234567890AB}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidDisplayName_ThrowsArgumentException(string invalidName)
    {
        var identity = new ApplicationIdentity("Valid App");
        var uninstall = new UninstallInfo("uninstall.exe");

        var act = () => new ApplicationRecord(
            Guid.NewGuid(),
            invalidName,
            "Publisher",
            "1.0",
            null,
            null,
            null,
            null,
            InstallationScope.PerUser,
            ArchitectureType.Neutral,
            InstallerType.Unknown,
            identity,
            uninstall);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Identity_NormalizesStringsAndPaths()
    {
        var identity = new ApplicationIdentity(
            "  Mozilla   Firefox  ",
            "  Mozilla   Corporation  ",
            " 130.0 ",
            @"  C:\Program Files\Mozilla Firefox\  ");

        identity.NormalizedName.Should().Be("mozilla firefox");
        identity.NormalizedPublisher.Should().Be("mozilla corporation");
        identity.InstallLocation.Should().Be(@"C:\Program Files\Mozilla Firefox");
    }
}
