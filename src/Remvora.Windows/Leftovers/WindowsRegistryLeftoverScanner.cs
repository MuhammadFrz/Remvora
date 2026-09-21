using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Remvora.Application.Leftovers;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Policies;
using Remvora.Windows.Registry;

namespace Remvora.Windows.Leftovers;

/// <summary>
/// Scans targeted Windows registry software branches for application remnants.
/// </summary>
public sealed partial class WindowsRegistryLeftoverScanner : ISubLeftoverScanner
{
    private readonly IRegistryAccessor _registryAccessor;
    private readonly ICandidateScoringPolicy _scoringPolicy;
    private readonly ILogger<WindowsRegistryLeftoverScanner> _logger;

    public WindowsRegistryLeftoverScanner(
        IRegistryAccessor registryAccessor,
        ICandidateScoringPolicy scoringPolicy,
        ILogger<WindowsRegistryLeftoverScanner> logger)
    {
        _registryAccessor = registryAccessor ?? throw new ArgumentNullException(nameof(registryAccessor));
        _scoringPolicy = scoringPolicy ?? throw new ArgumentNullException(nameof(scoringPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CandidateKind SupportedKind => CandidateKind.RegistryKey;

    public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(
        ApplicationRecord application,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);

        var candidates = new List<CleanupCandidate>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Check if specific uninstall key remains
        if (!string.IsNullOrWhiteSpace(application.Identity.RegistryKeyPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = ParseRegistryKeyPath(application.Identity.RegistryKeyPath);
            if (parsed.HasValue && _registryAccessor.KeyExists(parsed.Value.Hive, parsed.Value.View, parsed.Value.SubKey))
            {
                var reasons = new List<string> { "Uninstall registry entry still present in Windows registry" };
                var eval = _scoringPolicy.Evaluate(
                    kind: CandidateKind.RegistryKey,
                    target: application.Identity.RegistryKeyPath,
                    evidenceReasons: reasons,
                    isInstallMonitorMatch: false,
                    isExactInstallDirectoryMatch: false,
                    isExactServiceOrTaskUnderInstallDir: false,
                    isVendorAndAppDirMatch: true,
                    isAppSpecificRegistryPath: true,
                    isAppSpecificAppDataFolder: false,
                    isShortcutTargetMatch: false,
                    isSharedRuntimeOrCache: false,
                    isMicrosoftOrSystemComponent: application.IsSystemComponent);

                if (seenTargets.Add(application.Identity.RegistryKeyPath))
                {
                    candidates.Add(new CleanupCandidate(
                        id: Guid.NewGuid(),
                        applicationId: application.Id,
                        kind: CandidateKind.RegistryKey,
                        target: application.Identity.RegistryKeyPath,
                        parentTarget: null,
                        evidenceReasons: reasons,
                        confidence: eval.Confidence,
                        confidenceScore: eval.Score,
                        risk: eval.Risk,
                        defaultSelected: eval.DefaultSelected,
                        isProtected: eval.IsProtected,
                        notes: "Uninstall metadata key"));
                }
            }
        }

        // 2. Scan standard Software scopes
        var searchScopes = new (RegistryHive Hive, RegistryView View, string HivePrefix)[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default, "HKCU"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM\\Software\\WOW6432Node")
        };

        var searchNames = GetSearchNames(application);

        foreach (var (hive, view, prefix) in searchScopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(CandidateKind.RegistryKey, prefix, candidates.Count));

            var basePath = prefix.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase) ? "Software\\WOW6432Node" : "Software";

            foreach (var name in searchNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Direct key: Software\<AppName>
                var directSubKey = $"{basePath}\\{name}";
                if (_registryAccessor.KeyExists(hive, view, directSubKey))
                {
                    var fullTarget = $"{prefix}\\{name}";
                    AddRegistryCandidate(fullTarget, application, candidates, seenTargets, isVendorMatch: false);
                }

                // Vendor key: Software\<Publisher>\<AppName>
                if (!string.IsNullOrWhiteSpace(application.Publisher))
                {
                    var vendorSubKey = $"{basePath}\\{application.Publisher}\\{name}";
                    if (_registryAccessor.KeyExists(hive, view, vendorSubKey))
                    {
                        var fullTarget = $"{prefix}\\{application.Publisher}\\{name}";
                        AddRegistryCandidate(fullTarget, application, candidates, seenTargets, isVendorMatch: true);
                    }
                }
            }
        }

        LogScanCompleted(_logger, candidates.Count, application.DisplayName);
        return Task.FromResult<IReadOnlyList<CleanupCandidate>>(candidates);
    }

    private void AddRegistryCandidate(
        string fullTarget,
        ApplicationRecord application,
        List<CleanupCandidate> candidates,
        HashSet<string> seenTargets,
        bool isVendorMatch)
    {
        if (!seenTargets.Add(fullTarget))
            return;

        var reasons = new List<string>
        {
            "Application software settings key found in registry",
            isVendorMatch ? "Matched publisher and application registry hierarchy" : "Matched application name"
        };

        var eval = _scoringPolicy.Evaluate(
            kind: CandidateKind.RegistryKey,
            target: fullTarget,
            evidenceReasons: reasons,
            isInstallMonitorMatch: false,
            isExactInstallDirectoryMatch: false,
            isExactServiceOrTaskUnderInstallDir: false,
            isVendorAndAppDirMatch: isVendorMatch,
            isAppSpecificRegistryPath: true,
            isAppSpecificAppDataFolder: false,
            isShortcutTargetMatch: false,
            isSharedRuntimeOrCache: false,
            isMicrosoftOrSystemComponent: application.IsSystemComponent);

        candidates.Add(new CleanupCandidate(
            id: Guid.NewGuid(),
            applicationId: application.Id,
            kind: CandidateKind.RegistryKey,
            target: fullTarget,
            parentTarget: null,
            evidenceReasons: reasons,
            confidence: eval.Confidence,
            confidenceScore: eval.Score,
            risk: eval.Risk,
            defaultSelected: eval.DefaultSelected,
            isProtected: eval.IsProtected,
            notes: "Software registry configuration branch"));
    }

    private static (RegistryHive Hive, RegistryView View, string SubKey)? ParseRegistryKeyPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        var span = rawPath.Trim();
        RegistryHive hive;
        RegistryView view = RegistryView.Default;
        string subKey;

        if (span.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.LocalMachine;
            subKey = span["HKLM\\".Length..];
        }
        else if (span.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.LocalMachine;
            subKey = span["HKEY_LOCAL_MACHINE\\".Length..];
        }
        else if (span.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.CurrentUser;
            subKey = span["HKCU\\".Length..];
        }
        else if (span.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.CurrentUser;
            subKey = span["HKEY_CURRENT_USER\\".Length..];
        }
        else
        {
            return null;
        }

        if (subKey.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase))
        {
            view = RegistryView.Registry32;
        }

        return (hive, view, subKey);
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Registry leftover scanner found {CandidateCount} candidates for '{AppName}'")]
    private static partial void LogScanCompleted(ILogger logger, int candidateCount, string appName);
}
