using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;

namespace Remvora.Application.Leftovers;

/// <summary>
/// Depth or thoroughness of leftover scanning.
/// </summary>
public enum ScanLevel
{
    /// <summary>
    /// Safe and fast: inspects exact install location, direct registry keys, and shortcuts only.
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Balanced (recommended): inspects install location, app data, registry, shortcuts, services, and tasks.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Deep: searches extended publisher locations and system-wide application-data caches.
    /// </summary>
    Thorough = 2
}

/// <summary>
/// Options configuring a leftover scan execution.
/// </summary>
public sealed record ScanOptions(
    ScanLevel Level = ScanLevel.Normal,
    bool IncludeFiles = true,
    bool IncludeRegistry = true,
    bool IncludeShortcuts = true,
    bool IncludeServices = true,
    bool IncludeTasks = true)
{
    public static ScanOptions Default => new();
}

/// <summary>
/// Real-time progress update reported during a leftover scan.
/// </summary>
public sealed record ScanProgress(
    CandidateKind CurrentKind,
    string CurrentTarget,
    int CandidatesFoundCount);

/// <summary>
/// Specialized sub-scanner for a specific category of leftover candidates.
/// </summary>
public interface ISubLeftoverScanner
{
    CandidateKind SupportedKind { get; }

    Task<IReadOnlyList<CleanupCandidate>> ScanAsync(
        ApplicationRecord application,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Composite leftover scanner coordinating candidate discovery across all system domains.
/// </summary>
public interface ILeftoverScanner
{
    Task<IReadOnlyList<CleanupCandidate>> ScanLeftoversAsync(
        ApplicationRecord application,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
