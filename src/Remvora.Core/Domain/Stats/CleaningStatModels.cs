namespace Remvora.Core.Domain.Stats;

/// <summary>
/// Categories of cleanup and uninstallation operations tracked for lifetime metrics.
/// </summary>
public enum CleaningCategory
{
    AppUninstall = 0,
    BatchUninstall = 1,
    SystemJunk = 2,
    AppCache = 3,
    LeftoverRemnants = 4,
    SecureShredder = 5,
    SystemScan = 6
}

/// <summary>
/// A persistent event recording disk space reclaimed and items removed.
/// </summary>
public sealed record CleaningStatEvent(
    Guid Id,
    DateTimeOffset Timestamp,
    CleaningCategory Category,
    int ItemsCount,
    long BytesSaved,
    string? Details);

/// <summary>
/// Aggregated lifetime reclamation statistics.
/// </summary>
public sealed record LifetimeStats(
    long TotalBytesSaved,
    int TotalAppsUninstalled,
    int TotalLeftoversCleaned,
    int TotalJunkAndCacheFilesPurged,
    DateTimeOffset? LastCleanedAt);
