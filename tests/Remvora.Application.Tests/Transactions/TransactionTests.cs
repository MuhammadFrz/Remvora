using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Application.Auditing;
using Remvora.Application.Elevation;
using Remvora.Application.Leftovers;
using Remvora.Application.Transactions;
using Remvora.Contracts;
using Remvora.Contracts.Operations;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Auditing;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Transactions;

namespace Remvora.Application.Tests.Transactions;

public sealed class TransactionTests
{
    private sealed class FakeTransactionRepository : ITransactionRepository
    {
        public Dictionary<Guid, OperationTransaction> Transactions { get; } = new();

        public Task SaveTransactionAsync(OperationTransaction transaction, CancellationToken cancellationToken = default)
        {
            Transactions[transaction.Id] = transaction;
            return Task.CompletedTask;
        }

        public Task<OperationTransaction?> GetTransactionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Transactions.TryGetValue(id, out var tx);
            return Task.FromResult(tx);
        }

        public Task<IReadOnlyList<OperationTransaction>> GetTransactionsAsync(Guid? applicationId = null, int limit = 50, CancellationToken cancellationToken = default)
        {
            var query = Transactions.Values.AsEnumerable();
            if (applicationId.HasValue)
            {
                query = query.Where(t => t.ApplicationId == applicationId.Value);
            }
            return Task.FromResult<IReadOnlyList<OperationTransaction>>(query.Take(limit).ToList());
        }

        public Task UpdatePhaseAsync(Guid id, TransactionPhase phase, DateTimeOffset? completedAt = null, string? summaryNotes = null, CancellationToken cancellationToken = default)
        {
            if (Transactions.TryGetValue(id, out var tx))
            {
                Transactions[id] = tx with
                {
                    Phase = phase,
                    CompletedAt = completedAt ?? tx.CompletedAt,
                    SummaryNotes = summaryNotes ?? tx.SummaryNotes
                };
            }
            return Task.CompletedTask;
        }

        public Task AddTransactionItemAsync(TransactionItem item, CancellationToken cancellationToken = default)
        {
            if (Transactions.TryGetValue(item.TransactionId, out var tx))
            {
                var list = tx.Items.ToList();
                list.Add(item);
                Transactions[item.TransactionId] = tx with { Items = list };
            }
            return Task.CompletedTask;
        }

