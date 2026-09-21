using Remvora.Core.Domain.Applications;

namespace Remvora.Core.Abstractions.Discovery;

/// <summary>
/// Options filtering which categories of applications are included in an inventory scan.
/// </summary>
public sealed record DiscoveryFilter
{
    public bool IncludeSystemComponents { get; init; }
    public bool IncludePerUser { get; init; } = true;
    public bool IncludePerMachine { get; init; } = true;
    public bool IncludeStoreApps { get; init; } = true;
    public string? SearchQuery { get; init; }
}

/// <summary>
/// Incremental progress report issued during application inventory discovery.
/// </summary>
public sealed record DiscoveryProgress(
    DiscoverySourceType CurrentSource,
    string CurrentItemName,
    int ItemsDiscoveredSoFar);
