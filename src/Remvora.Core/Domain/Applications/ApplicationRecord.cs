using Remvora.Core.Domain.Signatures;

namespace Remvora.Core.Domain.Applications;

/// <summary>
/// Canonical model representing an installed application in Remvora.
/// </summary>
public sealed record ApplicationRecord
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; }
    public string? Publisher { get; init; }
    public string? DisplayVersion { get; init; }
    public DateTimeOffset? InstallDate { get; init; }
    public string? InstallLocation { get; init; }
    public long? EstimatedSizeBytes { get; init; }
    public long? CalculatedSizeBytes { get; init; }
    public InstallationScope Scope { get; init; }
    public ArchitectureType Architecture { get; init; }
    public InstallerType InstallerType { get; init; }
    public ApplicationIdentity Identity { get; init; }
    public UninstallInfo Uninstall { get; init; }
    public IReadOnlyList<DiscoverySource> DiscoverySources { get; init; }
    public SignatureInfo? Signature { get; init; }
    public RunningStatus RunningState { get; init; }
    public bool IsSystemComponent { get; init; }
    public DateTimeOffset LastDiscovered { get; init; }

    public ApplicationRecord(
        Guid id,
        string displayName,
        string? publisher,
        string? displayVersion,
        DateTimeOffset? installDate,
        string? installLocation,
        long? estimatedSizeBytes,
        long? calculatedSizeBytes,
        InstallationScope scope,
        ArchitectureType architecture,
        InstallerType installerType,
        ApplicationIdentity identity,
        UninstallInfo uninstall,
        IReadOnlyList<DiscoverySource>? discoverySources = null,
        SignatureInfo? signature = null,
        RunningStatus runningState = RunningStatus.Unknown,
        bool isSystemComponent = false,
        DateTimeOffset? lastDiscovered = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        DisplayName = displayName.Trim();
        Publisher = publisher?.Trim();
        DisplayVersion = displayVersion?.Trim();
        InstallDate = installDate;
        InstallLocation = installLocation?.Trim();
        EstimatedSizeBytes = estimatedSizeBytes;
        CalculatedSizeBytes = calculatedSizeBytes;
        Scope = scope;
        Architecture = architecture;
        InstallerType = installerType;
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Uninstall = uninstall ?? throw new ArgumentNullException(nameof(uninstall));
        DiscoverySources = discoverySources ?? [];
        Signature = signature;
        RunningState = runningState;
        IsSystemComponent = isSystemComponent;
        LastDiscovered = lastDiscovered ?? DateTimeOffset.UtcNow;
    }
}
