using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Application.Auditing;
using Remvora.Application.Processes;
using Remvora.Application.RestorePoint;
using Remvora.Application.Uninstall;
using Remvora.Application.Workflows;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Auditing;
using Remvora.Core.Domain.Results;

namespace Remvora.Application.Tests.Workflows;

public sealed class UninstallOrchestratorTests
{
    private sealed class FakeProcessDetector : IProcessDetector
    {
        public List<RunningProcessInfo> ActiveProcesses { get; set; } = [];
        public List<int> TerminatedProcessIds { get; } = [];

        public Task<IReadOnlyList<RunningProcessInfo>> DetectProcessesAsync(ApplicationRecord application, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RunningProcessInfo>>(ActiveProcesses);

        public Task<bool> TerminateProcessAsync(int processId, bool force = false, CancellationToken cancellationToken = default)
        {
            TerminatedProcessIds.Add(processId);
            ActiveProcesses = ActiveProcesses.Where(p => p.ProcessId != processId).ToList();
            return Task.FromResult(true);
        }
    }

    private sealed class FakeRestorePointService : IRestorePointService
    {
        public bool IsSupported { get; set; } = true;
        public int CallCount { get; private set; }
        public bool ReturnFailure { get; set; }

        public Task<bool> IsRestorePointSupportedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(IsSupported);

        public Task<OperationResult<RestorePointResult>> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ReturnFailure)
            {
                return Task.FromResult(OperationResult.Failure<RestorePointResult>(
                    ErrorCode.SystemRestoreFailed,
                    "Failed to create restore point"));
            }

