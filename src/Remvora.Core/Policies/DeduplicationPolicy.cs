using Remvora.Core.Domain.Applications;

namespace Remvora.Core.Policies;

/// <summary>
/// Policy governing deduplication and merging of application records discovered across multiple sources.
/// </summary>
public interface IDeduplicationPolicy
{
    bool AreDuplicates(ApplicationRecord first, ApplicationRecord second);
    ApplicationRecord Merge(ApplicationRecord primary, ApplicationRecord secondary);
    IReadOnlyList<ApplicationRecord> Deduplicate(IEnumerable<ApplicationRecord> applications);
}

/// <summary>
/// Production deduplication engine enforcing strict prioritized identity rules.
/// Never merges records based on display name alone.
/// </summary>
public sealed class DeduplicationPolicy : IDeduplicationPolicy
{
    public bool AreDuplicates(ApplicationRecord first, ApplicationRecord second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        // Priority 1: Exact MSI Product Code (GUID)
        if (!string.IsNullOrWhiteSpace(first.Identity.ProductCode) &&
            !string.IsNullOrWhiteSpace(second.Identity.ProductCode) &&
            string.Equals(first.Identity.ProductCode, second.Identity.ProductCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Priority 2: Exact Package Family Name
        if (!string.IsNullOrWhiteSpace(first.Identity.PackageFamilyName) &&
            !string.IsNullOrWhiteSpace(second.Identity.PackageFamilyName) &&
            string.Equals(first.Identity.PackageFamilyName, second.Identity.PackageFamilyName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Priority 3: Exact Registry Key Path
        if (!string.IsNullOrWhiteSpace(first.Identity.RegistryKeyPath) &&
            !string.IsNullOrWhiteSpace(second.Identity.RegistryKeyPath) &&
            string.Equals(first.Identity.RegistryKeyPath, second.Identity.RegistryKeyPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Priority 4: Strong composite match: Normalized Publisher + Normalized Name + Version + Location
        // Both publisher and version must be present, or both publisher and location must match.
        var namesMatch = string.Equals(first.Identity.NormalizedName, second.Identity.NormalizedName, StringComparison.Ordinal);
        var publishersMatch = !string.IsNullOrEmpty(first.Identity.NormalizedPublisher) &&
                              string.Equals(first.Identity.NormalizedPublisher, second.Identity.NormalizedPublisher, StringComparison.Ordinal);
        var versionsMatch = !string.IsNullOrEmpty(first.Identity.Version) &&
                            string.Equals(first.Identity.Version, second.Identity.Version, StringComparison.OrdinalIgnoreCase);
        var locationsMatch = !string.IsNullOrEmpty(first.Identity.InstallLocation) &&
                             string.Equals(first.Identity.InstallLocation, second.Identity.InstallLocation, StringComparison.OrdinalIgnoreCase);

        if (namesMatch && publishersMatch)
        {
            if (versionsMatch && locationsMatch)
                return true;

            if (versionsMatch && string.IsNullOrEmpty(first.Identity.InstallLocation) && string.IsNullOrEmpty(second.Identity.InstallLocation))
                return true;

            if (locationsMatch)
                return true;
        }

        // NEVER deduplicate on display name alone!
        return false;
    }

    public ApplicationRecord Merge(ApplicationRecord primary, ApplicationRecord secondary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(secondary);

        // Combine unique discovery sources
        var combinedSources = new List<DiscoverySource>(primary.DiscoverySources);
        foreach (var source in secondary.DiscoverySources)
        {
            if (!combinedSources.Any(s => s.SourceType == source.SourceType && s.SourceIdentifier.Equals(source.SourceIdentifier, StringComparison.OrdinalIgnoreCase)))
            {
                combinedSources.Add(source);
            }
        }

        // Determine preferred metadata
        var preferredLocation = !string.IsNullOrWhiteSpace(primary.InstallLocation) ? primary.InstallLocation : secondary.InstallLocation;
        var preferredPublisher = !string.IsNullOrWhiteSpace(primary.Publisher) ? primary.Publisher : secondary.Publisher;
        var preferredVersion = !string.IsNullOrWhiteSpace(primary.DisplayVersion) ? primary.DisplayVersion : secondary.DisplayVersion;
        var preferredInstallDate = primary.InstallDate ?? secondary.InstallDate;
        var preferredEstimatedSize = primary.EstimatedSizeBytes ?? secondary.EstimatedSizeBytes;
        var preferredCalculatedSize = primary.CalculatedSizeBytes ?? secondary.CalculatedSizeBytes;
        var preferredSignature = primary.Signature ?? secondary.Signature;
        var preferredInstallerType = primary.InstallerType != InstallerType.Unknown ? primary.InstallerType : secondary.InstallerType;

        // Merge Uninstall info
        var uninstallString = !string.IsNullOrWhiteSpace(primary.Uninstall.UninstallString) ? primary.Uninstall.UninstallString : secondary.Uninstall.UninstallString;
        var quietUninstall = !string.IsNullOrWhiteSpace(primary.Uninstall.QuietUninstallString) ? primary.Uninstall.QuietUninstallString : secondary.Uninstall.QuietUninstallString;
        var modifyPath = !string.IsNullOrWhiteSpace(primary.Uninstall.ModifyPath) ? primary.Uninstall.ModifyPath : secondary.Uninstall.ModifyPath;
        var isMsi = primary.Uninstall.IsMsi || secondary.Uninstall.IsMsi;
        var requiresElevation = primary.Uninstall.RequiresElevation || secondary.Uninstall.RequiresElevation;

        var mergedUninstall = new UninstallInfo(uninstallString, quietUninstall, modifyPath, isMsi, requiresElevation);

        // Identity with merged attributes
        var mergedIdentity = new ApplicationIdentity(
            primary.DisplayName,
            preferredPublisher,
            preferredVersion,
            preferredLocation,
            primary.Identity.ProductCode ?? secondary.Identity.ProductCode,
            primary.Identity.PackageFamilyName ?? secondary.Identity.PackageFamilyName,
            primary.Identity.RegistryKeyPath ?? secondary.Identity.RegistryKeyPath);

        return new ApplicationRecord(
            primary.Id,
            primary.DisplayName,
            preferredPublisher,
            preferredVersion,
            preferredInstallDate,
            preferredLocation,
            preferredEstimatedSize,
            preferredCalculatedSize,
            primary.Scope != InstallationScope.Unknown ? primary.Scope : secondary.Scope,
            primary.Architecture != ArchitectureType.Neutral ? primary.Architecture : secondary.Architecture,
            preferredInstallerType,
            mergedIdentity,
            mergedUninstall,
            combinedSources,
            preferredSignature,
            primary.RunningState != RunningStatus.Unknown ? primary.RunningState : secondary.RunningState,
            primary.IsSystemComponent || secondary.IsSystemComponent,
            primary.LastDiscovered > secondary.LastDiscovered ? primary.LastDiscovered : secondary.LastDiscovered);
    }

    public IReadOnlyList<ApplicationRecord> Deduplicate(IEnumerable<ApplicationRecord> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);

        var list = applications.ToList();
        var result = new List<ApplicationRecord>();

        foreach (var app in list)
        {
            var existingIndex = result.FindIndex(r => AreDuplicates(r, app));
            if (existingIndex >= 0)
            {
                result[existingIndex] = Merge(result[existingIndex], app);
            }
            else
            {
                result.Add(app);
            }
        }

        return result;
    }
}
