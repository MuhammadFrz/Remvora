using Remvora.Core.Domain.Scanning;

namespace Remvora.Application.Scanning;

/// <summary>
/// Service contract for deep system scanning (redundant files, app caches, leftover remnants, app issues) and cleanup.
/// </summary>
public interface ISystemScanService
{
    /// <summary>
    /// Executes a deep scan across specified or all system categories.
    /// </summary>
    Task<IReadOnlyList<ScanGroup>> ScanSystemAsync(
        IEnumerable<ScanCategory>? categoriesToScan = null,
        IProgress<ScanProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Safely cleans or resolves the selected scan items.
    /// </summary>
    Task<ScanCleanupResult> CleanSelectedItemsAsync(
        IEnumerable<ScanItem> items,
        IProgress<ScanProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
