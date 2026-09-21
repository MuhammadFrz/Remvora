using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Remvora.Application.Leftovers;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Policies;

namespace Remvora.Windows.Leftovers;

/// <summary>
/// Scans Windows Scheduled Tasks for orphaned task registrations whose action commands point into the uninstalled application.
/// </summary>
public sealed partial class WindowsScheduledTaskLeftoverScanner : ISubLeftoverScanner
{
    private readonly ICandidateScoringPolicy _scoringPolicy;
    private readonly ILogger<WindowsScheduledTaskLeftoverScanner> _logger;

    public WindowsScheduledTaskLeftoverScanner(
        ICandidateScoringPolicy scoringPolicy,
        ILogger<WindowsScheduledTaskLeftoverScanner> logger)
    {
        _scoringPolicy = scoringPolicy ?? throw new ArgumentNullException(nameof(scoringPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CandidateKind SupportedKind => CandidateKind.ScheduledTask;

    public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(
        ApplicationRecord application,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        var candidates = new List<CleanupCandidate>();

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var tasksRoot = Path.Combine(systemRoot, "System32", "Tasks");
        if (!Directory.Exists(tasksRoot))
            return Task.FromResult<IReadOnlyList<CleanupCandidate>>(candidates);

        string? normalizedInstallDir = null;
        if (!string.IsNullOrWhiteSpace(application.InstallLocation))
        {
            try
            {
                normalizedInstallDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(application.InstallLocation));
            }
            catch
            {
                // Fallback
            }
        }

        var searchNames = GetSearchNames(application);

        try
        {
            var taskFiles = Directory.EnumerateFiles(tasksRoot, "*", SearchOption.AllDirectories);
            foreach (var taskFilePath in taskFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var taskRelativePath = Path.GetRelativePath(tasksRoot, taskFilePath);
                    var taskName = Path.GetFileName(taskFilePath);

                    // Skip Microsoft core tasks root if looking for 3rd-party apps
                    if (taskRelativePath.StartsWith(@"Microsoft\Windows\", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var isNameMatch = searchNames.Any(name => taskName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                                                              taskRelativePath.Contains(name, StringComparison.OrdinalIgnoreCase));
                    var isPathMatch = false;
                    var isMicrosoft = false;

                    // Parse task XML for action commands
                    string fileContent;
                    try
                    {
                        fileContent = File.ReadAllText(taskFilePath);
                    }
                    catch
                    {
                        // Inaccessible task file
                        continue;
                    }

                    if (fileContent.Contains("<Task", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var doc = XDocument.Parse(fileContent);
                            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                            var author = doc.Root?.Element(ns + "RegistrationInfo")?.Element(ns + "Author")?.Value;
                            if (!string.IsNullOrWhiteSpace(author) && author.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                            {
                                isMicrosoft = true;
                            }

                            if (normalizedInstallDir != null)
                            {
                                var execCommands = doc.Descendants(ns + "Command").Select(e => e.Value).ToList();
                                foreach (var cmd in execCommands)
                                {
                                    if (string.IsNullOrWhiteSpace(cmd))
                                        continue;

                                    try
                                    {
                                        var expanded = Environment.ExpandEnvironmentVariables(cmd).Trim('\"');
                                        var normalizedCmd = Path.GetFullPath(expanded);
                                        if (normalizedCmd.StartsWith(normalizedInstallDir, StringComparison.OrdinalIgnoreCase))
                                        {
                                            isPathMatch = true;
                                            break;
                                        }
                                    }
                                    catch
                                    {
                                        // Path expansion error
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // XML parsing error
                        }
                    }

                    if (isPathMatch || isNameMatch)
                    {
                        var reasons = new List<string>();
                        if (isPathMatch)
                            reasons.Add("Scheduled task executes an action within the application installation directory");
                        if (isNameMatch)
                            reasons.Add($"Scheduled task path '{taskRelativePath}' matches application identity");

                        var eval = _scoringPolicy.Evaluate(
                            kind: CandidateKind.ScheduledTask,
                            target: taskRelativePath,
                            evidenceReasons: reasons,
                            isInstallMonitorMatch: false,
                            isExactInstallDirectoryMatch: false,
                            isExactServiceOrTaskUnderInstallDir: isPathMatch,
                            isVendorAndAppDirMatch: isNameMatch,
                            isAppSpecificRegistryPath: false,
                            isAppSpecificAppDataFolder: false,
                            isShortcutTargetMatch: false,
                            isSharedRuntimeOrCache: false,
                            isMicrosoftOrSystemComponent: isMicrosoft);

                        candidates.Add(new CleanupCandidate(
                            id: Guid.NewGuid(),
                            applicationId: application.Id,
                            kind: CandidateKind.ScheduledTask,
                            target: taskRelativePath,
                            parentTarget: null,
                            evidenceReasons: reasons,
                            confidence: eval.Confidence,
                            confidenceScore: eval.Score,
                            risk: eval.Risk,
                            defaultSelected: eval.DefaultSelected,
                            isProtected: eval.IsProtected,
                            notes: $"Scheduled task: {taskRelativePath}"));
                    }
                }
                catch
                {
                    // Skip file errors
                }
            }
        }
        catch
        {
            // Skip directory enumeration errors
        }

        LogScanCompleted(_logger, candidates.Count, application.DisplayName);
        return Task.FromResult<IReadOnlyList<CleanupCandidate>>(candidates);
    }

    private static List<string> GetSearchNames(ApplicationRecord application)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(application.DisplayName))
            names.Add(application.DisplayName.Trim());

        if (!string.IsNullOrWhiteSpace(application.Identity.NormalizedName))
            names.Add(application.Identity.NormalizedName);

        return names.Where(n => n.Length >= 4).ToList();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Scheduled task leftover scanner found {CandidateCount} candidates for '{AppName}'")]
    private static partial void LogScanCompleted(ILogger logger, int candidateCount, string appName);
}
