using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Transactions;

namespace Remvora.Application.Transactions;

/// <summary>
/// Repository abstraction for journaled transactions and individual transaction items.
/// </summary>
public interface ITransactionRepository
{
    Task SaveTransactionAsync(OperationTransaction transaction, CancellationToken cancellationToken = default);

    Task<OperationTransaction?> GetTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationTransaction>> GetTransactionsAsync(
        Guid? applicationId = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task UpdatePhaseAsync(
        Guid transactionId,
        TransactionPhase phase,
        DateTimeOffset? completedAt = null,
        string? summaryNotes = null,
        CancellationToken cancellationToken = default);

    Task AddTransactionItemAsync(
        TransactionItem item,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);
}
