using Microsoft.Extensions.Logging;
using Remvora.Application.WindowsApps;
using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.WindowsApps;
using Remvora.Windows.Packages;

namespace Remvora.Windows.WindowsApps;

/// <summary>
/// Production Windows Apps manager detecting modern Store/MSIX packages,
/// classifying sponsored and consumer bloatware, and executing safe removals.
/// </summary>
public sealed partial class WindowsPackageAppManager : IWindowsAppsManager
{
    private static readonly HashSet<string> s_protectedPackageFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Windows.ShellExperienceHost_cw5n1h2txyewy",
        "Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy",
        "Microsoft.Windows.Search_cw5n1h2txyewy",
        "Microsoft.WindowsStore_8wekyb3d8bbwe",
        "Microsoft.Windows.SecHealthUI_8wekyb3d8bbwe",
        "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe",
        "windows.immersivecontrolpanel_cw5n1h2txyewy"
    };

    private static readonly Dictionary<string, (string DisplayName, BloatwareCategory Category)> s_knownBloatware = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.BingNews"] = ("Microsoft News", BloatwareCategory.Sponsored),
        ["Microsoft.BingWeather"] = ("MSN Weather", BloatwareCategory.Redundant),
        ["Microsoft.GetHelp"] = ("Get Help", BloatwareCategory.Redundant),
        ["Microsoft.Getstarted"] = ("Tips / Get Started", BloatwareCategory.Redundant),
        ["Microsoft.MicrosoftSolitaireCollection"] = ("Solitaire Collection", BloatwareCategory.Entertainment),
        ["Microsoft.People"] = ("Microsoft People", BloatwareCategory.Redundant),
        ["Microsoft.XboxApp"] = ("Xbox Console Companion", BloatwareCategory.Entertainment),
        ["Microsoft.XboxGameOverlay"] = ("Xbox Game Bar Overlay", BloatwareCategory.Entertainment),
        ["Microsoft.XboxGamingOverlay"] = ("Xbox Gaming Bar", BloatwareCategory.Entertainment),
        ["Microsoft.XboxIdentityProvider"] = ("Xbox Identity Provider", BloatwareCategory.Entertainment),
        ["Microsoft.XboxSpeechToTextOverlay"] = ("Xbox Speech to Text", BloatwareCategory.Entertainment),
        ["Microsoft.ZuneMusic"] = ("Media Player / Groove Music", BloatwareCategory.Entertainment),
        ["Microsoft.ZuneVideo"] = ("Films & TV", BloatwareCategory.Entertainment),
        ["SpotifyAB.SpotifyMusic"] = ("Spotify Music", BloatwareCategory.Entertainment),
        ["Clipchamp.Clipchamp"] = ("Clipchamp Video Editor", BloatwareCategory.Redundant)
    };

    private readonly IPackageAccessor _packageAccessor;
    private readonly ILogger<WindowsPackageAppManager> _logger;

    public WindowsPackageAppManager(
        IPackageAccessor packageAccessor,
        ILogger<WindowsPackageAppManager> logger)
    {
        _packageAccessor = packageAccessor ?? throw new ArgumentNullException(nameof(packageAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<WindowsAppPackage>> GetPackagesAsync(
        bool includeProvisioned = true,
        CancellationToken cancellationToken = default)
    {
        var discovered = _packageAccessor.GetUserPackages();
        var list = new List<WindowsAppPackage>(discovered.Count);

        foreach (var pkg in discovered)
        {
            var isProtected = IsSystemProtectedPackage(pkg.FamilyName, pkg.DisplayName);
            var bloatCategory = ClassifyBloatware(pkg.FamilyName, pkg.DisplayName);

            list.Add(new WindowsAppPackage(
                packageFullName: pkg.FullName,
                packageFamilyName: pkg.FamilyName,
                displayName: string.IsNullOrWhiteSpace(pkg.DisplayName) ? pkg.FamilyName : pkg.DisplayName,
                publisher: pkg.Publisher,
                version: pkg.Version,
                installLocation: pkg.InstallLocation,
                isProvisioned: false,
                isFramework: pkg.IsFramework,
                isSystemProtected: isProtected,
                bloatwareType: bloatCategory));
        }

        return Task.FromResult<IReadOnlyList<WindowsAppPackage>>(list);
    }

    public async Task<OperationResult> RemovePackageAsync(
        WindowsAppPackage package,
        bool allUsers = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (package.IsSystemProtected)
        {
            LogRemovalBlocked(_logger, package.DisplayName);
            return OperationResult.Failure(ErrorCode.ProtectedTarget, $"Package '{package.DisplayName}' is a critical Windows system component and cannot be uninstalled.");
        }

        try
        {
            var result = await _packageAccessor.RemovePackageAsync(package.PackageFullName, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                LogPackageRemoved(_logger, package.DisplayName);
                return OperationResult.Success();
            }

            return OperationResult.Failure(ErrorCode.OperationFailed, result.ErrorText ?? $"Failed to remove package (Code: {result.ErrorCode})");
        }
        catch (Exception ex)
        {
            LogRemovalFailed(_logger, ex, package.DisplayName);
            return OperationResult.Failure(ErrorCode.OperationFailed, ex.Message);
        }
    }

    private static bool IsSystemProtectedPackage(string familyName, string displayName)
    {
        if (s_protectedPackageFamilies.Contains(familyName))
            return true;

        if (familyName.StartsWith("Microsoft.Windows.", StringComparison.OrdinalIgnoreCase) &&
            (familyName.Contains("Shell", StringComparison.OrdinalIgnoreCase) ||
             familyName.Contains("Search", StringComparison.OrdinalIgnoreCase) ||
             familyName.Contains("SecHealth", StringComparison.OrdinalIgnoreCase) ||
             familyName.Contains("ControlPanel", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static BloatwareCategory ClassifyBloatware(string familyName, string displayName)
    {
        foreach (var (prefix, info) in s_knownBloatware)
        {
            if (familyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return info.Category;
        }

        if (familyName.Contains("CandyCrush", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("King.com", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("TikTok", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("Disney", StringComparison.OrdinalIgnoreCase) ||
            familyName.Contains("Facebook", StringComparison.OrdinalIgnoreCase))
        {
            return BloatwareCategory.Sponsored;
        }

        return BloatwareCategory.None;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Package removal blocked: '{DisplayName}' is system-protected.")]
    private static partial void LogRemovalBlocked(ILogger logger, string displayName);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Successfully removed modern package '{DisplayName}'.")]
    private static partial void LogPackageRemoved(ILogger logger, string displayName);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Error removing modern package '{DisplayName}'")]
    private static partial void LogRemovalFailed(ILogger logger, Exception ex, string displayName);
}
