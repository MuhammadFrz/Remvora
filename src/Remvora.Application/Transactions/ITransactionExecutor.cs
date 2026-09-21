using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Domain.Transactions;

namespace Remvora.Application.Transactions;

/// <summary>
/// Progress reporting payload during cleanup execution.
/// </summary>
public sealed record CleanupExecutionProgress(
    int CurrentIndex,
    int TotalCount,
    string CurrentTarget,
    string StatusMessage);

/// <summary>
/// Service coordinating the transactional deletion of selected leftover items.
/// </summary>
public interface ITransactionExecutor
{
    Task<CleanupResult> ExecuteCleanupAsync(
        CleanupPlan plan,
        IEnumerable<Guid> selectedCandidateIds,
        IProgress<CleanupExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
