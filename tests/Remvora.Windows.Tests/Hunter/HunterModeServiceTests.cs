using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Application.Hunter;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Policies;
using Remvora.Windows.Hunter;
using Xunit;

namespace Remvora.Windows.Tests.Hunter;

public sealed class HunterModeServiceTests
{
    private readonly ProtectedPathsPolicy _policy = new();

    [Fact]
    public async Task ResolveTargetFromPath_WithCurrentProcessExe_ReturnsTarget()
    {
        string currentExe = Environment.ProcessPath!;
        var repo = new FakeApplicationRepository();
        var service = new WindowsHunterModeService(repo, _policy, NullLogger<WindowsHunterModeService>.Instance);

        var result = await service.ResolveTargetFromPathAsync(currentExe);

        result.Should().NotBeNull();
        result!.ProcessName.Should().NotBeNullOrWhiteSpace();
        result.ExecutablePath.Should().Be(currentExe);
        result.MatchedApplication.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveTargetFromPath_WithNonExistentPath_ReturnsNull()
    {
        var repo = new FakeApplicationRepository();
        var service = new WindowsHunterModeService(repo, _policy, NullLogger<WindowsHunterModeService>.Instance);

        var result = await service.ResolveTargetFromPathAsync(@"C:\NonExistentFolder\fake_app_123.exe");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTargetFromPath_WhenRepositoryHasMatchingApp_ReturnsRegisteredApplication()
    {
        string currentExe = Environment.ProcessPath!;
        string currentDir = Path.GetDirectoryName(currentExe)!;

        var identity = new ApplicationIdentity("Test Target Application", "Remvora Test", "2.0.0", currentDir);
        var uninstall = new UninstallInfo(@"C:\test\uninstall.exe");
        var testGuid = Guid.NewGuid();

        var registeredApp = new ApplicationRecord(
            id: testGuid,
            displayName: "Test Target Application",
            publisher: "Remvora Test",
            displayVersion: "2.0.0",
            installDate: DateTimeOffset.UtcNow,
            installLocation: currentDir,
            estimatedSizeBytes: 1024,
            calculatedSizeBytes: null,
            scope: InstallationScope.PerMachine,
            architecture: ArchitectureType.X64,
            installerType: InstallerType.InnoSetup,
            identity: identity,
            uninstall: uninstall);

        var repo = new FakeApplicationRepository(new[] { registeredApp });
        var service = new WindowsHunterModeService(repo, _policy, NullLogger<WindowsHunterModeService>.Instance);

        var result = await service.ResolveTargetFromPathAsync(currentExe);

        result.Should().NotBeNull();
        result!.Confidence.Should().Contain("Registered Application");
        result.MatchedApplication.Should().NotBeNull();
        result.MatchedApplication!.Id.Should().Be(testGuid);
        result.MatchedApplication.DisplayName.Should().Be("Test Target Application");
    }

    [Fact]
    public void TerminateTargetProcess_WhenTargetIsSystemProtected_RejectsTermination()
    {
        var repo = new FakeApplicationRepository();
        var service = new WindowsHunterModeService(repo, _policy, NullLogger<WindowsHunterModeService>.Instance);

        // Process ID 0 or 4 (System) or protected process
        var result = service.TerminateTargetProcess(4);

        result.IsSuccess.Should().BeFalse();
    }

    private sealed class FakeApplicationRepository : IApplicationRepository
    {
        private readonly List<ApplicationRecord> _apps;

        public FakeApplicationRepository(IEnumerable<ApplicationRecord>? apps = null)
        {
            _apps = apps != null ? new List<ApplicationRecord>(apps) : new List<ApplicationRecord>();
        }

        public Task<IReadOnlyList<ApplicationRecord>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ApplicationRecord>>(_apps);

        public Task<ApplicationRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_apps.Find(a => a.Id == id));

        public Task SaveAsync(IEnumerable<ApplicationRecord> applications, CancellationToken cancellationToken = default)
        {
            _apps.AddRange(applications);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _apps.RemoveAll(a => a.Id == id);
            return Task.CompletedTask;
        }
    }
}
