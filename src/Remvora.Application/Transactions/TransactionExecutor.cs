using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Remvora.Application.Auditing;
using Remvora.Application.Elevation;
using Remvora.Application.Leftovers;
using Remvora.Contracts;
using Remvora.Core.Domain.Auditing;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Transactions;

namespace Remvora.Application.Transactions;

/// <summary>
/// Orchestrates transactional execution of cleanup plans with automatic backups,
/// elevated worker routing, journal persistence, and audit trail generation.
/// </summary>
public sealed partial class TransactionExecutor : ITransactionExecutor
{
    private readonly ICleanupPlanner _planner;
    private readonly ITransactionRepository _transactionRepository;
    private readonly ITransactionBackupService _backupService;
    private readonly IElevatedWorkerClient _workerClient;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TransactionExecutor> _logger;

    public TransactionExecutor(
        ICleanupPlanner planner,
        ITransactionRepository transactionRepository,
        ITransactionBackupService backupService,
        IElevatedWorkerClient workerClient,
        IAuditLogRepository auditLogRepository,
        ILogger<TransactionExecutor> logger)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _transactionRepository = transactionRepository ?? throw new ArgumentNullException(nameof(transactionRepository));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
        _auditLogRepository = auditLogRepository ?? throw new ArgumentNullException(nameof(auditLogRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CleanupResult> ExecuteCleanupAsync(
        CleanupPlan plan,
        IEnumerable<Guid> selectedCandidateIds,
        IProgress<CleanupExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selectedCandidateIds);

        var startedAt = DateTimeOffset.UtcNow;
        var validation = _planner.ValidatePlan(plan, selectedCandidateIds);

        if (!validation.IsValid)
        {
            LogPlanValidationFailed(_logger, validation.BlockedReasons.Count);
            var err = new OperationError(ErrorCode.ProtectedTarget, "Cleanup blocked: one or more selected items violate protection policy.", Details: string.Join("; ", validation.BlockedReasons));
            return new CleanupResult(plan.Id, plan.ApplicationId, startedAt, DateTimeOffset.UtcNow, 0, 0, 1, 0, false, [err]);
        }

        var candidatesToProcess = validation.ValidCandidates;
        var transactionId = Guid.NewGuid();

        var transaction = new OperationTransaction(
            id: transactionId,
            planId: plan.Id,
            applicationId: plan.ApplicationId,
            operationType: "Cleanup Leftovers",
            phase: TransactionPhase.Applying,
            startedAt: startedAt);

        await _transactionRepository.SaveTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
        LogTransactionStarted(_logger, transactionId, candidatesToProcess.Count);

        IElevatedSession? elevatedSession = null;
        var needsElevation = plan.RequiresElevation || candidatesToProcess.Any(c => c.Kind is CandidateKind.Service or CandidateKind.ScheduledTask || c.Target.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase));

        if (needsElevation)
        {
            var connectResult = await _workerClient.StartSessionAsync(requestElevation: true, cancellationToken).ConfigureAwait(false);
            if (connectResult.IsSuccess && connectResult.Value != null)
            {
                elevatedSession = connectResult.Value;
            }
            else
            {
                LogWorkerSessionFailed(_logger, connectResult.Error?.Message ?? "Unknown error");
            }
        }

        var items = new List<TransactionItem>();
        var errors = new List<OperationError>();
        int succeeded = 0;
        int failed = 0;
        long reclaimedBytes = 0;
        bool rebootRequired = false;

        try
        {
            for (var i = 0; i < candidatesToProcess.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var candidate = candidatesToProcess[i];
                progress?.Report(new CleanupExecutionProgress(i + 1, candidatesToProcess.Count, candidate.Target, $"Removing {candidate.Kind}: {candidate.Target}"));

                var itemType = MapItemType(candidate.Kind);
                string? backupPath = null;

                // 1. Attempt Backup
                if (itemType is TransactionItemType.File or TransactionItemType.RegistryKey)
                {
                    var backupResult = await _backupService.BackupItemAsync(transactionId, itemType, candidate.Target, cancellationToken).ConfigureAwait(false);
                    if (backupResult.IsSuccess)
                    {
                        backupPath = backupResult.Value;
                    }
                }

                // 2. Execute Deletion
                var commandType = MapCommandType(candidate.Kind);
                bool itemSuccess = false;
                string? itemError = null;

                if (elevatedSession != null)
                {
                    var opResult = await elevatedSession.ExecuteAsync(commandType, candidate.Target, cancellationToken: cancellationToken).ConfigureAwait(false);
                    itemSuccess = opResult.IsSuccess;
                    itemError = opResult.ErrorMessage;
                    reclaimedBytes += opResult.BytesReclaimed;
                    if (opResult.RebootRequired)
                        rebootRequired = true;
                }
                else
                {
                    // Local unelevated deletion
                    try
                    {
                        ExecuteLocalDelete(candidate);
                        itemSuccess = true;
                        reclaimedBytes += candidate.SizeBytes ?? 0L;
                    }
                    catch (Exception ex)
                    {
                        itemSuccess = false;
                        itemError = ex.Message;
                    }
                }

                if (itemSuccess)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                    if (itemError != null)
                        errors.Add(new OperationError(ErrorCode.OperationFailed, itemError, candidate.Target));
                }

                var item = new TransactionItem(
                    id: Guid.NewGuid(),
                    transactionId: transactionId,
                    itemType: itemType,
                    targetLocation: candidate.Target,
                    backupPath: backupPath,
                    result: itemSuccess ? TransactionItemResult.Succeeded : TransactionItemResult.Failed,
                    isReversible: !string.IsNullOrWhiteSpace(backupPath),
                    errorMessage: itemError);

                items.Add(item);
                await _transactionRepository.AddTransactionItemAsync(item, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (elevatedSession != null)
            {
                await elevatedSession.DisposeAsync().ConfigureAwait(false);
            }
        }

        var completedAt = DateTimeOffset.UtcNow;
        var finalPhase = failed == 0
            ? TransactionPhase.Completed
            : (succeeded > 0 ? TransactionPhase.PartialFailure : TransactionPhase.Failed);

        var finalTransaction = transaction with
        {
            Phase = finalPhase,
            CompletedAt = completedAt,
            Items = items,
            SummaryNotes = $"Processed {candidatesToProcess.Count} items. Succeeded: {succeeded}, Failed: {failed}."
        };

        await _transactionRepository.UpdatePhaseAsync(
            transactionId,
            finalPhase,
            completedAt,
            finalTransaction.SummaryNotes,
            cancellationToken).ConfigureAwait(false);

        // Audit log
        await RecordAuditAsync(
            applicationId: plan.ApplicationId,
            transactionId: transactionId,
            action: "Cleanup Leftovers",
            result: finalPhase.ToString(),
            severity: failed == 0 ? AuditSeverity.Information : AuditSeverity.Warning,
            details: finalTransaction.SummaryNotes,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        LogTransactionCompleted(_logger, transactionId, succeeded, failed, reclaimedBytes);

        return new CleanupResult(
            PlanId: plan.Id,
            ApplicationId: plan.ApplicationId,
            StartedAt: startedAt,
            CompletedAt: completedAt,
            TotalItems: candidatesToProcess.Count,
            SucceededItems: succeeded,
            FailedItems: failed,
            ReclaimedSizeBytes: reclaimedBytes,
            RebootRequired: rebootRequired,
            Errors: errors);
    }

    private static void ExecuteLocalDelete(CleanupCandidate candidate)
    {
        if (candidate.Kind is CandidateKind.File or CandidateKind.Shortcut)
        {
            if (File.Exists(candidate.Target))
                File.Delete(candidate.Target);
        }
        else if (candidate.Kind is CandidateKind.Directory)
        {
            if (Directory.Exists(candidate.Target))
                Directory.Delete(candidate.Target, recursive: true);
        }
    }

    private static TransactionItemType MapItemType(CandidateKind kind) => kind switch
    {
        CandidateKind.File => TransactionItemType.File,
        CandidateKind.Directory => TransactionItemType.Directory,
        CandidateKind.RegistryKey => TransactionItemType.RegistryKey,
        CandidateKind.RegistryValue => TransactionItemType.RegistryValue,
        CandidateKind.Service => TransactionItemType.Service,
        CandidateKind.ScheduledTask => TransactionItemType.ScheduledTask,
        CandidateKind.Shortcut => TransactionItemType.File,
        _ => TransactionItemType.File
    };

    private static ElevatedCommandType MapCommandType(CandidateKind kind) => kind switch
    {
        CandidateKind.File => ElevatedCommandType.DeleteFile,
        CandidateKind.Directory => ElevatedCommandType.DeleteDirectory,
        CandidateKind.RegistryKey => ElevatedCommandType.DeleteRegistryKey,
        CandidateKind.RegistryValue => ElevatedCommandType.DeleteRegistryValue,
        CandidateKind.Service => ElevatedCommandType.DeleteService,
        CandidateKind.ScheduledTask => ElevatedCommandType.DeleteScheduledTask,
        CandidateKind.Shortcut => ElevatedCommandType.DeleteFile,
        _ => ElevatedCommandType.DeleteFile
    };

    private async Task RecordAuditAsync(
        Guid applicationId,
        Guid transactionId,
        string action,
        string result,
        AuditSeverity severity,
        string details,
        CancellationToken cancellationToken)
    {
        try
        {
            var evt = new AuditEvent(
                Id: Guid.NewGuid(),
                Timestamp: DateTimeOffset.UtcNow,
                Category: AuditCategory.FileModification,
                Severity: severity,
                Action: action,
                Target: transactionId.ToString(),
                ApplicationId: applicationId,
                TransactionId: transactionId,
                Result: result,
                ErrorCode: ErrorCode.None,
                Details: details);

            await _auditLogRepository.AppendAsync(evt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAuditFailed(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Plan validation blocked execution: {BlockedCount} items protected.")]
    private static partial void LogPlanValidationFailed(ILogger logger, int blockedCount);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Started transaction #{TransactionId} with {ItemCount} items")]
    private static partial void LogTransactionStarted(ILogger logger, Guid transactionId, int itemCount);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Failed to launch elevated worker session: {Message}")]
    private static partial void LogWorkerSessionFailed(ILogger logger, string message);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Transaction #{TransactionId} completed: {Succeeded} succeeded, {Failed} failed ({ReclaimedBytes} bytes reclaimed)")]
    private static partial void LogTransactionCompleted(ILogger logger, Guid transactionId, int succeeded, int failed, long reclaimedBytes);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Failed to persist audit event for cleanup transaction")]
    private static partial void LogAuditFailed(ILogger logger, Exception ex);
}
