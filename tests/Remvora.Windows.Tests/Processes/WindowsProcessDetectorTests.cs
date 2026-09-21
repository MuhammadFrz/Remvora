using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Core.Domain.Applications;
using Remvora.Windows.Processes;

namespace Remvora.Windows.Tests.Processes;

public sealed class WindowsProcessDetectorTests
{
    [Fact]
    public async Task DetectProcessesAsync_WhenApplicationPathMatchesCurrentProcessDirectory_DetectsProcess()
    {
        var detector = new WindowsProcessDetector(NullLogger<WindowsProcessDetector>.Instance);

        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var processDirectory = Path.GetDirectoryName(currentProcess.MainModule?.FileName) ?? AppContext.BaseDirectory;

        var app = new ApplicationRecord(
            id: Guid.NewGuid(),
            displayName: "Test Process Runner",
            publisher: "Test",
            displayVersion: "1.0",
            installDate: null,
            installLocation: processDirectory,
            estimatedSizeBytes: 100,
            calculatedSizeBytes: null,
            scope: InstallationScope.PerUser,
            architecture: ArchitectureType.X64,
            installerType: InstallerType.CustomExecutable,
            identity: new ApplicationIdentity("Test Process Runner", "Test", "1.0", processDirectory),
            uninstall: new UninstallInfo(currentProcess.MainModule?.FileName));

        var detected = await detector.DetectProcessesAsync(app);

        detected.Should().NotBeEmpty();
        detected.Should().Contain(p => p.ProcessId == currentProcess.Id);
    }

    [Fact]
    public async Task DetectProcessesAsync_WhenInstallLocationDoesNotExist_ReturnsEmpty()
    {
        var detector = new WindowsProcessDetector(NullLogger<WindowsProcessDetector>.Instance);

        var app = new ApplicationRecord(
            id: Guid.NewGuid(),
            displayName: "Nonexistent App 9999",
            publisher: "Test",
            displayVersion: "1.0",
            installDate: null,
            installLocation: @"C:\NonexistentDirectory_XYZ_123456789",
            estimatedSizeBytes: null,
            calculatedSizeBytes: null,
            scope: InstallationScope.PerUser,
            architecture: ArchitectureType.X64,
            installerType: InstallerType.Unknown,
            identity: new ApplicationIdentity("Nonexistent App 9999"),
            uninstall: new UninstallInfo(@"C:\NonexistentDirectory_XYZ_123456789\fake_unins.exe"));

        var detected = await detector.DetectProcessesAsync(app);

        detected.Should().BeEmpty();
    }
}
