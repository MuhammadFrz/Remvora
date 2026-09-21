using Remvora.Core.Domain.Stats;

namespace Remvora.Application.Stats;

/// <summary>
/// Persistence contract for recording and aggregating lifetime cleanup and reclamation statistics.
/// </summary>
public interface ICleaningStatsRepository
{
    /// <summary>
    /// Records a new cleanup or uninstallation event.
    /// </summary>
    Task RecordEventAsync(CleaningStatEvent statEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves aggregated lifetime metrics.
    /// </summary>
    Task<LifetimeStats> GetLifetimeStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves recent cleanup history events.
    /// </summary>
    Task<IReadOnlyList<CleaningStatEvent>> GetRecentEventsAsync(int limit = 50, CancellationToken cancellationToken = default);
}
