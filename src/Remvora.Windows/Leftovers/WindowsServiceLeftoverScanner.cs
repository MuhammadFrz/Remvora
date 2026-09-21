using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Remvora.Application.Leftovers;
using Remvora.Core.CommandLine;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Policies;

namespace Remvora.Windows.Leftovers;

/// <summary>
/// Scans Windows Service Control Manager registrations for orphaned background services belonging to the uninstalled application.
/// </summary>
public sealed partial class WindowsServiceLeftoverScanner : ISubLeftoverScanner
{
    private const string ServicesKeyPath = @"SYSTEM\CurrentControlSet\Services";
    private readonly ICandidateScoringPolicy _scoringPolicy;
    private readonly ILogger<WindowsServiceLeftoverScanner> _logger;

    public WindowsServiceLeftoverScanner(
        ICandidateScoringPolicy scoringPolicy,
        ILogger<WindowsServiceLeftoverScanner> logger)
    {
        _scoringPolicy = scoringPolicy ?? throw new ArgumentNullException(nameof(scoringPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CandidateKind SupportedKind => CandidateKind.Service;

    public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(
        ApplicationRecord application,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        var candidates = new List<CleanupCandidate>();

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

        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var servicesKey = baseKey.OpenSubKey(ServicesKeyPath);
        if (servicesKey == null)
            return Task.FromResult<IReadOnlyList<CleanupCandidate>>(candidates);

        var subKeyNames = servicesKey.GetSubKeyNames();

        foreach (var serviceName in subKeyNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var serviceSubKey = servicesKey.OpenSubKey(serviceName);
                if (serviceSubKey == null)
                    continue;

                var imagePathObj = serviceSubKey.GetValue("ImagePath");
                var rawImagePath = imagePathObj as string;
                var displayName = serviceSubKey.GetValue("DisplayName") as string;

                var isPathMatch = false;
                var isNameMatch = false;

                // 1. Check binary path under application install directory
                if (normalizedInstallDir != null && !string.IsNullOrWhiteSpace(rawImagePath))
                {
                    var parsed = CommandLineParser.Parse(rawImagePath);
                    if (parsed != null && !string.IsNullOrWhiteSpace(parsed.ExecutablePath))
                    {
                        try
                        {
                            var normalizedExe = Path.GetFullPath(Environment.ExpandEnvironmentVariables(parsed.ExecutablePath));
                            if (normalizedExe.StartsWith(normalizedInstallDir, StringComparison.OrdinalIgnoreCase))
                            {
                                isPathMatch = true;
                            }
                        }
                        catch
                        {
                            // Path expansion / parse error
                        }
                    }
                }

                // 2. Check service name or display name match
                if (!isPathMatch)
                {
                    if (searchNames.Any(name => string.Equals(serviceName, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        isNameMatch = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(displayName) &&
                             searchNames.Any(name => displayName.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        isNameMatch = true;
                    }
                }

                if (isPathMatch || isNameMatch)
                {
                    var isSystem = IsSystemBinary(rawImagePath);
                    var reasons = new List<string>();
                    if (isPathMatch)
                        reasons.Add($"Service executable points into application directory: {rawImagePath}");
                    if (isNameMatch)
                        reasons.Add($"Service name or display name matches application: {displayName ?? serviceName}");

                    var eval = _scoringPolicy.Evaluate(
                        kind: CandidateKind.Service,
                        target: serviceName,
                        evidenceReasons: reasons,
                        isInstallMonitorMatch: false,
                        isExactInstallDirectoryMatch: false,
                        isExactServiceOrTaskUnderInstallDir: isPathMatch,
                        isVendorAndAppDirMatch: isNameMatch,
                        isAppSpecificRegistryPath: false,
                        isAppSpecificAppDataFolder: false,
                        isShortcutTargetMatch: false,
                        isSharedRuntimeOrCache: false,
                        isMicrosoftOrSystemComponent: isSystem);

                    candidates.Add(new CleanupCandidate(
                        id: Guid.NewGuid(),
                        applicationId: application.Id,
                        kind: CandidateKind.Service,
                        target: serviceName,
                        parentTarget: null,
                        evidenceReasons: reasons,
                        confidence: eval.Confidence,
                        confidenceScore: eval.Score,
                        risk: eval.Risk,
                        defaultSelected: eval.DefaultSelected,
                        isProtected: eval.IsProtected,
                        notes: $"Windows Service: {displayName ?? serviceName}"));
                }
            }
            catch
            {
                // Skip individual service inspection errors
            }
        }

        LogScanCompleted(_logger, candidates.Count, application.DisplayName);
        return Task.FromResult<IReadOnlyList<CleanupCandidate>>(candidates);
    }

    private static bool IsSystemBinary(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return false;

        var lower = imagePath.ToLowerInvariant();
        return lower.Contains("system32") || lower.Contains("syswow64") || lower.Contains("svchost.exe");
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Service leftover scanner found {CandidateCount} candidates for '{AppName}'")]
    private static partial void LogScanCompleted(ILogger logger, int candidateCount, string appName);
}
