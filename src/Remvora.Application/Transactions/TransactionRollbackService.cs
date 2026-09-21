using Microsoft.Extensions.Logging;
using Remvora.Application.Auditing;
using Remvora.Core.Domain.Auditing;
using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Transactions;

namespace Remvora.Application.Transactions;

/// <summary>
/// Reverses completed destructive transactions by replaying backed-up items in reverse order.
/// </summary>
public sealed partial class TransactionRollbackService : ITransactionRollbackService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly ITransactionBackupService _backupService;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<TransactionRollbackService> _logger;

    public TransactionRollbackService(
        ITransactionRepository transactionRepository,
        ITransactionBackupService backupService,
        IAuditLogRepository auditLogRepository,
        ILogger<TransactionRollbackService> logger)
    {
        _transactionRepository = transactionRepository ?? throw new ArgumentNullException(nameof(transactionRepository));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _auditLogRepository = auditLogRepository ?? throw new ArgumentNullException(nameof(auditLogRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationResult<RollbackSummary>> RollbackTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await _transactionRepository.GetTransactionAsync(transactionId, cancellationToken).ConfigureAwait(false);
        if (transaction is null)
        {
            LogTransactionNotFound(_logger, transactionId);
            return OperationResult.Failure<RollbackSummary>(ErrorCode.NotFound, $"Transaction #{transactionId} was not found.");
        }

        var reversibleItems = transaction.Items
            .Where(i => i.IsReversible && i.Result == TransactionItemResult.Succeeded && !string.IsNullOrWhiteSpace(i.BackupPath))
            .Reverse()
            .ToList();

        if (reversibleItems.Count == 0)
        {
            LogNoReversibleItems(_logger, transactionId);
            return OperationResult.Success(new RollbackSummary(transactionId, 0, 0, ["No reversible items found for this transaction."]));
        }

        LogStartingRollback(_logger, transactionId, reversibleItems.Count);

        int restored = 0;
        int failed = 0;
        var errors = new List<string>();

        foreach (var item in reversibleItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var restoreResult = await _backupService.RestoreItemAsync(item, cancellationToken).ConfigureAwait(false);
            if (restoreResult.IsSuccess)
            {
                restored++;
            }
            else
            {
                failed++;
                var msg = restoreResult.Error?.Message ?? "Unknown restore error";
                errors.Add($"{item.TargetLocation}: {msg}");
                LogItemRestoreFailed(_logger, item.TargetLocation, msg);
            }
        }

        var finalPhase = failed == 0 ? TransactionPhase.RolledBack : TransactionPhase.RollbackPartial;
        var summaryNotes = $"Rollback completed: {restored} items restored, {failed} items failed.";

        await _transactionRepository.UpdatePhaseAsync(
            transactionId,
            finalPhase,
            DateTimeOffset.UtcNow,
            summaryNotes,
            cancellationToken).ConfigureAwait(false);

        // Audit log
        await RecordAuditAsync(
            applicationId: transaction.ApplicationId,
            transactionId: transactionId,
            action: "Rollback Transaction",
            result: finalPhase.ToString(),
            severity: failed == 0 ? AuditSeverity.Information : AuditSeverity.Warning,
            details: summaryNotes,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        LogRollbackFinished(_logger, transactionId, restored, failed);

        return OperationResult.Success(new RollbackSummary(transactionId, restored, failed, errors));
    }

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
                Category: AuditCategory.Rollback,
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Transaction #{TransactionId} not found for rollback")]
    private static partial void LogTransactionNotFound(ILogger logger, Guid transactionId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Transaction #{TransactionId} has no reversible items to restore")]
    private static partial void LogNoReversibleItems(ILogger logger, Guid transactionId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Starting rollback for transaction #{TransactionId} ({Count} items)")]
    private static partial void LogStartingRollback(ILogger logger, Guid transactionId, int count);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failed to restore '{Target}': {Error}")]
    private static partial void LogItemRestoreFailed(ILogger logger, string target, string error);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Rollback finished for #{TransactionId}: {Restored} restored, {Failed} failed")]
    private static partial void LogRollbackFinished(ILogger logger, Guid transactionId, int restored, int failed);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Failed to persist rollback audit event")]
    private static partial void LogAuditFailed(ILogger logger, Exception ex);
}
