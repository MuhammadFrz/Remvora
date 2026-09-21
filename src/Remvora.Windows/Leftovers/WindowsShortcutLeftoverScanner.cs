using Microsoft.Extensions.Logging;
using Remvora.Application.Leftovers;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Policies;

namespace Remvora.Windows.Leftovers;

/// <summary>
/// Scans Start Menu and Desktop locations for orphaned shortcuts (.lnk) pointing to an uninstalled application.
/// </summary>
public sealed partial class WindowsShortcutLeftoverScanner : ISubLeftoverScanner
{
    private readonly ICandidateScoringPolicy _scoringPolicy;
    private readonly ILogger<WindowsShortcutLeftoverScanner> _logger;

    public WindowsShortcutLeftoverScanner(
        ICandidateScoringPolicy scoringPolicy,
        ILogger<WindowsShortcutLeftoverScanner> logger)
    {
        _scoringPolicy = scoringPolicy ?? throw new ArgumentNullException(nameof(scoringPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CandidateKind SupportedKind => CandidateKind.Shortcut;

    public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(
        ApplicationRecord application,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        var candidates = new List<CleanupCandidate>();
        var seenShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var searchRoots = GetShortcutRoots();
        var searchNames = GetSearchNames(application);

        foreach (var (rootName, rootPath) in searchRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(rootPath))
                continue;

            progress?.Report(new ScanProgress(CandidateKind.Shortcut, rootPath, candidates.Count));

            // 1. Check direct .lnk files in root (e.g. Desktop\MyApp.lnk)
            try
            {
                foreach (var lnkFile in Directory.EnumerateFiles(rootPath, "*.lnk", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(lnkFile);

                    if (searchNames.Any(name => fileNameWithoutExt.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        AddShortcutCandidate(lnkFile, rootName, application, candidates, seenShortcuts, isFolderMatch: false);
                    }
                }
            }
            catch
            {
                // Access denied on folder
            }

            // 2. Check application/vendor subfolders in Start Menu (e.g. Programs\Vendor\ or Programs\MyApp\)
            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dirName = Path.GetFileName(subDir);

                    var isAppFolder = searchNames.Any(name => dirName.Contains(name, StringComparison.OrdinalIgnoreCase));
                    var isVendorFolder = !string.IsNullOrWhiteSpace(application.Publisher) &&
                                         dirName.Contains(application.Publisher, StringComparison.OrdinalIgnoreCase);

                    if (isAppFolder || isVendorFolder)
                    {
                        // Check .lnk files inside this matching folder
                        try
                        {
                            foreach (var lnk in Directory.EnumerateFiles(subDir, "*.lnk", SearchOption.AllDirectories))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                AddShortcutCandidate(lnk, rootName, application, candidates, seenShortcuts, isFolderMatch: true);
                            }
                        }
                        catch
                        {
                            // Skip inaccessible subfolders
                        }
                    }
                }
            }
            catch
            {
                // Skip directory enumeration errors
            }
        }

        LogScanCompleted(_logger, candidates.Count, application.DisplayName);
        return Task.FromResult<IReadOnlyList<CleanupCandidate>>(candidates);
    }

    private void AddShortcutCandidate(
        string shortcutPath,
        string rootName,
        ApplicationRecord application,
        List<CleanupCandidate> candidates,
        HashSet<string> seenShortcuts,
        bool isFolderMatch)
    {
        if (!seenShortcuts.Add(shortcutPath))
            return;

        long? size = null;
        try
        {
            size = new FileInfo(shortcutPath).Length;
        }
        catch
        {
            // Ignore size query failure
        }

        var reasons = new List<string>
        {
            $"Shortcut found in {rootName}",
            isFolderMatch ? "Located inside application Start Menu folder" : "Shortcut filename matches application name"
        };

        var eval = _scoringPolicy.Evaluate(
            kind: CandidateKind.Shortcut,
            target: shortcutPath,
            evidenceReasons: reasons,
            isInstallMonitorMatch: false,
            isExactInstallDirectoryMatch: false,
            isExactServiceOrTaskUnderInstallDir: false,
            isVendorAndAppDirMatch: isFolderMatch,
            isAppSpecificRegistryPath: false,
            isAppSpecificAppDataFolder: false,
            isShortcutTargetMatch: true,
            isSharedRuntimeOrCache: false,
            isMicrosoftOrSystemComponent: application.IsSystemComponent);

        candidates.Add(new CleanupCandidate(
            id: Guid.NewGuid(),
            applicationId: application.Id,
            kind: CandidateKind.Shortcut,
            target: shortcutPath,
            parentTarget: Path.GetDirectoryName(shortcutPath),
            evidenceReasons: reasons,
            confidence: eval.Confidence,
            confidenceScore: eval.Score,
            risk: eval.Risk,
            defaultSelected: eval.DefaultSelected,
            isProtected: eval.IsProtected,
            sizeBytes: size,
            notes: $"Shortcut in {rootName}"));
    }

    private static List<(string Name, string Path)> GetShortcutRoots()
    {
        var roots = new List<(string, string)>();

        var userStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (!string.IsNullOrWhiteSpace(userStartMenu))
            roots.Add(("User Start Menu", userStartMenu));

        var commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        if (!string.IsNullOrWhiteSpace(commonStartMenu))
            roots.Add(("Common Start Menu", commonStartMenu));

        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrWhiteSpace(userDesktop))
            roots.Add(("User Desktop", userDesktop));

        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (!string.IsNullOrWhiteSpace(commonDesktop))
            roots.Add(("Common Desktop", commonDesktop));

        return roots;
    }

    private static List<string> GetSearchNames(ApplicationRecord application)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(application.DisplayName))
            names.Add(application.DisplayName.Trim());

        if (!string.IsNullOrWhiteSpace(application.Identity.NormalizedName))
            names.Add(application.Identity.NormalizedName);

        return names.Where(n => n.Length >= 3).ToList();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Shortcut leftover scanner found {CandidateCount} candidates for '{AppName}'")]
    private static partial void LogScanCompleted(ILogger logger, int candidateCount, string appName);
}
