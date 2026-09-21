using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Transactions;

namespace Remvora.Application.Transactions;

/// <summary>
/// Service responsible for capturing recoverable snapshots of items prior to destructive operations
/// and replaying snapshots during rollback.
/// </summary>
public interface ITransactionBackupService
{
    Task<OperationResult<string>> BackupItemAsync(
        Guid transactionId,
        TransactionItemType itemType,
        string targetLocation,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RestoreItemAsync(
        TransactionItem item,
        CancellationToken cancellationToken = default);
}
