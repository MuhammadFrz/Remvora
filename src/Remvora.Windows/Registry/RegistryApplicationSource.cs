using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;

namespace Remvora.Windows.Registry;

/// <summary>
/// Discovers installed applications from Windows standard uninstall registry locations across 32-bit and 64-bit views.
/// </summary>
public sealed partial class RegistryApplicationSource : IApplicationDiscoverySource
{
    private const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Wow6432UninstallSubKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly IRegistryAccessor _registry;
    private readonly ILogger<RegistryApplicationSource> _logger;

    public DiscoverySourceType SourceType => DiscoverySourceType.Registry64;
    public string DisplayName => "Windows Uninstall Registry";

    public RegistryApplicationSource(IRegistryAccessor registry, ILogger<RegistryApplicationSource> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<ApplicationRecord> DiscoverAsync(
        DiscoveryFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        filter ??= new DiscoveryFilter();

        // 1. HKLM 64-bit
        if (filter.IncludePerMachine)
        {
            await foreach (var app in EnumerateKeyAsync(
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                UninstallSubKey,
                DiscoverySourceType.Registry64,
                InstallationScope.PerMachine,
                ArchitectureType.X64,
                filter,
                cancellationToken).ConfigureAwait(false))
            {
                yield return app;
            }

            // 2. HKLM 32-bit (WOW6432Node)
            await foreach (var app in EnumerateKeyAsync(
                RegistryHive.LocalMachine,
                RegistryView.Registry32,
                UninstallSubKey,
                DiscoverySourceType.Registry32,
                InstallationScope.PerMachine,
                ArchitectureType.X86,
                filter,
                cancellationToken).ConfigureAwait(false))
            {
                yield return app;
            }
        }

        // 3. HKCU (Per-user)
        if (filter.IncludePerUser)
        {
            await foreach (var app in EnumerateKeyAsync(
                RegistryHive.CurrentUser,
                RegistryView.Default,
                UninstallSubKey,
                DiscoverySourceType.RegistryUser,
                InstallationScope.PerUser,
                ArchitectureType.X64,
                filter,
                cancellationToken).ConfigureAwait(false))
            {
                yield return app;
            }
        }
    }

    private async IAsyncEnumerable<ApplicationRecord> EnumerateKeyAsync(
        RegistryHive hive,
        RegistryView view,
        string rootSubKey,
        DiscoverySourceType sourceType,
        InstallationScope scope,
        ArchitectureType architecture,
        DiscoveryFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var hivePrefix = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
        var subKeyNames = _registry.GetSubKeyNames(hive, view, rootSubKey);

        foreach (var subKeyName in subKeyNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullSubKeyPath = $"{rootSubKey}\\{subKeyName}";
            var values = _registry.GetValues(hive, view, fullSubKeyPath);
            if (values == null || values.Count == 0)
                continue;

            var app = ParseApplication(hivePrefix, fullSubKeyPath, subKeyName, values, sourceType, scope, architecture);
            if (app == null)
                continue;

            if (app.IsSystemComponent && !filter.IncludeSystemComponents)
                continue;

            if (!string.IsNullOrWhiteSpace(filter.SearchQuery) &&
                !app.DisplayName.Contains(filter.SearchQuery, StringComparison.OrdinalIgnoreCase) &&
                !(app.Publisher?.Contains(filter.SearchQuery, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            yield return app;
            await Task.Yield();
        }
    }

    private static ApplicationRecord? ParseApplication(
        string hivePrefix,
        string registryPath,
        string subKeyName,
        IReadOnlyDictionary<string, object?> values,
        DiscoverySourceType sourceType,
        InstallationScope scope,
        ArchitectureType architecture)
    {
        var displayName = GetStringValue(values, "DisplayName");
        if (string.IsNullOrWhiteSpace(displayName))
            return null; // Ignore non-displayable/component entries

        var publisher = GetStringValue(values, "Publisher");
        var displayVersion = GetStringValue(values, "DisplayVersion");
        var installLocation = GetStringValue(values, "InstallLocation");
        var uninstallString = GetStringValue(values, "UninstallString");
        var quietUninstallString = GetStringValue(values, "QuietUninstallString");
        var modifyPath = GetStringValue(values, "ModifyPath");

        // Parse estimated size (in KB from registry)
        long? estimatedSizeBytes = null;
        if (values.TryGetValue("EstimatedSize", out var sizeObj) && sizeObj != null)
        {
            if (sizeObj is int sizeInt && sizeInt > 0)
                estimatedSizeBytes = sizeInt * 1024L;
            else if (sizeObj is long sizeLong && sizeLong > 0)
                estimatedSizeBytes = sizeLong * 1024L;
            else if (long.TryParse(sizeObj.ToString(), out var parsedSize) && parsedSize > 0)
                estimatedSizeBytes = parsedSize * 1024L;
        }

        // Parse install date
        DateTimeOffset? installDate = null;
        var dateStr = GetStringValue(values, "InstallDate");
        if (!string.IsNullOrWhiteSpace(dateStr))
        {
            if (DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                installDate = new DateTimeOffset(dt, TimeSpan.Zero);
            else if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtParsed))
                installDate = new DateTimeOffset(dtParsed, TimeSpan.Zero);
        }

        // System component check
        var isSystemComponent = GetDwordValue(values, "SystemComponent") == 1;

        // Windows Installer check (MSI)
        var isMsi = GetDwordValue(values, "WindowsInstaller") == 1;
        string? productCode = null;

        // If subKeyName is a GUID (e.g. {12345678-ABCD-1234-ABCD-1234567890AB}), it is likely a ProductCode
        if (Guid.TryParse(subKeyName.Trim('{', '}'), out _))
        {
            productCode = subKeyName.StartsWith('{') ? subKeyName.ToUpperInvariant() : $"{{{subKeyName.ToUpperInvariant()}}}";
            isMsi = true;
        }

        // Determine installer type
        var installerType = DetermineInstallerType(values, uninstallString, isMsi);

        var fullRegistryIdentifier = $"{hivePrefix}\\{registryPath}";

        var identity = new ApplicationIdentity(
            displayName,
            publisher,
            displayVersion,
            installLocation,
            productCode,
            null,
            fullRegistryIdentifier);

        var uninstallInfo = new UninstallInfo(
            uninstallString,
            quietUninstallString,
            modifyPath,
            isMsi,
            scope == InstallationScope.PerMachine);

        var rawDict = values.ToDictionary(
            kv => kv.Key,
            kv => kv.Value?.ToString() ?? string.Empty);

        var discoverySource = new DiscoverySource(
            sourceType,
            fullRegistryIdentifier,
            DateTimeOffset.UtcNow,
            rawDict);

        return new ApplicationRecord(
            Guid.NewGuid(),
            displayName,
            publisher,
            displayVersion,
            installDate,
            installLocation,
            estimatedSizeBytes,
            null,
            scope,
            architecture,
            installerType,
            identity,
            uninstallInfo,
            [discoverySource],
            null,
            RunningStatus.Unknown,
            isSystemComponent);
    }

    private static InstallerType DetermineInstallerType(
        IReadOnlyDictionary<string, object?> values,
        string? uninstallString,
        bool isMsi)
    {
        if (isMsi)
            return InstallerType.Msi;

        if (values.ContainsKey("Inno Setup: Setup Version") || values.ContainsKey("Inno Setup Code, Inno Setup: App Path"))
            return InstallerType.InnoSetup;

        if (values.ContainsKey("BundleProviderKey") || values.ContainsKey("BundleCachePath"))
            return InstallerType.WiXBurn;

        if (!string.IsNullOrWhiteSpace(uninstallString))
        {
            if (uninstallString.Contains("unins000.exe", StringComparison.OrdinalIgnoreCase))
                return InstallerType.InnoSetup;

            if (uninstallString.Contains("Nullsoft", StringComparison.OrdinalIgnoreCase) ||
                uninstallString.Contains("uninst.exe", StringComparison.OrdinalIgnoreCase))
                return InstallerType.Nsis;

            if (uninstallString.Contains("MsiExec", StringComparison.OrdinalIgnoreCase))
                return InstallerType.Msi;
        }

        return InstallerType.CustomExecutable;
    }

    private static string? GetStringValue(IReadOnlyDictionary<string, object?> values, string name)
    {
        if (values.TryGetValue(name, out var val) && val != null)
        {
            var str = val.ToString()?.Trim();
            return string.IsNullOrEmpty(str) ? null : str;
        }
        return null;
    }

    private static int GetDwordValue(IReadOnlyDictionary<string, object?> values, string name)
    {
        if (values.TryGetValue(name, out var val) && val != null)
        {
            if (val is int intVal) return intVal;
            if (int.TryParse(val.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }
}
