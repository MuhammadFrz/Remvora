using Remvora.Core.Domain.Applications;

namespace Remvora.Core.Abstractions.Discovery;

/// <summary>
/// Defines a single discovery adapter capable of querying an installed application source.
/// </summary>
public interface IApplicationDiscoverySource
{
    DiscoverySourceType SourceType { get; }
    string DisplayName { get; }
    IAsyncEnumerable<ApplicationRecord> DiscoverAsync(DiscoveryFilter filter, CancellationToken cancellationToken);
}

/// <summary>
/// Orchestrates discovery across all configured sources, performing deduplication and progress reporting.
/// </summary>
public interface IApplicationDiscoveryService
{
    Task<IReadOnlyList<ApplicationRecord>> DiscoverAllAsync(
        DiscoveryFilter? filter = null,
        IProgress<DiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistence contract for the discovered application inventory.
/// </summary>
public interface IApplicationRepository
{
    Task<IReadOnlyList<ApplicationRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ApplicationRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<ApplicationRecord> applications, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
