using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;

namespace Remvora.Application.Leftovers;

/// <summary>
/// Composite leftover scanner aggregating specialized sub-scanners across
/// files, registry, shortcuts, services, and scheduled tasks.
/// </summary>
public sealed partial class CompositeLeftoverScanner : ILeftoverScanner
{
    private readonly IEnumerable<ISubLeftoverScanner> _subScanners;
    private readonly ILogger<CompositeLeftoverScanner> _logger;

    public CompositeLeftoverScanner(
        IEnumerable<ISubLeftoverScanner> subScanners,
        ILogger<CompositeLeftoverScanner> logger)
    {
        _subScanners = subScanners ?? throw new ArgumentNullException(nameof(subScanners));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<CleanupCandidate>> ScanLeftoversAsync(
        ApplicationRecord application,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        LogScanStarted(_logger, application.DisplayName, options.Level);

        var allCandidates = new List<CleanupCandidate>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subScanner in _subScanners)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsScannerEnabled(subScanner.SupportedKind, options))
                continue;

            try
            {
                var subProgress = progress is not null
                    ? new Progress<ScanProgress>(p => progress.Report(new ScanProgress(p.CurrentKind, p.CurrentTarget, allCandidates.Count + p.CandidatesFoundCount)))
                    : null;

                var candidates = await subScanner.ScanAsync(application, options, subProgress, cancellationToken).ConfigureAwait(false);

                foreach (var candidate in candidates)
                {
                    var key = $"{candidate.Kind}:{candidate.Target}";
                    if (seenTargets.Add(key))
                    {
                        allCandidates.Add(candidate);
                    }
                }

                progress?.Report(new ScanProgress(subScanner.SupportedKind, "Scan complete for category", allCandidates.Count));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogSubScannerFailed(_logger, ex, subScanner.GetType().Name, application.DisplayName);
            }
        }

        stopwatch.Stop();
        LogScanCompleted(_logger, allCandidates.Count, application.DisplayName, stopwatch.ElapsedMilliseconds);

        return allCandidates;
    }

    private static bool IsScannerEnabled(CandidateKind kind, ScanOptions options)
    {
        return kind switch
        {
            CandidateKind.File or CandidateKind.Directory => options.IncludeFiles,
            CandidateKind.RegistryKey or CandidateKind.RegistryValue => options.IncludeRegistry,
            CandidateKind.Shortcut => options.IncludeShortcuts,
            CandidateKind.Service => options.IncludeServices,
            CandidateKind.ScheduledTask => options.IncludeTasks,
            _ => true
        };
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Beginning leftover scan for '{AppName}' at level {ScanLevel}")]
    private static partial void LogScanStarted(ILogger logger, string appName, ScanLevel scanLevel);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Sub-scanner '{ScannerName}' failed during scan of '{AppName}'")]
    private static partial void LogSubScannerFailed(ILogger logger, Exception ex, string scannerName, string appName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Leftover scan found {CandidateCount} candidates for '{AppName}' in {ElapsedMs}ms")]
    private static partial void LogScanCompleted(ILogger logger, int candidateCount, string appName, long elapsedMs);
}
