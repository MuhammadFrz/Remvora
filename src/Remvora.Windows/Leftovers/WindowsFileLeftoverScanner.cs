using Microsoft.Extensions.Logging;
using Remvora.Application.Leftovers;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Policies;

namespace Remvora.Windows.Leftovers;

/// <summary>
/// Scans the filesystem for leftover application directories, data files, and caches.
/// </summary>
public sealed partial class WindowsFileLeftoverScanner : ISubLeftoverScanner
{
    private readonly ICandidateScoringPolicy _scoringPolicy;
    private readonly ILogger<WindowsFileLeftoverScanner> _logger;

    public WindowsFileLeftoverScanner(
        ICandidateScoringPolicy scoringPolicy,
        ILogger<WindowsFileLeftoverScanner> logger)
    {
        _scoringPolicy = scoringPolicy ?? throw new ArgumentNullException(nameof(scoringPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CandidateKind SupportedKind => CandidateKind.Directory;

    public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(
        ApplicationRecord application,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        var candidates = new List<CleanupCandidate>();

        // 1. Scan InstallLocation if it still exists
        if (!string.IsNullOrWhiteSpace(application.InstallLocation) && Directory.Exists(application.InstallLocation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(CandidateKind.Directory, application.InstallLocation, candidates.Count));

            var dirInfo = new DirectoryInfo(application.InstallLocation);
            var size = CalculateDirectorySize(dirInfo, cancellationToken);
            var reasons = new List<string> { "Application install directory still present after uninstall" };

            var eval = _scoringPolicy.Evaluate(
                kind: CandidateKind.Directory,
                target: application.InstallLocation,
                evidenceReasons: reasons,
                isInstallMonitorMatch: false,
                isExactInstallDirectoryMatch: true,
                isExactServiceOrTaskUnderInstallDir: false,
                isVendorAndAppDirMatch: true,
                isAppSpecificRegistryPath: false,
                isAppSpecificAppDataFolder: false,
                isShortcutTargetMatch: false,
                isSharedRuntimeOrCache: false,
                isMicrosoftOrSystemComponent: application.IsSystemComponent);

            candidates.Add(new CleanupCandidate(
                id: Guid.NewGuid(),
                applicationId: application.Id,
                kind: CandidateKind.Directory,
                target: application.InstallLocation,
                parentTarget: Path.GetDirectoryName(application.InstallLocation),
                evidenceReasons: reasons,
                confidence: eval.Confidence,
                confidenceScore: eval.Score,
                risk: eval.Risk,
                defaultSelected: eval.DefaultSelected,
                isProtected: eval.IsProtected,
                sizeBytes: size,
                notes: "Primary application installation folder"));
        }

        // 2. Scan standard app-data roots
        var dataRoots = GetDataRoots();
        var searchNames = GetSearchNames(application);

        foreach (var (rootName, rootPath) in dataRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(rootPath))
                continue;

            progress?.Report(new ScanProgress(CandidateKind.Directory, rootPath, candidates.Count));

            foreach (var name in searchNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Direct folder: e.g. AppData\Local\MyApp
                var candidatePath = Path.Combine(rootPath, name);
                if (Directory.Exists(candidatePath))
                {
                    AddFolderCandidate(candidatePath, rootName, application, candidates, isVendorMatch: false, cancellationToken);
                }

                // Publisher\App folder: e.g. AppData\Local\Vendor\MyApp
                if (!string.IsNullOrWhiteSpace(application.Publisher))
                {
                    var vendorCandidatePath = Path.Combine(rootPath, application.Publisher, name);
                    if (Directory.Exists(vendorCandidatePath))
                    {
                        AddFolderCandidate(vendorCandidatePath, rootName, application, candidates, isVendorMatch: true, cancellationToken);
                    }
                }
            }
        }

        LogScanCompleted(_logger, candidates.Count, application.DisplayName);
        return Task.FromResult<IReadOnlyList<CleanupCandidate>>(candidates);
    }

    private void AddFolderCandidate(
        string folderPath,
        string rootName,
        ApplicationRecord application,
        List<CleanupCandidate> candidates,
        bool isVendorMatch,
        CancellationToken cancellationToken)
    {
        // Don't add if already added or is identical to install location
        if (candidates.Any(c => string.Equals(c.Target, folderPath, StringComparison.OrdinalIgnoreCase)))
            return;

        if (string.Equals(application.InstallLocation, folderPath, StringComparison.OrdinalIgnoreCase))
            return;

        var dirInfo = new DirectoryInfo(folderPath);
        var size = CalculateDirectorySize(dirInfo, cancellationToken);

        var reasons = new List<string>
        {
            $"Application data directory found in {rootName}",
            isVendorMatch ? "Matched publisher and application folder hierarchy" : "Matched application name"
        };

        var eval = _scoringPolicy.Evaluate(
            kind: CandidateKind.Directory,
            target: folderPath,
            evidenceReasons: reasons,
            isInstallMonitorMatch: false,
            isExactInstallDirectoryMatch: false,
            isExactServiceOrTaskUnderInstallDir: false,
            isVendorAndAppDirMatch: isVendorMatch,
            isAppSpecificRegistryPath: false,
            isAppSpecificAppDataFolder: true,
            isShortcutTargetMatch: false,
            isSharedRuntimeOrCache: false,
            isMicrosoftOrSystemComponent: application.IsSystemComponent);

        candidates.Add(new CleanupCandidate(
            id: Guid.NewGuid(),
            applicationId: application.Id,
            kind: CandidateKind.Directory,
            target: folderPath,
            parentTarget: Path.GetDirectoryName(folderPath),
            evidenceReasons: reasons,
            confidence: eval.Confidence,
            confidenceScore: eval.Score,
            risk: eval.Risk,
            defaultSelected: eval.DefaultSelected,
            isProtected: eval.IsProtected,
            sizeBytes: size,
            notes: $"Discovered in {rootName}"));
    }

    private static List<(string Name, string Path)> GetDataRoots()
    {
        var roots = new List<(string, string)>();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            roots.Add(("LocalAppData", localAppData));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            roots.Add(("RoamingAppData", appData));

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
            roots.Add(("ProgramData", programData));

        return roots;
    }

    private static List<string> GetSearchNames(ApplicationRecord application)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(application.DisplayName))
            names.Add(application.DisplayName.Trim());

        if (!string.IsNullOrWhiteSpace(application.Identity.NormalizedName))
            names.Add(application.Identity.NormalizedName);

        // Filter out very short names (< 3 chars) to avoid false positives
        return names.Where(n => n.Length >= 3).ToList();
    }

    private static long CalculateDirectorySize(DirectoryInfo dir, CancellationToken cancellationToken)
    {
        long size = 0;
        try
        {
            // Do not follow reparse points
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return 0;

            foreach (var file in dir.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    size += file.Length;
                }
                catch
                {
                    // Ignore inaccessible file length error
                }
            }
        }
        catch
        {
            // Ignore access errors on directory enumeration
        }

        return size;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "File leftover scanner found {CandidateCount} candidates for '{AppName}'")]
    private static partial void LogScanCompleted(ILogger logger, int candidateCount, string appName);
}
