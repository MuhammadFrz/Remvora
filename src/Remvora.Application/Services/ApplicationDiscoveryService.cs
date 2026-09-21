using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Policies;

namespace Remvora.Application.Services;

/// <summary>
/// Production service coordinating installed application discovery across all registered sources.
/// </summary>
public sealed partial class ApplicationDiscoveryService : IApplicationDiscoveryService
{
    private readonly IEnumerable<IApplicationDiscoverySource> _sources;
    private readonly IDeduplicationPolicy _deduplicationPolicy;
    private readonly ILogger<ApplicationDiscoveryService> _logger;

    public ApplicationDiscoveryService(
        IEnumerable<IApplicationDiscoverySource> sources,
        IDeduplicationPolicy deduplicationPolicy,
        ILogger<ApplicationDiscoveryService> logger)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _deduplicationPolicy = deduplicationPolicy ?? throw new ArgumentNullException(nameof(deduplicationPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<ApplicationRecord>> DiscoverAllAsync(
        DiscoveryFilter? filter = null,
        IProgress<DiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new DiscoveryFilter();
        var stopwatch = Stopwatch.StartNew();
        LogDiscoveryStarted(_logger);

        var rawApplications = new List<ApplicationRecord>();
        var totalDiscovered = 0;

        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LogQueryingSource(_logger, source.SourceType, source.DisplayName);
            var sourceCount = 0;

            try
            {
                await foreach (var app in source.DiscoverAsync(filter, cancellationToken).ConfigureAwait(false))
                {
                    rawApplications.Add(app);
                    sourceCount++;
                    totalDiscovered++;

                    progress?.Report(new DiscoveryProgress(source.SourceType, app.DisplayName, totalDiscovered));
                }

                LogSourceCompleted(_logger, sourceCount, source.SourceType);
            }
            catch (OperationCanceledException)
            {
                LogDiscoveryCancelled(_logger, source.SourceType);
                throw;
            }
            catch (Exception ex)
            {
                LogSourceFailed(_logger, ex, source.SourceType);
            }
        }

        LogDeduplicating(_logger, rawApplications.Count);

        var deduplicated = _deduplicationPolicy.Deduplicate(rawApplications)
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        stopwatch.Stop();
        LogDiscoveryCompleted(
            _logger,
            stopwatch.ElapsedMilliseconds,
            deduplicated.Count,
            rawApplications.Count - deduplicated.Count);

        return deduplicated;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Beginning application discovery across registered sources...")]
    private static partial void LogDiscoveryStarted(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Querying discovery source: {SourceType} ({SourceDisplayName})")]
    private static partial void LogQueryingSource(ILogger logger, DiscoverySourceType sourceType, string sourceDisplayName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Discovered {Count} records from {SourceType}")]
    private static partial void LogSourceCompleted(ILogger logger, int count, DiscoverySourceType sourceType);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Application discovery was cancelled during {SourceType}")]
    private static partial void LogDiscoveryCancelled(ILogger logger, DiscoverySourceType sourceType);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Error while discovering applications from source {SourceType}. Continuing with remaining sources.")]
    private static partial void LogSourceFailed(ILogger logger, Exception ex, DiscoverySourceType sourceType);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Discovered {TotalCount} total raw application records. Applying deduplication policy...")]
    private static partial void LogDeduplicating(ILogger logger, int totalCount);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Application discovery completed in {ElapsedMs}ms. Inventory contains {FinalCount} canonical applications (merged {MergedCount} duplicates).")]
    private static partial void LogDiscoveryCompleted(ILogger logger, long elapsedMs, int finalCount, int mergedCount);
}