        public Task<OperationResult> DeleteTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default)
        {
            Transactions.Remove(transactionId);
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FakeTransactionBackupService : ITransactionBackupService
    {
        public List<(Guid TransactionId, TransactionItemType ItemType, string TargetLocation)> BackedUpItems { get; } = [];
        public List<TransactionItem> RestoredItems { get; } = [];
        public bool FailBackup { get; set; }
        public bool FailRestore { get; set; }

        public Task<OperationResult<string>> BackupItemAsync(Guid transactionId, TransactionItemType itemType, string targetLocation, CancellationToken cancellationToken = default)
        {
            if (FailBackup)
            {
                return Task.FromResult(OperationResult.Failure<string>(ErrorCode.BackupFailed, "Backup failed intentionally"));
            }

            BackedUpItems.Add((transactionId, itemType, targetLocation));
            return Task.FromResult(OperationResult.Success($"C:\\Backups\\{Path.GetFileName(targetLocation)}.bak"));
        }

        public Task<OperationResult> RestoreItemAsync(TransactionItem item, CancellationToken cancellationToken = default)
        {
            if (FailRestore)
            {
                return Task.FromResult(OperationResult.Failure(ErrorCode.OperationFailed, "Restore failed intentionally"));
            }

            RestoredItems.Add(item);
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FakeElevatedWorkerClient : IElevatedWorkerClient
    {
        public FakeElevatedSession? SessionToReturn { get; set; }
        public bool FailStartSession { get; set; }
        public bool StartSessionCalled { get; private set; }

        public Task<OperationResult<IElevatedSession>> StartSessionAsync(bool requestElevation = true, CancellationToken cancellationToken = default)
        {
            StartSessionCalled = true;
            if (FailStartSession || SessionToReturn == null)
            {
                return Task.FromResult(OperationResult.Failure<IElevatedSession>(ErrorCode.WorkerUnavailable, "Cannot start worker"));
            }

            return Task.FromResult(OperationResult.Success<IElevatedSession>(SessionToReturn));
        }
    }

    private sealed class FakeElevatedSession : IElevatedSession
    {
        public int ServerProcessId => 12345;
        public string SessionToken => "test-token";
        public List<(ElevatedCommandType Command, string Target)> ExecutedCommands { get; } = [];
        public bool FailExecution { get; set; }
        public bool Disposed { get; private set; }

        public Task<ElevatedOperationResult> ExecuteAsync(ElevatedCommandType commandType, string target, string? secondaryTarget = null, bool backupRequested = false, CancellationToken cancellationToken = default)
        {
            ExecutedCommands.Add((commandType, target));
            if (FailExecution)
            {
                return Task.FromResult(new ElevatedOperationResult(Guid.NewGuid(), IsSuccess: false, ErrorCode: 100, ErrorMessage: "Elevated execution failed"));
            }

            return Task.FromResult(new ElevatedOperationResult(Guid.NewGuid(), IsSuccess: true, BytesReclaimed: 1024));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public List<AuditEvent> LoggedEvents { get; } = [];

        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            LoggedEvents.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(Guid? applicationId = null, int limit = 100, CancellationToken cancellationToken = default)
        {
            var query = LoggedEvents.AsEnumerable();
            if (applicationId.HasValue)
            {
                query = query.Where(e => e.ApplicationId == applicationId.Value);
            }
            return Task.FromResult<IReadOnlyList<AuditEvent>>(query.Take(limit).ToList());
        }
    }

    private sealed class FakeCleanupPlanner : ICleanupPlanner
    {
        public bool IsValid { get; set; } = true;
        public List<string> BlockedReasons { get; } = [];

        public CleanupPlan CreatePlan(ApplicationRecord application, IReadOnlyList<CleanupCandidate> candidates)
        {
            return new CleanupPlan(
                id: Guid.NewGuid(),
                applicationId: application.Id,
                createdAt: DateTimeOffset.UtcNow,
                candidates: candidates);
        }

        public PlanValidationResult ValidatePlan(CleanupPlan plan, IEnumerable<Guid> selectedCandidateIds)
        {
            var selectedSet = selectedCandidateIds.ToHashSet();
            var selectedCandidates = plan.Candidates.Where(c => selectedSet.Contains(c.Id)).ToList();

            if (!IsValid)
            {
                return PlanValidationResult.Failure(selectedCandidates, BlockedReasons);
            }

            return PlanValidationResult.Success(selectedCandidates);
        }
    }

    [Fact]
    public async Task ExecuteCleanupAsync_WhenValidationFails_ReturnsFailureWithoutModifications()
    {
        // Arrange
        var planner = new FakeCleanupPlanner { IsValid = false };
        planner.BlockedReasons.Add("Target path is protected");
        var repo = new FakeTransactionRepository();
        var backup = new FakeTransactionBackupService();
        var worker = new FakeElevatedWorkerClient();
        var audit = new FakeAuditLogRepository();

        var executor = new TransactionExecutor(
            planner,
            repo,
            backup,
            worker,
            audit,
            NullLogger<TransactionExecutor>.Instance);

        var plan = new CleanupPlan(
            id: Guid.NewGuid(),
            applicationId: Guid.NewGuid(),
            createdAt: DateTimeOffset.UtcNow,
            candidates: []);

        // Act
        var result = await executor.ExecuteCleanupAsync(plan, [Guid.NewGuid()]);

        // Assert
        result.TotalItems.Should().Be(0);
        result.FailedItems.Should().Be(1);
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCode.ProtectedTarget);
        repo.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteCleanupAsync_WithElevatedCandidates_ExecutesViaWorkerAndSavesTransaction()
    {
        // Arrange
        var appId = Guid.NewGuid();
        var appRecord = new ApplicationRecord(
            id: appId,
            displayName: "TestApp",
            publisher: "TestCorp",
            displayVersion: "1.0",
            installDate: null,
            installLocation: "C:\\Program Files\\TestApp",
            estimatedSizeBytes: 1024,
            calculatedSizeBytes: null,
            scope: InstallationScope.PerMachine,
            architecture: ArchitectureType.X64,
            installerType: InstallerType.Msi,
            identity: new ApplicationIdentity("TestApp", "TestCorp", "1.0"),
            uninstall: new UninstallInfo("msiexec /x {12345}"));

        var cand1 = new CleanupCandidate(
            id: Guid.NewGuid(),
            applicationId: appId,
            kind: CandidateKind.Service,
            target: "TestService",
            parentTarget: null,
            evidenceReasons: ["Remnant service"],
            confidence: CandidateConfidence.High,
            confidenceScore: 90,
            risk: RiskLevel.Low,
            defaultSelected: true,
            isProtected: false,
            sizeBytes: 0);

        var cand2 = new CleanupCandidate(
            id: Guid.NewGuid(),
            applicationId: appId,
            kind: CandidateKind.File,
            target: "C:\\Program Files\\App\\leftover.dll",
            parentTarget: null,
            evidenceReasons: ["Remnant file"],
            confidence: CandidateConfidence.High,
            confidenceScore: 85,
            risk: RiskLevel.Low,
            defaultSelected: true,
            isProtected: false,
            sizeBytes: 2048);

        var planner = new FakeCleanupPlanner();
        var plan = planner.CreatePlan(appRecord, [cand1, cand2]);

        var repo = new FakeTransactionRepository();
        var backup = new FakeTransactionBackupService();
        var workerSession = new FakeElevatedSession();
        var worker = new FakeElevatedWorkerClient { SessionToReturn = workerSession };
        var audit = new FakeAuditLogRepository();

        var executor = new TransactionExecutor(
            planner,
            repo,
            backup,
            worker,
            audit,
            NullLogger<TransactionExecutor>.Instance);

        // Act
        var result = await executor.ExecuteCleanupAsync(plan, [cand1.Id, cand2.Id]);

        // Assert
        result.TotalItems.Should().Be(2);
        result.SucceededItems.Should().Be(2);
        result.FailedItems.Should().Be(0);

        backup.BackedUpItems.Should().ContainSingle(b => b.TargetLocation == cand2.Target);

        worker.StartSessionCalled.Should().BeTrue();
        workerSession.ExecutedCommands.Should().HaveCount(2);
        workerSession.Disposed.Should().BeTrue();

        var tx = repo.Transactions.Values.Single();
        tx.PlanId.Should().Be(plan.Id);
        tx.Phase.Should().Be(TransactionPhase.Completed);
        tx.Items.Should().HaveCount(2);

        audit.LoggedEvents.Should().ContainSingle(e => e.Action == "Cleanup Leftovers");
    }

    [Fact]
    public async Task RollbackTransactionAsync_WhenTransactionNotFound_ReturnsNotFound()
    {
        // Arrange
        var repo = new FakeTransactionRepository();
        var backup = new FakeTransactionBackupService();
        var audit = new FakeAuditLogRepository();

        var rollbackService = new TransactionRollbackService(
            repo,
            backup,
            audit,
            NullLogger<TransactionRollbackService>.Instance);

        // Act
        var result = await rollbackService.RollbackTransactionAsync(Guid.NewGuid());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCode.NotFound);
    }

    [Fact]
    public async Task RollbackTransactionAsync_WithReversibleItems_RestoresInReverseOrderAndUpdatesPhase()
    {
        // Arrange
        var txId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        var item1 = new TransactionItem(
            id: Guid.NewGuid(),
            transactionId: txId,
            itemType: TransactionItemType.File,
            targetLocation: "C:\\App\\file1.txt",
            backupPath: "C:\\Backups\\file1.bak",
            result: TransactionItemResult.Succeeded,
            isReversible: true);

        var item2 = new TransactionItem(
            id: Guid.NewGuid(),
            transactionId: txId,
            itemType: TransactionItemType.File,
            targetLocation: "C:\\App\\file2.txt",
            backupPath: "C:\\Backups\\file2.bak",
            result: TransactionItemResult.Succeeded,
            isReversible: true);

        var tx = new OperationTransaction(
            id: txId,
            planId: Guid.NewGuid(),
            applicationId: appId,
            operationType: "Cleanup Leftovers",
            phase: TransactionPhase.Completed,
            startedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            completedAt: DateTimeOffset.UtcNow,
            items: [item1, item2]);

        var repo = new FakeTransactionRepository();
        await repo.SaveTransactionAsync(tx);

        var backup = new FakeTransactionBackupService();
        var audit = new FakeAuditLogRepository();

        var rollbackService = new TransactionRollbackService(
            repo,
            backup,
            audit,
            NullLogger<TransactionRollbackService>.Instance);

        // Act
        var result = await rollbackService.RollbackTransactionAsync(txId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ItemsRestored.Should().Be(2);
        result.Value!.ItemsFailed.Should().Be(0);

        // Verify reverse order: item2 restored before item1
        backup.RestoredItems.Should().HaveCount(2);
        backup.RestoredItems[0].TargetLocation.Should().Be("C:\\App\\file2.txt");
        backup.RestoredItems[1].TargetLocation.Should().Be("C:\\App\\file1.txt");

        // Transaction phase should now be RolledBack
        var updatedTx = await repo.GetTransactionAsync(txId);
        updatedTx!.Phase.Should().Be(TransactionPhase.RolledBack);

        audit.LoggedEvents.Should().ContainSingle(e => e.Action == "Rollback Transaction");
    }
}