            return Task.FromResult(OperationResult.Success(
                new RestorePointResult(42, description, DateTimeOffset.UtcNow)));
        }
    }

    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public List<AuditEvent> Events { get; } = [];

        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(Guid? applicationId = null, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
    }

    private sealed class FakeUninstallStrategy : IUninstallStrategy
    {
        public InstallerType SupportedType { get; init; } = InstallerType.InnoSetup;
        public bool CanHandleResult { get; set; } = true;
        public UninstallExecutionResult ExecutionResult { get; set; } = UninstallExecutionResult.Success(0, TimeSpan.FromSeconds(1));
        public int ExecutionCount { get; private set; }

        public bool CanHandle(ApplicationRecord application) => CanHandleResult;

        public Task<UninstallExecutionResult> ExecuteAsync(
            ApplicationRecord application,
            UninstallOptions options,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(ExecutionResult);
        }
    }

    private static ApplicationRecord CreateTestApp(
        string name = "Test App",
        InstallerType type = InstallerType.InnoSetup,
        string? productCode = null,
        string? packageFamilyName = null)
    {
        return new ApplicationRecord(
            id: Guid.NewGuid(),
            displayName: name,
            publisher: "Test Publisher",
            displayVersion: "1.0",
            installDate: null,
            installLocation: @"C:\Program Files\TestApp",
            estimatedSizeBytes: 1024,
            calculatedSizeBytes: null,
            scope: InstallationScope.PerMachine,
            architecture: ArchitectureType.X64,
            installerType: type,
            identity: new ApplicationIdentity(name, "Test Publisher", "1.0", @"C:\Program Files\TestApp", productCode, packageFamilyName),
            uninstall: new UninstallInfo(@"C:\Program Files\TestApp\unins000.exe", IsMsi: type == InstallerType.Msi));
    }

    [Fact]
    public async Task UninstallAsync_WhenProcessRunningAndWarnEnabled_ReturnsFailureWithRemainingProcesses()
    {
        var procDetector = new FakeProcessDetector
        {
            ActiveProcesses = [new RunningProcessInfo(1234, "testapp", @"C:\Program Files\TestApp\testapp.exe", "Test App")]
        };
        var restoreService = new FakeRestorePointService();
        var auditRepo = new FakeAuditLogRepository();
        var strategy = new FakeUninstallStrategy();

        var orchestrator = new UninstallOrchestrator(
            [strategy],
            procDetector,
            restoreService,
            auditRepo,
            NullLogger<UninstallOrchestrator>.Instance);

        var app = CreateTestApp();
        var options = new UninstallWorkflowOptions(WarnIfProcessesRunning: true, TerminateProcesses: false);

        var result = await orchestrator.UninstallAsync(app, options);

        result.IsSuccess.Should().BeFalse();
        result.RemainingProcesses.Should().HaveCount(1);
        strategy.ExecutionCount.Should().Be(0);
    }

    [Fact]
    public async Task UninstallAsync_WhenProcessRunningAndTerminateEnabled_TerminatesProcessesAndProceeds()
    {
        var procDetector = new FakeProcessDetector
        {
            ActiveProcesses = [new RunningProcessInfo(1234, "testapp", @"C:\Program Files\TestApp\testapp.exe", "Test App")]
        };
        var restoreService = new FakeRestorePointService();
        var auditRepo = new FakeAuditLogRepository();
        var strategy = new FakeUninstallStrategy();

        var orchestrator = new UninstallOrchestrator(
            [strategy],
            procDetector,
            restoreService,
            auditRepo,
            NullLogger<UninstallOrchestrator>.Instance);

        var app = CreateTestApp();
        var options = new UninstallWorkflowOptions(TerminateProcesses: true);

        var result = await orchestrator.UninstallAsync(app, options);

        result.IsSuccess.Should().BeTrue();
        procDetector.TerminatedProcessIds.Should().Contain(1234);
        strategy.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task UninstallAsync_WhenRestorePointRequested_InvokesRestorePointService()
    {
        var procDetector = new FakeProcessDetector();
        var restoreService = new FakeRestorePointService();
        var auditRepo = new FakeAuditLogRepository();
        var strategy = new FakeUninstallStrategy();

        var orchestrator = new UninstallOrchestrator(
            [strategy],
            procDetector,
            restoreService,
            auditRepo,
            NullLogger<UninstallOrchestrator>.Instance);

        var app = CreateTestApp();
        var options = new UninstallWorkflowOptions(CreateRestorePoint: true);

        var result = await orchestrator.UninstallAsync(app, options);

        result.IsSuccess.Should().BeTrue();
        result.RestorePointSequence.Should().Be(42);
        restoreService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task UninstallAsync_WhenNoStrategyCanHandle_FailsWithStrategyNotFoundAndAudits()
    {
        var procDetector = new FakeProcessDetector();
        var restoreService = new FakeRestorePointService();
        var auditRepo = new FakeAuditLogRepository();
        var strategy = new FakeUninstallStrategy { CanHandleResult = false };

        var orchestrator = new UninstallOrchestrator(
            [strategy],
            procDetector,
            restoreService,
            auditRepo,
            NullLogger<UninstallOrchestrator>.Instance);

        var app = CreateTestApp();
        var result = await orchestrator.UninstallAsync(app, UninstallWorkflowOptions.Default);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("No suitable uninstall strategy found");
        auditRepo.Events.Should().ContainSingle(e => e.ErrorCode == ErrorCode.UninstallStrategyNotFound);
    }

    [Fact]
    public async Task UninstallAsync_WhenStrategySucceeds_ReturnsSuccessAndAudits()
    {
        var procDetector = new FakeProcessDetector();
        var restoreService = new FakeRestorePointService();
        var auditRepo = new FakeAuditLogRepository();
        var strategy = new FakeUninstallStrategy();

        var orchestrator = new UninstallOrchestrator(
            [strategy],
            procDetector,
            restoreService,
            auditRepo,
            NullLogger<UninstallOrchestrator>.Instance);

        var app = CreateTestApp();
        var result = await orchestrator.UninstallAsync(app, UninstallWorkflowOptions.Default);

        result.IsSuccess.Should().BeTrue();
        auditRepo.Events.Should().ContainSingle(e => e.Result == "Success");
    }

    [Fact]
    public async Task UninstallAsync_RoutesToMsiStrategy_WhenAppIsMsi()
    {
        var procDetector = new FakeProcessDetector();
        var restoreService = new FakeRestorePointService();
        var auditRepo = new FakeAuditLogRepository();

        var msiStrategy = new FakeUninstallStrategy { SupportedType = InstallerType.Msi };
        var otherStrategy = new FakeUninstallStrategy { SupportedType = InstallerType.InnoSetup };

        var orchestrator = new UninstallOrchestrator(
            [otherStrategy, msiStrategy],
            procDetector,
            restoreService,
            auditRepo,
            NullLogger<UninstallOrchestrator>.Instance);

        var app = CreateTestApp(type: InstallerType.Msi, productCode: "{11111111-2222-3333-4444-555555555555}");
        var result = await orchestrator.UninstallAsync(app, UninstallWorkflowOptions.Default);

        result.IsSuccess.Should().BeTrue();
        msiStrategy.ExecutionCount.Should().Be(1);
        otherStrategy.ExecutionCount.Should().Be(0);
    }

    [Fact]
    public async Task UninstallAsync_RoutesToPackageStrategy_WhenAppIsStorePackage()
    {
        var procDetector = new FakeProcessDetector();
        var restoreService = new FakeRestorePointService();
        var auditRepo = new FakeAuditLogRepository();

        var pkgStrategy = new FakeUninstallStrategy { SupportedType = InstallerType.StorePackage };
        var otherStrategy = new FakeUninstallStrategy { SupportedType = InstallerType.InnoSetup };

        var orchestrator = new UninstallOrchestrator(
            [otherStrategy, pkgStrategy],
            procDetector,
            restoreService,
            auditRepo,
            NullLogger<UninstallOrchestrator>.Instance);

        var app = CreateTestApp(type: InstallerType.StorePackage, packageFamilyName: "Publisher.App_8wekyb3d8bbwe");
        var result = await orchestrator.UninstallAsync(app, UninstallWorkflowOptions.Default);

        result.IsSuccess.Should().BeTrue();
        pkgStrategy.ExecutionCount.Should().Be(1);
        otherStrategy.ExecutionCount.Should().Be(0);
    }
}
