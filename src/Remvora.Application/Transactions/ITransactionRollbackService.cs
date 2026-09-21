using Remvora.Core.Domain.Results;

namespace Remvora.Application.Transactions;

/// <summary>
/// Summary details of a completed transaction rollback.
/// </summary>
public sealed record RollbackSummary(
    Guid TransactionId,
    int ItemsRestored,
    int ItemsFailed,
    IReadOnlyList<string> ErrorMessages);

/// <summary>
/// Service responsible for reversing destructive transactions by replaying backed up resources.
/// </summary>
public interface ITransactionRollbackService
{
    Task<OperationResult<RollbackSummary>> RollbackTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);
}
